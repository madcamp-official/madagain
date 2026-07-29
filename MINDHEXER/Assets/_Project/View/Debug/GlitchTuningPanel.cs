using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.View
{
    /// <summary>
    /// F5 인게임 치지직(HackableGlitchManager) 튜닝 패널 — Play 중 실시간 조정하고 JSON으로 저장한다.
    /// (F1=MoveTuningPanel, F2=LightingTuningPanel, F3=PoseTunePanel, F4=PoseSeqPanel이 이미 있어서
    /// View/Debug 폴더 전체를 확인하고 안 쓰는 F5로 잡았다.)
    /// <see cref="MoveTuningPanel"/>과 같은 패턴(JsonUtility로 컴포넌트 통째 직렬화, 필드 미러링 없음).
    ///
    /// <para>매 프레임 필드를 그대로 읽어 <see cref="HackableGlitchManager"/>에 반영하므로
    /// (머티리얼 전역값은 매니저의 <c>Update()</c>가 매 프레임 다시 밀어넣음, 밀도/알파는 애초에
    /// 매 프레임 계산됨) 슬라이더를 움직이는 즉시 반영된다 — 별도 콜백이 필요 없다.</para>
    /// </summary>
    public class GlitchTuningPanel : MonoBehaviour
    {
        const float PanelWidth = 400f;

        HackableGlitchManager _mgr;
        FirstPersonPlayer _fpp;

        bool _open;
        Vector2 _scroll;
        bool _secHackRange = true, _secRange = true, _secDensity = true, _secAlpha = true, _secGaze, _secShader, _secWave, _secColor;

        string _defJson;
        bool _captured;

        CursorLockMode _prevLock;
        bool _prevLookFrozen;

        static string FilePath => Path.Combine(Application.persistentDataPath, "glitch_tuning.json");

        void Awake()
        {
            _mgr = GetComponent<HackableGlitchManager>();
        }

        void Start()
        {
            Capture();
            Load();
        }

        void Capture()
        {
            if (_captured || _mgr == null) return;
            _defJson = JsonUtility.ToJson(_mgr);
            _captured = true;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb.f5Key.wasPressedThisFrame) return;

            if (_fpp == null) _fpp = FindAnyObjectByType<FirstPersonPlayer>();

            _open = !_open;
            if (_open)
            {
                _prevLock = Cursor.lockState;
                if (_fpp != null) _prevLookFrozen = _fpp.LookFrozen;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                if (_fpp != null) _fpp.LookFrozen = true;
            }
            else
            {
                Cursor.lockState = _prevLock;
                Cursor.visible = _prevLock != CursorLockMode.Locked;
                if (_fpp != null) _fpp.LookFrozen = _prevLookFrozen;
            }
        }

        void OnGUI()
        {
            if (!_open || _mgr == null) return;

            GUILayout.BeginArea(new Rect(12f, 12f, PanelWidth, Screen.height - 24f), GUI.skin.box);
            GUILayout.Label("<b>치지직 튜닝 (F5)</b> — 열어둔 채 조준·이동하며 조절 가능", Rich());

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("저장")) Save();
            if (GUILayout.Button("불러오기")) Load();
            if (GUILayout.Button("기본값 복원")) ResetAll();
            GUILayout.EndHorizontal();

            _scroll = GUILayout.BeginScrollView(_scroll);

            DrawHackRange();
            DrawRange();
            DrawDensity();
            DrawAlpha();
            DrawGaze();
            DrawShader();
            DrawWave();
            DrawColor();

            GUILayout.EndScrollView();
            GUILayout.Label($"<size=10>{FilePath}</size>", Rich());
            GUILayout.EndArea();
        }

        // ── 섹션 ────────────────────────────────────────────────────────

        void DrawHackRange()
        {
            if (!Section("해킹가능거리(hackRange, 미확정값)", ref _secHackRange)) return;
            _mgr.overrideHackRange = GUILayout.Toggle(_mgr.overrideHackRange, " 전역으로 강제 적용");
            _mgr.hackRangeOverride = F("해킹가능거리(m)", _mgr.hackRangeOverride, 1f, 60f);
            Info(_mgr.overrideHackRange
                ? "씬의 모든 Hackable.hackRange를 이 값으로 매 프레임 강제한다 — 실제 조준·해킹 판정 거리 자체가 바뀐다."
                : "꺼져 있으면 각 Hackable에 원래 세팅된 hackRange를 그대로 쓴다.");
        }

        void DrawRange()
        {
            if (!Section("거리 기준(hackRange)", ref _secRange)) return;
            _mgr.fadeDistance = F("hackRange 밖 추가 흐림거리(m)", _mgr.fadeDistance, 0.5f, 40f);
            _mgr.power = F("바깥 감쇠 지수", _mgr.power, 0.5f, 8f);
            Info("hackRange 안쪽은 무조건 최대. 밖은 여기부터 fadeDistance만큼 더 가면서 바닥값까지 흐려진다.");
            _mgr.responseSpeed = F("전환 속도(밀도·알파·모드 공통)", _mgr.responseSpeed, 0.5f, 40f);
        }

        void DrawDensity()
        {
            if (!Section("밀도(선 개수)", ref _secDensity)) return;
            _mgr.densityFloor = F("최소 밀도(Floor)", _mgr.densityFloor, 0f, 1f);
            _mgr.densityMax = F("최대 밀도(hackRange 안쪽)", _mgr.densityMax, 0f, 1f);
            Info("셰이더가 이 값을 문턱값으로 써서 켜지는 선의 비율을 바꾼다.");
        }

        void DrawAlpha()
        {
            if (!Section("불투명도(켜진 선의 진하기)", ref _secAlpha)) return;
            _mgr.alphaFloor = F("최소 불투명도(Floor)", _mgr.alphaFloor, 0f, 1f);
            _mgr.alphaMax = F("최대 불투명도(hackRange 안쪽)", _mgr.alphaMax, 0f, 1f);
            Info("밀도보다 덜 떨어지게 Floor를 densityFloor보다 높게 두는 게 기본 설계 의도.");
        }

        void DrawGaze()
        {
            if (!Section("조준(IsGazed, 3D 모드)", ref _secGaze)) return;
            _mgr.gazeDensity = F("조준 밀도", _mgr.gazeDensity, 0f, 1f);
            _mgr.gazeAlpha = F("조준 불투명도", _mgr.gazeAlpha, 0f, 1f);
            Info("2D 최댓값과 동일하게 맞추는 게 기본. 3D다움은 아래 웨이브 왜곡이 담당.");
        }

        void DrawShader()
        {
            if (!Section("가로줄 스캔 노이즈", ref _secShader)) return;
            _mgr.rowCount = F("가로줄 밀도(줄 개수)", _mgr.rowCount, 20f, 800f);
            _mgr.scrollSpeed = F("줄 갱신 속도(초당 스텝)", _mgr.scrollSpeed, 0f, 60f);
            _mgr.tearChance = F("트래킹 에러 확률", _mgr.tearChance, 0f, 1f);
            _mgr.lineBrightness = F("켜진 선의 밝기", _mgr.lineBrightness, 0f, 4f);
        }

        void DrawWave()
        {
            if (!Section("3D 웨이브 왜곡(가로선 꼬불거림)", ref _secWave)) return;
            _mgr.waveAmp = F("진폭", _mgr.waveAmp, 0f, 0.3f);
            _mgr.waveFreq = F("공간 주파수(1/m)", _mgr.waveFreq, 0f, 30f);
            _mgr.waveSpeed = F("속도", _mgr.waveSpeed, 0f, 15f);
            _mgr.wave3DMult = F("3D(조준) 시 배율", _mgr.wave3DMult, 0f, 6f);
        }

        void DrawColor()
        {
            if (!Section("색(인광 톤)", ref _secColor)) return;
            var c = _mgr.glitchColor;
            c.r = F("R", c.r, 0f, 3f);
            c.g = F("G", c.g, 0f, 3f);
            c.b = F("B", c.b, 0f, 3f);
            _mgr.glitchColor = c;
            GUILayout.Box("", GUILayout.Height(18f), GUILayout.Width(PanelWidth - 40f));
            var last = GUILayoutUtility.GetLastRect();
            var prev = GUI.color;
            GUI.color = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b));
            GUI.DrawTexture(last, Texture2D.whiteTexture);
            GUI.color = prev;
        }

        // ── 저장/로드 ───────────────────────────────────────────────────

        void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(_mgr, true));
                Debug.Log("[GlitchTuning] 저장: " + FilePath);
            }
            catch (System.Exception e) { Debug.LogWarning("[GlitchTuning] 저장 실패: " + e.Message); }
        }

        void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                JsonUtility.FromJsonOverwrite(File.ReadAllText(FilePath), _mgr);
                Debug.Log("[GlitchTuning] 로드: " + FilePath);
            }
            catch (System.Exception e) { Debug.LogWarning("[GlitchTuning] 로드 실패: " + e.Message); }
        }

        void ResetAll()
        {
            if (!string.IsNullOrEmpty(_defJson)) JsonUtility.FromJsonOverwrite(_defJson, _mgr);
        }

        // ── GUI 헬퍼 ────────────────────────────────────────────────────

        static GUIStyle _rich;
        static GUIStyle Rich()
        {
            if (_rich == null) _rich = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
            return _rich;
        }

        static bool Section(string title, ref bool open)
        {
            GUILayout.Space(4f);
            open = GUILayout.Toggle(open, (open ? "▼ " : "▶ ") + title, GUI.skin.button);
            return open;
        }

        static void Info(string s) => GUILayout.Label($"<size=11>{s}</size>", Rich());

        static float F(string label, float v, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, Rich(), GUILayout.Width(180f));
            GUILayout.Label(v.ToString("0.###"), Rich(), GUILayout.Width(48f));
            float r = GUILayout.HorizontalSlider(v, min, max);
            GUILayout.EndHorizontal();
            return r;
        }
    }
}
