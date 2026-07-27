using System.Text;
using UnityEngine;
using MindHexer.Shared.Protocol;
using MindHexer.Shared.Net;
using MindHexer.Controller.Net;

namespace MindHexer.Controller.UI
{
    /// <summary>
    /// S10e 컨트롤러 상태/진단 HUD. (상단 중앙 패널)
    /// 폰에서 콘솔을 못 보므로, 연결 진단에 필요한 값(내 IP / 대상 IP / WS 에러)을 화면에 띄운다.
    /// 조이스틱/패턴 패드는 각자 그린다. IMGUI라 씬 에셋 불필요.
    /// </summary>
    public sealed class ControllerHud : MonoBehaviour
    {
        [SerializeField] private PairingFlow _flow;
        [SerializeField] private RttProbeBehaviour _rtt;
        [SerializeField] private DiscoveryListenerBehaviour _discovery;
        [SerializeField] private WsClient _ws;

        [Tooltip("고DPI 폰에서 UI가 너무 작지 않도록 하는 스케일.")]
        public float UiScale = 2.0f;

        private string _ipInput = "192.168.";
        private string _localIps = "";
        private Texture2D _white;
        private GUIStyle _label, _button, _field, _title;
        private bool _stylesReady;

        private void Awake()
        {
            if (_flow == null) _flow = GetComponent<PairingFlow>();
            if (_rtt == null) _rtt = GetComponent<RttProbeBehaviour>();
            if (_discovery == null) _discovery = GetComponent<DiscoveryListenerBehaviour>();
            if (_ws == null) _ws = GetComponent<WsClient>();

            _white = new Texture2D(1, 1);
            _white.SetPixel(0, 0, Color.white);
            _white.Apply();

            var sb = new StringBuilder();
            foreach (var (iface, ip) in LocalIPv4.AllIPv4()) sb.Append(ip).Append(' ');
            _localIps = sb.Length > 0 ? sb.ToString().Trim() : "(없음)";
            _ipInput = LocalIPv4.GuessServerHost("192.168."); // 추정 서버(헤드셋) IP로 미리 채움
        }

        private void OnDestroy()
        {
            if (_white != null) Destroy(_white);
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            int fs = Mathf.RoundToInt(15 * UiScale);
            _label = new GUIStyle(GUI.skin.label) { fontSize = fs };
            _button = new GUIStyle(GUI.skin.button) { fontSize = fs };
            _field = new GUIStyle(GUI.skin.textField) { fontSize = fs };
            _title = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(19 * UiScale), fontStyle = FontStyle.Bold };
            _stylesReady = true;
        }

        private void OnGUI()
        {
            EnsureStyles();
            DrawStatusPanel();
        }

        private void DrawStatusPanel()
        {
            float pad = 12 * UiScale;
            float panelW = Mathf.Min(Screen.width - pad * 2, 520 * UiScale);
            float panelH = 250 * UiScale;
            float x = (Screen.width - panelW) * 0.5f;
            var area = new Rect(x, pad, panelW, panelH);
            DrawRect(area, new Color(0f, 0f, 0f, 0.6f));

            GUILayout.BeginArea(new Rect(area.x + pad, area.y + pad, area.width - pad * 2, area.height - pad * 2));
            GUILayout.Label("MINDHEXER — S10e", _title);

            string status = _flow != null ? _flow.StatusText : "-";
            GUILayout.Label($"상태: {status}", _label);

            string target = _flow != null && !string.IsNullOrEmpty(_flow.TargetIp) ? _flow.TargetIp : "(미정)";
            GUILayout.Label($"대상(헤드셋) IP: {target}", _label);
            GUILayout.Label($"내 IP: {_localIps}", _label);

            string disc = _discovery != null && _discovery.HasServer ? "발견" : "검색 중";
            GUILayout.Label($"디스커버리: {disc}", _label);

            if (_rtt != null && _rtt.AverageRttMs >= 0)
            {
                string ok = _rtt.MeetsTarget ? "OK" : "높음";
                GUILayout.Label($"RTT: {_rtt.AverageRttMs:0.0} ms ({ok})", _label);
            }

            if (_ws != null && !string.IsNullOrEmpty(_ws.LastError))
                GUILayout.Label($"WS 오류: {_ws.LastError}", _label);

            GUILayout.Space(4 * UiScale);
            GUILayout.BeginHorizontal();
            GUILayout.Label("수동 IP:", _label, GUILayout.Width(72 * UiScale));
            _ipInput = GUILayout.TextField(_ipInput, _field, GUILayout.MinWidth(180 * UiScale));
            if (GUILayout.Button("연결", _button, GUILayout.Width(88 * UiScale)))
            {
                if (_flow != null && !string.IsNullOrWhiteSpace(_ipInput))
                    _flow.ConnectManually(_ipInput.Trim());
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawRect(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _white);
            GUI.color = prev;
        }
    }
}
