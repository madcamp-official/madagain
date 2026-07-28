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
        [SerializeField] private MindHexer.Controller.Input.ArcorePoseSource _arcore;
        [SerializeField] private UdpSender _sender;

        [Tooltip("고DPI 폰에서 UI가 너무 작지 않도록 하는 스케일.")]
        public float UiScale = 2.0f;

        [Tooltip("6DoF 추적이 이 시간(초) 이상 안정되면 패널을 숨긴다. 추적이 깨지면 다시 뜬다.")]
        public float HideAfterStableSeconds = 3f;

        private string _ipInput = "192.168.";
        private bool _ipEditOpen;
        private float _stableSince = -1f;
        private Texture2D _white;
        private GUIStyle _label, _button, _field, _title;
        private bool _stylesReady;

        private void Awake()
        {
            if (_flow == null) _flow = GetComponent<PairingFlow>();
            if (_rtt == null) _rtt = GetComponent<RttProbeBehaviour>();
            if (_discovery == null) _discovery = GetComponent<DiscoveryListenerBehaviour>();
            if (_arcore == null) _arcore = GetComponent<MindHexer.Controller.Input.ArcorePoseSource>();
            if (_sender == null) _sender = GetComponent<UdpSender>();

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
            if (PanelVisible) DrawStatusPanel();
            else DrawMinimalStrip();
        }

        /// <summary>
        /// 패널을 띄울지. 6DoF 추적이 <see cref="HideAfterStableSeconds"/> 이상 안정되면 접고,
        /// 추적이 깨지면 다시 편다.
        ///
        /// <para>직결 UDP는 단방향이라 "상대가 받았다"는 신호가 없다. 그래서 연결 성공 대신
        /// <b>추적 안정</b>을 기준으로 삼는다 — 실제로 실패하는 지점이 거기고, 헤드셋을 쓴 뒤에는
        /// 어차피 이 화면을 볼 수 없으므로 자동으로 접히는 게 맞다.</para>
        ///
        /// <para>IP를 편집 중이면 접지 않는다(입력하다 사라지면 곤란하다).</para>
        /// </summary>
        private bool PanelVisible
        {
            get
            {
                if (_ipEditOpen) return true;
                if (_arcore == null) return true;

                bool stable = _arcore.State == MindHexer.Controller.Input.ArcorePoseSource.PoseState.Tracking;
                if (!stable) { _stableSince = -1f; return true; }

                if (_stableSince < 0f) _stableSince = Time.unscaledTime;
                return Time.unscaledTime - _stableSince < HideAfterStableSeconds;
            }
        }

        /// <summary>패널을 접었을 때 남기는 한 줄. 헤드셋을 벗고 상태만 훑을 때 쓴다.</summary>
        private void DrawMinimalStrip()
        {
            uint sent = _sender != null ? _sender.SentCount : 0;
            string s = $"6DoF OK · {sent} pkt";
            var size = _label.CalcSize(new GUIContent(s));
            float pad = 8 * UiScale;
            var r = new Rect(pad, pad, size.x + pad * 2, size.y + pad);
            DrawRect(r, new Color(0f, 0f, 0f, 0.35f));
            GUI.Label(new Rect(r.x + pad, r.y + pad * 0.5f, size.x, size.y), s, _label);
        }

        // 상단 중앙 패널 — 좌(조이스틱)/우(패턴) 엄지 영역과 겹치지 않게.
        private void DrawStatusPanel()
        {
            float pad = 12 * UiScale;
            float panelW = Mathf.Min(Screen.width - pad * 2, 460 * UiScale);
            float panelH = 250 * UiScale;   // AR 상태 2줄 추가분 포함
            float x = (Screen.width - panelW) * 0.5f;
            var area = new Rect(x, pad, panelW, panelH);
            DrawRect(area, new Color(0f, 0f, 0f, 0.55f));

            GUILayout.BeginArea(new Rect(area.x + pad, area.y + pad, area.width - pad * 2, area.height - pad * 2));
            GUILayout.Label("MINDHEXER — S10e", _title);

            // PairingFlow가 없으면 직결 모드다(ControllerBootstrap.DirectMode) — 페어링 상태 대신
            // 실제로 어디로 몇 개나 쏘고 있는지를 보여준다. 이게 유일한 송신 확인 수단이다.
            if (_flow == null)
            {
                string tgt = _sender != null ? _sender.TargetIp : "-";
                uint sent = _sender != null ? _sender.SentCount : 0;
                GUILayout.Label($"직결 → {tgt}:{NetworkConstants.UdpInputPort}", _label);
                GUILayout.Label($"송신: {sent} 패킷", _label);
            }
            else
            {
                GUILayout.Label($"상태: {_flow.StatusText}", _label);

                string disc = _discovery != null && _discovery.HasServer ? "발견" : "검색 중";
                GUILayout.Label($"디스커버리: {disc}", _label);
            }

            if (_rtt != null && _rtt.AverageRttMs >= 0)
            {
                string ok = _rtt.MeetsTarget ? "OK" : "높음";
                GUILayout.Label($"RTT: {_rtt.AverageRttMs:0.0} ms ({ok}, 목표 {NetworkConstants.TargetRttMs})", _label);
            }
            else
            {
                GUILayout.Label("RTT: -", _label);
            }

            // 6DoF 상태 — 손에 든 상태에서 VIO가 실제로 버티는지 기기에서 바로 봐야 한다.
            if (_arcore != null)
            {
                Vector3 p = _arcore.Pose != null ? _arcore.Pose.localPosition : Vector3.zero;
                GUILayout.Label($"AR: {_arcore.StatusText}", _label);
                GUILayout.Label($"pos: ({p.x:0.00}, {p.y:0.00}, {p.z:0.00})", _label);
            }

            // IP 입력창은 <b>버튼을 눌러야</b> 열린다. 항상 그려두면 조작 중 잘못 스치기만 해도
            // 소프트 키보드가 뜨는데, 헤드셋을 쓴 상태에선 그게 떴는지도 모른 채 조작이 죽는다.
            GUILayout.Space(6 * UiScale);
            if (!_ipEditOpen)
            {
                if (GUILayout.Button("IP 변경", _button, GUILayout.Width(140 * UiScale)))
                {
                    _ipInput = _sender != null ? _sender.TargetIp : _ipInput;
                    _ipEditOpen = true;
                }
            }
            else
            {
                GUILayout.BeginHorizontal();
                _ipInput = GUILayout.TextField(_ipInput, _field, GUILayout.MinWidth(180 * UiScale));
                if (GUILayout.Button("적용", _button, GUILayout.Width(80 * UiScale)))
                {
                    string ip = (_ipInput ?? string.Empty).Trim();
                    if (ip.Length > 0)
                    {
                        // 직결 모드엔 PairingFlow가 없으므로 송신기에 직접 꽂는다.
                        if (_flow != null) _flow.ConnectManually(ip);
                        else if (_sender != null) _sender.SetTarget(ip);
                    }
                    _ipEditOpen = false;
                }
                if (GUILayout.Button("취소", _button, GUILayout.Width(80 * UiScale)))
                    _ipEditOpen = false;
                GUILayout.EndHorizontal();
            }
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
