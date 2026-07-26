using UnityEngine;
using UnityEngine.InputSystem;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// F10 — 몹 밸런스 패널. AIConfig 수치를 플레이하며 직접 맞춘다.
    /// (F1=플레이어 전투 · F7=NavMesh · F10=몹 밸런스)
    ///
    /// ★ <b>예지가 도는 중에는 만지지 말 것.</b> 포크 도중에 규칙이 바뀌면 앞뒤 틱이 어긋난다.
    ///   패널이 열려 있는 동안은 DevPanels.AnyOpen으로 플레이어 입력이 막히므로 실수 방지는 된다.
    ///
    /// 틱 대신 <b>초</b>로 보여준다 — 밸런스는 초 단위로 감을 잡는 게 맞다(60틱 = 1초).
    /// </summary>
    public class MobBalancePanel : MonoBehaviour
    {
        bool open;
        int  tab;
        Vector2 scroll;
        static readonly string[] Tabs = { "근접", "돌진(핑키)", "원거리", "공중", "공통" };

        /// <summary>패널이 열려 있는가(열려 있으면 Main이 플레이어 입력을 막는다).</summary>
        public static bool AnyOpen;
        void OnDisable() { AnyOpen = false; }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.f10Key.wasPressedThisFrame)
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
            const float W = 430f;
            GUILayout.BeginArea(new Rect(Screen.width - W - 12f, 12f, W, Screen.height - 24f), GUI.skin.box);
            GUILayout.Label("<b>몹 밸런스 (F10)</b> — <color=#ff8080>예지 중 변경 금지</color>", Rich());

            tab = GUILayout.Toolbar(tab, Tabs);
            scroll = GUILayout.BeginScrollView(scroll);

            switch (tab)
            {
                case 0: Melee();  break;
                case 1: Charge(); break;
                case 2: Ranged(); break;
                case 3: Fly();    break;
                default: Common(); break;
            }

            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<b>저장</b>", Rich(), GUILayout.Height(26f))) MobBalanceSave.Save();
            if (GUILayout.Button("불러오기", GUILayout.Height(26f))) MobBalanceSave.Load();
            if (GUILayout.Button("기본값", GUILayout.Height(26f))) MobBalanceSave.ResetToDefaults();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ── 근접 ──
        void Melee()
        {
            GUILayout.Label("<b>근접 그런트</b>", Rich());
            GUILayout.Label("<size=10><color=#ffb060>★ 사거리 두 값은 아직 미확정 — 붙어서 맞아보며 잡아야 함</color></size>", Rich());
            AIConfig.MeleeReach    = F("팔 길이(m) ★미확정",  AIConfig.MeleeReach,    0.2f, 2f);
            AIConfig.MeleeHitExtra = F("판정 여유(m) ★미확정", AIConfig.MeleeHitExtra, 0f,   1f);
            GUILayout.Label($"<size=10>→ 실효 사거리 ≈ {AIConfig.MeleeRangeFor(SimConfig.EnemyRadius):0.00}m " +
                            $"(적반경 {SimConfig.EnemyRadius:0.00} + 플레이어 {SimConfig.PlayerRadius:0.00} + 팔)</size>", Rich());

            GUILayout.Space(4f);
            AIConfig.MeleeWindupTicks   = Sec("선딜(텔레그래프)", AIConfig.MeleeWindupTicks,   0.05f, 2f);
            AIConfig.MeleeActiveTicks   = Sec("판정",             AIConfig.MeleeActiveTicks,   0.02f, 0.6f);
            AIConfig.MeleeRecoveryTicks = Sec("후딜",             AIConfig.MeleeRecoveryTicks, 0.05f, 2.5f);
            Total("공격 1회 총 길이",
                  AIConfig.MeleeWindupTicks + AIConfig.MeleeActiveTicks + AIConfig.MeleeRecoveryTicks);

            GUILayout.Space(4f);
            AIConfig.MeleeHitHalfAngle = F("부채꼴 반각(도)", AIConfig.MeleeHitHalfAngle, 10f, 120f);
            AIConfig.MeleeDamage       = I("피해",            AIConfig.MeleeDamage,       0, 5);

            GUILayout.Space(4f);
            GUILayout.Label("<b>이동</b> <size=10>(SimConfig — 전 몹 공통)</size>", Rich());
            SimConfig.EnemyMoveSpeed = F("이동 속도", SimConfig.EnemyMoveSpeed, 1f, 12f);
            GUILayout.Label($"<size=10>플레이어({SimConfig.PlayerMoveSpeed:0.0})의 " +
                            $"{SimConfig.EnemyMoveSpeed / Mathf.Max(0.01f, SimConfig.PlayerMoveSpeed):0.00}배</size>", Rich());
        }

        // ── 돌진 ──
        void Charge()
        {
            GUILayout.Label("<b>돌진 (핑키)</b>", Rich());
            AIConfig.ChargeMinRange    = F("돌진 개시 거리(m)", AIConfig.ChargeMinRange,   1f, 15f);
            AIConfig.ChargeWindupTicks = Sec("준비 시간",        AIConfig.ChargeWindupTicks, 0.1f, 2.5f);
            AIConfig.ChargeSpeed       = F("돌진 속도(최고)",    AIConfig.ChargeSpeed,      4f, 25f);
            AIConfig.ChargeMaxDist     = F("돌진 거리(m)",       AIConfig.ChargeMaxDist,    2f, 30f);

            GUILayout.Space(4f);
            GUILayout.Label("<b>가속 곡선</b> <size=10>(예지 결과 바뀜 — 끄면 예전 동작)</size>", Rich());
            AIConfig.ChargeAccelOn = GUILayout.Toggle(AIConfig.ChargeAccelOn,
                AIConfig.ChargeAccelOn ? " 서서히 최고속으로 수렴" : " <color=#ffb060>즉시 최고속(예전)</color>", Rich());
            if (AIConfig.ChargeAccelOn)
            {
                AIConfig.ChargeAccelK = F("수렴 속도 k", AIConfig.ChargeAccelK, 1f, 20f);
                // 실제로 어떻게 붙는지 숫자로 — 감으로 잡기 어려운 값이라 표로 보여준다
                GUILayout.Label(
                    $"<size=10>0.1초 {AIConfig.ChargeSpeedAt(0.1f):0.0} · 0.25초 {AIConfig.ChargeSpeedAt(0.25f):0.0}" +
                    $" · 0.5초 {AIConfig.ChargeSpeedAt(0.5f):0.0} · 1초 {AIConfig.ChargeSpeedAt(1f):0.0} m/s</size>", Rich());
            }
            GUILayout.Label($"<size=10>→ {AIConfig.ChargeMaxDist:0.0}m 주파 시간 ≈ {ChargeRunSeconds():0.00}초</size>", Rich());

            GUILayout.Space(4f);
            GUILayout.Label("<b>휘청(후딜)</b>", Rich());
            AIConfig.ChargeHitRecovery  = Sec("명중 후",   AIConfig.ChargeHitRecovery,  0.1f, 3f);
            AIConfig.ChargeMissRecovery = Sec("빗나감 후", AIConfig.ChargeMissRecovery, 0.1f, 4f);

            GUILayout.Space(4f);
            AIConfig.ChargeRadiusMul     = F("반경 배율",     AIConfig.ChargeRadiusMul,     1f, 3f);
            AIConfig.ChargeDamage        = I("접촉 피해",     AIConfig.ChargeDamage,        0, 5);
            AIConfig.ChargeWallStopFrac  = F("벽 정지 임계",  AIConfig.ChargeWallStopFrac,  0.05f, 0.9f);

            GUILayout.Space(4f);
            GUILayout.Label("<size=10><color=#ffb060>★ 보류 — 발걸음 애니메이션과 같이 봐야 확정</color></size>", Rich());
            AIConfig.ChargeChaseSpeedMul = F("평소 추격 속도 배율", AIConfig.ChargeChaseSpeedMul, 0.1f, 1.5f);
            GUILayout.Label($"<size=10>→ 실제 추격 속도 {SimConfig.EnemyMoveSpeed * AIConfig.ChargeChaseSpeedMul:0.00}m/s</size>", Rich());
        }

        // ── 원거리 ──
        void Ranged()
        {
            GUILayout.Label("<b>원거리 솔저 (단발 저격형)</b>", Rich());
            GUILayout.Label("<size=10>조준 중 제자리 고정. 시야는 안 잠김(발사 방향만 조준 시작 시 고정)</size>", Rich());
            AIConfig.RangedAimTicks = Sec("조준(제자리 고정)", AIConfig.RangedAimTicks, 0.2f, 5f);
            AIConfig.RangedCooldown = Sec("발사 후 정비",      AIConfig.RangedCooldown, 0.2f, 5f);
            Total("한 발 주기", AIConfig.RangedAimTicks + AIConfig.RangedCooldown);

            GUILayout.Space(4f);
            GUILayout.Label("<b>유지 거리</b>", Rich());
            AIConfig.RangedBandMin = F("이보다 가까우면 후퇴", AIConfig.RangedBandMin, 0.5f, 12f);
            AIConfig.RangedBandMax = F("이보다 멀면 접근",     AIConfig.RangedBandMax, 2f,  25f);
            if (AIConfig.RangedBandMin >= AIConfig.RangedBandMax)
                GUILayout.Label("<size=10><color=#ff8080>최소 ≥ 최대 — 값이 뒤집혔습니다</color></size>", Rich());

            GUILayout.Space(4f);
            AIConfig.RangedDamage = I("피해", AIConfig.RangedDamage, 0, 5);
            GUILayout.Label("<size=10><color=#ffb060>★ 보류 — 발걸음과 같이 봐야 확정</color></size>", Rich());
            AIConfig.RangedMoveSpeed = F("이동 속도", AIConfig.RangedMoveSpeed, 1f, 10f);
        }

        // ── 공중 ──
        void Fly()
        {
            GUILayout.Label("<b>공중 (커코데몬)</b>", Rich());
            GUILayout.Label("<size=10>공격 타이밍은 지상 원거리와 <b>분리</b>돼 있다 — 여기서 따로 잡는다</size>", Rich());
            AIConfig.FlyAimTicks = Sec("조준", AIConfig.FlyAimTicks, 0.2f, 5f);
            AIConfig.FlyCooldown = Sec("발사 후 정비", AIConfig.FlyCooldown, 0.2f, 5f);
            Total("한 발 주기", AIConfig.FlyAimTicks + AIConfig.FlyCooldown);

            GUILayout.Space(4f);
            GUILayout.Label("<b>부유</b>", Rich());
            AIConfig.FlyHoverOffset  = F("플레이어 위 높이(m)", AIConfig.FlyHoverOffset,  0f, 8f);
            AIConfig.FlyHoverJitter  = F("개체별 높이 편차(±m)", AIConfig.FlyHoverJitter, 0f, 3f);
            GUILayout.Label($"<size=10>→ 개체마다 {AIConfig.FlyHoverOffset - AIConfig.FlyHoverJitter:0.0} ~ " +
                            $"{AIConfig.FlyHoverOffset + AIConfig.FlyHoverJitter:0.0}m 사이에서 고정 " +
                            $"(id 해시라 같은 몹은 항상 같은 높이)</size>", Rich());
            AIConfig.FlySpeed        = F("부유 속도",           AIConfig.FlySpeed,        0.5f, 10f);
            AIConfig.FlyMinClearance = F("지면 최소 여유(m)",   AIConfig.FlyMinClearance, 0.2f, 4f);

            GUILayout.Space(4f);
            GUILayout.Label("<b>유지 거리</b>", Rich());
            AIConfig.FlyBandMin = F("이보다 가까우면 후퇴", AIConfig.FlyBandMin, 0.5f, 12f);
            AIConfig.FlyBandMax = F("이보다 멀면 접근",     AIConfig.FlyBandMax, 2f,  25f);
            if (AIConfig.FlyBandMin >= AIConfig.FlyBandMax)
                GUILayout.Label("<size=10><color=#ff8080>최소 ≥ 최대 — 값이 뒤집혔습니다</color></size>", Rich());

            GUILayout.Space(4f);
            GUILayout.Label("<b>관성 이동</b> <size=10>(예지 결과 바뀜 — 끄면 예전 동작)</size>", Rich());
            AIConfig.FlyInertiaOn = GUILayout.Toggle(AIConfig.FlyInertiaOn,
                AIConfig.FlyInertiaOn ? " 가감속 + 급선회 시 미끄러짐" : " <color=#ffb060>즉시 방향 전환(예전)</color>", Rich());
            if (AIConfig.FlyInertiaOn)
            {
                GUILayout.Label("<size=10><b>수평</b></size>", Rich());
                AIConfig.FlyAccel    = F("가속(낮을수록 굼뜸)",   AIConfig.FlyAccel,    0.3f, 10f);
                AIConfig.FlyDrag     = F("감속(낮을수록 미끄럼)", AIConfig.FlyDrag,     0.1f, 6f);
                AIConfig.FlyMaxSpeed = F("속도 상한",             AIConfig.FlyMaxSpeed, 1f,   15f);
                GUILayout.Label($"<size=10>→ 정지까지 약 {(AIConfig.FlyDrag > 0.01f ? 3f / AIConfig.FlyDrag : 99f):0.0}초 미끄러짐" +
                                $" · 최고속 도달 약 {(AIConfig.FlyAccel > 0.01f ? 3f / AIConfig.FlyAccel : 99f):0.0}초</size>", Rich());

                GUILayout.Label("<size=10><b>수직</b> — 목표 높이가 바뀔 때 지나쳤다 되돌아옴</size>", Rich());
                AIConfig.FlyAccelY    = F("수직 가속",   AIConfig.FlyAccelY,    0.3f, 10f);
                AIConfig.FlyDragY     = F("수직 감속",   AIConfig.FlyDragY,     0.1f, 6f);
                AIConfig.FlyMaxSpeedY = F("수직 상한",   AIConfig.FlyMaxSpeedY, 1f,   15f);
            }
        }

        // ── 공통 ──
        void Common()
        {
            GUILayout.Label("<b>투사체</b>", Rich());
            AIConfig.ProjectileSpeed  = F("속도",     AIConfig.ProjectileSpeed,  4f, 30f);
            AIConfig.ProjectileRadius = F("반경(m)",  AIConfig.ProjectileRadius, 0.05f, 1f);
            AIConfig.LeadFactor       = F("리드 조준(0=없음,1=완벽)", AIConfig.LeadFactor, 0f, 1f);
            AIConfig.MissOffsetDeg    = F("대시 시 빗맞힘(도)",       AIConfig.MissOffsetDeg, 0f, 45f);

            GUILayout.Space(4f);
            GUILayout.Label("<b>몹 분리(겹침 방지)</b>", Rich());
            AIConfig.SeparationRadius   = F("개인공간(m)",  AIConfig.SeparationRadius,   0.2f, 4f);
            AIConfig.SeparationWeight   = F("분리 세기",    AIConfig.SeparationWeight,   0f,   3f);
            AIConfig.SeparationMaxPush  = F("최대 밀어냄",  AIConfig.SeparationMaxPush,  0.5f, 6f);
            AIConfig.SeparationScaleMin = F("개체 편차 하한", AIConfig.SeparationScaleMin, 0.1f, 1f);

            GUILayout.Space(4f);
            GUILayout.Label("<b>지각</b>", Rich());
            AIConfig.EnemyEyeHeight = F("적 눈높이(m)",       AIConfig.EnemyEyeHeight, 0.2f, 2f);
            AIConfig.PlayerTorso    = F("플레이어 겨냥점(m)", AIConfig.PlayerTorso,    0.2f, 2f);
        }

        /// <summary>돌진이 최대 거리를 주파하는 데 걸리는 시간(초). 가속 곡선을 수치적으로 되짚는다.</summary>
        static float ChargeRunSeconds()
        {
            if (!AIConfig.ChargeAccelOn)
                return AIConfig.ChargeMaxDist / Mathf.Max(0.01f, AIConfig.ChargeSpeed);
            // 거리곡선은 단조증가라 틱 단위로 훑으면 충분하다(최대 5초에서 포기).
            for (int t = 1; t <= 300; t++)
            {
                float sec = t * SimConfig.TickDelta;
                if (AIConfig.ChargeDistAt(sec) >= AIConfig.ChargeMaxDist) return sec;
            }
            return 5f;
        }

        // ── 위젯 ──
        static float F(string label, float v, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {v:0.00}", GUILayout.Width(210f));
            float nv = GUILayout.HorizontalSlider(v, min, max);
            GUILayout.EndHorizontal();
            return nv;
        }

        static int I(string label, int v, int min, int max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {v}", GUILayout.Width(210f));
            int nv = Mathf.RoundToInt(GUILayout.HorizontalSlider(v, min, max));
            GUILayout.EndHorizontal();
            return nv;
        }

        /// <summary>틱 값을 초로 보여주고 초로 조절한다(밸런스는 초로 감을 잡는 게 맞다).</summary>
        static int Sec(string label, int ticks, float minSec, float maxSec)
        {
            float sec = ticks * SimConfig.TickDelta;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {sec:0.00}초 ({ticks}틱)", GUILayout.Width(210f));
            float ns = GUILayout.HorizontalSlider(sec, minSec, maxSec);
            GUILayout.EndHorizontal();
            return Mathf.Max(1, Mathf.RoundToInt(ns / SimConfig.TickDelta));
        }

        static void Total(string label, int ticks)
            => GUILayout.Label($"<size=10>→ {label} {ticks * SimConfig.TickDelta:0.00}초 ({ticks}틱)</size>", Rich());

        static GUIStyle Rich() => new GUIStyle(GUI.skin.label) { richText = true };
    }

    /// <summary>Play 시 자동 부착.</summary>
    public static class MobBalancePanelBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<MobBalancePanel>() == null)
                new GameObject("[MobBalancePanel]").AddComponent<MobBalancePanel>();
        }
    }
}
