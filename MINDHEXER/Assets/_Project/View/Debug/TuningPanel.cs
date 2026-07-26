using UnityEngine;
using UnityEngine.InputSystem;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// F1 인게임 튜닝 패널 — SimConfig/CombatConfig의 static 수치를 Play 중 실시간 조정.
    /// 튜닝 기간 전용 도구(임시방편): 값이 확정되면 코드에 굳히고, 예측(예지) 발동 중엔
    /// 값을 바꾸지 말 것(결정론 깨짐). 기본값은 첫 실행 때 캡처해 리셋으로 복원.
    /// </summary>
    public class TuningPanel : MonoBehaviour
    {
        bool open;
        Vector2 scroll;
        bool captured;

        // 기본값 캡처(코드 초기값 = 리셋 목표)
        float dMove, dJump, dAirBoost, dDashInit, dDashDecay, dAtkRange, dAtkAngle, dAtkHeight;
        float dLgMin, dLgMax, dLgAim, dLgStop, dLgHeight, dLgFov, dLgUp;
        int dJumpBuf, dAirBoostT, dDashTicks, dDashCharges, dDashRecharge, dDashReserve;
        int dAtkW, dAtkA, dAtkR, dLgW, dLgTravel, dLgR, dLgCool, dLgBind, dLgHitStop, dLgStacks, dLgReserve, dHp, dHitStun;
        int dAtk2W, dAtk2A, dAtk2R, dCombo;   // 2연타 콤보

        void Capture()
        {
            dMove = SimConfig.PlayerMoveSpeed; dJump = SimConfig.PlayerJumpSpeed;
            dJumpBuf = SimConfig.JumpBufferTicks; dAirBoost = SimConfig.AirJumpBoost; dAirBoostT = SimConfig.AirJumpBoostTicks;
            dDashInit = SimConfig.DashInitialSpeed; dDashTicks = SimConfig.DashDurationTicks; dDashDecay = SimConfig.DashDecay;
            dDashCharges = SimConfig.DashMaxCharges; dDashRecharge = SimConfig.DashRechargeTicks; dDashReserve = SimConfig.DashReserveWindow;
            dAtkW = CombatConfig.Atk1WindupTicks; dAtkA = CombatConfig.Atk1ActiveTicks; dAtkR = CombatConfig.Atk1RecoveryTicks;
            dAtk2W = CombatConfig.Atk2WindupTicks; dAtk2A = CombatConfig.Atk2ActiveTicks; dAtk2R = CombatConfig.Atk2RecoveryTicks;
            dCombo = CombatConfig.ComboWindowTicks;
            dAtkRange = CombatConfig.AttackConeRange; dAtkAngle = CombatConfig.AttackConeHalfAngle; dAtkHeight = CombatConfig.AttackHeightTolerance;
            dLgW = CombatConfig.LungeWindupTicks; dLgTravel = CombatConfig.LungeTravelTicks;
            dLgR = CombatConfig.LungeRecoveryTicks; dLgCool = CombatConfig.LungeCooldownTicks;
            dLgMin = CombatConfig.LungeMinRange; dLgMax = CombatConfig.LungeMaxRange; dLgAim = CombatConfig.LungeAimRadius;
            dLgStop = CombatConfig.LungeStopDistance; dLgHeight = CombatConfig.LungeHeightTolerance; dLgBind = CombatConfig.LungeBindExtraTicks;
            dLgHitStop = CombatConfig.LungeHitStopTicks; dLgFov = CombatConfig.LungeFovKick;
            dLgUp = CombatConfig.LungeAimUp; dLgStacks = CombatConfig.LungeMaxStacks; dLgReserve = CombatConfig.LungeReserveWindow;
            dHp = CombatConfig.PlayerMaxHp; dHitStun = CombatConfig.PlayerHitStunTicks;
            captured = true;
        }

        void ResetAll()
        {
            SimConfig.PlayerMoveSpeed = dMove; SimConfig.PlayerJumpSpeed = dJump;
            SimConfig.JumpBufferTicks = dJumpBuf; SimConfig.AirJumpBoost = dAirBoost; SimConfig.AirJumpBoostTicks = dAirBoostT;
            SimConfig.DashInitialSpeed = dDashInit; SimConfig.DashDurationTicks = dDashTicks; SimConfig.DashDecay = dDashDecay;
            SimConfig.DashMaxCharges = dDashCharges; SimConfig.DashRechargeTicks = dDashRecharge; SimConfig.DashReserveWindow = dDashReserve;
            CombatConfig.Atk1WindupTicks = dAtkW; CombatConfig.Atk1ActiveTicks = dAtkA; CombatConfig.Atk1RecoveryTicks = dAtkR;
            CombatConfig.Atk2WindupTicks = dAtk2W; CombatConfig.Atk2ActiveTicks = dAtk2A; CombatConfig.Atk2RecoveryTicks = dAtk2R;
            CombatConfig.ComboWindowTicks = dCombo;
            CombatConfig.AttackConeRange = dAtkRange; CombatConfig.AttackConeHalfAngle = dAtkAngle; CombatConfig.AttackHeightTolerance = dAtkHeight;
            CombatConfig.LungeWindupTicks = dLgW; CombatConfig.LungeTravelTicks = dLgTravel;
            CombatConfig.LungeRecoveryTicks = dLgR; CombatConfig.LungeCooldownTicks = dLgCool;
            CombatConfig.LungeMinRange = dLgMin; CombatConfig.LungeMaxRange = dLgMax; CombatConfig.LungeAimRadius = dLgAim;
            CombatConfig.LungeStopDistance = dLgStop; CombatConfig.LungeHeightTolerance = dLgHeight; CombatConfig.LungeBindExtraTicks = dLgBind;
            CombatConfig.LungeHitStopTicks = dLgHitStop; CombatConfig.LungeFovKick = dLgFov;
            CombatConfig.LungeAimUp = dLgUp; CombatConfig.LungeMaxStacks = dLgStacks; CombatConfig.LungeReserveWindow = dLgReserve;
            CombatConfig.PlayerMaxHp = dHp; CombatConfig.PlayerHitStunTicks = dHitStun;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            // F2=포즈 재생, F6=콤보 튜닝이 쓰므로 NavMesh 시각화는 F7이다.
            if (kb.f7Key.wasPressedThisFrame)
            {
                var v = NavMeshDebugView.Toggle();
                Debug.Log(v != null
                    ? "[NavMesh 시각화] 켬 — 파랑=Walkable, 노랑=Jump, 그 외=커스텀 Area. 마커 끝점 초록=면 위 / 빨강=면 밖"
                    : "[NavMesh 시각화] 끔");
            }
            if (kb.f1Key.wasPressedThisFrame)
            {
                if (!captured) Capture();
                open = !open;
                DevPanels.TuningPanelOpen = open;
                Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = open;
            }
        }

        void OnGUI()
        {
            if (!open) return;
            const float W = 380f;
            GUILayout.BeginArea(new Rect(Screen.width - W - 12f, 12f, W, Screen.height - 24f), GUI.skin.box);
            GUILayout.Label("<b>튜닝 패널 (F1)</b> — 예지 중 변경 금지", Rich());
            scroll = GUILayout.BeginScrollView(scroll);

            GUILayout.Label("<b>이동·점프</b>", Rich());
            SimConfig.PlayerMoveSpeed = FSlider("이동 속도", SimConfig.PlayerMoveSpeed, 3f, 14f);
            SimConfig.PlayerJumpSpeed = FSlider("점프 속도", SimConfig.PlayerJumpSpeed, 5f, 16f);
            SimConfig.JumpBufferTicks = ISlider("점프 버퍼(틱)", SimConfig.JumpBufferTicks, 0, 12);
            SimConfig.AirJumpBoost = FSlider("2단점프 임펄스", SimConfig.AirJumpBoost, 0f, 14f);
            SimConfig.AirJumpBoostTicks = ISlider("임펄스 지속(틱)", SimConfig.AirJumpBoostTicks, 1, 30);

            GUILayout.Label("<b>대시 (진짜 임펄스: 초기 속도 → 드래그)</b>", Rich());
            SimConfig.DashInitialSpeed = FSlider("초기 속도/힘(m/s)", SimConfig.DashInitialSpeed, 20f, 90f);
            SimConfig.DashDecay = FSlider("드래그(낮을수록 빨리 멈춤)", SimConfig.DashDecay, 0.5f, 0.95f);
            SimConfig.DashDurationTicks = ISlider("최대 지속(틱)", SimConfig.DashDurationTicks, 4, 24);
            GUILayout.Label($"→ 총 거리 ≈ {DashDistance():0.00} m", Rich());
            SimConfig.DashMaxCharges = ISlider("스택", SimConfig.DashMaxCharges, 1, 3);
            SimConfig.DashRechargeTicks = ISlider("재충전(틱)", SimConfig.DashRechargeTicks, 20, 180);
            SimConfig.DashReserveWindow = ISlider("예약 구간(막판 틱)", SimConfig.DashReserveWindow, 0, 20);

            // 대시 연출 — 방향별로 다르게(앞=넓어짐 / 뒤=좁아짐 / 옆=롤). View 전용이라 예지 무해.
            var fb = CombatFeedback.Instance;
            if (fb != null)
            {
                GUILayout.Label("<size=11><b>대시 연출</b> (앞=FOV 좁아짐 · 뒤=넓어짐 · 옆=기울임)</size>", Rich());

                // 실측 표시 — "넓어져야 하는데 좁아 보인다" 같은 체감 문제를 숫자로 가른다
                var vcamNow = Main.Instance != null ? Main.Instance.GameplayVcam : null;
                GUILayout.Label(
                    $"<size=10>마지막 대시 <b>{DashLabel(fb.LastDashFwd, fb.LastDashSide)}</b>  " +
                    $"앞뒤 <b>{fb.LastDashFwd:+0.00;-0.00}</b>  좌우 <b>{fb.LastDashSide:+0.00;-0.00}</b>\n" +
                    $"지금 FOV 변화 <b>{fb.FovDeltaNow:+0.0;-0.0}°</b>  (대시 {FovKickPart(fb):+0.0;-0.0} · " +
                    $"찌르기 {fb.LungeFovNow:+0.0;-0.0} · 해제 {fb.ReleaseNow:+0.0;-0.0})  롤 {fb.RollNow:+0.0;-0.0}°" +
                    (vcamNow != null ? $"\n실제 FOV {vcamNow.Lens.FieldOfView:0.0}°" : "") + "</size>", Rich());

                fb.dashFovInvert = GUILayout.Toggle(fb.dashFovInvert, " FOV 부호 뒤집기 (체감이 반대면)");
                fb.dashFovFwd       = FSlider("앞뒤 FOV 킥",   fb.dashFovFwd,       0f, 3f);
                fb.dashFovBackScale = FSlider("뒤 비율",       fb.dashFovBackScale, 0f, 4f);
                fb.fovKickAttack    = FSlider("들어가는 속도", fb.fovKickAttack,    1f, 40f);
                fb.fovKickDecay     = FSlider("풀리는 속도",   fb.fovKickDecay,     0.5f, 20f);
                fb.dashRoll         = FSlider("옆 기울임(도)", fb.dashRoll,         0f, 20f);
                fb.dashRollDecay    = FSlider("기울임 복귀",   fb.dashRollDecay,    1f, 20f);

                GUILayout.Label("<size=11><b>찌르기 FOV</b> (이동 시작에 좁아져 유지 → 히트스톱 풀리면 복귀)</size>", Rich());
                fb.lungeZoomIn        = FSlider("좁힘(도)",          fb.lungeZoomIn,        0f, 40f);
                fb.lungeZoomInTime    = FSlider("좁아지는 시간(초)", fb.lungeZoomInTime,    0.01f, 0.5f);
                fb.lungeZoomOutTime   = FSlider("복귀 시간(초)",     fb.lungeZoomOutTime,   0.05f, 1.5f);
                GUILayout.Label($"<size=10>지금 찌르기 FOV <b>{fb.LungeFovNow:+0.0;-0.0}°</b>" +
                                "   시간은 <b>초</b> — 클수록 느리다.</size>", Rich());

                GUILayout.Label("<size=10>아래는 해제 순간 한 번 터지는 추가 확장 — 기본 0(안 씀)</size>", Rich());
                fb.lungeZoomOut       = FSlider("해제 확장(도)",     fb.lungeZoomOut,       0f, 40f);
                if (fb.lungeZoomOut > 0.01f)
                {
                    fb.lungeReleaseRise = FSlider("벌어지는 시간(초)", fb.lungeReleaseRise, 0f, 0.4f);
                    fb.lungeReleaseFall = FSlider("돌아오는 시간(초)", fb.lungeReleaseFall, 0.05f, 1.5f);
                }
            }

            GUILayout.Label("<b>평타</b>  <size=10>틱·콤보·판정은 전부 <b>F6</b>으로 옮겼습니다</size>", Rich());
            GUILayout.Label($"<size=10>현재 방식: {(CombatConfig.UseSphereMelee ? $"구 오버랩 · 실효 {CombatConfig.MeleeReach:0.00}m" : $"부채꼴 · {CombatConfig.AttackConeRange:0.00}m / {CombatConfig.AttackConeHalfAngle * 2f:0}°")}</size>", Rich());

            GUILayout.Label("<b>타깃 런지</b>", Rich());
            CombatConfig.LungeMaxRange = FSlider("최대 사거리(m)", CombatConfig.LungeMaxRange, 4f, 20f);
            CombatConfig.LungeMinRange = FSlider("최소 사거리(m)", CombatConfig.LungeMinRange, 0f, 3f);
            CombatConfig.LungeAimRadius = FSlider("조준 보정 반경(m)", CombatConfig.LungeAimRadius, 0.5f, 4f);
            CombatConfig.LungeTravelTicks = ISlider("블링크 틱(순간이동급)", CombatConfig.LungeTravelTicks, 1, 6);
            CombatConfig.LungeWindupTicks = ISlider("선딜(틱)", CombatConfig.LungeWindupTicks, 0, 12);
            CombatConfig.LungeRecoveryTicks = ISlider("후딜(틱)", CombatConfig.LungeRecoveryTicks, 2, 40);
            CombatConfig.LungeCooldownTicks = ISlider("쿨타임(틱, 0=없음)", CombatConfig.LungeCooldownTicks, 0, 180);
            CombatConfig.LungeMaxStacks = ISlider("최대 스택(처치 충전)", CombatConfig.LungeMaxStacks, 1, 5);
            CombatConfig.LungeReserveWindow = ISlider("예약 구간(쿨 막판 틱)", CombatConfig.LungeReserveWindow, 0, 30);
            CombatConfig.LungeStopDistance = FSlider("정지 간격(m)", CombatConfig.LungeStopDistance, 0.4f, 2f);
            CombatConfig.LungeAimUp = FSlider("살짝 위(m)", CombatConfig.LungeAimUp, 0f, 2f);
            CombatConfig.LungeHeightTolerance = FSlider("높이 허용(m, 위/아래)", CombatConfig.LungeHeightTolerance, 0.3f, 12f);
            CombatConfig.LungeBindExtraTicks = ISlider("바인드 여유(틱)", CombatConfig.LungeBindExtraTicks, 0, 30);
            CombatConfig.LungeHitStopTicks = ISlider("임팩트 히트스톱(틱)", CombatConfig.LungeHitStopTicks, 0, 20);
            CombatConfig.LungeFovKick = FSlider("임팩트 FOV킥(도)", CombatConfig.LungeFovKick, 0f, 25f);

            GUILayout.Label("<b>층이동(도약) — 주저·멈칫</b>", Rich());
            GUILayout.Label("0으로 내리면 그 단계가 아예 사라짐. 몹이 못 올라오는지 확인용.", Rich());
            SimConfig.TraversalPauseMin   = ISlider("주저 최소(틱)", SimConfig.TraversalPauseMin, 0, 30);
            SimConfig.TraversalPauseMax   = ISlider("주저 최대(틱)", SimConfig.TraversalPauseMax, 0, 60);
            SimConfig.TraversalRecoverMin = ISlider("멈칫 최소(틱)", SimConfig.TraversalRecoverMin, 0, 30);
            SimConfig.TraversalRecoverMax = ISlider("멈칫 최대(틱)", SimConfig.TraversalRecoverMax, 0, 60);
            SimConfig.TraversalAscendShape  = FSlider("상승 가속(1=등속, 클수록 초반 폭발)", SimConfig.TraversalAscendShape, 1f, 4f);
            SimConfig.TraversalDescendShape = FSlider("하강 가속(1=등속, 클수록 막판 폭발)", SimConfig.TraversalDescendShape, 1f, 4f);
            if (GUILayout.Button("주저·멈칫 전부 0 (즉시 도약)"))
            {
                SimConfig.TraversalPauseMin = SimConfig.TraversalPauseMax = 0;
                SimConfig.TraversalRecoverMin = SimConfig.TraversalRecoverMax = 0;
            }
            GUILayout.Label($"→ 길이 8m 링크 기준 주저 {Preview(SimConfig.TraversalPauseMin, SimConfig.TraversalPauseMax)}틱 · " +
                            $"멈칫 {Preview(SimConfig.TraversalRecoverMin, SimConfig.TraversalRecoverMax)}틱", Rich());

            GUILayout.Label("<b>몹 분산(뭉치기 방지)</b>", Rich());
            GUILayout.Label("개성값(개체 고정 0~1)이 분리 세기를 '최소배율~1배'로 갈라 놓습니다.", Rich());
            AIConfig.SeparationWeight   = FSlider("분리 세기", AIConfig.SeparationWeight, 0f, 3f);
            AIConfig.SeparationRadius   = FSlider("개인공간 반경(m)", AIConfig.SeparationRadius, 0.2f, 4f);
            AIConfig.SeparationMaxPush  = FSlider("분리 상한", AIConfig.SeparationMaxPush, 0.5f, 6f);
            AIConfig.SeparationScaleMin = FSlider("개체차 최소배율(1=개체차 없음)", AIConfig.SeparationScaleMin, 0.1f, 1f);
            GUILayout.Label($"→ 실효 세기 범위 {AIConfig.SeparationWeight * AIConfig.SeparationScaleMin:0.00} ~ {AIConfig.SeparationWeight:0.00}", Rich());

            GUILayout.Label("<b>플레이어</b>", Rich());
            CombatConfig.PlayerMaxHp = ISlider("최대 HP(다음 스폰부터)", CombatConfig.PlayerMaxHp, 1, 20);
            CombatConfig.PlayerHitStunTicks = ISlider("피격 경직(틱)", CombatConfig.PlayerHitStunTicks, 0, 60);

            GUILayout.Space(8f);
            if (GUILayout.Button("기본값으로 리셋")) ResetAll();
            if (GUILayout.Button("저장")) CombatTuningSave.Save();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /// <summary>길이 8m 링크가 받을 틱 수(슬라이더 효과 즉시 확인용).</summary>
        static int Preview(int min, int max)
            => TraversalBallistics.LengthToTicks(8f, min, max,
                   SimConfig.TraversalLengthRef, SimConfig.TraversalLengthExp);

        /// <summary>임펄스 총 거리 = v0·dt·(1-decay^N)/(1-decay). PlayerMovement와 동일 공식.</summary>
        static float DashDistance()
        {
            float d = Mathf.Clamp(SimConfig.DashDecay, 0.01f, 0.999f);
            int n = SimConfig.DashDurationTicks;
            float sum = (1f - Mathf.Pow(d, n)) / (1f - d);
            return SimConfig.DashInitialSpeed * SimConfig.TickDelta * sum;
        }

        static float FSlider(string label, float v, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {v:0.00}", GUILayout.Width(190f));
            float nv = GUILayout.HorizontalSlider(v, min, max);
            GUILayout.EndHorizontal();
            return nv;
        }

        static int ISlider(string label, int v, int min, int max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {v}", GUILayout.Width(190f));
            int nv = Mathf.RoundToInt(GUILayout.HorizontalSlider(v, min, max));
            GUILayout.EndHorizontal();
            return nv;
        }

        /// <summary>합산된 FOV 변화에서 대시 몫만 떼어낸다(표시용).</summary>
        static float FovKickPart(CombatFeedback fb) => fb.FovDeltaNow - fb.LungeFovNow - fb.ReleaseNow;

        /// <summary>마지막 대시 방향을 사람이 읽을 이름으로 — "뒤로 쳤는데 반응이 없다"를 가른다.</summary>
        static string DashLabel(float f, float s)
        {
            if (Mathf.Abs(f) < 0.01f && Mathf.Abs(s) < 0.01f) return "없음";
            if (Mathf.Abs(f) >= Mathf.Abs(s)) return f > 0f ? "앞" : "<color=#ffb060>뒤</color>";
            return s > 0f ? "오른쪽" : "왼쪽";
        }

        static GUIStyle Rich()
        {
            var s = new GUIStyle(GUI.skin.label) { richText = true };
            return s;
        }
    }

    /// <summary>Play 시 자동 부착.</summary>
    public static class TuningPanelBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<TuningPanel>() == null)
                new GameObject("[TuningPanel]").AddComponent<TuningPanel>();
        }
    }
}
