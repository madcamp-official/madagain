using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.View
{
    /// <summary>
    /// F1 인게임 이동 튜닝 패널 — 가속·도약·등반·연출 수치를 Play 중 실시간 조정하고 JSON으로 저장한다.
    ///
    /// <para>저장은 <see cref="JsonUtility"/>로 컴포넌트를 통째 직렬화한다(필드 미러링 없음).
    /// 그래서 <b>AnimationCurve까지 그대로</b> 저장되고, 나중에 필드를 추가해도 저장 코드를 안 고쳐도 된다.</para>
    ///
    /// <para><b>개발자 도구</b>(OnGUI) — PC 튜닝 전용이다. VR 빌드에선 보이지 않는다.
    /// 값이 확정되면 코드 기본값으로 굳히는 것을 전제로 한다.</para>
    /// </summary>
    public class MoveTuningPanel : MonoBehaviour
    {
        const float PanelWidth = 400f;

        FirstPersonPlayer _fpp;
        AutoTraversal _auto;
        MotionFeel _feel;
        MantleRig _rig;

        bool _open;
        Vector2 _scroll;
        bool _secMove = true, _secJump = true, _secClimb, _secFeel, _secArm;

        // 코드 기본값(첫 프레임 캡처) — '기본값 복원'의 목표
        string _defMove, _defAuto, _defFeel, _defRig;
        bool _captured;

        CursorLockMode _prevLock;
        bool _prevLookFrozen;

        static string FilePath => Path.Combine(Application.persistentDataPath, "move_tuning.json");

        [System.Serializable]
        class Snapshot { public string move, auto, feel, rig; }

        void Awake()
        {
            _fpp = GetComponent<FirstPersonPlayer>();
            _auto = GetComponent<AutoTraversal>();
            _feel = GetComponent<MotionFeel>();
            _rig = GetComponent<MantleRig>();
        }

        void Start()
        {
            Capture();
            Load();
        }

        void Capture()
        {
            if (_captured) return;
            _defMove = JsonUtility.ToJson(_fpp.move);
            _defAuto = _auto != null ? JsonUtility.ToJson(_auto) : null;
            _defFeel = _feel != null ? JsonUtility.ToJson(_feel) : null;
            _defRig = _rig != null ? JsonUtility.ToJson(_rig) : null;
            _captured = true;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb.f1Key.wasPressedThisFrame) return;

            _open = !_open;
            if (_open)
            {
                _prevLock = Cursor.lockState;
                _prevLookFrozen = _fpp.LookFrozen;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                _fpp.LookFrozen = true;      // 슬라이더를 끄는 마우스가 화면을 돌리지 않게
            }
            else
            {
                Cursor.lockState = _prevLock;
                Cursor.visible = _prevLock != CursorLockMode.Locked;
                _fpp.LookFrozen = _prevLookFrozen;
            }
        }

        void OnGUI()
        {
            if (!_open) return;

            GUILayout.BeginArea(new Rect(Screen.width - PanelWidth - 12f, 12f, PanelWidth, Screen.height - 24f),
                                GUI.skin.box);
            GUILayout.Label("<b>이동 튜닝 (F1)</b>  — 열어둔 채 움직이며 조절 가능", Rich());

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("저장")) Save();
            if (GUILayout.Button("불러오기")) Load();
            if (GUILayout.Button("기본값 복원")) ResetAll();
            GUILayout.EndHorizontal();

            _scroll = GUILayout.BeginScrollView(_scroll);

            DrawMove();
            DrawJump();
            DrawClimb();
            DrawFeel();
            DrawArm();

            GUILayout.EndScrollView();
            GUILayout.Label($"<size=10>{FilePath}</size>", Rich());
            GUILayout.EndArea();
        }

        // ── 섹션 ────────────────────────────────────────────────────────

        void DrawMove()
        {
            if (!Section("이동 (가속/감속)", ref _secMove)) return;
            var m = _fpp.move;

            m.maxSpeed = F("최고 속도", m.maxSpeed, 2f, 14f);
            m.acceleration = F("가속도", m.acceleration, 5f, 120f);
            m.deceleration = F("감속도", m.deceleration, 5f, 200f);
            Info($"0→최고속 {m.maxSpeed / Mathf.Max(1f, m.acceleration):0.00}초  ·  " +
                 $"정지 {m.maxSpeed / Mathf.Max(1f, m.deceleration):0.00}초");
            m.airControl = F("공중 제어", m.airControl, 0f, 1f);
            _fpp.gravity = F("중력", _fpp.gravity, 5f, 50f);
            _fpp.lookSens = F("마우스 감도", _fpp.lookSens, 0.02f, 0.5f);

            Info("<b>지연 보상</b> (VR용 — PC는 age=0이라 효과 없음)");
            m.maxCatchUp = F("따라잡기 상한(초)", m.maxCatchUp, 0f, 0.4f);
            m.baselineCompensation = F("기저 지연 보정(초)", m.baselineCompensation, 0f, 0.15f);
        }

        void DrawJump()
        {
            if (_auto == null || !Section("시선 도약", ref _secJump)) return;

            _auto.coneAngle = F("시야 원뿔 반각(도)", _auto.coneAngle, 5f, 70f);
            _auto.distancePenalty = F("거리 감점(1m당)", _auto.distancePenalty, 0f, 0.3f);
            Info("0 = 순수 정면 우선 · 크게 = 최근접 우선");
            _auto.jumpSearchRadius = F("검색 반경(m)", _auto.jumpSearchRadius, 2f, 20f);
            _auto.maxDirectUp = F("직행 도약 최대 높이(m)", _auto.maxDirectUp, 0.3f, 2f);
            _auto.maxMantleUp = F("잡고 오르기 최대 높이(m)", _auto.maxMantleUp, 0.5f, 3.5f);
            _auto.maxDropTarget = F("허용 최대 낙차(m)", _auto.maxDropTarget, 1f, 20f);

            Info("<b>궤적</b>");
            _auto.flightGravity = F("도약 중력", _auto.flightGravity, 8f, 60f);
            _auto.clearance = F("모서리 위 여유(m)", _auto.clearance, 0.05f, 1.2f);
            _auto.airSpeedCap = F("수평 속도 상한(m/s)", _auto.airSpeedCap, 2f, 20f);
            _auto.launchShape = F("발구름 가속 지수", _auto.launchShape, 1f, 3f);
            _auto.curveBias = F("대각선 휘어짐", _auto.curveBias, 0f, 0.8f);
            Info(FlightInfo(4f) + "  ·  " + FlightInfo(8f));

            Info("<b>가장자리</b>");
            _auto.edgeProbeAhead = F("낙차 검사 거리(m)", _auto.edgeProbeAhead, 0.1f, 1.2f);
            _auto.safeDrop = F("틈 인정 낙차(m)", _auto.safeDrop, 0.2f, 2f);
            _auto.inputBuffer = F("입력 버퍼(초)", _auto.inputBuffer, 0f, 0.5f);
            _auto.minSpeed = F("발동 최소 속도", _auto.minSpeed, 0.1f, 4f);
            _auto.cooldown = F("쿨다운(초)", _auto.cooldown, 0f, 0.6f);
        }

        void DrawClimb()
        {
            if (_auto == null || !Section("잡고 올라가기", ref _secClimb)) return;

            _auto.armLength = F("팔 길이(m)", _auto.armLength, 0.3f, 0.9f);
            _auto.pullDurationMin = F("당김 시간 최소(초)", _auto.pullDurationMin, 0.1f, 0.8f);
            _auto.pullDurationMax = F("당김 시간 최대(초)", _auto.pullDurationMax, 0.1f, 1.2f);
            _auto.overDuration = F("넘김 시간(초)", _auto.overDuration, 0.05f, 0.5f);
            _auto.swayFrequency = F("좌우 교차 빈도(Hz)", _auto.swayFrequency, 0f, 8f);
            _auto.swayAmplitude = F("좌우 교차 진폭(도)", _auto.swayAmplitude, 0f, 10f);
            Info($"당김 {_auto.pullDurationMax:0.00}초 → 교차 {_auto.pullDurationMax * _auto.swayFrequency:0.0}회");

            Info("<b>종료 관성</b>");
            _auto.exitBoost = F("전방 임펄스(m/s)", _auto.exitBoost, 0f, 12f);
            _auto.exitBoostDuration = F("임펄스 지속(초)", _auto.exitBoostDuration, 0f, 0.4f);
            Info($"밀리는 거리 ≈ {_auto.exitBoost * _auto.exitBoostDuration * 0.5f:0.00} m");
            _auto.detectRadius = F("걸어서 오르기 반경(m)", _auto.detectRadius, 0.5f, 3f);
            _auto.minHeight = F("최소 등반 높이(m)", _auto.minHeight, 0.1f, 1f);

            _auto.logDecisions = GUILayout.Toggle(_auto.logDecisions, " 판정 로그");
            _auto.drawGizmos = GUILayout.Toggle(_auto.drawGizmos, " 기즈모");
        }

        void DrawFeel()
        {
            if (_feel == null || !Section("화면 연출", ref _secFeel)) return;

            Info("<b>발구름</b>");
            _feel.launchDipPerMeter = F("높이 1m당 침하(m)", _feel.launchDipPerMeter, 0f, 0.2f);
            _feel.launchDipMax = F("침하 상한(m)", _feel.launchDipMax, 0f, 0.4f);
            _feel.launchDuration = F("지속(초)", _feel.launchDuration, 0.05f, 0.6f);

            Info("<b>착지</b> (강도 = 착지 순간 낙하 속도)");
            _feel.landDipPerSpeed = F("속도 1m/s당 침하(m)", _feel.landDipPerSpeed, 0f, 0.05f);
            _feel.landDipMax = F("침하 상한(m)", _feel.landDipMax, 0f, 0.5f);
            _feel.landMinSpeed = F("연출 시작 속도(m/s)", _feel.landMinSpeed, 0f, 12f);
            _feel.landDuration = F("지속(초)", _feel.landDuration, 0.05f, 0.8f);

            Info("<b>잡고 오르기 안착</b> (높이 무관 고정)");
            _feel.settleDip = F("침하(m)", _feel.settleDip, 0f, 0.25f);
            _feel.settleDuration = F("지속(초)", _feel.settleDuration, 0.05f, 0.6f);

            Info("<b>VR 감쇠</b>");
            _feel.vrPositionScale = F("위치 배율", _feel.vrPositionScale, 0f, 1f);
            _feel.vrRollScale = F("롤 배율 (멀미 주의)", _feel.vrRollScale, 0f, 1f);
        }

        void DrawArm()
        {
            if (_rig == null || !Section("팔 (임시 캡슐)", ref _secArm)) return;
            _rig.shoulderWidth = F("어깨 폭(m)", _rig.shoulderWidth, 0.2f, 0.8f);
            _rig.shoulderDrop = F("머리→어깨(m)", _rig.shoulderDrop, 0.05f, 0.5f);
            _rig.armThickness = F("팔 두께(m)", _rig.armThickness, 0.01f, 0.15f);
        }

        string FlightInfo(float dist)
        {
            float rise = Mathf.Max(0.0001f, _auto.clearance);
            float ballistic = 2f * Mathf.Sqrt(2f * rise / _auto.flightGravity);
            float byDist = _auto.airSpeedCap > 0.01f ? dist / _auto.airSpeedCap : 0f;
            return $"{dist:0}m 도약 ≈ {Mathf.Max(ballistic, byDist):0.00}초";
        }

        // ── 저장/로드 ───────────────────────────────────────────────────

        void Save()
        {
            try
            {
                var s = new Snapshot
                {
                    move = JsonUtility.ToJson(_fpp.move),
                    auto = _auto != null ? JsonUtility.ToJson(_auto) : null,
                    feel = _feel != null ? JsonUtility.ToJson(_feel) : null,
                    rig = _rig != null ? JsonUtility.ToJson(_rig) : null,
                };
                File.WriteAllText(FilePath, JsonUtility.ToJson(s, true));
                Debug.Log("[MoveTuning] 저장: " + FilePath);
            }
            catch (System.Exception e) { Debug.LogWarning("[MoveTuning] 저장 실패: " + e.Message); }
        }

        void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var s = JsonUtility.FromJson<Snapshot>(File.ReadAllText(FilePath));
                if (s == null) return;
                ApplyJson(s.move, s.auto, s.feel, s.rig);
                Debug.Log("[MoveTuning] 로드: " + FilePath);
            }
            catch (System.Exception e) { Debug.LogWarning("[MoveTuning] 로드 실패: " + e.Message); }
        }

        void ResetAll() => ApplyJson(_defMove, _defAuto, _defFeel, _defRig);

        void ApplyJson(string move, string auto, string feel, string rig)
        {
            if (!string.IsNullOrEmpty(move)) JsonUtility.FromJsonOverwrite(move, _fpp.move);
            if (_auto != null && !string.IsNullOrEmpty(auto)) JsonUtility.FromJsonOverwrite(auto, _auto);
            if (_feel != null && !string.IsNullOrEmpty(feel)) JsonUtility.FromJsonOverwrite(feel, _feel);
            if (_rig != null && !string.IsNullOrEmpty(rig)) JsonUtility.FromJsonOverwrite(rig, _rig);
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
