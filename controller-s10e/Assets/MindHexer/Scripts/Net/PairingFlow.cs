using UnityEngine;
using MindHexer.Shared.Net;
using MindHexer.Shared.Protocol;
using MindHexer.Controller.Input;

namespace MindHexer.Controller.Net
{
    /// <summary>
    /// S10e 연결 수립 + 재연결 전체 흐름 조립. (SPEC 2.3 / 5.1)
    ///   디스커버리 발견 → (UDP/RTT 대상 설정 + WebSocket 접속) → PairAck → 스트리밍/RTT 시작
    ///   연결 실패/끊김 → <see cref="ReconnectPolicy"/> 지수 백오프로 재시도.
    /// 브로드캐스트가 막힌 환경 대비 <see cref="ConnectManually"/>(IP 직접 입력) 폴백 제공(SPEC 2.3-3).
    ///
    /// 스트리밍(TouchGyroCapture)/RTT 프로브는 페어링 전까지 비활성 → 페어링 성공 시 활성화.
    /// </summary>
    public sealed class PairingFlow : MonoBehaviour
    {
        [SerializeField] private DiscoveryListenerBehaviour _discovery;
        [SerializeField] private WsClient _ws;
        [SerializeField] private UdpSender _udpSender;
        [SerializeField] private RttProbeBehaviour _rttProbe;
        [SerializeField] private TouchGyroCapture _capture;
        [SerializeField] private PatternPadInput _pattern;

        [Tooltip("서버 IP 자동 취득: 디스커버리 비콘 우선, 없으면 게이트웨이(핫스팟 호스트=헤드셋) 자동 추정.")]
        public bool AutoAcquireServerIp = true;

        [Tooltip("디스커버리를 이만큼(초) 기다린 뒤에도 못 찾으면 게이트웨이 IP로 자동 접속.")]
        [Range(0f, 10f)] public float DiscoveryGraceSeconds = 3f;

        [Tooltip("수동 서버 IP(선택). 비우면 자동 취득. 값이 있으면 부팅 즉시 이 IP로.")]
        public string ManualServerIp = "";

        private enum FlowState { NoTarget, Connecting, Paired, Rejected }
        private FlowState _state = FlowState.NoTarget;

        private readonly ReconnectPolicy _reconnect = new ReconnectPolicy();
        private string _targetIp;
        private int _targetWsPort;
        private bool _hasTarget;
        private float _nextAttemptTime;
        private float _enableTime;

        public bool IsPaired => _state == FlowState.Paired;
        /// <summary>현재 접속 대상 IP(HUD 표시용). 없으면 빈 문자열.</summary>
        public string TargetIp => _targetIp ?? "";

        /// <summary>사람이 읽는 현재 상태(HUD 표시용).</summary>
        public string StatusText => _state switch
        {
            FlowState.NoTarget => _discovery != null && _discovery.HasServer ? "서버 발견됨" : "서버 자동 탐색 중…",
            FlowState.Connecting => $"연결 중… {_targetIp} (시도 {_reconnect.Attempt})",
            FlowState.Paired => $"페어링됨 ({_targetIp})",
            FlowState.Rejected => "거부됨(버전 불일치)",
            _ => "-"
        };

        private void Awake()
        {
            if (_discovery == null) _discovery = GetComponent<DiscoveryListenerBehaviour>();
            if (_ws == null) _ws = GetComponent<WsClient>();
            if (_udpSender == null) _udpSender = GetComponent<UdpSender>();
            if (_rttProbe == null) _rttProbe = GetComponent<RttProbeBehaviour>();
            if (_capture == null) _capture = GetComponent<TouchGyroCapture>();
            if (_pattern == null) _pattern = GetComponent<PatternPadInput>();
        }

        private void OnEnable()
        {
            SetStreaming(false); // 페어링 전 비활성

            if (_discovery != null) _discovery.ServerDiscovered += OnServerDiscovered;
            if (_ws != null)
            {
                _ws.Paired += OnPaired;
                _ws.Rejected += OnRejected;
                _ws.Disconnected += OnDisconnected;
            }
            if (_pattern != null) _pattern.PatternCompleted += OnPatternCompleted;

            _enableTime = Time.time;

            // 수동 IP가 지정돼 있으면 그 값으로 즉시. 아니면 디스커버리(비콘) → 게이트웨이 순으로 자동 취득.
            if (!string.IsNullOrWhiteSpace(ManualServerIp))
                SetTarget(ManualServerIp.Trim(), NetworkConstants.WebSocketPort);
        }

        private void OnDisable()
        {
            if (_discovery != null) _discovery.ServerDiscovered -= OnServerDiscovered;
            if (_ws != null)
            {
                _ws.Paired -= OnPaired;
                _ws.Rejected -= OnRejected;
                _ws.Disconnected -= OnDisconnected;
            }
            if (_pattern != null) _pattern.PatternCompleted -= OnPatternCompleted;
        }

        // 스와이프 패턴 완성 → 페어링돼 있으면 서버로 전송(WebSocket 확정 이벤트).
        private void OnPatternCompleted(int[] nodes)
        {
            if (_state != FlowState.Paired || _ws == null) return;
            _ws.SendPattern(nodes);
        }

        private void Update()
        {
            // 대상이 아직 없고, 디스커버리도 유예시간 내 못 찾았으면 → 게이트웨이(핫스팟 호스트=헤드셋)로 자동 접속.
            if (!_hasTarget && AutoAcquireServerIp && (Time.time - _enableTime) >= DiscoveryGraceSeconds)
            {
                string host = LocalIPv4.GuessServerHost(null);
                if (!string.IsNullOrEmpty(host))
                {
                    Debug.Log($"[Flow] 디스커버리 미발견 → 게이트웨이 자동 접속: {host}");
                    SetTarget(host, NetworkConstants.WebSocketPort);
                }
                else
                {
                    _enableTime = Time.time; // 재추정을 위해 유예 리셋
                }
            }

            // 재연결 스케줄러: 대상이 있고 아직 페어링/거부 상태가 아니면 백오프에 맞춰 재시도.
            if (!_hasTarget || _state == FlowState.Paired || _state == FlowState.Rejected) return;
            if (Time.time < _nextAttemptTime) return;

            AttemptConnect();
        }

        private void OnServerDiscovered(DiscoveredServer server)
        {
            Debug.Log($"[Flow] server discovered {server.Ip}:{server.WebSocketPort} v{server.ProtocolVersion}");
            SetTarget(server.Ip, server.WebSocketPort);
        }

        /// <summary>IP 직접 입력 폴백(SPEC 2.3-3). WS 포트는 기본값 사용.</summary>
        public void ConnectManually(string ip) => SetTarget(ip, NetworkConstants.WebSocketPort);

        private void SetTarget(string ip, int wsPort)
        {
            _targetIp = ip;
            _targetWsPort = wsPort;
            _hasTarget = true;
            _reconnect.Reset();
            _state = FlowState.Connecting;
            _nextAttemptTime = Time.time; // 즉시 첫 시도
        }

        private void AttemptConnect()
        {
            // UDP/RTT 대상을 먼저 맞춘 뒤 WebSocket 접속(페어링). 스트리밍은 PairAck 후 켠다.
            if (_udpSender != null) _udpSender.SetTarget(_targetIp);
            if (_rttProbe != null) _rttProbe.SetTarget(_targetIp);
            if (_ws != null) _ws.Connect(_targetIp, _targetWsPort);

            // 이번 시도가 실패(무응답)할 경우를 대비해 다음 시도 시각을 백오프로 예약.
            _nextAttemptTime = Time.time + (float)(_reconnect.NextDelayMs() / 1000.0);
            Debug.Log($"[Flow] connect attempt → {_targetIp}:{_targetWsPort}, next in {_nextAttemptTime - Time.time:0.0}s");
        }

        private void OnPaired()
        {
            Debug.Log("[Flow] paired → streaming on");
            _state = FlowState.Paired;
            _reconnect.Reset();
            SetStreaming(true);
        }

        private void OnRejected(string reason)
        {
            Debug.LogWarning($"[Flow] pairing rejected: {reason}");
            _state = FlowState.Rejected; // 버전 불일치는 재시도해도 무의미 → 중단
            SetStreaming(false);
        }

        private void OnDisconnected()
        {
            // 페어링 상태에서 끊긴 경우에만 재연결 개시(연결 시도 중의 소켓 교체는 무시).
            if (_state != FlowState.Paired) return;
            Debug.LogWarning("[Flow] disconnected → reconnecting");
            SetStreaming(false);
            _state = FlowState.Connecting;
            _reconnect.Reset();
            _nextAttemptTime = Time.time + (float)(_reconnect.NextDelayMs() / 1000.0);
        }

        private void SetStreaming(bool on)
        {
            if (_capture != null) _capture.enabled = on;
            if (_rttProbe != null) _rttProbe.enabled = on;
        }
    }
}
