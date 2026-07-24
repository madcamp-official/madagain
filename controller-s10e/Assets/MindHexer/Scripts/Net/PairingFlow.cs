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

        [Tooltip("부팅 시 자동 접속할 기본 서버 IP. Windows PC 모바일 핫스팟 호스트는 항상 192.168.137.1. " +
                 "비우면 디스커버리/수동입력만 사용. 디스커버리·수동입력이 오면 그 값으로 덮어씀.")]
        public string DefaultServerIp = "192.168.137.1";

        private enum FlowState { NoTarget, Connecting, Paired, Rejected }
        private FlowState _state = FlowState.NoTarget;

        private readonly ReconnectPolicy _reconnect = new ReconnectPolicy();
        private string _targetIp;
        private int _targetWsPort;
        private bool _hasTarget;
        private float _nextAttemptTime;

        public bool IsPaired => _state == FlowState.Paired;

        /// <summary>사람이 읽는 현재 상태(HUD 표시용).</summary>
        public string StatusText => _state switch
        {
            FlowState.NoTarget => _discovery != null && _discovery.HasServer ? "서버 발견됨" : "서버 검색 중…",
            FlowState.Connecting => $"연결 중… (시도 {_reconnect.Attempt})",
            FlowState.Paired => "페어링됨",
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

            // 기본 서버 IP가 설정돼 있으면 부팅 즉시 자동 접속 시도.
            // (자동 디스커버리가 안 되는 PC-핫스팟 구성에서 폰 조작 없이 붙게 함.
            //  디스커버리/수동입력이 오면 SetTarget이 대상을 덮어씀.)
            if (!string.IsNullOrWhiteSpace(DefaultServerIp))
                SetTarget(DefaultServerIp.Trim(), NetworkConstants.WebSocketPort);
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
        }

        private void Update()
        {
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
