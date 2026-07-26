using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 예측 연출 전용 튜닝값(전부 잠정). 리터럴 하드코딩 금지 — 이 한 곳에서만 조정한다.
    /// 전투/이동 수치는 여기 두지 않는다(그건 SimConfig/CombatConfig/AIConfig 소유).
    /// 공유 파일이 아니라 예측 세션 단독 파일이라 다른 세션과 충돌하지 않는다.
    /// </summary>
    public static class PredictionConfig
    {
        // 사정권(이 반경 내 적만 루트 대상)
        public const float Range = 16f;

        // [HUD 게이지, 2026-07-22] 예지(F) 자원. 예전엔 무제한이라 HUD의 PREDICTION 다이얼에
        // 붙일 수치가 없었다 — 발동 시 전부 소모하고 실시간으로 재충전되는 게이지를 둔다.
        // Idle일 때만 차오르며(미리보기·자동실행 중엔 멈춤), 슬로모와 무관하게 실시간이다.
        /// <summary>0 → 100%까지 걸리는 실시간 초.</summary>
        public const float ChargeRechargeSeconds = 30f;
        /// <summary>예지 진입 시 남기는 잔량(0 = 전부 소모).</summary>
        public const float ChargeAfterEnter = 0f;
        /// <summary>이만큼은 차야 F가 먹는다. 게이지가 곧 예측 지평이라 너무 적게 남은 상태로
        /// 쓰면 1초짜리 경로만 나와서 자원만 버리게 된다 — 그걸 막는 하한선.</summary>
        public const float ChargeMinToUse = 0.25f;

        /// <summary>
        /// 게이지(0~1)를 내다볼 시간(초)으로 환산한다. <b>게이지가 곧 예측 지평</b> —
        /// 아껴서 길게 볼지, 짧게 자주 쓸지가 플레이어의 선택이 된다.
        /// 범위는 PredictionSettings.Min/MaxDurationSeconds(1~5초)와 같이 간다.
        /// </summary>
        public static float ChargeToSeconds(float charge01) =>
            Mathf.Lerp(Game.Prediction.PredictionSettings.MinDurationSeconds,
                       Game.Prediction.PredictionSettings.MaxDurationSeconds,
                       Mathf.Clamp01(charge01));

        // 미리보기 카메라 (3인칭 궤도 — 마우스로 플레이어 중심 회전)
        public const float CamDist  = 7f;      // 피벗에서 뒤로(휠 줌의 기본값)
        // [2026-07-22] 미리보기 중 마우스 휠로 궤도 거리를 조절한다. 경로가 길면 뒤로 빼서
        // 전체를 보고, 액션 잔상을 자세히 볼 땐 당긴다. 표시 전용이라 예측 결과에는 영향 없다.
        public const float CamDistMin = 2.5f;
        public const float CamDistMax = 22f;
        /// <summary>휠 한 칸당 거리 변화(m).</summary>
        public const float CamZoomPerNotch = 1.1f;
        public const float CamLookY = 1.2f;    // 피벗 높이(플레이어 기준)
        public const float OrbitSens      = 0.15f;
        public const float OrbitPitchInit = 18f;    // 진입 시 살짝 위에서
        public const float OrbitPitchMin  = -10f;
        public const float OrbitPitchMax  = 80f;
        public const float CamCollisionRadius = 0.28f;
        public const float CamCollisionPadding = 0.12f;
        public const float CamCollisionMinDistance = 0.45f;
        // [예측 세션 추가, 2026-07-21] 진입 시 1인칭→3인칭 궤도 전환 연출 — 예전엔 목표 거리로
        // 즉시 스냅해서 "탁 하고 한 번에" 바뀌어 보였다. 초반엔 빠르게 멀어지다 후반부로
        // 갈수록 감속하는 완급(ease-out)을 줘서 자연스럽게 3인칭으로 빠져나가게 한다.
        public const float EnterOrbitPullbackSeconds = 0.55f;

        // [끊김 완화 로딩 연출, 2026-07-23] 진입 후 예측 검색(Build)이 끝나 잔상이 뜨기 전까지,
        // 초록 물결(RadialInvertFx)을 0→최대 톱니파로 반복 재생해 "스캔/로딩" 펄스를 보여준다.
        // 무거운 Build는 이 반복 구간이 끝나는 순간 실행돼 끊김이 펄스에 묻힌다.
        public const float RippleLoopSeconds   = 0.9f;    // 잔상 뜨기 전까지 물결을 반복하는 총 시간
        public const float RipplePeriodSeconds = 0.4f;    // 물결 한 펄스(0→최대) 주기
        // 잔상(투사주법 분신)이 켜질 때 알파를 0→목표로 올리는 페이드인 시간.
        public const float AfterimageFadeInSeconds = 0.25f;

        // 루트 색 (순서: PredictionPlanner.PlanByProfile이 고정하는 안전형/기회형/공격형)
        public static readonly Color[] RouteColors =
        {
            new Color(0.2f, 1f, 0.9f),   // 청록
            new Color(1f, 0.35f, 0.7f),  // 자홍
            new Color(1f, 0.8f, 0.2f),   // 주황
        };

        /// <summary>[잔상 밀도 상향, 2026-07-22] 경로 표시 선(발밑에 깔리던 LineRenderer)을 그릴지.
        /// 잔상이 촘촘해지면서 경로는 잔상 행렬 자체로 충분히 읽혀 선이 오히려 지저분해졌다 —
        /// 코드는 남겨두고 이 스위치로만 끈다(돔·킬 마커는 그대로).</summary>
        public const bool ShowRoutePathLine = false;

        // 라인 / 돔 / 고스트 / 마커 시각
        public const float RouteWidthSel = 0.32f;   // 선택 루트 굵기
        public const float RouteWidthDim = 0.12f;   // 비선택 루트 굵기
        public const float RouteDimMul   = 0.45f;   // 비선택 루트 밝기 배율
        // >>> [예측 세션 추가] RouteAlphaSel/Dim, ActionMarkColor — 원래 없던 값.
        public const float RouteAlphaSel = 0.75f;   // 선택 루트 반투명도
        public const float RouteAlphaDim = 0.4f;    // 비선택 루트 반투명도(더 흐리게)
        // [예측 세션 수정, 2026-07-20] 1.6 → 2.8: 이동 헤드와 경로가 전개되는 과정을
        // 충분히 눈으로 따라갈 수 있게 늦춘다. 예측 계산·판정·실행 속도에는 영향이 없다.
        // [2026-07-22] 5.0 → 8.0: 잔상이 깔리는 속도가 아직 빨라 경로를 읽기 전에 다 펼쳐진다는
        // 피드백. 이건 <b>표시 전용 타이머</b>라 예측 결과·판정·실행에는 전혀 영향이 없다
        // (Preview 중 좌클릭으로 언제든 확정 가능 — 다 펼쳐질 때까지 기다릴 필요 없다).
        public const float PreviewRevealSeconds = 8.0f;
        // [잔상 유지, 2026-07-22] 예전엔 헤드 바로 뒤에 붙어 따라오다 스윕이 끝나면 통째로
        // 페이드아웃되는 "꼬리"였다(PreviewAfterimageSpacing / PreviewAfterimageFadeSeconds).
        // 지금은 "투사주법"처럼 — 헤드가 지나간 자리에 일정 거리마다 분신을 한 번 찍어두고,
        // 그 분신은 Preview가 끝날 때까지 그 자리에 그대로 남는다(움직이지도, 사라지지도 않음).
        // [잔상 밀도 상향, 2026-07-22] 산데비스탄처럼 — 잔상을 촘촘히 깔아서 옆으로 죽 훑으면
        // 연속 동작(걷기 사이클)이 그대로 읽히게 한다. 간격이 스트라이드(GhostRunStrideMeters)에
        // 비해 충분히 작아야 인접 잔상의 다리 각도가 조금씩만 달라져 "이어진 동작"으로 보인다.
        /// <summary>남겨두는 분신 최대 개수(풀 크기). 경로가 길어 이 개수로 모자라면
        /// 아래 Step이 자동으로 벌어져서 경로 전체를 균등하게 덮는다.</summary>
        public const int PreviewAfterimageCount = 90;
        /// <summary>분신을 찍는 경로상 간격(m). 스트라이드 3.4m / 0.4m ≈ 사이클당 8~9장 —
        /// 걷기 동작이 이어져 보이는 최소선은 유지하면서 너무 빽빽하지 않은 값.</summary>
        public const float PreviewAfterimageStepMeters = 0.4f;
        /// <summary>남은 분신의 불투명도(스윕이 끝나도 이 값 그대로 유지된다). 촘촘해진 만큼
        /// 겹침이 심해서 낮춰 잡는다 — 겹쳐 쌓이면서 자연스럽게 진해진다.
        /// [2026-07-22] 0.16 → 0.24: 배경 윤곽선과 구분되도록 잔상 쪽을 올렸다.</summary>
        public const float PreviewAfterimageHeadAlpha = 0.24f;
        // [2026-07-22] 경로 그라데이션을 <b>초록 → 군청/남색</b> 스펙트럼으로 교체.
        // 예전엔 초록 → 파랑 → 보라 → 빨강이라 뒤쪽 절반이 예지의 초록 톤(RadialInvert
        // 네온 그린)과 완전히 따로 놀았다. 지금은 같은 계열 안에서 시간이 흐를수록
        // 차갑고 깊어지기만 한다 — "가까운 미래는 선명, 먼 미래는 가라앉는다"로 읽힌다.
        // 이름 순서 = 경로 진행 순서(0 → 1).
        public static readonly Color PreviewPathGreen = new Color(0.16f, 1f, 0.42f);   // 초록
        public static readonly Color PreviewPathTeal  = new Color(0.06f, 0.86f, 0.78f); // 청록
        public static readonly Color PreviewPathBlue  = new Color(0.10f, 0.42f, 0.96f); // 파랑
        // [2026-07-22] 배경을 어둡게 누른 만큼 끝단을 올렸다 — 원래 (0.09,0.11,0.52)는
        // 어두운 바탕에 그대로 잠겨서 먼 미래 잔상이 안 보였다. 남색은 유지하되 명도만 확보.
        public static readonly Color PreviewPathNavy  = new Color(0.16f, 0.24f, 0.86f); // 군청/남색
        public const float MissGlitchSeconds = 0.38f;
        public const float MissGlitchMaxAlpha = 0.58f;
        // <<< [예측 세션 추가 끝]

        // >>> [잔상 아바타 애니메이션, 2026-07-22] 잔상이 캡슐이 아니라 히어로 아바타가 되면서
        // "가만히 선 바인드 포즈"로 굳어 보이던 문제를 없애기 위해, Mixamo 클립을 특정 시각에서
        // 한 프레임만 굽는(SampleAnimation) 방식으로 포즈를 입힌다. 클립은 Resources에 복제해 둔
        // GhostRun/GhostWalk/GhostJump/GhostSlash. 아래는 그 "어느 시점을 굽느냐" 튜닝 값들.
        /// <summary>달리기 한 사이클(클립 1회 재생)이 커버하는 이동 거리(m). 경로 진행 거리를
        /// 이 값으로 나눠 클립 위상을 만든다 — 작게 잡으면 다리가 더 빨리 돈다.</summary>
        public const float GhostRunStrideMeters = 3.4f;
        // [액션 구간 잔상, 2026-07-22] 예전엔 액션 잔상이 "그 틱 한 장"뿐이고 나머지 잔상은
        // 전부 달리기 포즈였다 — 대시·런지 구간까지 달리는 걸로 보인다는 피드백. 이제 액션은
        // "틱 구간"을 가지며, 그 구간에 걸리는 잔상들은 전부 해당 액션 클립을 아래 정규화 구간에
        // 걸쳐 나눠 굽는다 → 연속으로 보면 대시/찌르기 동작이 펼쳐진다.
        // (Window는 Sim의 실제 지속 틱을 그대로 쓰지 않고 "보기 좋은 길이"로 잡은 연출 값이다.
        //  Attack 20틱 ≈ CombatConfig의 windup6+active2+recovery12, Lunge는 블링크 3틱이 너무
        //  짧아 도착 후 여운까지 포함해 넉넉히 잡는다.)
        public const int GhostAttackWindowTicks = 20;
        public const float GhostAttackFromNormalized = 0.28f;   // 스윙 시작
        public const float GhostAttackToNormalized = 0.62f;     // 임팩트 후 따라나감

        // [찌르기 전용 포즈, 2026-07-22] 런지는 Slash 클립 앞부분을 빌려 쓰다가, 전용 2키 클립
        // GhostLunge(준비 → 꽂힘, GhostDashPoseBaker가 굽는다)로 바뀌었다. 클립 전체가 곧 찌르기
        // 동작이므로 정규화 구간은 0→1을 그대로 쓴다.
        public const int GhostLungeWindowTicks = 12;
        public const float GhostLungeFromNormalized = 0f;       // 찌르기 준비(웅크려 장전)
        public const float GhostLungeToNormalized = 1f;         // 꽂히는 순간(완전히 뻗음)
        /// <summary>런지 잔상 전방 기울기(도) — 준비에서 꽂힘까지 구간에 걸쳐 이만큼 깊어진다.
        /// GhostDashPoseBaker.LungePrepPitch/LungeHitPitch와 짝이므로 포즈를 다시 구우면 같이 맞출 것.</summary>
        public const float GhostLungeFromPitch = 18f;
        public const float GhostLungeToPitch = 60f;

        public const int GhostJumpWindowTicks = 34;
        public const float GhostJumpFromNormalized = 0.14f;
        public const float GhostJumpToNormalized = 0.62f;

        /// <summary>대시 지속(연출용). Sim의 SimConfig.DashDurationTicks(24)와 맞춰둔다.</summary>
        public const int GhostDashWindowTicks = 24;
        // 방향별 대시 "몸통 눕힘". 클립(GhostDashForward/…)에는 팔다리 각도만 들어 있고, 몸 전체를
        // 눕히는 각도는 여기서 배치 회전으로 준다 — AnimationClip.SampleAnimation이 휴머노이드
        // 클립의 루트 회전(RootQ)을 적용하지 않아서 클립 안에 담을 수가 없다(실측 확인).
        // GhostDashPoseBaker가 포즈를 구울 때 전제한 각도이므로, 포즈를 다시 구우면 여기도 같이 맞출 것.
        /// <summary>앞 대시 — 스프린트 발진처럼 앞으로 깊게 눕는다(+ = 앞).</summary>
        public const float GhostDashForwardPitch = 55f;
        /// <summary>뒤 대시 — 뒤로 눕는다(− = 뒤). [피드백 반영, 2026-07-22] -40°는 "넘어지는"
        /// 그림이라 -20°로 완화 — 상체를 뒤로 힘주며 버티되 두 발은 땅 가까이 남는다.</summary>
        public const float GhostDashBackwardPitch = -20f;
        /// <summary>옆 대시 — 얼굴은 정면인 채 몸만 가는 쪽으로 기운다(roll). 앞 대시와의 결정적 차이.</summary>
        public const float GhostDashSideRoll = 30f;
        /// <summary>포즈 샘플 시각을 이 해상도(초당 스텝)로 양자화한다. 같은 칸이면 재샘플을
        /// 건너뛰므로, 움직이지 않는 정지 잔상은 사실상 한 번만 굽는다(휴머노이드 리타게팅 비용 절감).</summary>
        // [잔상 밀도 상향, 2026-07-22] 분신이 한 자리에 고정돼 포즈를 한 번만 굽게 된 뒤로는
        // 재샘플 비용이 사실상 없다 — 해상도를 올려 인접 잔상 사이 포즈 차이를 매끄럽게 만든다.
        public const float GhostPoseSampleRate = 60f;
        // <<< [잔상 아바타 애니메이션 끝]
        public const float DomeWidth     = 0.18f;
        public static readonly Color DomeColor  = new Color(0.3f, 0.9f, 1f, 0.9f);
        public static readonly Color GhostColor = new Color(0.5f, 0.9f, 1f, 0.5f);   // 정지 잔상(반투명)
        public static readonly Color StartMarkerColor = new Color(0.85f, 1f, 0.75f);  // 시작점(=나), 불투명 밝은 연두

        // 정지 포스트fx (산데비스탄 에메랄드 틴트 + 비네트)
        public const float FxSaturation      = 0f;
        public const float FxExposure        = -0.3f;
        // [2026-07-22] 흰색으로 바꿔봤다가 산데비스탄 초록 톤으로 되돌림(RadialInvert 셰이더와
        // 같은 결정). saturation=0으로 이미 흑백이라 여기 색이 그대로 화면 바탕색이 된다.
        // [2026-07-22] 잔상과 배경이 같은 초록 대역에서 겹친다는 피드백 — 배경(월드)을 더
        // 어둡고 덜 선명하게 눌러 뒤로 물린다. 잔상은 채도 높은 냉색이라 그대로 앞으로 나온다.
        public static readonly Color FxTint  = new Color(0.40f, 0.56f, 0.48f);
        public const float FxVignette        = 0.55f;
        public const float FxVignetteSmooth  = 0.65f;
        public static readonly Color FxVignetteColor = new Color(0.01f, 0.05f, 0.03f);
        public const float FxWeightSpeed     = 8f;   // 정지 진입/해제 페이드 속도

        // [2026-07-21 추가] 정지 진입 색반전(RadialInvertFeature) — 카메라 pull-back 진행률(0~1)에
        // 맞춰 화면 중심에서 원이 자라며 반전된다. MaxRadius는 화면비 보정 UV 기준 화면 대각선의
        // 절반(1인칭 중심 기준 코너까지 거리)보다 넉넉하게 잡아 초광각 화면에서도 t=1에 완전히 덮게 한다.
        public const float RadialInvertMaxRadius = 1.35f;

        // Following(자동실행) 1인칭 카메라 회전 제한(도/초) — 예측이 겨냥을 홱 바꿔도 화면이
        // 순간이동하듯 스냅되지 않고, 사용자가 지금 무슨 방향으로 도는지 눈으로 따라올 수
        // 있게 제한된 속도로 회전한다. 시간 배속(슬로모)과는 무관 — 이건 항상 실시간 그대로.
        public const float FollowingCamTurnSpeed = 300f;

        // 성공 입력부터 다음 액션 잔상까지 실제 시간 1초를 목표로 연속 보정한다.
        // [예측 세션 수정, 2026-07-21] 속도감 튜닝: GoodWindowTicks를 8→11로 넓혀 확보한
        // 여유를 슬로모 강도를 줄이는 데 쓰고, 대신 빠른/느린 구간의 대비를 키워서
        // "쭉 빠르게 이동하다 임팩트 직전 한 박자만 확 느려지는" 리듬감을 낸다.
        public const float RhythmNormalMinSeconds = 0.4f;
        public const float RhythmNormalMaxSeconds = 0.85f;
        public const float RhythmNormalReadPadding = 0.18f;
        public const float RhythmComboMinSeconds = 0.22f;
        public const float RhythmComboMaxSeconds = 0.38f;
        public const float RhythmComboReadPadding = 0.08f;
        public const float RhythmComboPositionRadius = 0.9f;
        public const int RhythmComboMaxGapTicks = 24;
        public const float RhythmMinTimeScale = 0.5f;
        public const float RhythmMaxTimeScale = 1.7f;
        public const float RhythmCurveMinSeconds = 0.12f;
        // 세그먼트 중 감속(느려지는) 구간이 앞부분까지 잠식하지 않도록, 감속 시작 지점의
        // 하한을 세그먼트의 마지막 30%로 고정한다 — 나머지 70%는 항상 빠른 스케일을 쓴다.
        public const float RhythmDecelStartFloor = 0.7f;

        // [예측 세션 추가, 2026-07-21] 이동/회전 완급 페이싱. 이벤트까지 남은 시간 기준의
        // 위 감속 커브 위에 얹히는 틱별 보정 — 대시·런지 트리거 직후 몇 틱은 스케일을 강제로
        // 확 끌어올려 "쫀득한" 스냅을 주고(오버라이드), 순수 회전(제자리 선회) 중에는 반대로
        // 낮춰서 방향 전환을 눈으로 따라올 여유를 준다(기존 target에 곱하는 감쇠).
        public const float RhythmBurstTimeScale = 2.4f;   // 대시/런지 직후 강제 스케일
        public const int   RhythmBurstTicks = 12;          // 트리거 틱 이후 이 틱 수만큼 유지
        public const float RhythmTurnTimeScale = 0.55f;    // 순수 회전 구간에 곱하는 감쇠 배율
        public const float RhythmTurnYawDegPerTick = 2.5f; // 이 이상 틱당 요 변화면 "회전 중"
        public const float RhythmTurnMoveSpeedThreshold = 1.5f; // 이 미만 이동속도(유닛/초)여야 회전으로 간주
        // 다음 판정 틱까지 이 틱 수보다 많이 남았으면 실시간 기반 감속 커브를 무시하고 최고
        // 속도로 유지한다(짧은 액션이 줄줄이 이어질 때 매번 멈췄다 가는 느낌을 없애기 위함).
        public const int RhythmApproachTicks = 20;
        // [예측 세션 추가, 2026-07-21] 판정이 한참 남은 순수 이동 구간 전용 상한 — 걷는 속도감을
        // 더 키워달라는 피드백으로 RhythmMaxTimeScale(1.7)보다 한 단계 더 빠르게 잡는다.
        public const float RhythmWalkTimeScale = 2.6f;
        // [예측 세션 수정, 2026-07-21] 0.42 → 0.6 → 0.85: 판정이 너무 빡세다는 피드백 — 이벤트
        // 도달 후 입력을 기다려주는 실시간 유예를 늘려서 Miss로 강제 전환(직접 조작行)되기까지
        // 여유를 준다. RhythmJudge.GoodWindowTicks(22)와 짝을 맞춰 재조정.
        public const float RhythmWaitGoodSeconds = 0.85f;
        // 예측 경로 확정 직후의 첫 박자는 시간 제한 없이 사용자가 원하는 순간에 직접 누른다
        // (TryConsumeFollowingInput이 pending==0일 때 Miss 판정을 걸지 않음) — 이 값은 오직
        // 접근링 연출 속도용이며 실제 입력 마감과는 무관하다.
        public const float RhythmFirstBeatDisplaySeconds = 1.2f;
        // [2026-07-22] 실행 시작 시 1인칭 시야가 내 잔상에 가려 적이 안 보인다는 피드백.
        // 주변(지나간·먼 미래) 잔상은 여러 개가 겹쳐 쌓여 불투명 벽이 되므로 알파를 낮추고,
        // 카메라 주변 잔상이 사라지는 반경(FadeNear)을 넓혀 앞쪽 시야를 비운다.
        public const float ExecutionGhostAlpha = 0.10f;      // 지나간 잔상(더 옅게)
        public const float ExecutionGhostFadeNear = 1.4f;    // 이 거리 안쪽 잔상은 사라짐(넓힘)
        public const float ExecutionGhostFadeFar = 3.6f;

        // >>> [다음 잔상 강조, 2026-07-22] Following 중 "다음에 어디로 가야 하는가"가 안 읽힌다는
        // 피드백. 예전엔 판정 대상 잔상이 alpha 0.28, 지나간 잔상이 0.16, 남은 잔상이 0.08로
        // 차이가 거의 없었고, 게다가 셋 다 ExecutionGhostFade* 근접 페이드를 그대로 먹어서
        // <b>가까워질수록 흐려졌다</b> — 목표에 도착할 때쯤 그 목표가 사라지는 구조였다.
        // 이제 판정 대상(=다음 액션)만 확실히 띄우고, 그 다음 것을 중간 밝기로 예고한다.
        /// <summary>지금 쳐야 할 액션 잔상의 기본 불투명도.</summary>
        public const float GhostNextAlpha = 0.85f;
        /// <summary>그 위에 얹히는 맥동 진폭(±). 시선을 끌되 깜빡임으로 읽히지 않을 정도.</summary>
        public const float GhostNextPulseAmplitude = 0.16f;
        /// <summary>맥동 주기(Hz). 실시간 기준 — 슬로모와 무관하게 일정하게 뛴다.</summary>
        public const float GhostNextPulseHz = 1.8f;
        /// <summary>다음 잔상에만 적용하는 근접 페이드 하한 — 코앞에 와도 이 아래로 안 흐려진다.</summary>
        public const float GhostNextProximityFloor = 0.6f;
        public const float GhostNextWhiteBlend = 0.5f;
        /// <summary>다음의 다음 액션 잔상 — "그 뒤엔 저기"를 미리 알려주는 예고 단계.</summary>
        public const float GhostAfterNextAlpha = 0.34f;
        public const float GhostAfterNextWhiteBlend = 0.2f;
        /// <summary>아직 한참 남은 잔상. [2026-07-22] 여러 개가 겹쳐 시야를 막아 더 옅게(0.07→0.04).</summary>
        public const float GhostFutureAlpha = 0.04f;
        // <<< [다음 잔상 강조 끝]
        public static readonly Color ExecutionFxTint = new Color(0.52f, 1f, 0.62f);
        public static readonly Color ExecutionFxVignetteColor = new Color(0.01f, 0.22f, 0.06f);
        public static readonly Color ExecutionPlayerColor = new Color(0.25f, 1f, 0.62f);
        public const float RhythmSidePromptAlpha = 0.38f;
        public const float ExecutionSpeedLineAlpha = 0.13f;
        public const float ExecutionSpeedLineRate = 2.8f;

        // >>> [자유 주행(Freerun), 2026-07-22] PredictionFreerun 전용 튜닝값. 위의 Rhythm* 는
        // "정해진 틱에 키를 누른다"(시간축)를 위한 값이고, 아래는 "잔상에 닿으면 터진다"
        // (공간축)를 위한 값이라 서로 안 섞인다.
        /// <summary>노드 발동 수평 반경(m). 난이도를 낮게 두는 게 목적이라 넉넉하게 잡는다.</summary>
        public const float FreerunNodeRadius = 2.2f;
        /// <summary>노드 발동 수직 허용치(m). 공중 노드는 점프 궤적을 정확히 맞출 수 없으므로
        /// 공중 적의 표준 hover 높이(AIConfig.FlyHoverOffset=2m)를 덮을 만큼 관대해야 한다.</summary>
        public const float FreerunNodeVerticalRadius = 3.0f;
        /// <summary>대상이 있는 노드가 그 적으로부터 유지하는 거리(m) — 실제 타격 거리 어림값.
        /// <see cref="FreerunNodesFollowTarget"/>가 true일 때만 쓰인다.</summary>
        public const float FreerunTargetStandoff = 1.8f;
        /// <summary>대상이 있는 노드를 그 적을 따라 움직이게 할 것인가.
        /// [2026-07-22 false로 되돌림] 이론상으론 추종이 맞지만("적이 움직이면 그 자리에 적이
        /// 없다"), 실제 플레이에서는 <b>목표가 움직이는 것 자체가 훨씬 큰 혼란</b>이었다.
        /// 예측이 보여준 그림과 실행 중 그림이 달라지면 예지를 보는 의미가 없다.</summary>
        public const bool FreerunNodesFollowTarget = false;
        /// <summary>지금 노드부터 몇 개 앞까지 발동을 허용하는가. 1이면 하나까지 건너뛸 수 있다.</summary>
        public const int FreerunLookaheadNodes = 1;
        /// <summary>노드 발동 후 다음 노드가 터지기까지의 최소 간격(틱). 런지 이동(8틱) 중
        /// 다음 노드가 겹쳐 터지는 것을 막되, 런지→평타 콤보는 살아남을 만큼 짧게.</summary>
        public const int FreerunNodeCooldownTicks = 6;
        /// <summary>평타 노드가 조준을 스냅해줄 최대 거리(m). 이 밖이면 조준을 안 건드린다.</summary>
        public const float FreerunAttackAssistRange = 4.0f;
        /// <summary>처치 확정 시 느려지는 실시간 길이(초)와 그 최저 배속.</summary>
        public const float FreerunKillSlowSeconds = 0.35f;
        public const float FreerunKillSlowScale = 0.35f;
        /// <summary>노드에 닿은 직후 느려지는 길이(초)와 배속 — "다음은 어디로"를 찾을 판독 시간.
        /// 처벌이 아니라 안내라서 매번 걸리며, 처치 슬로모보다 짧고 덜 깊다.</summary>
        public const float FreerunNodeSlowSeconds = 0.55f;
        public const float FreerunNodeSlowScale = 0.45f;

        // ── 다음 목표 안내(화면) ──
        /// <summary>안내 마름모를 노드보다 이만큼 위에 띄운다(m) — 잔상 머리 위.</summary>
        public const float FreerunGuideHeight = 2.4f;
        /// <summary>화면 가장자리 여백(px). 이 안쪽이면 "화면 안"으로 본다.</summary>
        public const float FreerunGuideEdgeMargin = 70f;
        public const float FreerunGuideSize = 26f;
        public const float FreerunGuidePulseHz = 1.6f;
        public static readonly Color FreerunGuideBright = new Color(0.45f, 1f, 0.82f, 0.95f);
        public static readonly Color FreerunGuideDim = new Color(0.25f, 0.75f, 0.62f, 0.45f);
        /// <summary>이동 키 안내에서 한 축을 "눌러야 한다"고 볼 최소 비중(전체 거리 대비).
        /// 낮을수록 대각(W+D)이 자주 뜨고, 높을수록 한 키만 뜬다.</summary>
        public const float FreerunMoveKeyGate = 0.35f;

        // ── 다음 목표 안내(월드 기둥) ──
        /// <summary>다음 노드 자리에 세우는 빛기둥의 높이·굵기(m).</summary>
        public const float FreerunBeaconHeight = 5f;
        public const float FreerunBeaconRadius = 0.22f;
        public static readonly Color FreerunBeaconColor = new Color(0.45f, 1f, 0.82f, 0.5f);
        /// <summary>잔상이 깨져 사라지는 데 걸리는 실시간(초).</summary>
        public const float FreerunShatterSeconds = 0.45f;
        /// <summary>깨지는 동안 부풀어 오르는 배율(1 → 이 값).</summary>
        public const float FreerunShatterScale = 1.8f;
        /// <summary>깨지는 동안 떠오르는 높이(m).</summary>
        public const float FreerunShatterRise = 0.7f;
        /// <summary>제한 시간 = 예측 지평 × 이 배수 + 여유(초). 직접 걸어가면 예측(최적 궤적)
        /// 보다 느릴 수밖에 없으므로 넉넉히 준다.</summary>
        public const float FreerunTimeBudgetMul = 3.0f;
        public const float FreerunTimeBudgetPad = 4.0f;
        /// <summary>마지막 노드를 소진한 뒤 여운으로 남기는 시간(초).</summary>
        public const float FreerunFinishLingerSeconds = 0.8f;
        // <<< [자유 주행 끝]

        // >>> [클릭 체인(Chain), 2026-07-22] PredictionClickChain 전용 튜닝값.
        // 튜닝 목표는 <b>클릭 간격 0.6~1.2초</b>다 — 이보다 뜸해지면 플레이어가 관객이 되고,
        // 촘촘해지면 자동 주행 구간의 질주감이 사라진다. 런지 너프 강도의 상한선 역할도 한다
        // (런지를 죽이면 경로에서 액션 노드가 줄어 클릭이 뜸해진다).

        /// <summary>포켓(조준 창)이 열려 있는 최대 실시간(초). 넘기면 실패가 아니라 자동 발동.</summary>
        public const float ClickChainPocketSeconds = 2.2f;
        /// <summary>포켓이 열린 동안의 시간 배속. 거의 정지시켜 판독 시간을 준다.</summary>
        public const float ClickChainPocketTimeScale = 0.08f;
        /// <summary>잔상 사이 자동 주행 구간의 배속. 1보다 올리면 질주감이 세진다.</summary>
        public const float ClickChainRunTimeScale = 1f;
        /// <summary>배속 변화 속도(초당). 스냅이 아니라 감속/가속으로 읽히게 한다.</summary>
        public const float ClickChainTimeScaleRate = 6f;

        /// <summary>커서로 조준하는가. false면 화면 중앙 고정 조준(카메라를 돌려야 함) —
        /// 기록 재생이 시선을 소유하므로 기본은 커서다.</summary>
        public const bool ClickChainCursorAim = true;
        /// <summary>클릭 판정 반경(화면 높이 비율). 조준 실력을 묻는 게 아니라 선택을 묻는
        /// 방식이므로 넉넉하게 잡는다.</summary>
        public const float ClickChainHitRadiusScreen = 0.072f;
        /// <summary>클릭 판정 반경 하한(px).</summary>
        public const float ClickChainHitRadiusMinPx = 46f;
        /// <summary>조준점을 잔상 발밑에서 이 높이만큼 올린다(m) — 가슴 높이가 찍기 편하다.</summary>
        public const float ClickChainAimHeight = 1.1f;

        public const float ClickChainReticlePulseHz = 2.2f;
        public static readonly Color ClickChainReticleBright = new Color(0.49f, 1f, 0.82f, 0.95f);
        public static readonly Color ClickChainReticleDim = new Color(0.28f, 0.78f, 0.66f, 0.4f);
        // <<< [클릭 체인 끝]

        // >>> [자석 주행(Magnet), 2026-07-22] PredictionMagnetRun 전용.
        /// <summary>포획 반경(m). 자유 주행(2.2)보다 훨씬 넉넉한 게 이 모드의 핵심 —
        /// 대시로 지나치거나 살짝 빗나가도 잡히게 한다.</summary>
        public const float MagnetCaptureRadius = 4.2f;
        public const float MagnetCaptureVerticalRadius = 4.0f;
        /// <summary>이동 입력을 노드 쪽으로 섞기 시작하는 거리(m).</summary>
        public const float MagnetSteerRadius = 6.5f;
        /// <summary>최대 유도 강도(0~1). 1이면 조작을 완전히 뺏으므로 절반 이하로 둔다.</summary>
        public const float MagnetSteerStrength = 0.45f;
        /// <summary>이 크기 이상 이동 입력이 있을 때만 유도한다 — 서 있는데 끌려가면 안 된다.</summary>
        public const float MagnetSteerMinInput = 0.04f;

        /// <summary>공유 게이지가 초당 닳는 양(0~1 기준). 1/이 값 = 아무것도 안 했을 때 버티는 초.</summary>
        public const float MagnetGaugeDrainPerSecond = 0.085f;
        /// <summary>노드 하나를 소진할 때 돌려받는 양.</summary>
        public const float MagnetGaugeNodeRefund = 0.11f;
        /// <summary>처치 확정 시 돌려받는 양 — "잘 이으면 더 오래 본다".</summary>
        public const float MagnetGaugeKillRefund = 0.16f;

        /// <summary>노드에 닿았을 때 다음 노드 쪽으로 시선을 돌리는 시간(초).</summary>
        public const float MagnetTurnSeconds = 0.28f;
        /// <summary>이 각도(도) 미만이면 굳이 안 돌린다 — 미세하게 튀는 게 더 거슬린다.</summary>
        public const float MagnetTurnMinDegrees = 12f;

        public const float MagnetNodeSlowSeconds = 0.3f;
        public const float MagnetNodeSlowScale = 0.55f;

        public static readonly Color MagnetGaugeBack = new Color(0.06f, 0.12f, 0.11f, 0.7f);
        public static readonly Color MagnetGaugeHigh = new Color(0.45f, 1f, 0.82f, 0.9f);
        public static readonly Color MagnetGaugeLow = new Color(1f, 0.42f, 0.38f, 0.95f);
        // <<< [자석 주행 끝]

        // >>> [난타(Drum), 2026-07-22] PredictionDrumRhythm 전용.
        // [실측 기준, 2026-07-22] 실제 3초 경로의 액션 마커는 13개 · 간격 5~20틱이다
        // (초당 4.7액션). 아래 값들은 그 사이에 연결 노트가 실제로 들어가도록 맞춘 것 —
        // 이 전제가 바뀌면(런지 너프로 액션이 줄면) 다시 재야 한다.
        /// <summary>연결 노트 간격(틱) — 경로 시작 구간. 클수록 헐겁다(12틱=0.2초).</summary>
        public const float DrumLinkIntervalStart = 12f;
        /// <summary>연결 노트 간격(틱) — 경로 끝 구간. 후반일수록 촘촘해진다.</summary>
        public const float DrumLinkIntervalEnd = 6f;
        /// <summary>구간별 ±변주 폭(틱).</summary>
        public const float DrumLinkJitter = 2f;
        /// <summary>간격 하한(틱). 이보다 촘촘하면 사람이 칠 수 없다(5틱 ≈ 0.083초).</summary>
        public const int DrumLinkIntervalMin = 5;
        /// <summary>액션 노트와 연결 노트 사이 최소 간격(틱). 붙으면 A/B를 구분할 시간이 없다.</summary>
        public const int DrumMinSeparationTicks = 4;

        /// <summary>노트가 화면에 나타나 중심까지 오는 시간(틱). 90틱 = 1.5초.</summary>
        public const float DrumLookaheadTicks = 90f;
        public const float DrumPerfectWindowTicks = 4f;
        public const float DrumGoodWindowTicks = 10f;

        public const int DrumScorePerfect = 300;
        public const int DrumScoreGood = 150;
        public const int DrumScoreComboStep = 10;

        public const float DrumActionNoteSize = 26f;
        public const float DrumLinkNoteSize = 15f;
        public static readonly Color DrumActionNoteColor = new Color(1f, 0.77f, 0.42f, 1f);
        public static readonly Color DrumLinkNoteColor = new Color(0.49f, 1f, 0.82f, 1f);
        public static readonly Color DrumHitRingColor = new Color(0.6f, 0.9f, 0.85f, 0.55f);
        // <<< [난타 끝]

        // >>> [3인칭 관전(모드 9), 2026-07-22] 실행 중 3인칭 궤도 카메라.
        /// <summary>움직이는 플레이어 뒤 거리(m).</summary>
        public const float ThirdPersonDistance = 5.2f;
        /// <summary>내려다보는 각도(도).</summary>
        public const float ThirdPersonPitch = 16f;
        /// <summary>카메라가 진행 방향을 따라가는 부드러움(작을수록 빠르게 붙는다).</summary>
        public const float ThirdPersonYawSmooth = 0.18f;
        /// <summary>피벗 높이(m).</summary>
        public const float ThirdPersonPivotY = 1.5f;
        /// <summary>본체 색 — 잔상(반투명)과 달리 불투명해야 "내가 저기 있다"로 읽힌다.</summary>
        public static readonly Color ThirdPersonBodyColor = new Color(0.82f, 0.95f, 0.92f, 1f);
        // <<< [3인칭 관전 끝]

        // >>> [슬로우 조준(Slow Aim), 2026-07-22] PredictionSlowAim 전용.
        // 설계 기준은 "초보자도 할 수 있게" — 감속은 미리·천천히, 판정은 널널하게,
        // 시간 압박은 게이지로만.

        /// <summary>노드에 이만큼 남았을 때부터 미리 감속을 시작한다(틱). 30틱=0.5초.</summary>
        public const int SlowAimSlowLeadTicks = 30;
        /// <summary>접근 구간(감속 중) 배속.</summary>
        public const float SlowAimApproachTimeScale = 0.42f;
        /// <summary>조준 포켓 배속. [2026-07-22 완화] 0.06은 "아예 멈춰 있는 듯"으로 읽혀서
        /// 올렸다 — 적이 다가오는 게 눈에 보여야 조준에 긴장이 생긴다.</summary>
        public const float SlowAimPocketTimeScale = 0.18f;
        /// <summary>노드 사이 주행 배속.</summary>
        public const float SlowAimRunTimeScale = 1f;
        /// <summary>[2026-07-22] 좌·우·뒤(그리고 앞) 대시가 재생되는 동안의 배속. 대시는 몇 틱
        /// 안에 멀리 이동해서 1배속으로 재생하면 "순간이동"처럼 보인다 — 이 구간만 시간을 느리게
        /// 흘려 "옆으로 대시했다"가 눈에 읽히게 한다.</summary>
        public const float SlowAimDashTimeScale = 0.32f;
        /// <summary>대시 발동 후 이만큼의 틱 동안 <see cref="SlowAimDashTimeScale"/>로 느리게 본다.
        /// 대시 지속(sim)보다 넉넉히 잡아 이동이 끝까지 보이게 한다.</summary>
        public const int SlowAimDashViewTicks = 14;
        /// <summary>느려지는 속도(초당 배속 변화). 작을수록 부드럽게 브레이크가 걸린다.</summary>
        public const float SlowAimSlowDownRate = 1.6f;
        /// <summary>빨라지는 속도. 감속보다 훨씬 커야 "클릭 → 시원하게 터진다"가 된다.</summary>
        public const float SlowAimSpeedUpRate = 6.5f;

        /// <summary>
        /// 런지 각도 허용치(도). 대시·점프는 각도를 아예 묻지 않는다 — 방향이 기록된 cmd
        /// (dashDirection + 그 순간 yaw)로 정해지므로 사용자 회전이 결과를 못 바꾼다.
        /// 판정할 수 없는 걸 판정하는 척하지 않기 위해 게이트 자체를 뺐다.
        /// </summary>
        public const float SlowAimToleranceStrike = 45f;

        /// <summary>조준 포켓이 열린 동안 예지 게이지가 초당 닳는 양(0~1).
        /// 0.06이면 아무것도 안 해도 ~16초 버틴다 — 시간 스트레스를 주지 않는 게 목적.</summary>
        public const float SlowAimGaugeDrainPerSecond = 0.06f;

        /// <summary>이 틱 안에 붙어 있는 점프 두 개는 더블 점프로 보고 한 노드로 합친다.
        /// 각각 슬로우를 걸면 답답하다는 요구 사항.</summary>
        public const int SlowAimDoubleJumpMergeTicks = 30;

        /// <summary>노드에 이만큼 남았을 때 조준 포켓(키 표시·자유 회전)이 열린다(틱).
        /// <see cref="SlowAimSlowLeadTicks"/>보다 작아야 "감속 → 그 다음 조준" 순서가 된다.
        /// 이 구간에서는 재생을 막지 않으므로 sim이 느리게나마 계속 흐른다.</summary>
        public const int SlowAimPocketLeadTicks = 14;

        /// <summary>잔상이 깨지기 시작하는 거리(m). 이보다 가까워지면 서서히 부서진다.</summary>
        public const float SlowAimGhostClearRadius = 3.2f;
        /// <summary>완전히 사라지는 거리(m). 이 안쪽이면 시야를 가리므로 숨긴다.</summary>
        public const float SlowAimGhostBreakRadius = 1.4f;

        // [2026-07-22] 목표 잔상은 훨씬 좁은 반경을 쓴다. 액션 간격이 0.2초라 다음 노드는
        // 늘 3m 안쪽이고, 위 반경(3.2m)을 그대로 쓰면 목표가 이동 내내 흐려져 "누를 때가
        // 돼서야 흰색이 되는" 것처럼 보인다. 시야를 가리는 건 지나친 잔상들이다.
        /// <summary>목표 잔상이 깨지기 시작하는 거리(m).</summary>
        public const float SlowAimNextGhostClearRadius = 1.5f;
        /// <summary>목표 잔상이 사라지는 거리(m) — 사실상 겹쳤을 때만.</summary>
        public const float SlowAimNextGhostBreakRadius = 0.7f;

        // [핵심 수정, 2026-07-22] 실측 액션 간격이 5~20틱인데 감속을 30틱 전부터 걸어서
        // 사실상 항상 슬로우가 켜져 있었다("액션 취할 때 계속 슬로모션" 증상). 앞 노드와
        // 이만큼 벌어진 노드만 탐색(감속·조준) 대상으로 삼고, 붙어 있는 건 연타로 잇는다.
        /// <summary>탐색 노드로 볼 최소 간격(틱). 이보다 붙어 있으면 슬로우 없이 연타.</summary>
        public const int SlowAimMinGapTicks = 26;
        /// <summary>연타 노드의 키 표시가 미리 뜨는 시간(틱). 짧아야 흐름이 안 끊긴다.</summary>
        public const int SlowAimQuickPocketLeadTicks = 5;

        /// <summary>액션 발동 후 이 틱 동안은 무조건 정상 속도 이상 — 절대 안 느려진다.
        /// 액션 간격이 20틱 남짓이라 이 값이 크면 감속 구간이 안 남는다(=조준이 완전 정지에서만
        /// 일어남). 액션 재생을 덮을 만큼만 짧게.</summary>
        public const int SlowAimBurstTicks = 8;
        /// <summary>잔상과 딱 맞췄을 때의 가속 배속.
        /// [2026-07-22] 액션 사이 이동이 "너무 빠르다(순간이동 같다)"는 피드백 — 예측 안 썼을 때와
        /// 같은 이동 속도를 원함. 1.0으로 두어 발동 후에도 정상 속도로만 재생한다(가속 보상 제거).</summary>
        public const float SlowAimBurstBoost = 1f;
        /// <summary>이 각도(도) 안이면 정타 — 가속 + 금색 연출.</summary>
        public const float SlowAimPerfectAngle = 14f;

        /// <summary>성공 순간 링이 터지는 시간(초).</summary>
        public const float SlowAimHitFlashSeconds = 0.3f;

        // [2026-07-22 롤백] 목표 잔상을 흰색으로 덮어쓰던 방식은 경로 그라데이션과 따로 놀아
        // 어색했다 — 색은 기본 규칙(그라데이션 + GhostNextPulse* 맥동)에 맡기고, 위치는
        // 아래 화면 표지로 알린다.
        /// <summary>목표 표지가 깜박이는 속도(Hz).</summary>
        public const float SlowAimMarkerBlinkHz = 1.8f;
        public static readonly Color SlowAimMarkerBright = new Color(1f, 1f, 1f, 0.95f);
        public static readonly Color SlowAimMarkerDim = new Color(0.85f, 0.9f, 1f, 0.28f);
        /// <summary>화면 밖 화살표를 가장자리에서 이만큼 안쪽에 둔다(px).</summary>
        public const float SlowAimMarkerEdgeMargin = 74f;
        // <<< [슬로우 조준 끝]
    }
}
