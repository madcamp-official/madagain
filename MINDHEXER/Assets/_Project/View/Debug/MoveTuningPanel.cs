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

            _auto.coneAngle = F("도약 원뿔 반각(도)", _auto.coneAngle, 5f, 70f);
            Info("원뿔은 <b>필터</b>일 뿐 — 통과한 것 중 <b>가장 가까운</b> 목표로 뛴다\n" +
                 "축은 <b>이동 방향 하나</b> — 시선은 관여 안 함(뒤·옆 낙하 보호가 뚫려서)");
            _auto.jumpSearchRadius = F("검색 반경(m)", _auto.jumpSearchRadius, 2f, 20f);
            _auto.maxDirectUp = F("직행 도약 최대 높이(m)", _auto.maxDirectUp, 0.3f, 2f);
            _auto.maxMantleUp = F("잡고 오르기 최대 높이(m)", _auto.maxMantleUp, 0.5f, 3.5f);
            _auto.maxDropTarget = F("허용 최대 낙차(m)", _auto.maxDropTarget, 1f, 20f);

            Info("<b>궤적</b>");
            _auto.clearance = F("모서리 위 여유(m)", _auto.clearance, 0.05f, 1.2f);
            _auto.airSpeedCap = F("이동 속도 상한(m/s)", _auto.airSpeedCap, 2f, 20f);
            Info("비행 시간 = 실제 이동거리 / 이 속도 (중력은 시간에서 역산)");
            _auto.minFlightTime = F("비행 시간 하한(초)", _auto.minFlightTime, 0.05f, 1f);
            _auto.maxFlightTime = F("비행 시간 상한(초)", _auto.maxFlightTime, 0.2f, 2f);
            Info("가속·감속은 <b>탄도가 알아서</b> 한다(이징 없음) — 세기는 중력·시간으로 조절");
            _auto.curveBias = F("대각선 휘어짐", _auto.curveBias, 0f, 0.8f);
            Info(FlightInfo(4f) + "  ·  " + FlightInfo(8f));
            _auto.pathSamples = Mathf.RoundToInt(F("경로 검사 샘플", _auto.pathSamples, 0f, 16f));
            Info(_auto.pathSamples > 0
                ? "궤적 중간이 막히면 다음 후보로 넘어간다"
                : "<b>경로 검사 꺼짐</b> — 지형을 뚫고 지나갈 수 있다");

            Info("<b>가장자리</b>");
            _auto.edgeProbeAhead = F("낙차 검사 거리(m)", _auto.edgeProbeAhead, 0.1f, 1.2f);
            _auto.safeDrop = F("틈 인정 낙차(m)", _auto.safeDrop, 0.2f, 2f);
            _auto.maxSafeFall = F("그냥 떨어지는 낙차 상한(m)", _auto.maxSafeFall, 0.5f, 12f);
            Info($"≤{_auto.safeDrop:0.0}m 걷기 · ≤{_auto.maxSafeFall:0.0}m 자유 낙하 · 그보다 깊거나 무저갱이면 정지");
            _auto.inputBuffer = F("입력 버퍼(초)", _auto.inputBuffer, 0f, 0.5f);
            _auto.minSpeed = F("발동 최소 속도", _auto.minSpeed, 0.1f, 4f);
            _auto.feelMinTravel = F("연출 최소 이동(m)", _auto.feelMinTravel, 0f, 3f);
            Info("이보다 짧은 도약엔 화면 연출을 넣지 않는다(낮은 턱 흔들림 방지)");
            _auto.cooldown = F("쿨다운(초)", _auto.cooldown, 0f, 0.6f);
        }

        void DrawClimb()
        {
            if (_auto == null || !Section("잡고 올라가기", ref _secClimb)) return;

            _auto.armLength = F("팔 길이(m)", _auto.armLength, 0.3f, 0.9f);
            _auto.directLatchRange = F("바로 잡기 거리(m)", _auto.directLatchRange, 0f, 2.5f);
            Info("이 거리 안이면 도약 없이 선 자리에서 잡는다 (0 = 항상 도약)");
            _auto.pullDurationMin = F("당김 시간 최소(초)", _auto.pullDurationMin, 0.1f, 0.8f);
            _auto.pullDurationMax = F("당김 시간 최대(초)", _auto.pullDurationMax, 0.1f, 1.2f);
            _auto.overDuration = F("넘김 시간(초)", _auto.overDuration, 0.05f, 0.8f);
            Info($"넘김 구간 상승 ≈ {_auto.armLength + 0.35f:0.00}m — 짧으면 확 튄다");
            _auto.swayFrequency = F("좌우 교차 빈도(Hz)", _auto.swayFrequency, 0f, 8f);
            _auto.swayAmplitude = F("좌우 교차 진폭(도)", _auto.swayAmplitude, 0f, 10f);
            Info($"당김 {_auto.pullDurationMax:0.00}초 → 교차 {_auto.pullDurationMax * _auto.swayFrequency:0.0}회");

            Info("<b>종료 관성</b>");
            _auto.exitBoost = F("전방 임펄스(m/s)", _auto.exitBoost, 0f, 12f);
            _auto.exitBoostDuration = F("임펄스 지속(초)", _auto.exitBoostDuration, 0f, 0.4f);
            Info($"밀리는 거리 ≈ {_auto.exitBoost * _auto.exitBoostDuration * 0.5f:0.00} m");
            _auto.detectRadius = F("걸어서 오르기 반경(m)", _auto.detectRadius, 0.5f, 3f);
            _auto.minHeight = F("최소 등반 높이(m)", _auto.minHeight, 0.1f, 1f);
            _auto.walkUpConeAngle = F("전방 판정 반각(도)", _auto.walkUpConeAngle, 20f, 120f);
            _auto.lowStepHeight = F("낮은 단차 기준(m)", _auto.lowStepHeight, 0f, 1.5f);
            Info("이 높이 이하는 방향 판정 없이 넘어간다(쳐다보지 않아도, 옆·뒤로도)");
            Info("stepOffset 0.3m — 최소 등반 높이는 그보다 커야 낮은 턱에서 안 뛴다");

            _auto.logDecisions = GUILayout.Toggle(_auto.logDecisions, " 판정 로그");
            _auto.drawGizmos = GUILayout.Toggle(_auto.drawGizmos, " 기즈모");
        }

        void DrawFeel()
        {
            if (_feel == null || !Section("화면 연출", ref _secFeel)) return;

            Info("<b>발구름 — 침하(아래)</b>");
            _feel.launchDipPerMeter = F("높이 1m당 침하(m)", _feel.launchDipPerMeter, 0f, 0.2f);
            _feel.launchDipMax = F("침하 상한(m)", _feel.launchDipMax, 0f, 0.4f);
            _feel.launchDuration = F("지속(초)", _feel.launchDuration, 0.05f, 0.6f);

            Info("<b>발구름 — 업킥(위로 '탁')</b>");
            _feel.launchKickBase = F("기본 킥(m)", _feel.launchKickBase, 0f, 0.3f);
            _feel.launchKickPerMeter = F("높이 1m당 킥(m)", _feel.launchKickPerMeter, 0f, 0.3f);
            _feel.launchKickMax = F("킥 상한(m)", _feel.launchKickMax, 0f, 0.4f);
            _feel.launchKickDuration = F("지속(초)", _feel.launchKickDuration, 0.05f, 0.5f);

            Info("<b>착지 — 침하(아래)</b> (강도 = 착지 순간 낙하 속도)");
            _feel.landDipPerSpeed = F("속도 1m/s당 침하(m)", _feel.landDipPerSpeed, 0f, 0.05f);
            _feel.landDipMax = F("침하 상한(m)", _feel.landDipMax, 0f, 0.5f);
            _feel.landMinSpeed = F("연출 시작 속도(m/s)", _feel.landMinSpeed, 0f, 12f);
            _feel.landDuration = F("지속(초)", _feel.landDuration, 0.05f, 0.8f);

            Info("<b>착지 — 업킥(위로 '탁')</b>");
            _feel.landKickPerSpeed = F("속도 1m/s당 킥(m)", _feel.landKickPerSpeed, 0f, 0.05f);
            _feel.landKickMax = F("킥 상한(m)", _feel.landKickMax, 0f, 0.5f);
            _feel.landKickDuration = F("지속(초)", _feel.landKickDuration, 0.05f, 0.6f);
            Info("킥을 침하보다 <b>짧게</b> 둬야 '탁 튀었다 가라앉는' 순서로 읽힌다");

            Info("<b>잡고 오르기 안착</b> (높이 무관 고정)");
            _feel.settleDip = F("침하(m)", _feel.settleDip, 0f, 0.25f);
            _feel.settleDuration = F("지속(초)", _feel.settleDuration, 0.05f, 0.6f);

            Info("<b>롤 킥 — 도약·착지 좌우 '파박'</b>");
            _feel.launchRollDeg = F("도약 진폭(도)", _feel.launchRollDeg, 0f, 12f);
            _feel.launchRollDuration = F("도약 지속(초)", _feel.launchRollDuration, 0.05f, 0.8f);
            _feel.launchRollCycles = F("도약 왕복 수", _feel.launchRollCycles, 0.5f, 4f);
            _feel.landRollDeg = F("착지 진폭(도)", _feel.landRollDeg, 0f, 12f);
            _feel.landRollDuration = F("착지 지속(초)", _feel.landRollDuration, 0.05f, 0.8f);
            _feel.landRollCycles = F("착지 왕복 수", _feel.landRollCycles, 0.5f, 4f);
            Info("방향은 매번 반대쪽부터 — 같은 쪽만 기울면 금방 티가 난다");

            Info("<b>실려가기 — 지하철 스웨이</b> (레일·피스톤·프레서 등 강제 이동 전부 공통)");
            _feel.carryRollGain = F("좌우 버티는 각(도/m·s⁻¹)", _feel.carryRollGain, 0f, 6f);
            _feel.carryRollMax = F("롤 상한(도)", _feel.carryRollMax, 0f, 20f);
            _feel.carryFovGain = F("전후 FOV 변화(도/m·s⁻¹)", _feel.carryFovGain, 0f, 4f);
            _feel.carryFovMax = F("FOV 변화 상한(도)", _feel.carryFovMax, 0f, 25f);
            _feel.carryFrequency = F("반응성(완만한 승차)", _feel.carryFrequency, 2f, 20f);
            _feel.carryDamping = F("댐핑(1 미만 = 정지 시 반대로 넘어갔다 흔들림)", _feel.carryDamping, 0.1f, 1f);
            _feel.carrySmoothTime = F("입력 스무딩(초)", _feel.carrySmoothTime, 0f, 0.3f);
            Info("물리 충돌처럼 델타가 들쭉날쭉할 때 떨림 방지. 0=끔");
            _feel.carryImpactSpeed = F("충돌 판정 속도(m/s)", _feel.carryImpactSpeed, 1f, 15f);
            _feel.carryImpactFrequency = F("충돌 시 반응성", _feel.carryImpactFrequency, 2f, 30f);
            Info("이 속도 넘으면 반응성을 올려 짧게 홀드했다 빨리 정착");
            Info("0 = 끔. RailPlatform.Carry() 한 곳에서만 발화 — 소스 안 가림");

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

        /// <summary>평지(rise=fall=clearance) 기준 파생값 — 실제 중력은 시간에서 역산된다.</summary>
        string FlightInfo(float dist)
        {
            float rise = Mathf.Max(0.0001f, _auto.clearance);
            float k = 2f * Mathf.Sqrt(2f * rise);                       // total = k/√g
            float byTravel = _auto.airSpeedCap > 0.01f ? dist / _auto.airSpeedCap : 0f;

            float lo = Mathf.Max(0.05f, _auto.minFlightTime);
            float hi = Mathf.Max(lo, _auto.maxFlightTime);
            float total = Mathf.Clamp(byTravel, lo, hi);
            float g = (k / total) * (k / total);
            return $"{dist:0}m ≈ {total:0.00}초 (g {g:0.0}, 상승·하강 {total * 0.5f:0.00}초씩)";
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
