using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.View
{
    /// <summary>
    /// F4 — 절차 애니메이션 튜닝 패널. ViewmodelMotion의 레이어별 on/off와 수치를 실시간 조절한다.
    /// (F1 = 전투, F2 = 포즈 재생, F3 = 시퀀스 구간, F4 = 절차 모션)
    /// </summary>
    public class MotionTunePanel : MonoBehaviour
    {
        bool open;
        Vector2 scroll;
        int tab;
        static readonly string[] Tabs = { "숨/달리기", "기울임/공중", "착지/피격/시선", "화면 구도" };

        /// <summary>패널이 열려 있는가(열려 있으면 Main이 플레이어 입력을 막는다).</summary>
        public static bool AnyOpen;

        void OnDisable() { AnyOpen = false; }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.f4Key.wasPressedThisFrame)
            {
                open = !open;
                AnyOpen = open;
                Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = open;
            }
        }

        void OnGUI()
        {
            if (!open) return;
            var m = ViewmodelMotion.Instance;
            const float W = 400f;
            float H = Mathf.Min(Screen.height - 24f, 520f);
            GUILayout.BeginArea(new Rect(Screen.width - W - 12f, 12f, W, H), GUI.skin.box);
            GUILayout.Label("<b>절차 애니메이션 튜닝 (F4)</b>", Rich());

            if (m == null) { GUILayout.Label("ViewmodelMotion 없음"); GUILayout.EndArea(); return; }

            // ── 실시간 진단: 레이어가 정말 도는지 확인 ──
            bool posing = PosePlayer.Instance != null && PosePlayer.Instance.IsPlaying;
            var fp = FingerPoser.Instance;
            string fpState = fp == null ? "<color=#f66>없음</color>"
                           : fp.enabled ? "켜짐" : "<color=#f66>꺼짐(손가락 안 움직임)</color>";
            int ikOn = 0, ikTotal = 0;
            foreach (var ik in Object.FindObjectsByType<HandIK>(FindObjectsSortMode.None))
            { ikTotal++; if (ik.enabled && ik.weight > 0.01f) ikOn++; }
            string katState = !m.driveKatana ? "루트 구동"
                            : m.HasKatana ? "<b>칼 구동</b>" : "<color=#f66>Katana 못 찾음</color>";
            GUILayout.Label(
                $"<size=11>속도 {m.Speed:0.0}m/s" + (m.Speed >= m.speedCap - 0.05f ? "<color=#fc6>(상한)</color>" : "") +
                $" → 이동강도 <b>{m.MoveAmt:0.00}</b>" +
                (m.DashAmt > 0.01f ? $"  대시 <b>{m.DashAmt:0.00}</b>" : "") +
                $"\n오프셋 {m.LastPos.x:+0.000;-0.000}/{m.LastPos.y:+0.000;-0.000}/{m.LastPos.z:+0.000;-0.000}" +
                $"  회전 {m.LastRot.z:+0.0;-0.0}°\n" +
                $"{katState}  ·  IK {ikOn}/{ikTotal}" +
                (ikTotal > 0 && ikOn == 0 ? " <color=#f66>(꺼짐 — 팔이 안 따라옴)</color>" : "") +
                $"  ·  손가락 {fpState}" +
                (posing ? "\n<color=#fc6>포즈 재생 중 — 절차 모션 정지</color>" : "") + "</size>", Rich());
            m.driveKatana = GUILayout.Toggle(m.driveKatana, " 칼만 움직이고 팔은 IK로 따라오게 (권장)");

            // 레이어 on/off
            GUILayout.BeginHorizontal();
            m.enableBreathe = GUILayout.Toggle(m.enableBreathe, "숨", GUI.skin.button);
            m.enableBob     = GUILayout.Toggle(m.enableBob,     "달리기", GUI.skin.button);
            m.enableStrafe  = GUILayout.Toggle(m.enableStrafe,  "기울임", GUI.skin.button);
            m.enableAir     = GUILayout.Toggle(m.enableAir,     "공중", GUI.skin.button);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            m.enableLand = GUILayout.Toggle(m.enableLand, "착지", GUI.skin.button);
            m.enableHit  = GUILayout.Toggle(m.enableHit,  "피격", GUI.skin.button);
            m.enableSway = GUILayout.Toggle(m.enableSway, "시선지연", GUI.skin.button);
            GUILayout.EndHorizontal();

            tab = GUILayout.Toolbar(tab, Tabs);
            scroll = GUILayout.BeginScrollView(scroll);

            if (tab == 0)
            {
                GUILayout.Label("<b>숨쉬기 (HP 낮을수록 크고 빠르게)</b>", Rich());
                m.breatheAmpFull = FS("정상 진폭(m)",   m.breatheAmpFull, 0f, 0.02f);
                m.breatheAmpLow  = FS("빈사 진폭(m)",   m.breatheAmpLow,  0f, 0.06f);
                m.breatheSpdFull = FS("정상 속도",      m.breatheSpdFull, 0.5f, 5f);
                m.breatheSpdLow  = FS("빈사 속도",      m.breatheSpdLow,  1f, 12f);
                m.breathePitch   = FS("가슴 피치(도)",  m.breathePitch,   0f, 2f);
                m.hpOverride     = FS("HP 강제(-1=실제)", m.hpOverride,  -1f, 1f);

                GUILayout.Space(4f);
                GUILayout.Label("<b>달리기 (W 이동)</b>", Rich());
                m.runDrop  = FS("손 하강량(m)",        m.runDrop,  0f, 0.20f);
                m.backLift = FS("후진 순 상승량(m)",   m.backLift, 0f, 0.15f);
                GUILayout.Label("<size=10>후진 상승은 하강량과 독립 — 기준보다 딱 이만큼 올라갑니다</size>", Rich());
                m.bobVert  = FS("상하 흔들림(m)",    m.bobVert,  0f, 0.08f);
                m.bobHoriz = FS("좌우 흔들림(m)",    m.bobHoriz, 0f, 0.10f);
                m.bobRoll  = FS("터벅 기울임(도)",   m.bobRoll,  0f, 6f);
                m.bobRate  = FS("걸음 주기",         m.bobRate,  0.2f, 2.5f);
                m.bobAirFade = FS("공중 흔들림 감쇠", m.bobAirFade, 1f, 25f);
                GUILayout.Label("<size=10>공중에선 발걸음이 없으므로 흔들림이 꺼집니다(하강은 유지)</size>", Rich());
                m.moveEnterSpeed = FS("진입/이탈 속도", m.moveEnterSpeed, 1f, 20f);
                m.refSpeed = FS("기준 속도(m/s)",    m.refSpeed, 2f, 20f);
                m.speedCap = FS("속도 상한(m/s)",    m.speedCap, 3f, 25f);
                GUILayout.Label("<size=10>상한 = 대시·블링크가 '초고속 달리기'로 읽히지 않게 자르는 값</size>", Rich());
            }
            else if (tab == 1)
            {
                GUILayout.Label("<b>이동방향 기울임</b>", Rich());
                m.strafeRoll   = FS("좌우 롤(도)",     m.strafeRoll,   0f, 8f);
                m.strafeShift  = FS("좌우 밀림(m)",    m.strafeShift,  0f, 0.08f);
                m.strafePush   = FS("전후 밀림(m)",    m.strafePush,   0f, 0.08f);
                m.strafeSpring = FS("따라오는 속도",   m.strafeSpring, 1f, 20f);

                GUILayout.Space(4f);
                GUILayout.Label("<b>공중 관성 (점프/낙하)</b>", Rich());
                m.airFactor = FS("처짐 계수",      m.airFactor, 0f, 0.02f);
                m.airMax    = FS("최대 처짐(m)",   m.airMax,    0f, 0.2f);
                m.airSpring = FS("따라오는 속도",  m.airSpring, 1f, 25f);

                GUILayout.Space(4f);
                GUILayout.Label("<b>대시</b>", Rich());
                m.enableDash    = GUILayout.Toggle(m.enableDash, " 대시 연동");
                m.dashDrop      = FS("하강량(m)",       m.dashDrop,      0f, 0.15f);
                m.dashPull      = FS("뒤로 당김(m)",    m.dashPull,      0f, 0.15f);
                m.dashRoll      = FS("방향 기울임(도)", m.dashRoll,      0f, 15f);
                m.dashSpring    = FS("진입/이탈 속도",  m.dashSpring,    2f, 40f);
                m.dashGrip      = FS("손가락 쥠",       m.dashGrip,      0f, 1f);
                m.dashGripPulse = FS("시작 순간 움켜쥠", m.dashGripPulse, 0f, 1f);
            }
            else if (tab == 2)
            {
                GUILayout.Label("<b>착지 딥</b>", Rich());
                m.landKick      = FS("킥 세기",           m.landKick,      0f, 0.2f);
                m.landMinSpeed  = FS("최소 낙하속도",     m.landMinSpeed,  0f, 10f);
                m.landGripPulse = FS("손가락 쥠",         m.landGripPulse, 0f, 1f);
                if (GUILayout.Button("착지 테스트")) m.KickLand(10f);

                GUILayout.Space(4f);
                GUILayout.Label("<b>손가락 (달리기·공중)</b>", Rich());
                m.enableFingers  = GUILayout.Toggle(m.enableFingers, " 손가락 연동");
                m.runGrip        = FS("달릴 때 쥠",       m.runGrip,        0f, 1f);
                m.airGrip        = FS("공중에서 쥠",      m.airGrip,        0f, 1f);
                m.jumpGripPulse  = FS("점프 순간 움켜쥠", m.jumpGripPulse,  0f, 1f);
                var fpc = FingerPoser.Instance;
                if (fpc != null)
                {
                    fpc.sustainSpeed = FS("지속 그립 반응", fpc.sustainSpeed, 1f, 20f);
                    GUILayout.Label($"<size=10>현재 지속 {fpc.sustainGrip:0.00} / 순간 {fpc.gripSpring:+0.00;-0.00}</size>", Rich());
                }

                GUILayout.Space(4f);
                GUILayout.Label("<b>피격 휘청</b>", Rich());
                m.hitPosKick   = FS("위치 킥",     m.hitPosKick,   0f, 0.2f);
                m.hitRotKick   = FS("회전 킥",     m.hitRotKick,   0f, 40f);
                m.hitGripPulse = FS("손가락 쥠",   m.hitGripPulse, 0f, 1f);
                if (GUILayout.Button("피격 테스트")) m.KickHit();

                GUILayout.Space(4f);
                GUILayout.Label("<b>시선 지연</b>", Rich());
                m.swayFactor = FS("끌림 계수",     m.swayFactor, 0f, 1f);
                m.swayMax    = FS("최대 끌림(도)", m.swayMax,    0f, 15f);
                m.swayShift  = FS("위치 밀림",     m.swayShift,  0f, 0.01f);
                m.swaySpring = FS("복귀 속도",     m.swaySpring, 1f, 30f);

                GUILayout.Space(4f);
                GUILayout.Label("<b>충격 스프링 (착지·피격 공용)</b>", Rich());
                m.posStiff = FS("위치 강성", m.posStiff, 20f, 400f);
                m.posDamp  = FS("위치 감쇠", m.posDamp,  2f,  50f);
                m.rotStiff = FS("회전 강성", m.rotStiff, 20f, 500f);
                m.rotDamp  = FS("회전 감쇠", m.rotDamp,  2f,  60f);
            }
            else
            {
                // 절차 오프셋이 얹히는 "기준" 배치 — 1인칭 화면에서 손·칼이 보이는 위치
                GUILayout.Label("<b>기준 배치 (절차 오프셋의 기준점)</b>", Rich());
                Vector3 b = m.BasePos;
                b.x = FS("X (좌우)",   b.x, -1.5f, 1.5f);
                b.y = FS("Y (상하)",   b.y, -3f,   1f);
                b.z = FS("Z (앞뒤)",   b.z, -1f,   1.5f);
                m.SetBase(b);
                GUILayout.Label("<size=10>Game 뷰를 보며 손·칼이 화면 우하단에 오도록 맞추십시오.</size>", Rich());
                GUILayout.Space(4f);
                if (GUILayout.Button("현재 루트 위치를 기준으로 재캡처", GUILayout.Height(24f))) m.RecaptureBase();
                GUILayout.Label($"<size=10>현재 기준: {m.BasePos:F3}\n확정되면 이 값을 씬의 KatanaViewmodel Transform에 적어두십시오(Play 종료 시 초기화됨).</size>", Rich());
            }

            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("전부 리셋(오프셋 0)", GUILayout.Height(24f))) m.ResetAll();
            if (GUILayout.Button("손가락 쥠 테스트", GUILayout.Height(24f)))
            {
                var f = FingerPoser.Instance;
                if (f == null) Debug.Log("[F4] FingerPoser 없음 — 손 뼈에 붙이십시오.");
                else if (!f.enabled) Debug.Log("[F4] FingerPoser가 꺼져 있습니다. 콘솔에서 play release 를 실행하십시오.");
                else f.PulseGrip(0.8f);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        static float FS(string label, float v, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {v:0.000}", GUILayout.Width(180f));
            float nv = GUILayout.HorizontalSlider(v, min, max);
            GUILayout.EndHorizontal();
            return nv;
        }

        static GUIStyle Rich() => new GUIStyle(GUI.skin.label) { richText = true };
    }

    /// <summary>Play 시 자동 부착.</summary>
    public static class MotionTunePanelBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<MotionTunePanel>() == null)
                new GameObject("[MotionTunePanel]").AddComponent<MotionTunePanel>();
        }
    }
}
