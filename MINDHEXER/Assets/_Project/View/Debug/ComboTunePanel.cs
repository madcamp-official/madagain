using UnityEngine;
using UnityEngine.InputSystem;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// F6 — 평타 2연타 콤보 튜닝. 틱 수치를 실시간 조절하고, 콤보 상태를 눈으로 본다.
    ///
    /// [콤보 자동 반복]은 sim에 좌클릭을 대신 넣어준다.
    /// 콤보창이 열리는 순간을 노려 평타2를 잇고, 평타2가 끝나면 다시 평타1부터 —
    /// 즉 <b>완벽한 타이밍으로 계속 2연타</b>를 친다. 애니메이션·이펙트 확인용.
    /// (F1 전투 전반 · F2 재생/카메라 · F3 시퀀스 · F4 절차 모션 · F5 이펙트 · F6 콤보)
    /// </summary>
    public class ComboTunePanel : MonoBehaviour
    {
        bool open;
        int  tab;
        Vector2 scroll;
        static readonly string[] Tabs = { "콤보", "판정", "찌르기" };

        [Tooltip("콤보를 자동으로 계속 친다")]
        public bool autoCombo;
        [Tooltip("평타2를 콤보창의 어느 시점에 낼지(0=창 열리자마자, 1=만료 직전)")]
        [Range(0f, 1f)] public float comboTiming = 0.25f;
        [Tooltip("평타2가 끝난 뒤 다음 평타1까지 쉬는 틱")]
        public int restTicks = 10;

        int  restLeft;
        byte prevPhase;

        /// <summary>패널이 열려 있는가(열려 있으면 Main이 플레이어 입력을 막는다).</summary>
        public static bool AnyOpen;

        void OnDisable() { AnyOpen = false; autoCombo = false; DevInput.Clear(); }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f6Key.wasPressedThisFrame)
            {
                open = !open;
                AnyOpen = open;
                Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = open;
                if (!open) { autoCombo = false; DevInput.Clear(); }
            }

            if (autoCombo) DriveAutoCombo();
        }

        /// <summary>sim 상태를 보고 알맞은 순간에 좌클릭을 넣는다.</summary>
        void DriveAutoCombo()
        {
            var main = Main.Instance;
            if (main == null) return;
            ref readonly PlayerCombatState c = ref main.World.player.combat;

            // 평타2가 끝난 직후에는 조금 쉬어 후딜을 눈으로 확인할 수 있게 한다
            if (c.attackPhase == CombatConfig.PhNone && prevPhase == CombatConfig.PhRecovery
                && c.comboStep == 0 && c.comboWindow == 0)
                restLeft = Mathf.Max(0, restTicks);
            prevPhase = c.attackPhase;

            if (restLeft > 0) { restLeft--; return; }
            if (c.attackPhase != CombatConfig.PhNone) return;   // 공격 중이면 기다린다

            if (c.comboStep == 1 && c.comboWindow > 0)
            {
                // 콤보창 안 — 지정한 비율 시점에 평타2를 낸다
                int total = Mathf.Max(1, CombatConfig.ComboWindowTicks);
                int fireAt = Mathf.Clamp(Mathf.RoundToInt(total * (1f - comboTiming)), 1, total);
                if (c.comboWindow <= fireAt) DevInput.PressAttack();
            }
            else if (c.comboStep == 0)
            {
                DevInput.PressAttack();                          // 평타1 시작
            }
        }

        void OnGUI()
        {
            if (!open) return;
            const float W = 400f;
            float H = Mathf.Min(Screen.height - 24f, 520f);
            GUILayout.BeginArea(new Rect(12f, 12f, W, H), GUI.skin.box);
            GUILayout.Label("<b>평타 2연타 콤보 (F6)</b>", Rich());

            var main = Main.Instance;
            if (main == null) { GUILayout.Label("Main 없음"); GUILayout.EndArea(); return; }

            // ── 실시간 상태 ──
            ref readonly PlayerCombatState c = ref main.World.player.combat;
            string ph = c.attackPhase == CombatConfig.PhNone ? "대기"
                      : c.attackPhase == CombatConfig.PhWindup ? "선딜"
                      : c.attackPhase == CombatConfig.PhActive ? "<color=#ff8080>판정</color>" : "후딜";
            GUILayout.Label(
                $"<size=12>진행 <b>평타{c.attackStep + 1}</b> {ph} {c.attackPhaseTicks}틱   ·   다음 <b>평타{c.comboStep + 1}</b>\n" +
                (c.comboWindow > 0
                    ? $"<color=#80e080>콤보창 {c.comboWindow}/{CombatConfig.ComboWindowTicks}틱 남음</color>"
                    : "<color=#c0c0c0>콤보창 닫힘</color>") +
                (c.attackBuffered ? "   <color=#ffb060>선입력 대기</color>" : "") + "</size>", Rich());

            // 콤보창 게이지
            if (CombatConfig.ComboWindowTicks > 0)
            {
                var r = GUILayoutUtility.GetRect(1f, 8f);
                GUI.Box(r, GUIContent.none);
                if (c.comboWindow > 0)
                {
                    float f = c.comboWindow / (float)CombatConfig.ComboWindowTicks;
                    GUI.color = new Color(0.5f, 0.9f, 0.5f);
                    GUI.Box(new Rect(r.x, r.y, r.width * f, r.height), GUIContent.none);
                    GUI.color = Color.white;
                }
            }

            GUILayout.Space(6f);

            // ── 자동 반복 ──
            bool now = GUILayout.Toggle(autoCombo,
                autoCombo ? " <b><color=#80e080>콤보 자동 반복 중</color></b> — 다시 눌러 정지"
                          : " <b>콤보 자동 반복</b> (평타1↔평타2 계속)", Rich(), GUILayout.Height(26f));
            if (now != autoCombo) { autoCombo = now; restLeft = 0; if (!now) DevInput.Clear(); }

            if (autoCombo)
            {
                comboTiming = FSlider("평타2 타이밍 (0=즉시 1=막판)", comboTiming, 0f, 1f, 0.05f, "0.00");
                restTicks   = ISlider("2연타 후 쉬는 틱", restTicks, 0, 60);
            }

            GUILayout.Space(4f);
            tab = GUILayout.Toolbar(tab, Tabs);
            scroll = GUILayout.BeginScrollView(scroll);

            if (tab == 1) { DrawHitTab();    GUILayout.EndScrollView(); DrawFooter(); GUILayout.EndArea(); return; }
            if (tab == 2) { DrawThrustTab(); GUILayout.EndScrollView(); DrawFooter(); GUILayout.EndArea(); return; }

            // ── 즉발 판정 ──
            CombatConfig.AttackInstantJudge = GUILayout.Toggle(CombatConfig.AttackInstantJudge,
                CombatConfig.AttackInstantJudge
                    ? " <b><color=#80e080>즉발 판정</color></b> — 누른 틱에 바로 맞음 (판정은 길이에 안 더해짐)"
                    : " <b>선딜 후 판정</b> — 예전 방식 (판정이 길이에 더해짐)", Rich());
            GUILayout.Label(CombatConfig.AttackInstantJudge
                ? "<size=10>판정창이 0틱부터 열려 선딜과 겹친다. 캔슬해도 이미 때린 건 유효.\n" +
                  "이펙트는 선딜만큼 늦게 나온다 — 판정과 연출을 분리.</size>"
                : "<size=10>선딜이 끝나야 판정. 총 길이 = 선딜 + 판정 + 후딜.</size>", Rich());
            GUILayout.Space(4f);

            GUILayout.Label("<b>평타1</b> (짧은 후딜 — 이어치기용)", Rich());
            CombatConfig.Atk1WindupTicks   = ISlider("선딜(틱)", CombatConfig.Atk1WindupTicks, 1, 20);
            CombatConfig.Atk1ActiveTicks   = ISlider("판정(틱)", CombatConfig.Atk1ActiveTicks, 1, 10);
            CombatConfig.Atk1RecoveryTicks = ISlider("후딜(틱)", CombatConfig.Atk1RecoveryTicks, 1, 40);

            GUILayout.Space(4f);
            GUILayout.Label("<b>평타2</b> (마무리 — 긴 후딜)", Rich());
            CombatConfig.Atk2WindupTicks   = ISlider("선딜(틱)", CombatConfig.Atk2WindupTicks, 1, 20);
            CombatConfig.Atk2ActiveTicks   = ISlider("판정(틱)", CombatConfig.Atk2ActiveTicks, 1, 10);
            CombatConfig.Atk2RecoveryTicks = ISlider("후딜(틱)", CombatConfig.Atk2RecoveryTicks, 2, 60);

            GUILayout.Space(4f);
            GUILayout.Label("<b>콤보</b>", Rich());
            CombatConfig.ComboWindowTicks = ISlider("콤보창(틱)", CombatConfig.ComboWindowTicks, 1, 60);

            GUILayout.Space(4f);
            GUILayout.Label("<b>적 경직</b>  <size=10>스턴 = 히트스톱 + 추가</size>", Rich());
            CombatConfig.StunExtraTicks    = ISlider("추가 경직(틱)", CombatConfig.StunExtraTicks, 0, 60);
            CombatConfig.Atk1HitStopTicks  = ISlider("평타1 히트스톱", CombatConfig.Atk1HitStopTicks, 0, 20);
            CombatConfig.Atk2HitStopTicks  = ISlider("평타2 히트스톱", CombatConfig.Atk2HitStopTicks, 0, 20);
            GUILayout.Label(
                $"<size=11>평타1 스턴 <b>{CombatConfig.AtkStun(0)}틱</b> ({CombatConfig.AtkStun(0) * SimConfig.TickDelta:0.000}초)" +
                $" · 평타2 <b>{CombatConfig.AtkStun(1)}틱</b> ({CombatConfig.AtkStun(1) * SimConfig.TickDelta:0.000}초)" +
                $" · 찌르기 <b>{CombatConfig.LungeStun}틱</b> ({CombatConfig.LungeStun * SimConfig.TickDelta:0.000}초)\n" +
                "<size=10>히트스톱은 sim이 통째로 멈추므로 그동안 경직도 안 줄어든다 — 얼음이 풀린 뒤가 실제 경직.\n" +
                "이미 경직 중인 적은 더 긴 쪽으로 덮어쓴다(누적 안 함).</size></size>", Rich());

            float dt = SimConfig.TickDelta;
            int t1 = CombatConfig.AtkTotal(0);   // 즉발이면 판정은 겹치므로 더하지 않는다
            int t2 = CombatConfig.AtkTotal(1);
            GUILayout.Label(
                $"<size=11>평타1 {t1}틱 ({t1 * dt:0.000}초) · 평타2 {t2}틱 ({t2 * dt:0.000}초)" +
                (CombatConfig.AttackInstantJudge ? "  <size=10>(= 선딜+후딜)</size>" : "  <size=10>(= 선딜+판정+후딜)</size>") + "\n" +
                $"콤보창 {CombatConfig.ComboWindowTicks}틱 ({CombatConfig.ComboWindowTicks * dt:0.000}초)\n" +
                $"2연타 최속 {(t1 + t2) * dt:0.000}초</size>", Rich());

            GUILayout.EndScrollView();
            DrawFooter();
            GUILayout.EndArea();
        }

        /// <summary>판정 탭 — 오버워치식 구 오버랩 vs 기존 부채꼴.</summary>
        void DrawHitTab()
        {
            bool sphere = GUILayout.Toggle(CombatConfig.UseSphereMelee,
                CombatConfig.UseSphereMelee
                    ? " <b><color=#80e080>구 오버랩</color></b> (오버워치식 — 각도 없음·피치 반영·매 틱 판정)"
                    : " <b>부채꼴</b> (기존 — 수평 평면·1회 판정)", Rich());
            CombatConfig.UseSphereMelee = sphere;

            if (sphere)
            {
                GUILayout.Label("<size=10>시선 앞에 구를 놓고 겹치면 맞는다. 각도 경계가 없어 빗나감이 적고,\n" +
                                "활성 틱마다 다시 재므로 휘두르는 중 조준을 고쳐도 맞는다.</size>", Rich());
                CombatConfig.MeleeOffset    = FSlider("앞으로(m)",   CombatConfig.MeleeOffset,    0f,   4f, 0.05f, "0.00");
                CombatConfig.MeleeRadius    = FSlider("구 반지름(m)", CombatConfig.MeleeRadius,    0.1f, 3f, 0.05f, "0.00");
                CombatConfig.MeleeEyeHeight = FSlider("눈높이(m)",    CombatConfig.MeleeEyeHeight, 0f,   2f, 0.05f, "0.00");
                GUILayout.Label($"<size=11>실효 사거리 <b>{CombatConfig.MeleeReach:0.00}m</b>" +
                                $"   <size=10>(겐지 퀵멜리 측정치: 1.50 + 1.00 = 2.50m)</size></size>", Rich());

                GUILayout.Space(4f);
                if (GUILayout.Button("겐지 퀵멜리 값으로 (1.50 / 1.00)"))
                { CombatConfig.MeleeOffset = 1.5f; CombatConfig.MeleeRadius = 1.0f; }
            }
            else
            {
                GUILayout.Label("<size=10><color=#ffb060>수평 평면에서만 각도를 잰다 — 위아래를 봐도 판정이 같다.</color>\n" +
                                "높이는 아래 '높이 허용'으로만 거른다.</size>", Rich());
                CombatConfig.AttackConeRange       = FSlider("사거리(m)",    CombatConfig.AttackConeRange, 1f, 6f, 0.05f, "0.00");
                CombatConfig.AttackConeHalfAngle   = FSlider("반각(도)",     CombatConfig.AttackConeHalfAngle, 10f, 90f, 1f, "0");
                CombatConfig.AttackHeightTolerance = FSlider("높이 허용(m)", CombatConfig.AttackHeightTolerance, 0.3f, 3f, 0.05f, "0.00");
                GUILayout.Label($"<size=11>총 부채꼴 각 {CombatConfig.AttackConeHalfAngle * 2f:0}°</size>", Rich());
            }

            GUILayout.Space(6f);
            GUILayout.Label("<b>참고 — 적 캡슐</b>", Rich());
            GUILayout.Label($"<size=10>반지름 {SimConfig.EnemyRadius:0.00}m · 높이 {SimConfig.EnemyHeight:0.00}m\n" +
                            "구 판정은 적을 수직 캡슐로 본다(오버워치와 동일). 대형몹은 radius가 커져 자동 반영.</size>", Rich());
        }

        /// <summary>찌르기 탭 — 둠식 락온 연출 + 돌진 길이.</summary>
        void DrawThrustTab()
        {
            var cc = CombatCamera.Instance;

            // ── 방식 전환 (Sim + View 동시) ──
            bool doom = GUILayout.Toggle(CombatConfig.LungeDoomStyle,
                CombatConfig.LungeDoomStyle
                    ? " <b><color=#80e080>둠식</color></b> — 돌진이 보이고, 화면이 안 내려가며, 끝나면 시점 복귀"
                    : " <b>예전</b> — 3틱 순간이동 · 몸통 겨냥 · pitch 완전 강제", Rich());
            if (doom != CombatConfig.LungeDoomStyle)
            {
                CombatConfig.LungeDoomStyle = doom;              // Sim
                if (cc != null) cc.doomStyle = doom;             // View
            }
            GUILayout.Label("<size=10><color=#ffb060>이동 틱은 Sim 값이라 예지 결과가 함께 바뀝니다.</color>\n" +
                            "예지를 검증할 땐 '예전'으로 되돌리십시오 — 기존 값(3틱)은 지워지지 않았습니다.</size>", Rich());

            GUILayout.Space(4f);
            GUILayout.Label("<b>돌진</b>  <size=10>(Sim — 예지 영향)</size>", Rich());
            CombatConfig.LungeTravelTicksDoom =
                Mathf.RoundToInt(FSlider("둠식 이동(틱)", CombatConfig.LungeTravelTicksDoom, 1f, 20f, 1f, "0"));
            CombatConfig.LungeTravelTicks =
                Mathf.RoundToInt(FSlider("예전 이동(틱)", CombatConfig.LungeTravelTicks, 1f, 20f, 1f, "0"));
            GUILayout.Label($"<size=11>실제 사용 <b>{CombatConfig.LungeTravel}틱</b> " +
                            $"({CombatConfig.LungeTravel * SimConfig.TickDelta:0.000}초)" +
                            (CombatConfig.LungeDoomStyle ? " · ease-out 감속" : " · 등속") + "</size>", Rich());

            if (CombatConfig.LungeDoomStyle)
            {
                GUILayout.Space(4f);
                GUILayout.Label("<b>포물선 경로</b>  <size=10>위 적이면 위로, 아래 적이면 아래로 볼록</size>", Rich());
                CombatConfig.LungeArcAmount   = FSlider("호 크기(높이차 비율)", CombatConfig.LungeArcAmount,   0f, 1.5f, 0.05f, "0.00");
                CombatConfig.LungeArcMinBulge = FSlider("최소 부풂(m)",        CombatConfig.LungeArcMinBulge, 0f, 2f,   0.05f, "0.00");
                CombatConfig.LungeArcMaxBulge = FSlider("최대 부풂(m)",        CombatConfig.LungeArcMaxBulge, 0.2f, 6f, 0.1f,  "0.0");
                GUILayout.Label("<size=10>호가 있으면 이동 방향과 시선이 처음부터 일치해 각속도 폭발이 없다.\n" +
                                "0으로 두면 예전 직선 경로.</size>", Rich());
            }

            GUILayout.Space(6f);
            GUILayout.Label("<b>카메라 락온</b>  <size=10>(View — 예지 무해)</size>", Rich());
            if (cc == null) { GUILayout.Label("CombatCamera 없음"); return; }

            // ★ 끊김의 핵심 원인이었던 항목 — 끄면 예전처럼 매 프레임 재계산한다.
            cc.lockLungeAim = GUILayout.Toggle(cc.lockLungeAim,
                cc.lockLungeAim ? " <b><color=#80e080>에임이 경로를 따라감</color></b> (포물선 추종 + 도착 수렴)"
                                : " <b><color=#ffb060>매 프레임 대상 재계산</color></b> (도착 직전 홱 돌아감)", Rich());
            GUILayout.Label("<size=10>Sim과 같은 포물선 위의 지점에서 대상을 본다 — 몸과 시선이 안 어긋난다.\n" +
                            "진행 60% 이후 도착 각도로 수렴해, 끝날 땐 이미 멈춰 있다(되돌릴 것 없음).</size>", Rich());

            GUILayout.Space(4f);
            cc.aimHeightRatio = FSlider("겨냥 높이(키 대비)", cc.aimHeightRatio, 0.2f, 1.2f, 0.05f, "0.00");
            GUILayout.Label("<size=10>0.5=몸통 중심(예전, 화면이 내려감) · 0.85=가슴~머리</size>", Rich());

            cc.pitchWeight = FSlider("pitch 강제 정도", cc.pitchWeight, 0f, 1f, 0.05f, "0.00");
            GUILayout.Label("<size=10>0=상하 시점 그대로 · 1=대상 각도로 완전 강제(예전)</size>", Rich());

            cc.pitchDownLimit = FSlider("아래 한계(도)", cc.pitchDownLimit, 0f, 89f, 1f, "0");
            cc.enterRate      = FSlider("붙는 속도",     cc.enterRate,      5f, 120f, 5f, "0");
            cc.exitRestore    = FSlider("복귀 시간(초)", cc.exitRestore,    0f,  1f,  0.02f, "0.00");
            GUILayout.Label("<size=10>복귀 0 = 끝난 각도 그대로 유지(예전 동작)</size>", Rich());

            GUILayout.Space(6f);
            GUILayout.Label("<b>임팩트</b>", Rich());
            CombatConfig.LungeHitStopTicks =
                Mathf.RoundToInt(FSlider("히트스톱(틱)", CombatConfig.LungeHitStopTicks, 0f, 20f, 1f, "0"));
            CombatConfig.LungeFovKick = FSlider("FOV 킥(도)", CombatConfig.LungeFovKick, 0f, 25f, 0.5f, "0.0");

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("둠 기본값"))
            {
                CombatConfig.LungeDoomStyle = true; cc.doomStyle = true;
                CombatConfig.LungeTravelTicksDoom = 8;
                cc.aimHeightRatio = 0.85f; cc.pitchWeight = 0.35f;
                cc.pitchDownLimit = 20f; cc.enterRate = 55f; cc.exitRestore = 0.18f;
            }
            if (GUILayout.Button("예전 그대로"))
            {
                CombatConfig.LungeDoomStyle = false; cc.doomStyle = false;
                cc.aimHeightRatio = 0.5f; cc.pitchWeight = 1f;
                cc.pitchDownLimit = 89f; cc.enterRate = 24f; cc.exitRestore = 0f;
            }
            GUILayout.EndHorizontal();
            GUILayout.Label($"<size=10>{cc.Status}</size>", Rich());
        }

        void DrawFooter()
        {
            // ── 저장 (static 필드라 Play를 끄면 사라진다 — 파일로 남긴다) ──
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<b>저장</b>", Rich(), GUILayout.Height(26f))) CombatTuningSave.Save();
            if (GUILayout.Button("불러오기", GUILayout.Width(80f), GUILayout.Height(26f)))
                Debug.Log(CombatTuningSave.Load() ? "[F6] 저장값 불러옴" : "[F6] 저장 파일 없음");
            if (CombatTuningSave.Exists && GUILayout.Button("삭제", GUILayout.Width(56f), GUILayout.Height(26f)))
                CombatTuningSave.Delete();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("한 대 치기", GUILayout.Height(26f))) DevInput.PressAttack();
            if (GUILayout.Button("콤보 초기화", GUILayout.Height(26f))) { autoCombo = false; DevInput.Clear(); }
            GUILayout.EndHorizontal();

            GUILayout.Label($"<size=10>{(CombatTuningSave.Exists ? "<color=#80e080>저장 파일 있음 — 다음 Play에 자동 적용</color>" : "<color=#c0c0c0>미저장 — Play 종료 시 사라집니다</color>")}</size>", Rich());
        }

        static int ISlider(string label, int v, int min, int max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {v}", GUILayout.Width(150f));
            if (GUILayout.Button("−", GUILayout.Width(22f))) v--;
            int nv = Mathf.RoundToInt(GUILayout.HorizontalSlider(v, min, max));
            if (GUILayout.Button("+", GUILayout.Width(22f))) nv = v + 1;
            GUILayout.EndHorizontal();
            return Mathf.Clamp(nv, min, max);
        }

        static float FSlider(string label, float v, float min, float max, float step, string fmt)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {v.ToString(fmt)}", GUILayout.Width(190f));
            if (GUILayout.Button("−", GUILayout.Width(22f))) v -= step;
            v = GUILayout.HorizontalSlider(v, min, max);
            if (GUILayout.Button("+", GUILayout.Width(22f))) v += step;
            GUILayout.EndHorizontal();
            return Mathf.Clamp(v, min, max);
        }

        static GUIStyle Rich() => new GUIStyle(GUI.skin.label) { richText = true };
    }

    /// <summary>Play 시 자동 부착.</summary>
    public static class ComboTunePanelBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<ComboTunePanel>() == null)
                new GameObject("[ComboTunePanel]").AddComponent<ComboTunePanel>();
        }
    }
}
