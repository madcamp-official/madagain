using UnityEngine;
using MindHexer.Shared.Protocol;
using MindHexer.Controller.Net;

namespace MindHexer.Controller.UI
{
    /// <summary>
    /// S10e 컨트롤러 화면 UI. (SPEC 4.2 — 씬 1개, 3x3 터치 그리드 오버레이 + 연결 상태 UI)
    /// 그래픽 리소스를 최소화하기 위해 IMGUI(OnGUI)로 그린다 → 씬 에셋/프리팹 불필요, 즉시 동작.
    ///  - 3x3 그리드 선 + 현재 터치 셀 하이라이트(매핑은 공유 <see cref="HackGridMath"/>)
    ///  - 연결 상태(검색/연결/페어링/RTT) 패널
    ///  - IP 직접 입력 + 연결 버튼(브로드캐스트 실패 폴백, SPEC 2.3-3)
    ///
    /// 정식 프로덕션 UI는 uGUI로 교체 예정. 여기서는 기능 확인/데모용 오버레이.
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
            DrawGrid();
            DrawTouchHighlight();
            DrawStatusPanel();
        }

        // ---- 3x3 그리드 ----

        private void DrawGrid()
        {
            var col = new Color(1f, 1f, 1f, 0.25f);
            float w = Screen.width, h = Screen.height;
            float thickness = Mathf.Max(1f, UiScale);

            for (int i = 1; i < HackGridMath.Size; i++)
            {
                float x = w * i / HackGridMath.Size;
                DrawRect(new Rect(x - thickness / 2f, 0, thickness, h), col);
                float y = h * i / HackGridMath.Size;
                DrawRect(new Rect(0, y - thickness / 2f, w, thickness), col);
            }
        }

        private void DrawTouchHighlight()
        {
            if (UnityEngine.Input.touchCount == 0) return;
            Touch t = UnityEngine.Input.GetTouch(0);
            Vector2 norm = new Vector2(t.position.x / Screen.width, t.position.y / Screen.height);
            int cell = HackGridMath.ToCellIndex(norm);

            float cw = (float)Screen.width / HackGridMath.Size;
            float ch = (float)Screen.height / HackGridMath.Size;
            int gcol = cell % HackGridMath.Size;
            int grow = cell / HackGridMath.Size; // 0 = 하단
            // Input 좌표는 좌하단 원점, GUI 좌표는 좌상단 원점 → y 변환.
            float x = gcol * cw;
            float y = Screen.height - (grow + 1) * ch;
            DrawRect(new Rect(x, y, cw, ch), new Color(0.2f, 0.8f, 1f, 0.25f));
        }

        // ---- 상태 패널 ----

        private void DrawStatusPanel()
        {
            float pad = 12 * UiScale;
            float panelW = Mathf.Min(Screen.width - pad * 2, 420 * UiScale);
            float panelH = 200 * UiScale;
            var area = new Rect(pad, pad, panelW, panelH);
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

            GUILayout.Space(8 * UiScale);
            GUILayout.Label("서버 IP 직접 입력(폴백):", _label);
            GUILayout.BeginHorizontal();
            _ipInput = GUILayout.TextField(_ipInput, _field, GUILayout.MinWidth(200 * UiScale));
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
