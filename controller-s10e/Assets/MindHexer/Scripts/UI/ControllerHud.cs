using UnityEngine;
using MindHexer.Shared.Protocol;
using MindHexer.Controller.Net;

namespace MindHexer.Controller.UI
{
    /// <summary>
    /// S10e 컨트롤러 상태 HUD. (가로 화면 — 왼쪽 조이스틱 / 오른쪽 패턴 패드)
    /// 이 컴포넌트는 **상단 중앙의 연결상태/IP 패널**만 그린다.
    /// 조이스틱은 <see cref="MindHexer.Controller.Input.FloatingJoystickInput"/>,
    /// 패턴 패드(스와이프)는 <see cref="MindHexer.Controller.Input.PatternPadInput"/>가 각각 그린다.
    /// IMGUI라 씬 에셋/프리팹 불필요. 정식 UI는 uGUI로 교체 예정.
    /// </summary>
    public sealed class ControllerHud : MonoBehaviour
    {
        [SerializeField] private PairingFlow _flow;
        [SerializeField] private RttProbeBehaviour _rtt;
        [SerializeField] private DiscoveryListenerBehaviour _discovery;

        [Tooltip("고DPI 폰에서 UI가 너무 작지 않도록 하는 스케일.")]
        public float UiScale = 2.0f;

        private string _ipInput = "192.168.";
        private Texture2D _white;
        private GUIStyle _label, _button, _field, _title;
        private bool _stylesReady;

        private void Awake()
        {
            if (_flow == null) _flow = GetComponent<PairingFlow>();
            if (_rtt == null) _rtt = GetComponent<RttProbeBehaviour>();
            if (_discovery == null) _discovery = GetComponent<DiscoveryListenerBehaviour>();

            _white = new Texture2D(1, 1);
            _white.SetPixel(0, 0, Color.white);
            _white.Apply();
        }

        private void OnDestroy()
        {
            if (_white != null) Destroy(_white);
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            int fs = Mathf.RoundToInt(16 * UiScale);
            _label = new GUIStyle(GUI.skin.label) { fontSize = fs };
            _button = new GUIStyle(GUI.skin.button) { fontSize = fs };
            _field = new GUIStyle(GUI.skin.textField) { fontSize = fs };
            _title = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(20 * UiScale), fontStyle = FontStyle.Bold };
            _stylesReady = true;
        }

        private void OnGUI()
        {
            EnsureStyles();
            DrawStatusPanel();
        }

        // 상단 중앙 패널 — 좌(조이스틱)/우(패턴) 엄지 영역과 겹치지 않게.
        private void DrawStatusPanel()
        {
            float pad = 12 * UiScale;
            float panelW = Mathf.Min(Screen.width - pad * 2, 460 * UiScale);
            float panelH = 190 * UiScale;
            float x = (Screen.width - panelW) * 0.5f;
            var area = new Rect(x, pad, panelW, panelH);
            DrawRect(area, new Color(0f, 0f, 0f, 0.55f));

            GUILayout.BeginArea(new Rect(area.x + pad, area.y + pad, area.width - pad * 2, area.height - pad * 2));
            GUILayout.Label("MINDHEXER — S10e", _title);

            string status = _flow != null ? _flow.StatusText : "-";
            GUILayout.Label($"상태: {status}", _label);

            string disc = _discovery != null && _discovery.HasServer ? "발견" : "검색 중";
            GUILayout.Label($"디스커버리: {disc}", _label);

            if (_rtt != null && _rtt.AverageRttMs >= 0)
            {
                string ok = _rtt.MeetsTarget ? "OK" : "높음";
                GUILayout.Label($"RTT: {_rtt.AverageRttMs:0.0} ms ({ok}, 목표 {NetworkConstants.TargetRttMs})", _label);
            }
            else
            {
                GUILayout.Label("RTT: -", _label);
            }

            GUILayout.Space(6 * UiScale);
            GUILayout.BeginHorizontal();
            GUILayout.Label("서버 IP:", _label, GUILayout.Width(70 * UiScale));
            _ipInput = GUILayout.TextField(_ipInput, _field, GUILayout.MinWidth(180 * UiScale));
            if (GUILayout.Button("연결", _button, GUILayout.Width(90 * UiScale)))
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
