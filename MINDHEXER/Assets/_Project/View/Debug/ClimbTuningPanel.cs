using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.View
{
    /// <summary>
    /// 등반 손 조정 패널 (F7).
    ///
    /// <para><b>왜 별도 패널인가</b> — 등반은 <b>0.26초짜리 순간</b>이고, 실제 턱을 찾아가야 발동한다.
    /// 지나가는 자세는 슬라이더로 맞출 수 없다. 그래서 이 패널의 핵심은 값이 아니라 <b>세 가지 장치</b>다.</para>
    ///
    /// <list type="number">
    /// <item><b>가상 모서리</b> — 카메라 앞에 가짜 턱을 만들어 어디서든 등반을 불러온다.</item>
    /// <item><b>단계 고정</b> — 시간을 멈춰 그 자세를 계속 화면에 띄운다.</item>
    /// <item><b>한 번 재생</b> — 멈춰서 맞춘 뒤 실제 속도로 흘려 확인한다. 정지 화면만 예쁘고
    ///       움직임은 여전히 이상한 경우를 잡는다.</item>
    /// </list>
    ///
    /// <para>저장은 <b>별도 파일</b>이다(<c>climb_tuning.json</c>). 평상시 값(F6)과 섞으면
    /// 한쪽을 맞출 때 다른 쪽이 딸려 온다.</para>
    ///
    /// <para>키는 F7 — F1 이동 / F2 조명 / F3 포즈 / F4 포즈시퀀스 / F5 글리치 / F6 손·거미.</para>
    /// </summary>
    public class ClimbTuningPanel : MonoBehaviour
    {
        const float PanelWidth = 400f;

        bool _open;
        Vector2 _scroll;
        CursorLockMode _prevLock;
        FirstPersonPlayer _fpp;
        bool _prevLookFrozen;

        MantleRig _rig;

        // 가상 모서리
        float _ledgeDist = 1.1f, _ledgeHeight = 1.6f, _ledgeWidth = 0.7f;
        int _phaseIndex = 3;      // 기본은 Holding — 가장 오래 보이는 자세
        float _phaseT = 1f;

        static readonly MantleRig.Phase[] Phases =
        {
            MantleRig.Phase.Idle, MantleRig.Phase.Lowering, MantleRig.Phase.Reaching,
            MantleRig.Phase.Holding, MantleRig.Phase.Releasing, MantleRig.Phase.Raising
        };
        static readonly string[] PhaseNames = { "평상시", "내려감", "뻗어잡음", "잡고있음", "놓음", "올라옴" };

        public static string FilePath => "Assets/_Project/Poses/climb_tuning.json";

        [System.Serializable]
        class Saved
        {
            public Vector3 parkLocalR, parkLocalL, parkEulerR, parkEulerL;
            public Vector3 climbElbowLocal;
            public Vector3 handEulerR, handEulerL;
            public bool hasHandEuler;
            public float palmForwardOffset, palmUpOffset;
            public float climbWristFlexDeg, climbWristRollDeg;
            public float lowerTime, reachTime, releaseTime, raiseTime, fastEntryTime;
            public float airParkAmount, airParkSmooth, prepareWaitMax, airParkDelay, airParkVelocity;
            public bool captured;
        }

        void Start() { Find(); Load(); }

        void Find() { if (_rig == null) _rig = FindFirstObjectByType<MantleRig>(); if (_fpp == null) _fpp = FindFirstObjectByType<FirstPersonPlayer>(); }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb.f7Key.wasPressedThisFrame) return;

            _open = !_open;
            if (_open)
            {
                Find();
                _prevLock = Cursor.lockState;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                if (_fpp != null) { _prevLookFrozen = _fpp.LookFrozen; _fpp.LookFrozen = true; }
            }
            else
            {
                Cursor.lockState = _prevLock;
                Cursor.visible = _prevLock != CursorLockMode.Locked;
                if (_fpp != null) _fpp.LookFrozen = _prevLookFrozen;
                // ★ 패널을 닫으면 반드시 고정을 푼다. 켜 둔 채 닫으면 등반이 영영 안 끝나
                //    "등반 버그"로 오인하게 된다.
                if (_rig != null) _rig.debugFreeze = false;
            }
        }

        void OnGUI()
        {
            if (!_open) return;

            GUILayout.BeginArea(new Rect(12f, 12f, PanelWidth, Screen.height - 24f), GUI.skin.box);
            GUILayout.Label("<b>등반 손 조정 (F7)</b>", Rich());

            if (_rig == null)
            {
                Info("<b>MantleRig이 없습니다.</b> 손 리그는 <b>ViewmodelStudio</b> 씬에만 있습니다.");
                if (GUILayout.Button("대상 다시 찾기")) Find();
                GUILayout.EndArea();
                return;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("저장")) Save();
            if (GUILayout.Button("불러오기")) Load();
            if (GUILayout.Button("대상 다시 찾기")) Find();
            GUILayout.EndHorizontal();

            Info("현재 단계: <b>" + _rig.DebugPhaseName + "</b>" + (_rig.DebugHasAnchors ? " (모서리 있음)" : " (모서리 없음)"));

            _scroll = GUILayout.BeginScrollView(_scroll);

            // ── ① 가상 모서리 ──────────────────────────────────────────────
            GUILayout.Label("<b>① 가상 모서리 — 실제 턱을 안 찾아가도 된다</b>", Rich());
            _ledgeDist   = F("  거리(m)", _ledgeDist, 0.3f, 2.5f);
            _ledgeHeight = F("  높이(m)", _ledgeHeight, 0.5f, 2.5f);
            _ledgeWidth  = F("  손 간격(m)", _ledgeWidth, 0.2f, 1.5f);
            if (GUILayout.Button("모서리 만들고 등반 시작"))
            {
                _rig.debugFreeze = false;
                _rig.DebugFakeLedge(_ledgeDist, _ledgeHeight, _ledgeWidth);
            }

            // ── ② 단계 고정 ────────────────────────────────────────────────
            GUILayout.Label("<b>② 단계 고정 — 0.26초를 멈춰 세운다</b>", Rich());
            _rig.debugFreeze = GUILayout.Toggle(_rig.debugFreeze, " 시간 멈춤");
            GUILayout.BeginHorizontal();
            for (int i = 0; i < Phases.Length; i++)
                if (GUILayout.Toggle(_phaseIndex == i, PhaseNames[i], GUI.skin.button) && _phaseIndex != i)
                {
                    _phaseIndex = i;
                    _rig.debugFreeze = true;
                    _rig.DebugForcePhase(Phases[i], _phaseT);
                }
            GUILayout.EndHorizontal();
            float t = F("  단계 안 진행률", _phaseT, 0f, 1f);
            if (!Mathf.Approximately(t, _phaseT)) { _phaseT = t; _rig.DebugForcePhase(Phases[_phaseIndex], _phaseT); }
            Info("<b>내려감</b>에 멈춰 손이 화면 밖으로 나가는지, <b>올라옴</b>에 멈춰 평상시 자세로 " +
                 "정확히 돌아오는지 보십시오. 안 돌아오면 그게 곧 '등반 후 이상해짐'의 정체입니다.");

            // ── ③ 재생 ─────────────────────────────────────────────────────
            if (GUILayout.Button("③ 한 번 재생 (실제 속도)"))
            {
                _rig.debugFreeze = false;
                _rig.DebugFakeLedge(_ledgeDist, _ledgeHeight, _ledgeWidth);
            }

            // ── 값 ─────────────────────────────────────────────────────────
            GUILayout.Label("<b>손이 모서리에 얹히는 자리</b>", Rich());
            _rig.palmForwardOffset = F("  앞으로", _rig.palmForwardOffset, -0.2f, 0.2f);
            _rig.palmUpOffset      = F("  위로", _rig.palmUpOffset, -0.1f, 0.15f);

            GUILayout.Label("<b>팔꿈치 (모서리 기준 — 카메라 기준이 아니다)</b>", Rich());
            _rig.climbElbowLocal = V3("  위치", _rig.climbElbowLocal, -1f, 1f);
            Info("x=바깥쪽 · y=위 · z=모서리에서 몸 쪽. 모서리에 매달아야 고개를 돌려도 팔이 안 뒤틀립니다.");

            // ★ handEuler는 BuildBasis()에서만 쓰이므로 <b>등반 자세 전용</b>이다. 손바닥이 위를 보는
            //   문제가 정확히 이 값이고, 멈춰 놓고 봐야 잡히므로 F7에 둔다(F6과 같은 값을 공유).
            GUILayout.Label("<b>손 뼈 축 보정 — 손바닥이 어디를 보나 (등반 전용)</b>", Rich());
            _rig.handEulerR = V3("  R", _rig.handEulerR, -180f, 180f);
            _rig.handEulerL = V3("  L", _rig.handEulerL, -180f, 180f);
            Info("<b>잡고있음</b>에 멈춰 놓고 만지십시오. 리그마다 손 뼈가 보는 축이 달라 " +
                 "보정 없이는 손바닥이 하늘을 볼 수 있습니다. 지금 값은 R " + _rig.handEulerR + " 입니다.");

            GUILayout.Label("<b>손목 (잡는 순간의 반작용)</b>", Rich());
            _rig.climbWristFlexDeg = F("  꺾임°", _rig.climbWristFlexDeg, 0f, 40f);
            _rig.climbWristRollDeg = F("  롤°", _rig.climbWristRollDeg, 0f, 40f);
            Info("손이 <b>통째로</b> 뒤집혀 보이면 여기가 아니라 위의 축 보정입니다. 여긴 미세 조정용입니다.");

            GUILayout.Label("<b>대기 지점 park — 손이 내려가 사라지는 곳</b>", Rich());
            _rig.parkLocalR = V3("  R 위치", _rig.parkLocalR, -1f, 1f);
            _rig.parkEulerR = V3("  R 회전", _rig.parkEulerR, -180f, 180f);
            _rig.parkLocalL = V3("  L 위치", _rig.parkLocalL, -1f, 1f);
            _rig.parkEulerL = V3("  L 회전", _rig.parkEulerL, -180f, 180f);
            Info("<b>화면 밖</b>이어야 합니다. 여기서 시스템이 교대하므로, 보이는 자리면 손이 튀어 보입니다.");

            GUILayout.Label("<b>공중 파킹 — 점프·낙하 중 손 내리기</b>", Rich());
            _rig.airParkAmount   = F("  내리는 양", _rig.airParkAmount, 0f, 1f);
            _rig.airParkSmooth   = F("  내리고 올리는 시간", _rig.airParkSmooth, 0f, 0.6f);
            _rig.airParkDelay    = F("  체공 기준(초)", _rig.airParkDelay, 0f, 0.5f);
            _rig.prepareWaitMax  = F("  예고 대기 상한", _rig.prepareWaitMax, 0.1f, 3f);
            Info("판정 기준은 <b>체공시간</b>입니다. 낮은 단차는 체공이 짧아 안 내려가고, 낮아도 " +
                 "<b>멀리 뛰는 점프는 체공이 길어 내려갑니다</b>. 실제 체공 시간은 F6 진단 줄에 표시됩니다 — " +
                 "그 값을 보고 기준을 정하십시오. 대기 상한은 도약했다가 등반이 아니었을 때 스스로 복귀하는 시간입니다.");

            GUILayout.Label("<b>타이밍(초)</b>", Rich());
            _rig.lowerTime     = F("  내려감", _rig.lowerTime, 0.02f, 0.6f);
            _rig.reachTime     = F("  뻗어잡음", _rig.reachTime, 0.02f, 0.6f);
            _rig.releaseTime   = F("  놓음", _rig.releaseTime, 0.02f, 0.6f);
            _rig.raiseTime     = F("  올라옴", _rig.raiseTime, 0.02f, 0.6f);
            _rig.fastEntryTime = F("  압축 진입", _rig.fastEntryTime, 0.02f, 0.6f);
            Info("벽 앞에서 바로 잡는 경로는 여유가 구조적으로 0이라 <b>압축 진입</b> 시간으로 몰아 재생됩니다. " +
                 "\"내려갔다 올라오는 게 안 보인다\"의 첫 번째 후보입니다.");

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        public bool Save()
        {
            if (_rig == null) return false;
            var s = new Saved
            {
                parkLocalR = _rig.parkLocalR, parkLocalL = _rig.parkLocalL,
                parkEulerR = _rig.parkEulerR, parkEulerL = _rig.parkEulerL,
                climbElbowLocal = _rig.climbElbowLocal,
                handEulerR = _rig.handEulerR, handEulerL = _rig.handEulerL, hasHandEuler = true,
                palmForwardOffset = _rig.palmForwardOffset, palmUpOffset = _rig.palmUpOffset,
                climbWristFlexDeg = _rig.climbWristFlexDeg, climbWristRollDeg = _rig.climbWristRollDeg,
                lowerTime = _rig.lowerTime, reachTime = _rig.reachTime,
                releaseTime = _rig.releaseTime, raiseTime = _rig.raiseTime,
                fastEntryTime = _rig.fastEntryTime,
                airParkAmount = _rig.airParkAmount, airParkSmooth = _rig.airParkSmooth,
                airParkDelay = _rig.airParkDelay,
                prepareWaitMax = _rig.prepareWaitMax,
                captured = true
            };
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonUtility.ToJson(s, true), System.Text.Encoding.UTF8);
                Debug.Log("[F7] 저장 → " + FilePath);
                return true;
            }
            catch (System.Exception e) { Debug.LogWarning("[F7] 저장 실패: " + e.Message); return false; }
        }

        public bool Load()
        {
            if (_rig == null) return false;
            try
            {
                if (!File.Exists(FilePath)) return false;
                var s = JsonUtility.FromJson<Saved>(File.ReadAllText(FilePath, System.Text.Encoding.UTF8));
                if (s == null || !s.captured) return false;

                _rig.parkLocalR = s.parkLocalR; _rig.parkLocalL = s.parkLocalL;
                _rig.parkEulerR = s.parkEulerR; _rig.parkEulerL = s.parkEulerL;
                _rig.climbElbowLocal = s.climbElbowLocal;
                // F6도 같은 값을 저장한다. 여기서 안 담긴 옛 파일이 0으로 덮어쓰지 않게 막는다.
                if (s.hasHandEuler) { _rig.handEulerR = s.handEulerR; _rig.handEulerL = s.handEulerL; }
                _rig.palmForwardOffset = s.palmForwardOffset; _rig.palmUpOffset = s.palmUpOffset;
                _rig.climbWristFlexDeg = s.climbWristFlexDeg; _rig.climbWristRollDeg = s.climbWristRollDeg;
                // 시간이 0이면 손이 순간이동한다 — 저장된 적 없는 파일을 그대로 먹지 않게 막는다.
                if (s.lowerTime > 0.001f)
                {
                    _rig.lowerTime = s.lowerTime; _rig.reachTime = s.reachTime;
                    _rig.releaseTime = s.releaseTime; _rig.raiseTime = s.raiseTime;
                    _rig.fastEntryTime = s.fastEntryTime;
                }
                // 대기 상한이 0인 파일은 저장된 적 없는 것이다 — 0을 먹으면 예고가 즉시 취소된다.
                if (s.prepareWaitMax > 0.01f)
                {
                    _rig.airParkAmount = s.airParkAmount; _rig.airParkSmooth = s.airParkSmooth;
                    _rig.prepareWaitMax = s.prepareWaitMax; _rig.airParkDelay = s.airParkDelay;
                }
                Debug.Log("[F7] 불러옴");
                return true;
            }
            catch (System.Exception e) { Debug.LogWarning("[F7] 불러오기 실패: " + e.Message); return false; }
        }

        // ── 위젯 ──────────────────────────────────────────────────────────

        static GUIStyle _rich;
        static GUIStyle Rich()
        {
            if (_rich == null) _rich = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
            return _rich;
        }

        static void Info(string s) => GUILayout.Label($"<size=11>{s}</size>", Rich());

        static float F(string label, float v, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, Rich(), GUILayout.Width(110f));
            v = GUILayout.HorizontalSlider(v, min, max);
            GUILayout.Label(v.ToString("0.###"), Rich(), GUILayout.Width(52f));
            GUILayout.EndHorizontal();
            return v;
        }

        static Vector3 V3(string label, Vector3 v, float min, float max)
        {
            GUILayout.Label(label, Rich());
            v.x = F("   x", v.x, min, max);
            v.y = F("   y", v.y, min, max);
            v.z = F("   z", v.z, min, max);
            return v;
        }
    }

    /// <summary>씬마다 손으로 배치하지 않도록 자동 부착한다(F6 패널과 같은 방식).</summary>
    public static class ClimbTuningPanelBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindAnyObjectByType<ClimbTuningPanel>() == null)
                new GameObject("[ClimbTuningPanel]").AddComponent<ClimbTuningPanel>();
        }
    }
}
