using System.Text;
using UnityEngine;
using MindHexer.Shared.Net;
using MindHexer.Shared.Protocol;
using MindHexer.Headset.Net;
using MindHexer.Headset.Input;
using MindHexer.Headset.Gameplay;

namespace MindHexer.Headset.UI
{
    /// <summary>
    /// S24+(서버) 상태 HUD. 헤드셋 화면에 서버 리슨/페어링/수신 상태·6DoF·지터 버퍼 값을 표시한다.
    /// (기존엔 헤드셋에 화면 출력이 전혀 없어 연결돼도 "아무것도 안 뜨는" 상태였음 → 브링업 가시화.)
    /// IMGUI라 에셋/프리팹 불필요. VR(Cardboard) 렌더링은 별개.
    /// </summary>
    public sealed class HeadsetHud : MonoBehaviour
    {
        [SerializeField] private WebSocketServerHost _ws;
        [SerializeField] private UdpReceiver _rx;
        [SerializeField] private InputBridge _bridge;
        [SerializeField] private HackGrid _hack;

        public float UiScale = 2.0f;

        private Texture2D _white;
        private GUIStyle _label, _title;
        private bool _stylesReady;
        private string _ips = "";

        private float _rateTimer;
        private long _lastCount;
        private float _rate;

        private void Awake()
        {
            if (_ws == null) _ws = GetComponent<WebSocketServerHost>();
            if (_rx == null) _rx = GetComponent<UdpReceiver>();
            if (_bridge == null) _bridge = GetComponent<InputBridge>();
            if (_hack == null) _hack = GetComponent<HackGrid>();

            _white = new Texture2D(1, 1);
            _white.SetPixel(0, 0, Color.white);
            _white.Apply();

            var sb = new StringBuilder();
            foreach (var (iface, ip) in LocalIPv4.AllIPv4())
                sb.Append(ip).Append("  ");
            _ips = sb.Length > 0 ? sb.ToString().Trim() : LocalIPv4.Resolve();
        }

        private void OnDestroy()
        {
            if (_white != null) Destroy(_white);
        }

        private void Update()
        {
            _rateTimer += Time.deltaTime;
            if (_rateTimer >= 0.5f && _rx != null)
            {
                long c = _rx.AcceptedCount;
                _rate = (c - _lastCount) / _rateTimer;
                _lastCount = c;
                _rateTimer = 0f;
            }
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            int fs = Mathf.RoundToInt(15 * UiScale);
            _label = new GUIStyle(GUI.skin.label) { fontSize = fs };
            _title = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(20 * UiScale), fontStyle = FontStyle.Bold };
            _stylesReady = true;
        }

        private void OnGUI()
        {
            EnsureStyles();

            // 연결 끊김 경고(SPEC 5.1): UDP 1초+ 미수신 시 상단에 빨간 배너.
            bool warn = _ws != null ? _ws.LinkWarning : (_rx != null && _rx.AcceptedCount > 0 && _rx.IsTimedOut);
            if (warn)
            {
                float bh = 44 * UiScale;
                var bar = new Rect(0, 0, Screen.width, bh);
                var pc = GUI.color; GUI.color = new Color(0.75f, 0.05f, 0.05f, 0.92f);
                GUI.DrawTexture(bar, _white); GUI.color = pc;
                var ws = new GUIStyle(_title) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
                GUI.Label(bar, "⚠ UDP 스트림 끊김 — 컨트롤러 재연결 대기 중", ws);
            }

            float pad = 14 * UiScale;
            var area = new Rect(pad, pad, Mathf.Min(Screen.width - pad * 2, 720 * UiScale), 300 * UiScale);
            var prev = GUI.color; GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(area, _white); GUI.color = prev;

            GUILayout.BeginArea(new Rect(area.x + pad, area.y + pad, area.width - pad * 2, area.height - pad * 2));
            GUILayout.Label("MINDHEXER — S24+ (WebSocket 서버)", _title);

            int paired = _ws != null ? _ws.PairedCount : 0;
            GUILayout.Label($"서버 리슨: ws://{_ips} : {NetworkConstants.WebSocketPort}{NetworkConstants.WebSocketPath}", _label);
            GUILayout.Label($"UDP 수신 포트: {NetworkConstants.UdpInputPort}   프로토콜 v{NetworkConstants.ProtocolVersion}", _label);
            GUILayout.Label($"페어링된 컨트롤러: {paired}", _label);

            if (_rx != null)
            {
                string link = _rx.IsTimedOut ? "끊김/대기" : "수신 중";
                GUILayout.Label($"스트림: {link}  |  {_rate:0.0} pkt/s  (수용 {_rx.AcceptedCount}, 폐기 {_rx.DiscardedCount})", _label);
            }

            if (_bridge != null)
            {
                Vector3 p = _bridge.SmoothedPosition;
                Vector3 e = _bridge.SmoothedRotation.eulerAngles;
                Vector2 m = _bridge.MoveAxis;
                GUILayout.Space(6 * UiScale);
                GUILayout.Label($"pos = ({p.x:0.00}, {p.y:0.00}, {p.z:0.00})", _label);
                GUILayout.Label($"rot = ({e.x:0.0}, {e.y:0.0}, {e.z:0.0})", _label);
                GUILayout.Label($"move = ({m.x:0.00}, {m.y:0.00})", _label);
                GUILayout.Label($"지터버퍼: 지연 {_bridge.BufferDelayMs:0} ms, 지터 {_bridge.JitterMs:0.0} ms, 간격 {_bridge.IntervalMs:0.0} ms, 샘플 {_bridge.BufferedSamples}", _label);
                string comp = _bridge.LatencyCompensation
                    ? (_bridge.ClockLocked ? $"예측 +{_bridge.PredictLeadMs:0} ms" : "시계 동기화 중…")
                    : "off";
                GUILayout.Label($"지연 보정(송신시각 기반): {comp}", _label);
            }

            if (_hack != null)
                GUILayout.Label($"패턴 수신: [{_hack.LastPattern}] → {_hack.LastResult}", _label);

            GUILayout.EndArea();
        }
    }
}
