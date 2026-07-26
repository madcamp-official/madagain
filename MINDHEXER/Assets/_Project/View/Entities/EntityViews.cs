using System.Collections.Generic;
using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Sim 상태를 캡슐 GameObject로 비추는 거울. 1인칭이라 플레이어 몸은 렌더 끔.
    /// 콜라이더는 전부 제거 — 충돌 판정은 Sim이 CapsuleCast로 지형에만 한다.
    /// </summary>
    public class EntityViews
    {
        /// <summary>[2026-07-22] 예측 미리보기처럼 sim이 얼어 있는 프레임인가. Main이 매 프레임 채운다.
        /// 켜지면 몹 Animator를 정지(speed=0)시킨다 — 세계가 멈췄는데 다리만 계속 걷던 버그 수정.
        /// (viewSpeed가 0이어도 재생 배속이 하한 0.6에 붙어 클립이 계속 돌던 것이 원인.)</summary>
        public static bool SimFrozen;

        public Transform PlayerAnchor { get; private set; }
        readonly List<Transform> enemyViews = new List<Transform>();
        /// <summary>몹 뷰 Transform(읽기 전용). 인덱스는 SimWorld.enemies와 대응. 런지 타깃 윤곽 연출이 읽는다.</summary>
        public IReadOnlyList<Transform> EnemyViews => enemyViews;
        readonly List<ViewKind>  viewKinds     = new List<ViewKind>();
        readonly List<Renderer>  viewRenderers = new List<Renderer>();   // 틴트용, 캐시(Charge는 자식에 있음)
        readonly List<Animator>  viewAnimators = new List<Animator>();   // Charge만 채워짐, 그 외 null
        readonly List<float>     viewYaw       = new List<float>();      // Charge 몸통 회전 스무딩용(요철 방지)
        readonly List<EnemyMotion> viewMotion  = new List<EnemyMotion>();  // 절차 레이어(시선 추적) 개체 상태
        readonly List<EnemyGlow>   viewGlow    = new List<EnemyGlow>();    // 발광(어두운 맵 식별 + 상태 텔레그래프)
        readonly List<int>         viewPrevHp  = new List<int>();          // 피격 감지용
        readonly List<int>         viewSpawnedId = new List<int>();        // 슬롯의 현재 몹 id — 바뀌면 새 몹
        readonly List<int>         viewRevealedId = new List<int>();       // 이미 실체화 재생한 몹 id(박스당 1회 방지)
        readonly List<float>       viewSpawnTime  = new List<float>();      // 스폰(숨김) 시각(Time.time). 박스 못 닿아도 타임아웃 실체화용
        readonly List<FootstepDetector> viewFoot = new List<FootstepDetector>();  // 발 딛는 순간 감지

        // ── 발자국 스파크·소리 ──
        static FootstepSettings footCfg = FootstepSettings.Default;
        public static ref FootstepSettings Footstep => ref footCfg;
        readonly List<RustyJoints> viewRusty   = new List<RustyJoints>();  // 녹슨 관절(고착+스프링)
        readonly List<EnemyPose>   viewPose    = new List<EnemyPose>();    // 상태 텔레그래프(예비·차징·피격)
        readonly List<EnemyMove>   viewMove    = new List<EnemyMove>();    // 이동·상황 자세(급선회·휘청·뱅킹 등)
        readonly List<ChargeAnim>  viewCharge  = new List<ChargeAnim>();   // 돌진 전신 순수 절차(준비·돌진·이후)

        // ── 돌진 전신 절차 ──
        /// <summary>전역 on/off — 끄면 돌진 단계에서도 클립(Walk/Run)이 그대로 나온다.</summary>
        public static bool ChargeAnimEnabled = true;
        static ChargeAnimSettings chargeSettings = ChargeAnimSettings.Default;
        public static ref ChargeAnimSettings Charge => ref chargeSettings;

        // ── 이동·상황 자세 ──
        /// <summary>전역 on/off — 콘솔 mv. 생동감 레이어라 텔레그래프와 따로 끈다.</summary>
        public static bool MoveposeEnabled = true;
        static EnemyMoveSettings moveSettings = EnemyMoveSettings.Default;
        public static ref EnemyMoveSettings Movepose => ref moveSettings;

        // ── 상태 텔레그래프 (예비·준비·차징·피격) ──
        /// <summary>전역 on/off — 콘솔 tele. 예지의 판단 근거이기도 하므로 기본 켜짐.</summary>
        public static bool PoseEnabled = true;
        static EnemyPoseSettings poseSettings = EnemyPoseSettings.Default;
        public static ref EnemyPoseSettings Pose => ref poseSettings;
        // ★ 실제 수평 속도(m/s). e.vel.x/z는 항상 0이다(EnemyMovement가 "이동은 변위로 처리"한다).
        //   그래서 틱 차분(world - prevWorld)으로 직접 잰다. Sync에서 채우고 LateSync가 읽는다.
        readonly List<float>       viewSpeed   = new List<float>();
        // 재생 배속 전용 <b>저역통과(스무딩)</b> 속도. 경사에서 틱마다 진행량이 요동쳐도 다리 재생이 안 끊기게.
        // viewSpeed(방향·idle 판정용)는 그대로 두고, 배속 입력만 이걸 쓴다. 접지 시 3D 속도(경사면 실거리) 반영.
        readonly List<float>       viewAnimSpeed = new List<float>();
        readonly List<float>       viewAirTime   = new List<float>();     // IsAirborne 디바운스용(연속 공중 시간)
        readonly List<Vector3>     viewDir     = new List<Vector3>();      // 실제 이동 방향(단위)
        readonly List<float>       viewVertSpeed = new List<float>();      // 수직 속도(공중몹)
        // 조준 중 상체 비틀기 — 발은 viewAimBase에 고정하고 차이를 척추에 준다
        readonly List<float>       viewAimBase  = new List<float>();
        readonly List<float>       viewAimTwist = new List<float>();
        readonly List<bool>        viewAimWas   = new List<bool>();
        // 애니메이터가 가진 파라미터 캐시 — "클립을 나중에 넣으면 코드 수정 없이 자동으로 켜진다".
        // HasParam은 매 프레임 돌리기엔 비싸므로 개체 생성 시 1회만 조사한다.
        readonly List<AnimCaps>    viewCaps    = new List<AnimCaps>();

        // ── 녹슨 관절 ──
        /// <summary>전역 on/off — 콘솔 rust. ★ 기본 꺼짐(확인 후 켠다).</summary>
        public static bool RustEnabled = false;
        static RustyJointSettings rustSettings = RustyJointSettings.Default;
        public static ref RustyJointSettings Rust => ref rustSettings;
        // 종류별 on/off — 아직 확정 전이라 하나씩 껐다 켜며 본다(콘솔 rust melee|ranged|charge).
        // ★ 근(Ground)과 근층(Traversal)은 뷰가 같은 ViewKind.Melee라 함께 적용된다(원/원층도 동일).
        public static bool RustMelee  = true;
        public static bool RustRanged = true;
        public static bool RustCharge = true;

        /// <summary>이 뷰 종류에 녹슨 관절을 적용하는가.</summary>
        static bool RustApplies(ViewKind k) =>
              k == ViewKind.Melee  ? RustMelee
            : k == ViewKind.Ranged ? RustRanged
            : k == ViewKind.Charge ? RustCharge
            : false;   // 캡슐·비행은 본이 없거나 별도 설계

        // ── 발광 ──
        /// <summary>전역 on/off — 콘솔 glow.</summary>
        public static bool GlowEnabled = true;
        static EnemyGlowSettings glowSettings = EnemyGlowSettings.Default;
        public static ref EnemyGlowSettings Glow => ref glowSettings;
        // 오염·반사(셰이더 프로퍼티) — F8 패널에서 조절하면 몹 전체에 즉시 반영
        static EnemyDirtSettings dirtSettings = EnemyDirtSettings.Default;
        public static ref EnemyDirtSettings Dirt => ref dirtSettings;
        static readonly System.Random glowRng = new System.Random(12345);

        // ── 절차: 시선 추적 튜닝 (몹 종류별) ──
        /// <summary>전역 on/off — 콘솔에서 끄고 켠다.</summary>
        public static bool LookAtEnabled = true;
        static EnemyLookSettings lookBiped  = EnemyLookSettings.Default;
        static EnemyLookSettings lookCharge = new EnemyLookSettings
        {   // 돌진몹은 조금 둔하게 — 몸통은 덜 돌지만 머리는 크게 돈다(로봇 목)
            weight = 1f, maxYaw = 160f, maxPitch = 70f,
            torsoMaxYaw = 30f, torsoMaxPitch = 20f, turnSpeed = 300f,
            spine2Share = 0.6f, spine1Share = 0.4f,
        };
        public static ref EnemyLookSettings BipedLook  => ref lookBiped;
        public static ref EnemyLookSettings ChargeLook => ref lookCharge;

        static readonly Color ChaseColor     = new Color(1f, 0.4f, 0.3f);   // 추격 (빨강)
        static readonly Color LeapColor      = new Color(1f, 0.9f, 0.2f);   // 절벽 도약 중 (노랑)
        static readonly Color HitColor       = new Color(0.5f, 0.05f, 0.05f); // 피격/스턴 (검붉은)
        static readonly Color WindupColor    = new Color(1f, 0.95f, 0.4f);    // 공격 선딜 텔레그래프 (밝은 노랑)
        static readonly Color AttackColor    = new Color(1f, 0.15f, 0.05f);   // 타격 순간 (강렬 빨강)

        // 몹 시각 종류. Traversal은 아직 실제 모델이 없어 캡슐 유지.
        enum ViewKind { Capsule, Flying, Charge, Melee, Ranged, Orb }


        /// <summary>
        /// 이 애니메이터가 어떤 파라미터를 갖고 있는지 — <b>클립을 나중에 붙여도 코드를 안 고치게</b> 하는 장치.
        ///
        /// 없는 파라미터에 SetBool을 부르면 Unity가 매 프레임 경고를 쏟는다. 그렇다고 하드코딩으로
        /// "근접은 점프 없음"이라 적어두면, 나중에 클립을 넣어도 코드를 고쳐야 켜진다.
        /// 그래서 생성 시 1회 조사해 두고 <b>있으면 자동으로 구동</b>한다.
        ///
        /// → 팀원이 할 일: 클립 FBX 추가 + 컨트롤러에 상태·파라미터 추가. <b>코드는 안 건드려도 된다.</b>
        /// </summary>
        struct AnimCaps
        {
            public bool airborne;    // IsAirborne — 점프/낙하
            public bool running;     // IsRunning  — 걷기/달리기 전환
            public bool hurt;        // IsHurt     — 피격
            public bool dead;        // IsDead     — 사망
            public bool moving;      // IsMoving   — 이동/정지(Idle) 전환. 있으면 컨트롤러가 Idle을 갖는 것으로 본다
            public bool probed;

            public static AnimCaps Probe(Animator a)
            {
                var c = new AnimCaps { probed = true };
                if (a == null || a.runtimeAnimatorController == null) return c;
                foreach (var p in a.parameters)
                {
                    if (p.type != AnimatorControllerParameterType.Bool) continue;
                    switch (p.name)
                    {
                        case "IsAirborne": c.airborne = true; break;
                        case "IsRunning":  c.running  = true; break;
                        case "IsHurt":     c.hurt     = true; break;
                        case "IsDead":     c.dead     = true; break;
                        case "IsMoving":   c.moving   = true; break;
                    }
                }
                return c;
            }
        }

        // 걷기→달리기 전환 기준 속도(m/s). IsRunning 파라미터가 있는 컨트롤러에서만 쓰인다.
        // ※ FootSoldier_Run.fbx 는 이미 있으나 컨트롤러에 Run 상태가 없어 놀고 있다.
        //   컨트롤러에 IsRunning + Run 상태만 추가하면 이 값으로 바로 동작한다.
        const float RunThresholdSpeed = 4.2f;
        // IsAirborne 디바운스 시간(초). grounded가 이 시간 이상 연속 false여야 Jump로 본다.
        // 경사에서 1~2틱(<0.05s) 깜빡이는 건 무시하고, 진짜 점프·낙하(>0.15s)만 잡는다.
        const float AirborneDebounceTime = 0.1f;

        // 스폰 후 재생 박스(SpawnRevealVolume)에 이 시간(게임 초) 안에 못 닿으면 박스에 닿은 셈 치고
        // 강제로 실체화한다. 경로 문제로 몹이 박스를 못 지나 영영 투명해지는 것을 막는 안전장치.
        const float SpawnRevealTimeout = 4f;

        // 정지(Idle) 판정 속도(m/s). 이 미만이면 "서 있음"으로 보고 IsMoving=false를 던진다.
        //   컨트롤러에 IsMoving+Idle이 있으면 Idle 클립이 재생되고,
        //   아직 없으면(임시) 걷기 클립을 정지(anim.speed=0)시켜 제자리걸음을 막는다.
        const float IdleSpeedThreshold = 0.2f;
        // 배속 스무딩 속도(1/초). 클수록 빨리 따라가고 작을수록 부드럽다. 4 ≈ 시정수 0.25초(저주파 변동까지 뭉갬).
        const float AnimSpeedSmoothRate = 4f;

        // FlyingEnemy/ChargeEnemy/MeleeEnemy/RangedEnemy 프리팹: 래퍼 루트(스케일 1, yaw만 회전) + 자식이 임포트 시
        // 축변환·스케일을 그대로 들고 1m 기준으로 맞춰져 있음. Sync에서 wrapper.localScale = Vector3.one * e.height
        // 로 개체별 크기(대형몹 3배 등)를 반영한다.
        static GameObject flyingPrefab, chargePrefab, meleePrefab, rangedPrefab;
        static bool prefabsLoaded;

        // 돌진몹 원점은 모델 피벗이 발밑보다 살짝 위라 그냥 두면 발이 땅속에 파묻힘.
        // ★ Play 모드에서 Walk+Run 전체 사이클을 프레임별로 샘플링해 실측한 값(최저점 -0.182, 편차 거의 없음).
        //   이전의 0.24는 여유를 과하게 잡아 반대로 살짝 공중에 뜨는 원인이 됐다.
        const float ChargeFeetLift = 0.182f;
        // 근접/원거리 병사도 같은 방식(Play 모드에서 Walk+공격 사이클 전체 샘플링)으로 실측.
        // ★ applyRootMotion=true였을 때 잰 값(0.132)은 루트모션 드리프트가 섞여 부풀려진 값이었다 —
        //   그걸 끄고 나서 재니 실제로는 이만큼만 필요했다(계속 가라앉던 원인도 같은 버그).
        const float MeleeFeetLift  = 0.073f;
        const float RangedFeetLift = 0.128f;   // ★ 마찬가지로 applyRootMotion 끈 뒤 재측정한 값.

        /// <summary>
        /// 발 높이 미세 보정(m). <b>렌더만</b> 내리고 히트박스·판정은 그대로다.
        ///
        /// 위 FeetLift 실측값은 모델을 1배로 그리던 시절 기준이라, 렌더 배율(1.4배)이 붙은 뒤
        /// 보정도 같이 곱해지면서 살짝 과해졌다 — 그래서 몹이 아주 조금 떠 보인다.
        /// 여기서 빼서 맞춘다(음수 = 내림). 콘솔 <c>feet</c>로 실시간 조절.
        /// </summary>
        /// 실측으로 확정한 값 — 렌더 배율(1.4배)까지 반영하면 이만큼 내려야 발이 바닥에 붙는다.
        public const  float FeetTrimDefault = -0.3f;   // 눈으로 맞춘 값(콘솔 feet reset이 여기로 돌아온다)
        public static float FeetTrim = FeetTrimDefault;

        // Monolith(원거리) Aim(Gunplay) 포즈는 총을 몸통 정면 기준 몹의 왼쪽으로 약 45° 든다(레이즈드
        // 아이밍 스탠스). e.yaw로 몸통은 플레이어를 향하므로, 조준 중에만 몸을 +45° 돌려 총구를
        // 플레이어에 맞춘다(플레이 모드 실측·렌더로 확정 — 총열이 정확히 정면을 향하는 각). 몸통은 살짝
        // 비스듬해지지만 총구는 정확히 플레이어 조준. 여전히 어긋나면 이 값만 조정.
        const float RangedAimYawOffset = 45f;

        // 돌진몹만 시각적으로 더 크게(가독성/위협감) — 히트박스(e.radius/e.height)는 그대로, 렌더 크기만 배율.
        // ★ 1.8배까지 키웠더니 일반 크기 몹이 실제 키 3m를 넘어 충돌 캡슐(지름 ~1.15m)보다 훨씬 넓어져
        //   벽 옆을 지날 때 팔/어깨가 뚫고 나가는 문제가 생겼다 — 히트박스는 그대로 두기로 하고 시각 배율을 낮춤.
        //   Dismemberment.ChargeVisualScaleMul과 반드시 같은 값으로 유지(시체 크기와 안 맞으면 어색해짐).
        const float ChargeVisualScaleMul = 1.4f;
        // 다른 몹도 같은 이유로 렌더만 키운다(히트박스는 그대로 → 모델 가장자리는 판정에 안 걸린다).
        // ★ Dismemberment 쪽 같은 이름 상수와 값을 맞춰야 죽는 순간 시체 크기가 안 튄다.
        const float MeleeVisualScaleMul  = 1.4f;
        const float RangedVisualScaleMul = 1.4f;
        // 공중몹은 원점이 발밑이 아니라 모델 중심이라, 키우면 위아래로 균등하게 커진다
        // (= 아랫부분이 그만큼 더 내려온다). 그래서 배율을 낮게 잡는다.
        const float FlyingVisualScaleMul = 1.2f;

        // Walk/Run 클립은 제자리 걸음(루트 모션 없음)이라 "이 클립이 가정하는 실제 보폭 속도"를 몰라
        // 임의로 가정한 값(m/s) — 실제 이동 속도와 비교해 재생 속도를 맞추는 데만 쓴다. 미끄러져 보이면 이 값을 낮추고,
        // 다리가 너무 빨리 움직이면 값을 올린다.
        // ★ <b>실측값</b> — Tools/몹/걷기 속도 측정 (2026-07-22).
        //   방법: 클립을 프레임 샘플링해 "접지한 발이 뒤로 간 거리"를 적분 → ÷클립길이 → ×렌더스케일(2.01).
        //   프리팹은 정확히 1m로 정규화돼 있어 스케일 환산이 확정적이다.
        //
        //     근접 걷기 (walking_man, 1.033s)  0.66 × 2.01 = 1.33 m/s
        //     돌진 걷기 (walking_man, 1.033s)  0.41 × 2.01 = 0.83 m/s   ← 같은 클립인데 보폭이 다름(리타게팅 차이)
        //     돌진 달리기 (running,   0.633s)  1.04 × 2.01 = 2.08 m/s
        //   ※ 원거리(Monolith)는 보폭이 0.01로 <b>사실상 제자리 클립</b>이라 측정 불가 →
        //     걷기 기준값은 근접 것을 공유한다(어차피 같은 WalkClipPace를 쓴다).
        //
        //   예전 값(1.8 / 4.0)은 추측이었고 실측보다 훨씬 커서, 배속이 하한(0.6)에 붙어
        //   발이 계속 미끄러졌다. 콘솔 pace 로 실시간 재조절 가능.
        public static float WalkClipPace = 1.33f;
        public static float RunClipPace  = 2.08f;
        // 평소 걷기는 재생속도 범위를 좁게 잡아 다리가 실제 이동 속도보다 서두르지 않게(육중한 느낌).
        // 돌진 스프린트(Run)는 실제 속도 폭이 넓어(전속 14 vs 평소 2.1) 넓은 범위가 그대로 필요.
        // 분모(ClipPace)가 실측값이 되면서 배속이 실제 비율을 따라간다.
        // 근접 이동 6 m/s ÷ 1.33 = 4.5배 — 예전 상한 1.5로는 잘려서 여전히 미끄러진다.
        // 상한을 열어 실제 비율이 나오게 하되, 다리가 팽이처럼 돌지 않을 선에서 막는다.
        // ※ 여기서 잘린다는 건 "이동 속도가 클립이 감당할 범위를 넘었다"는 뜻이다 —
        //   그때는 이동 속도를 낮추거나 Run 클립으로 전환해야 한다(IsRunning 배선 완료).
        public static float WalkSpeedClampMin = 0.6f, WalkSpeedClampMax = 3.0f;
        public static float RunSpeedClampMin  = 0.5f, RunSpeedClampMax  = 4.0f;

        // 걷는 동안 실제 속도 벡터를 그대로 몸통 방향에 쓰면 분리(separation) 스티어링의 매 틱 잔떨림이
        // 그대로 회전으로 튀어나와 "대각선으로 홱홱 도는" 느낌이 났다. 초당 최대 회전각을 제한해 부드럽게 돈다.
        const float BodyTurnDegPerSec = 420f;
        // 이 속도 이상일 때만 몸통을 이동 방향으로 돌린다. 너무 낮으면 제자리 미세 이동
        // (분리 스티어링 잔떨림)에도 몸이 홱홱 돌아간다.
        const float BodyFaceMinSpeed = 0.5f;

        // ── 조준 중 상체 비틀기 ──
        // 조준 중엔 발이 땅에 고정(Plant)되는데 몸 전체를 회전시키면 발이 제자리에서 미끄러진다.
        // 실제로는 발은 두고 <b>상체만 비틀어</b> 총구를 따라간다.
        //   · 발 방향(viewYaw)은 조준 시작 시점으로 고정
        //   · 총구가 향해야 할 각도와의 차이를 척추에 비틀림으로 준다
        //   · 한계를 넘으면 그때만 발 방향이 천천히 따라간다(제자리 스텝)
        const float AimTwistMax      = 55f;    // 상체가 비틀 수 있는 최대 각
        const float AimFootCatchUp   = 90f;    // 한계 초과 시 발이 따라 도는 속도(도/초)

        // ── 파손 변형 (부위가 떨어져 전선으로 대롱거리는 개체) ──
        /// <summary>파손 개체가 나올 확률. 0=전부 멀쩡, 1=전부 파손.
        /// ★ 기본 0 — 절단 부위 연출은 보류. 콘솔 dmg 로 켜서 실험한다.</summary>
        public static float DamagedChance = 0f;
        /// <summary>지정하면 그 변형만 나온다(콘솔 테스트용). 예: "MeleeEnemy_d02". 빈 문자열=무작위.</summary>
        public static string ForceVariant = "";
        /// <summary>전선 설정 — 짧게 대롱거리는 정도(바닥에 끌리지 않는다).</summary>
        public static DamagedPart WireCfg = new DamagedPart
        {
            length = 0.45f, particles = 7, rootRadius = 0.016f, tipRadius = 0.008f,
            // 덜 출렁이게 — 감쇠를 높이고(속도가 빨리 죽음) 끝 무게를 낮춘다.
            damping = 0.90f, gravity = -9.8f, groundFriction = 0f,
            tipWeight = 1.2f, stiffness = 2,
            sparks = true, sparkInterval = 1.1f,
        };

        static readonly Dictionary<string, List<GameObject>> variantCache = new Dictionary<string, List<GameObject>>();
        static readonly System.Random damageRng = new System.Random(777);

        /// <summary>파손 변형을 확률로 고른다. 없으면 원본.</summary>
        static GameObject PickPrefab(string baseName, GameObject fallback)
        {
            var list = Variants(baseName);
            if (list.Count == 0) return fallback;

            // 특정 변형 강제(테스트) — 이름이 정확히 맞는 것만
            if (!string.IsNullOrEmpty(ForceVariant))
            {
                if (ForceVariant == "off") return fallback;
                foreach (var v in list) if (v.name == ForceVariant) return v;
                if (!ForceVariant.StartsWith(baseName)) return fallback;   // 다른 몹 지정 → 이 몹은 원본
                return fallback;
            }
            if (damageRng.NextDouble() > DamagedChance) return fallback;
            return list[damageRng.Next(list.Count)];
        }

        /// <summary>
        /// 모든 몹 뷰를 버려 다음 Sync에서 새로 만들게 한다.
        /// ★ 슬롯은 종류가 바뀔 때만 다시 만들어지므로, 파손 변형을 바꿔도 같은 종류를 소환하면
        ///   기존 뷰가 그대로 재사용돼 반영되지 않는다. 콘솔 dmg가 이걸 호출한다.
        /// </summary>
        public void InvalidateViews()
        {
            for (int i = 0; i < enemyViews.Count; i++)
                if (enemyViews[i] != null) Object.Destroy(enemyViews[i].gameObject);
            enemyViews.Clear(); viewKinds.Clear(); viewRenderers.Clear();
            viewAnimators.Clear(); viewYaw.Clear(); viewMotion.Clear();
            viewGlow.Clear(); viewPrevHp.Clear(); viewRusty.Clear(); viewSpeed.Clear(); viewAnimSpeed.Clear(); viewAirTime.Clear();
            viewFoot.Clear();
            // 남아 있는 전선·조각도 함께 정리(부모가 없어 자동으로 안 사라진다)
            foreach (var w in Object.FindObjectsByType<DanglingWire>(FindObjectsSortMode.None))
            {
                if (w.detachedPart != null) Object.Destroy(w.detachedPart.gameObject);
                Object.Destroy(w.gameObject);
            }
        }

        /// <summary>Resources/Enemies/Damaged 에서 이 몹의 변형을 전부 찾아 캐시.</summary>
        static List<GameObject> Variants(string baseName)
        {
            if (variantCache.TryGetValue(baseName, out var cached)) return cached;
            var list = new List<GameObject>();
            // d01~d12 까지 훑는다(조합이 그보다 많아질 일은 없다)
            for (int i = 1; i <= 12; i++)
            {
                var g = Resources.Load<GameObject>($"Enemies/Damaged/{baseName}_d{i:00}");
                if (g != null) list.Add(g);
            }
            variantCache[baseName] = list;
            return list;
        }

        /// <summary>파손 변형이면 소켓 본에 전선을 걸고 떨어진 조각을 매단다.</summary>
        static void AttachWires(Transform view)
        {
            var info = view.GetComponent<DamagedPartInfo>();
            if (info == null || info.partMesh == null) return;

            Transform socket = null;
            foreach (var t in view.GetComponentsInChildren<Transform>(true))
                if (t.name.IndexOf(info.socketBoneName, System.StringComparison.OrdinalIgnoreCase) >= 0)
                { socket = t; break; }
            if (socket == null) return;

            // ★ 뷰 루트에는 개체 크기 스케일(≈2.4배)이 걸려 있다. 전선을 그 자식으로 붙이면
            //   월드 좌표로 시뮬레이션한 결과가 렌더 때 한 번 더 곱해져 <b>길이가 배로 늘어난다</b>.
            //   그래서 전선·조각은 부모 없이 두고, 스크립트가 월드 좌표로 직접 몬다.
            var mat = view.GetComponentInChildren<Renderer>()?.sharedMaterial;

            // ★ 조각 메시의 정점은 <b>스킨 메시가 사는 로컬 공간</b> 좌표다.
            //   모델마다 임포트 스케일이 딴판이라(근접몹 0.0084, 비행몹 0.588) 뷰 루트 스케일만
            //   주면 100배 넘게 커진다. 스킨 렌더러의 실제 lossyScale을 그대로 써야 맞다.
            var smr = view.GetComponentInChildren<SkinnedMeshRenderer>();
            var piece = new GameObject("DetachedPart");
            piece.transform.localScale = smr != null ? smr.transform.lossyScale : view.lossyScale;
            piece.AddComponent<MeshFilter>().sharedMesh = info.partMesh;
            var pr = piece.AddComponent<MeshRenderer>();
            if (mat != null) pr.sharedMaterial = mat;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var wgo = new GameObject("DanglingWire");
            var wire = wgo.AddComponent<DanglingWire>();
            wire.Init(socket, piece.transform, WireCfg, WireMaterials.Wire);
            wire.owner = view;              // 몹이 사라지면 같이 정리된다
            wire.detachedPart = piece.transform;
            wire.meshSpace = smr != null ? smr.transform : view;   // 조각 크기 기준
        }

        static void LoadPrefabsOnce()
        {
            if (prefabsLoaded) return;
            flyingPrefab = Resources.Load<GameObject>("Enemies/FlyingEnemy");
            chargePrefab = Resources.Load<GameObject>("Enemies/ChargeEnemy");
            meleePrefab  = Resources.Load<GameObject>("Enemies/MeleeEnemy");
            rangedPrefab = Resources.Load<GameObject>("Enemies/RangedEnemy");
            prefabsLoaded = true;
        }

        public void Init()
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "PlayerAnchor";
            Object.Destroy(body.GetComponent<Collider>());
            body.GetComponent<Renderer>().enabled = false;  // 1인칭
            PlayerAnchor = body.transform;
        }

        public void Sync(in SimWorld w, in SimWorld prev, float alpha)
        {
            last = this;   // 콘솔 진단(SpeedReport)이 참조
            Vector3 pp = Vector3.Lerp(prev.player.pos, w.player.pos, alpha);
            PlayerAnchor.position = pp;
            PlayerAnchor.rotation = Quaternion.Euler(
                0f, Mathf.LerpAngle(prev.player.yaw, w.player.yaw, alpha), 0f);

            LoadPrefabsOnce();

            while (enemyViews.Count < w.enemyCount)
            {
                int idx = enemyViews.Count;
                AddView($"Enemy_{idx}", KindFor(w.enemies[idx].ai.mobility, w.enemies[idx].ai.combat), w.enemies[idx].yaw);
            }

            for (int i = 0; i < enemyViews.Count; i++)
            {
                // 처형 중(gloryStage>0)엔 몸 숨김 → Dismemberment 조각만 보이게
                bool active = i < w.enemyCount && w.enemies[i].alive && w.enemies[i].combat.gloryStage == 0;
                if (i < w.enemyCount)
                {
                    // 개체 풀 재사용으로 이동방식/전투방식이 바뀐 슬롯 → 시각을 다시 만든다.
                    ViewKind wantKind = KindFor(w.enemies[i].ai.mobility, w.enemies[i].ai.combat);
                    if (viewKinds[i] != wantKind) ReplaceView(i, $"Enemy_{i}", wantKind, w.enemies[i].yaw);

                    // 스폰 실체화 VFX — 팬 아래 재생 박스(SpawnRevealVolume)를 몹이 '지나는 순간' 재생.
                    // 몹은 콜라이더가 없어 물리 트리거가 안 먹으므로, sim 위치가 박스 안에 들어왔는지로 판정.
                    // 카메라 시야와 무관 — 팬에서 나와 박스를 통과하는 그 지점에서 확실히 뜬다.
                    int eid = w.enemies[i].id;
                    // 보스(Orb)는 실체화(첫 투명) 예외 — 스폰 즉시 그대로 보인다. 나머지 몹만 재생 박스 실체화.
                    if (w.enemies[i].alive && w.enemies[i].ai.mobility != MobilityType.Orb)
                    {
                        if (viewSpawnedId[i] != eid)
                        {
                            viewSpawnedId[i] = eid;
                            viewRevealedId[i] = int.MinValue;          // 새 몹: 아직 안 걷힘
                            viewSpawnTime[i] = Time.time;              // 타임아웃 기준(게임 시간 — 슬로모/정지 시 멈춤)
                            SpawnMaterialize.Prepare(enemyViews[i]);   // 즉시 숨김(오버레이 대기)
                        }
                        // 재생 박스를 지나면 그 지점에서 걷힌다. 못 지나도 SpawnRevealTimeout 뒤엔
                        // '박스에 닿은 셈' 치고 강제로 걷힌다(경로 문제로 영영 투명한 몹 방지).
                        if (viewRevealedId[i] != eid &&
                            (SpawnRevealVolume.Contains(w.enemies[i].pos)
                             || Time.time - viewSpawnTime[i] >= SpawnRevealTimeout))
                        {
                            viewRevealedId[i] = eid;
                            SpawnMaterialize.Reveal(enemyViews[i]);     // 박스 통과(또는 타임아웃) → 걷힘 시작
                        }
                    }
                }
                enemyViews[i].gameObject.SetActive(active);
                if (!active) continue;
                ref readonly EnemySim e = ref w.enemies[i];
                Vector3 ep = Vector3.Lerp(prev.enemies[i].pos, e.pos, alpha);

                // 보스(Orb): 발광 구 배치 + 빔 구동만. 바이페드 애니/시선/틴트 로직 전부 건너뜀.
                if (viewKinds[i] == ViewKind.Orb)
                {
                    Vector3 emitter = ep + Vector3.up * AIConfig.BossEmitterHeight;
                    enemyViews[i].position = emitter;
                    float os = e.radius * 2f;   // 구 지름 = sim 히트박스(e.radius)와 동일 → 비주얼=히트박스.
                    // 페이즈 전환 숨기(Hide)·재등장(Emerge) 중엔 보이는 반지름을 0.5배까지 선형 축소.
                    // 아래로 내려갈수록(anchor+RevealYOffset → anchor+HideYOffset) 작아진다.
                    if ((e.ai.state == EnemyState.Hide || e.ai.state == EnemyState.Emerge) && e.ai.anchorSet)
                    {
                        float revealY = e.ai.anchor.y + AIConfig.BossRevealYOffset;
                        float hiddenY = e.ai.anchor.y + AIConfig.BossHideYOffset;
                        float f = Mathf.InverseLerp(revealY, hiddenY, ep.y);   // 0=드러남, 1=완전히 숨음
                        os *= Mathf.Lerp(1f, 0.5f, f);
                    }
                    enemyViews[i].localScale = new Vector3(os, os, os);
                    var bv = enemyViews[i].GetComponent<BossView>();
                    if (bv != null) bv.Set(e.ai.state, emitter, e.ai.beamDir, e.combat.health);
                    continue;
                }

                // 틱 차분으로 실제 수평 속도 산출(프레임레이트 무관). prevWorld는 FixedUpdate마다
                // 갱신되므로 두 값은 정확히 한 틱 차이고, 렌더 프레임이 여러 번 돌아도 안정적이다.
                {
                    Vector3 d = e.pos - prev.enemies[i].pos; d.y = 0f;
                    viewSpeed[i] = d.magnitude / SimConfig.TickDelta;
                    // 이동 방향도 같이 — 급선회·경사 연출이 쓴다. 멈추면 직전 방향을 유지한다.
                    if (d.sqrMagnitude > 1e-8f) viewDir[i] = d.normalized;
                    // 수직 속도(공중몹 뱅킹·고도 변화용)
                    viewVertSpeed[i] = (e.pos.y - prev.enemies[i].pos.y) / SimConfig.TickDelta;
                    // 배속용 속도: 접지 시 경사면 실거리(3D)로 재고(경사에서 느리게 재생돼 미끄러지는 것 방지),
                    //   틱마다 요동치는 진행량을 저역통과로 눌러 다리 재생이 안 끊기게 한다. 공중(낙하/점프)은 수직이
                    //   커서 튀므로 수평만 쓴다. viewSpeed 자체는 방향·idle 판정용이라 손대지 않는다.
                    // ★ 수평 속도만 쓴다(과거 버전과 동일). 경사에서 수직 성분 v=Δy/틱 은 미분이라
                    //   y-스냅의 미세 변동을 크게 증폭해 배속을 흔들고(=몸 구부러지며 덜컹), 위치(히트박스)는
                    //   매끈한데 배속만 튀는 회귀를 만들었다. 경사에서 약간 느리게 재생(미끄러짐)은 감수한다.
                    float rawAnim = viewSpeed[i];
                    viewAnimSpeed[i] = Mathf.Lerp(viewAnimSpeed[i], rawAnim,
                                                  1f - Mathf.Exp(-AnimSpeedSmoothRate * Time.deltaTime));
                }
                ViewKind kind = viewKinds[i];
                if (kind == ViewKind.Capsule)
                {
                    // 캡슐 원점은 중심이라 몸 절반 올림 (Sim pos는 발밑). 개별 크기 반영(대형몹 3배).
                    enemyViews[i].position = ep + Vector3.up * (e.height * 0.5f);
                    enemyViews[i].localScale = new Vector3(e.radius * 2f, e.height * 0.5f, e.radius * 2f);
                }
                else
                {
                    // 실물 모델은 프리팹을 1m 기준으로 미리 맞춰뒀으므로 wrapper 스케일 = e.height 하나로 충분.
                    // Flying은 원점이 모델 중심(=캡슐과 동일하게 절반 올림), 나머지 바이페드는 원점이 발 근처지만
                    // 살짝 위라 종류별 FeetLift만큼 더 들어올려야 발이 바닥에 파묻히지 않는다.
                    // ★ 돌진몹 몸집 확대(ChargeBodyMul)는 렌더 전용 — 히트박스(e.height)엔 안 들어가고 여기서만 곱한다.
                    float visualScale = e.height * VisualMul(kind)
                                      * (kind == ViewKind.Charge ? AIConfig.ChargeBodyMul : 1f);
                    float feetLift = kind == ViewKind.Melee ? MeleeFeetLift
                                    : kind == ViewKind.Ranged ? RangedFeetLift
                                    : ChargeFeetLift;
                    // 발 오프셋은 실제로 그려지는 크기(visualScale) 기준이어야 커진 만큼 같이 들어올려진다.
                    // 공중몹은 원점이 모델 중심이라 발 보정이 없다 — 캡슐 중심(height*0.5)에 그대로 둔다.
                    // ★ FeetTrim은 <b>스케일을 안 곱한 절대값</b>이다. 미세 보정이라 배율까지 곱하면
                    //   대형몹에서만 과하게 내려간다. 렌더만 움직이고 히트박스(e.radius/height)는 그대로다.
                    // 단 하나 예외: 돌진몹은 ChargeBodyMul로 <b>몸집 자체</b>가 커진 종족이라, 눈으로 맞춘 트림도
                    //   같은 비율로 키워야 커지기 전과 똑같이 보인다(안 그러면 배율만큼 더 떠 보인다).
                    float trim = FeetTrim * (kind == ViewKind.Charge ? AIConfig.ChargeBodyMul : 1f);
                    Vector3 want = kind == ViewKind.Flying
                        ? ep + Vector3.up * (e.height * 0.5f)
                        : ep + Vector3.up * (feetLift * visualScale + trim);
                    enemyViews[i].position = want;
                    enemyViews[i].localScale = new Vector3(visualScale, visualScale, visualScale);
                }
                bool isChargeRun  = kind == ViewKind.Charge  && e.ai.state == EnemyState.ChargeRun;
                bool isAttacking  = kind == ViewKind.Melee   && (e.ai.state == EnemyState.Windup || e.ai.state == EnemyState.Active || e.ai.state == EnemyState.Recovery);
                bool isAiming     = kind == ViewKind.Ranged  && (e.ai.state == EnemyState.Aim || e.ai.state == EnemyState.Fire);
                // "커밋된 텔레그래프 방향을 그대로 봐야 하는" 상태 — 그 외엔 실제 이동 방향(속도)을 봐야 자연스럽다.
                bool faceCommitted = isChargeRun || isAttacking || isAiming;
                float bodyYaw = e.yaw;
                // 근접·돌진은 이동 방향(속도)을 봐야 자연스럽다(플레이어에게 달려드는 몹). e.yaw는 sim에서
                // "플레이어 응시"라 그대로 쓰면 대각선/옆으로 걷는 것처럼 보인다.
                // ★ 원거리(총병)는 예외 — 후진하며 조준선을 유지하는 게 자연스러우므로 걸을 때도 플레이어를
                //   계속 본다(이동 방향으로 안 돌린다). e.yaw가 이미 플레이어를 향하고 있다.
                if (IsBiped(kind) && kind != ViewKind.Ranged && !faceCommitted)
                {
                    // ★ e.vel.x/z 는 항상 0이다 — EnemyMovement가 "이동은 변위로 처리"하며 매 틱 0으로 만든다.
                    //   그래서 이 분기가 영영 안 걸려 몸통이 계속 플레이어를 정면으로 본 채
                    //   옆걸음·뒷걸음으로 이동했다(주석이 경고하던 바로 그 상태).
                    //   틱 차분으로 실측한 방향(viewDir)으로 교체한다.
                    if (viewSpeed[i] > BodyFaceMinSpeed)
                        bodyYaw = Mathf.Atan2(viewDir[i].x, viewDir[i].z) * Mathf.Rad2Deg;
                }
                // 조준 중인 원거리 몹만 총구 정면 보정(부드럽게 돌도록 smoothing 전에 target에 반영).
                if (kind == ViewKind.Ranged && isAiming) bodyYaw += RangedAimYawOffset;

                if (IsBiped(kind))
                {
                    if (isAiming)
                    {
                        // ── 조준 중: 발은 고정, 상체만 비튼다 ──
                        // 조준 동안 Sim이 위치를 고정(Plant)하므로 몸 전체를 돌리면 발이 미끄러진다.
                        if (!viewAimWas[i]) { viewAimBase[i] = viewYaw[i]; viewAimWas[i] = true; }

                        float need = Mathf.DeltaAngle(viewAimBase[i], bodyYaw);   // 총구가 더 돌아야 하는 각
                        float twist = Mathf.Clamp(need, -AimTwistMax, AimTwistMax);
                        // 상체 한계를 넘으면 그만큼만 발이 천천히 따라 돈다(제자리 스텝)
                        if (Mathf.Abs(need) > AimTwistMax)
                            viewAimBase[i] = Mathf.MoveTowardsAngle(viewAimBase[i], bodyYaw,
                                                                    AimFootCatchUp * Time.deltaTime);
                        viewAimTwist[i] = twist;
                        viewYaw[i] = viewAimBase[i];   // 발 방향
                        bodyYaw = viewYaw[i];
                    }
                    else
                    {
                        viewAimWas[i] = false;
                        // 조준이 끝나면 비틀림을 서서히 푼다(뚝 끊기면 상체가 튄다)
                        viewAimTwist[i] = Mathf.MoveTowards(viewAimTwist[i], 0f, 180f * Time.deltaTime);
                        // 분리 스티어링 잔떨림을 걸러내는 회전 속도 제한(시각 전용, e.yaw/전투 판정엔 영향 없음).
                        viewYaw[i] = Mathf.MoveTowardsAngle(viewYaw[i], bodyYaw, BodyTurnDegPerSec * Time.deltaTime);
                        bodyYaw = viewYaw[i];
                    }
                }
                enemyViews[i].rotation = Quaternion.Euler(0f, bodyYaw, 0f);

                // ── 돌진몹 애니메이터 파라미터/배속 ──
                //   러쉬 레이어(UseRushLegs): 돌진 3단계에도 Animator를 켜둔 채 다리는 클립(러쉬/Idle)이 굴리고,
                //   ChargeAnim이 상체만 덮는다. 컨트롤러가 상태를 고르게 여기서 파라미터를 던진다.
                //   러쉬 제거(UseRushLegs=false)면 ChargeAnim이 Animator를 꺼 다리를 고정하므로 손대지 않는다.
                if (kind == ViewKind.Charge)
                {
                    Animator anim = viewAnimators[i];
                    bool chargePhase = ChargeAnimEnabled && ChargeAnim.IsChargePhase(in e);
                    if (!(chargePhase && !ChargeAnim.UseRushLegs))
                    {
                        // ChargeRun → 러쉬(Charge 상태), Windup/Recovery → Idle. 비-돌진이면 속도로 걷기/idle 판정.
                        bool wantCharge = chargePhase ? (e.ai.state == EnemyState.ChargeRun) : isChargeRun;
                        bool wantIdle   = chargePhase
                            ? (e.ai.state == EnemyState.Windup || e.ai.state == EnemyState.Recovery)
                            : (viewSpeed[i] < IdleSpeedThreshold);
                        anim.SetBool("IsCharging", wantCharge);
                        anim.SetBool("IsIdle", wantIdle);
                        ApplyOptionalParams(i, anim, in e, viewSpeed[i]);   // IsAirborne
                        if (chargePhase || !e.grounded) anim.speed = 1f;    // 러쉬·Idle·점프는 등속
                        else if (isChargeRun) anim.speed = Mathf.Clamp(viewAnimSpeed[i] / RunClipPace, RunSpeedClampMin, RunSpeedClampMax);
                        else if (!wantIdle)   anim.speed = Mathf.Clamp(viewAnimSpeed[i] / WalkClipPace, WalkSpeedClampMin, WalkSpeedClampMax);
                        else                  anim.speed = 1f;              // Idle 등속
                    }
                }
                else if (kind == ViewKind.Melee)
                {
                    Animator anim = viewAnimators[i];
                    anim.SetBool("IsAttacking", isAttacking);
                    // ★ 점프·달리기·피격·사망은 파라미터가 있으면 자동으로 켜진다(AnimCaps 참고).
                    //   클립을 넣고 컨트롤러에 파라미터만 추가하면 코드 수정 없이 동작한다.
                    ApplyOptionalParams(i, anim, in e, viewSpeed[i]);
                    if (!isAttacking) ApplyWalkOrIdle(i, anim, viewAnimSpeed[i]);
                    else anim.speed = 1f;   // 공격 모션은 판정 타이밍과 맞춰야 하니 배속 없이 그대로 재생.
                }
                else if (kind == ViewKind.Ranged)
                {
                    Animator anim = viewAnimators[i];
                    anim.SetBool("IsAiming", isAiming);
                    // ★ 위 Melee와 동일 — 파라미터가 생기면 자동으로 켜진다.
                    //   ※ MonolithSentinel_AimTurn.fbx 는 임포트만 되고 아직 컨트롤러에 안 붙어 있다.
                    ApplyOptionalParams(i, anim, in e, viewSpeed[i]);
                    if (!isAiming) ApplyWalkOrIdle(i, anim, viewAnimSpeed[i]);
                    else anim.speed = 1f;
                }

                // [2026-07-22] 예측 미리보기 등 sim 정지 프레임에서는 몹 애니메이터도 얼린다.
                // 위 세 분기(Charge/Melee/Ranged)가 배속을 하한(0.6)에 붙여 놓아 세계가 멈춰도
                // 다리가 계속 움직였다 — speed=0으로 덮어써 완전히 정지시킨다.
                if (SimFrozen && IsBiped(kind) && viewAnimators[i] != null)
                    viewAnimators[i].speed = 0f;

                // 우선순위: 피격/스턴 > 공격 선딜(경고) > 타격 > 하강 단계 색
                // 실물 모델은 원래 텍스처 색을 그대로 유지 — 캡슐만 상태별로 틴트.
                if (kind == ViewKind.Capsule)
                {
                    Color col;
                    if (e.combat.stunTicks > 0)                 col = HitColor;
                    else if (e.ai.state == EnemyState.Windup)   col = WindupColor;
                    else if (e.ai.state == EnemyState.Active)   col = AttackColor;
                    else                                        col = PhaseColor(e.descentPhase);
                    Renderer r = viewRenderers[i];
                    if (r != null) r.material.color = col;
                }
            }
        }

        static Color PhaseColor(DescentPhase p)
            => p == DescentPhase.Leaping ? LeapColor : ChaseColor;

        static ViewKind KindFor(MobilityType m, CombatType c)
        {
            if (m == MobilityType.Orb)    return ViewKind.Orb;      // 보스(발광 구 코어)
            if (m == MobilityType.Flying) return ViewKind.Flying;
            if (m == MobilityType.Charge) return ViewKind.Charge;
            // 층이동(Traversal)은 전용 모델이 없어 한때 전부 근접 모델로 통일했었다.
            // ★ 층이동 특성 자체가 폐기 예정이라(2026-07-22, MobilityType 주석 참고) 전용 뷰를 만들 계획도 없다.
            // 이제 원거리 몸체(Ranged)도 실물 모델이 있으므로, 전투 판정(combat)에 맞춰 총/칼을 가른다.
            return c == CombatType.Ranged ? ViewKind.Ranged : ViewKind.Melee;
        }

        static bool IsBiped(ViewKind k) => k == ViewKind.Charge || k == ViewKind.Melee || k == ViewKind.Ranged;


        /// <summary>렌더 전용 크기 배율(히트박스는 이 값과 무관하게 Sim 그대로).
        /// Dismemberment.VisualMul과 값이 어긋나면 죽는 순간 시체 크기가 튄다.</summary>
        static float VisualMul(ViewKind k) =>
              k == ViewKind.Charge ? ChargeVisualScaleMul
            : k == ViewKind.Melee  ? MeleeVisualScaleMul
            : k == ViewKind.Ranged ? RangedVisualScaleMul
            : k == ViewKind.Flying ? FlyingVisualScaleMul
            : 1f;

        /// <summary>
        /// 절차 레이어 — Animator가 클립을 써 넣은 <b>뒤</b>에 본을 돌린다.
        /// Main.LateUpdate에서 호출된다(Update에서 하면 Animator가 전부 덮어쓴다).
        /// 1단계는 시선 추적만. 피격·경직·호흡은 상의 후 추가.
        /// </summary>
        public void LateSync(in SimWorld w, Vector3 lookTarget)
        {
            float dt = Time.deltaTime;
            var camT = Camera.main != null ? Camera.main.transform : null;

            for (int i = 0; i < enemyViews.Count && i < w.enemyCount; i++)
            {
                Transform view = enemyViews[i];
                if (view == null || !view.gameObject.activeSelf) continue;

                ViewKind kind = viewKinds[i];
                ref readonly EnemySim e = ref w.enemies[i];

                // ── 발광: 종류를 가리지 않는다(캡슐·비행 포함). 어두운 맵에서 전부 보여야 한다 ──
                if (GlowEnabled)
                {
                    var g = viewGlow[i];
                    if (!g.bound) g.Bind(view, glowRng);
                    if (g.HasRenderers)
                    {
                        // 피격 감지 — 체력이 줄면 깜빡
                        int hp = e.combat.health;
                        if (viewPrevHp[i] != int.MinValue && hp < viewPrevHp[i]) g.Flash(1f);
                        viewPrevHp[i] = hp;

                        float camDist = camT != null ? Vector3.Distance(camT.position, view.position) : 0f;
                        g.Apply(in e, in glowSettings, in dirtSettings, camDist, dt);
                    }
                    viewGlow[i] = g;
                }

                // ── 발 딛는 순간 스파크·소리 ──
                //   비행몹은 걷지 않으므로 제외. 캡슐(모델 없음)도 발 본이 없어 자동으로 걸러진다.
                if (footCfg.enabled && kind != ViewKind.Flying)
                {
                    var fs = viewFoot[i];
                    if (!fs.bound) fs.Bind(view);
                    if (fs.HasFeet) fs.Tick(in e, viewSpeed[i], dt, in footCfg, view.position);
                    viewFoot[i] = fs;
                }

                // ── 돌진 전신 절차(준비·돌진·이후): Animator를 끄고 전신을 직접 구동 ──
                //   이 단계에선 시선추적·텔레그래프·이동자세·녹슨관절을 모두 건너뛴다(ChargeAnim 단독 구동).
                if (kind == ViewKind.Charge)
                {
                    var ca = viewCharge[i];
                    // 단계 중이거나, 단계가 끝났어도 아직 자세가 남아 감쇠 중이면(꼬리 페이드) 계속 상체를 덮는다.
                    if (ChargeAnimEnabled && ca.HasBones && (ChargeAnim.IsChargePhase(in e) || ca.Settling))
                    {
                        chargeSettings.enabled = true;
                        ca.Apply(view, in e, in chargeSettings, dt, Time.time);   // 단계 종료 시 목표 0으로 스프링 복귀
                        viewCharge[i] = ca;
                        continue;
                    }
                    ca.Release();          // 완전히 잦아들면 릴리즈 — 이후 클립(Walk/Idle)이 상체까지 되찾는다
                    viewCharge[i] = ca;
                }

                // ── 시선 추적: 본이 있는 바이페드만 ──
                if (!LookAtEnabled) continue;
                if (!IsBiped(kind)) continue;          // 캡슐·비행은 본이 없다(비행은 별도 설계)

                var m = viewMotion[i];

                // 본 캐시는 개체당 1회
                if (!m.bound) m.Bind(view);
                if (!m.HasHead && m.spine2 == null && m.spine1 == null) { viewMotion[i] = m; continue; }

                var s = kind == ViewKind.Charge ? lookCharge : lookBiped;

                // ── 시선을 끌 상황 ──
                //   돌진 중엔 앞만 본다. 경직 중엔 목을 못 가눈다. 처형 대상도 마찬가지.
                bool suppress = e.ai.state == EnemyState.ChargeRun
                             || e.combat.stunTicks > 0
                             || e.combat.gloryStage > 0
                             || !e.alive;
                if (suppress) s.weight = 0f;

                m.ApplyLookAt(e.pos, view.eulerAngles.y, lookTarget, in s, dt);
                viewMotion[i] = m;

                // ── 상태 텔레그래프: 공격 예비·돌진 준비·차징·피격을 자세로 보여준다 ──
                // 시선 추적 뒤에 곱해서 얹으므로 고개 방향을 지우지 않는다.
                {
                    var pz = viewPose[i];
                    if (!pz.bound) pz.Bind(view, e.personality);
                    if (pz.HasBones)
                    {
                        poseSettings.enabled = PoseEnabled;
                        // 조준 중 상체 비틀림 — 발은 위에서 고정했고 그 차이를 척추가 받는다
                        pz.Apply(view, in e, in poseSettings, dt, Time.time, viewAimTwist[i]);
                    }
                    viewPose[i] = pz;
                }

                // ── 이동·상황 자세: 급선회·가감속·경사·휘청·반동·뱅킹·저체력 등 ──
                {
                    var mv = viewMove[i];
                    if (!mv.bound) mv.Bind(view, in e);
                    if (mv.HasBones)
                    {
                        moveSettings.enabled = MoveposeEnabled;
                        var tag = kind == ViewKind.Flying ? ViewKindTag.Flying
                                : kind == ViewKind.Charge ? ViewKindTag.Charge : ViewKindTag.Other;
                        // ★ 속도는 스무딩본(viewAnimSpeed)을 넘긴다 — 가감속 자세(미분)·벽막힘(게이트)이
                        //   경사에서 틱당 속도 노이즈에 덜컹대는 것을 막는다. 방향(viewDir)은 원본 유지.
                        mv.Apply(view, in e, tag, viewAnimSpeed[i], viewDir[i], viewVertSpeed[i],
                                 in moveSettings, dt, Time.time);
                    }
                    viewMove[i] = mv;
                }

                // ── 녹슨 관절: 여기까지 확정된 자세를 목표로 삼아 관절이 뒤따르게 한다 ──
                // 시선 추적·텔레그래프·이동자세가 끝난 뒤라 그 결과를 삐걱대며 뒤따른다.
                // bone.localRotation만 건드리므로 루트·Animator에는 영향이 없다.
                ApplyRust(i, view, in e, dt);
            }
        }

        /// <summary>몹별 실측 수평 속도 요약(콘솔 rust speed) — 게이트 임계를 정할 때 쓴다.</summary>
        public static string SpeedReport()
        {
            var inst = last;
            if (inst == null || inst.viewSpeed.Count == 0) return "표본 없음";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < inst.viewSpeed.Count && i < 6; i++)
                sb.Append($"[{i}] {inst.viewSpeed[i]:0.00}m/s  ");
            return sb.ToString();
        }
        static EntityViews last;   // 콘솔 진단용(인스턴스가 하나뿐인 구조)

        /// <summary>
        /// 선택적 애니메이터 파라미터를 <b>있을 때만</b> 구동한다.
        ///
        /// ★ 이 함수가 있는 이유 —
        ///   지금은 돌진몹(ObsidianSentinel)만 Jump 클립이 있고 근접·원거리는 없다.
        ///   나중에 클립을 넣을 때 <b>코드를 고치지 않아도 되게</b> 미리 배선해 둔 것이다.
        ///
        /// 팀원이 할 일(코드 수정 불필요):
        ///   ① 클립 임포트 — Art/&lt;몹&gt;/&lt;몹&gt;_Jump.fbx (Run·Hurt·Death도 동일)
        ///   ② 컨트롤러에 <b>파라미터 + 상태 + 전이</b> 추가
        ///        IsAirborne(bool) → Jump    : true면 Jump, false면 Walk/Run 복귀
        ///        IsRunning(bool)  → Run     : 속도 임계(RunThresholdSpeed)로 자동 전환
        ///        IsHurt(bool)     → Hurt    : 경직(stunTicks) 동안 유지
        ///        IsDead(bool)     → Death   : 죽는 순간 true
        ///      ※ 이름을 정확히 이대로 지어야 자동으로 잡힌다.
        ///      ※ 전이 조건은 ObsidianSentinelController의 Jump를 그대로 참고하면 된다.
        ///   ③ 끝. 다음 Play부터 바로 재생된다.
        /// </summary>
        void ApplyOptionalParams(int i, Animator anim, in EnemySim e, float speed)
        {
            if (anim == null) return;
            var caps = viewCaps[i];
            if (!caps.probed) { caps = AnimCaps.Probe(anim); viewCaps[i] = caps; }

            // 없는 파라미터에 쓰면 매 프레임 경고가 쏟아지므로 반드시 있을 때만 건드린다.
            // ★ IsAirborne 디바운스: 경사에서 grounded가 1~2틱 깜빡여도 Jump 클립으로 튀지 않게,
            //   일정 시간 이상 <b>연속으로</b> 공중일 때만 참으로 본다(진짜 점프·낙하는 그보다 길다).
            if (caps.airborne)
            {
                viewAirTime[i] = e.grounded ? 0f : viewAirTime[i] + Time.deltaTime;
                anim.SetBool("IsAirborne", viewAirTime[i] >= AirborneDebounceTime);
            }
            if (caps.running)  anim.SetBool("IsRunning",  speed >= RunThresholdSpeed);
            if (caps.hurt)     anim.SetBool("IsHurt",     e.combat.stunTicks > 0);
            if (caps.dead)     anim.SetBool("IsDead",     !e.alive);
            if (caps.moving)   anim.SetBool("IsMoving",   speed >= IdleSpeedThreshold);
        }

        /// <summary>
        /// 지상 걷기 배속 + 정지(Idle) 처리.
        ///   ① 움직이면 실측 속도에 맞춰 걷기 배속.
        ///   ② 서 있고 컨트롤러에 IsMoving+Idle이 있으면 → Idle 클립이 알아서 재생(배속 1).
        ///   ③ 서 있는데 아직 Idle이 없으면(임시) → 걷기를 정지시켜 제자리걸음을 막는다(프레임0로 스냅).
        /// 팀원이 Idle 클립 + IsMoving 파라미터만 넣으면 ②로 자동 전환된다(코드 수정 불필요).
        /// </summary>
        void ApplyWalkOrIdle(int i, Animator anim, float speed)
        {
            if (anim == null) return;
            if (speed >= IdleSpeedThreshold)
            {
                anim.speed = Mathf.Clamp(speed / WalkClipPace, WalkSpeedClampMin, WalkSpeedClampMax);
                return;
            }
            if (viewCaps[i].moving)     // Idle 클립 보유 → 정상 재생
            {
                anim.speed = 1f;
                return;
            }
            // 임시: Idle 클립 없음 → 걷기 정지(제자리걸음 차단) + 프레임0로 스냅
            anim.speed = 0f;
            var st = anim.GetCurrentAnimatorStateInfo(0);
            anim.Play(st.fullPathHash, 0, 0f);
        }

        /// <summary>관절 고착+스프링 적용. 기능이 꺼져 있으면 상태만 재동기화하고 빠진다.</summary>
        void ApplyRust(int i, Transform view, in EnemySim e, float dt)
        {
            var rj = viewRusty[i];

            bool target = RustEnabled && RustApplies(viewKinds[i]);
            if (!target)
            {
                // 껐다 켤 때 튀지 않게 현재 자세를 기준으로 맞춰둔다
                if (rj.bound) { rj.Resync(); viewRusty[i] = rj; }
                return;
            }

            if (!rj.bound) rj.Bind(view, i, in rustSettings);
            // 걷거나 뛸 때만 — 속도가 낮으면 게이트가 닫혀 자동으로 원본 자세가 나온다
            if (rj.HasJoints) rj.Apply(in rustSettings, dt, Time.time, viewAnimSpeed[i]);   // 스무딩본 — 경사 게이트 떨림 방지
            viewRusty[i] = rj;
        }

        void AddView(string name, ViewKind kind, float initialYaw)
        {
            enemyViews.Add(null);
            viewKinds.Add(ViewKind.Capsule);      // ReplaceView가 실제 값으로 덮어씀
            viewRenderers.Add(null);
            viewAnimators.Add(null);
            viewYaw.Add(initialYaw);
            viewMotion.Add(default);
            viewGlow.Add(default);
            viewPrevHp.Add(int.MinValue);
            viewSpawnedId.Add(int.MinValue);
            viewRevealedId.Add(int.MinValue);
            viewSpawnTime.Add(0f);
            viewFoot.Add(default);
            viewRusty.Add(default);
            viewPose.Add(default);
            viewMove.Add(default);
            viewCharge.Add(default);
            viewDir.Add(Vector3.forward);
            viewVertSpeed.Add(0f);
            viewAimBase.Add(initialYaw);
            viewAimTwist.Add(0f);
            viewAimWas.Add(false);
            viewCaps.Add(default);
            viewSpeed.Add(0f);
            viewAnimSpeed.Add(0f);
            viewAirTime.Add(0f);
            ReplaceView(enemyViews.Count - 1, name, kind, initialYaw);
        }

        void ReplaceView(int i, string name, ViewKind kind, float initialYaw)
        {
            if (enemyViews[i] != null) Object.Destroy(enemyViews[i].gameObject);
            viewYaw[i] = initialYaw;   // 슬롯 재사용 시 이전 개체의 회전에서 이어 도는 것 방지
            viewMotion[i] = default;   // 본 캐시·시선 각도 초기화(슬롯 재사용)
            viewGlow[i]   = default;   // 렌더러 캐시도 새 모델 기준으로 다시 잡는다
            viewPrevHp[i] = int.MinValue;
            viewFoot[i]   = default;   // 모델이 바뀌면 발 본도 다시 잡는다
            viewRusty[i]  = default;   // 모델이 바뀌면 본 캐시도 새로 잡아야 한다
            viewPose[i]   = default;
            viewMove[i]   = default;
            viewCharge[i] = default;
            viewAimTwist[i] = 0f; viewAimWas[i] = false; viewAimBase[i] = initialYaw;
            viewCaps[i]   = default;   // 모델이 바뀌면 파라미터를 다시 조사한다

            // 프리팹이 없으면(Resources 미배치 등) 캡슐로 대체 — 크래시 대신 예전 모습으로 폴백.
            if (kind == ViewKind.Flying && flyingPrefab == null) kind = ViewKind.Capsule;
            if (kind == ViewKind.Charge && chargePrefab == null) kind = ViewKind.Capsule;
            if (kind == ViewKind.Melee  && meleePrefab  == null) kind = ViewKind.Capsule;
            if (kind == ViewKind.Ranged && rangedPrefab == null) kind = ViewKind.Capsule;

            Transform t;
            Renderer  r;
            Animator  a = null;
            switch (kind)
            {
                case ViewKind.Flying:
                    t = Object.Instantiate(PickPrefab("FlyingEnemy", flyingPrefab)).transform;
                    r = t.GetComponentInChildren<Renderer>();
                    break;
                case ViewKind.Charge:
                    t = Object.Instantiate(PickPrefab("ChargeEnemy", chargePrefab)).transform;
                    r = t.GetComponentInChildren<Renderer>();
                    a = t.GetComponentInChildren<Animator>();
                    break;
                case ViewKind.Melee:
                    t = Object.Instantiate(PickPrefab("MeleeEnemy", meleePrefab)).transform;
                    r = t.GetComponentInChildren<Renderer>();
                    a = t.GetComponentInChildren<Animator>();
                    break;
                case ViewKind.Ranged:
                    t = Object.Instantiate(PickPrefab("RangedEnemy", rangedPrefab)).transform;
                    r = t.GetComponentInChildren<Renderer>();
                    a = t.GetComponentInChildren<Animator>();
                    break;
                case ViewKind.Orb:
                {
                    // 보스: 절차적 발광 구 + 빔(BossView가 LineRenderer 구성). 별도 프리팹 없이 자족.
                    var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Object.Destroy(orb.GetComponent<Collider>());
                    orb.AddComponent<BossView>().Init();   // 오브 재질(발광) + 빔 라인 구성
                    t = orb.transform;
                    r = orb.GetComponent<Renderer>();
                    break;
                }
                default:
                    t = MakeCapsule().transform;
                    r = t.GetComponent<Renderer>();
                    break;
            }
            t.name = name;
            AttachWires(t);   // 파손 변형이면 떨어진 부위를 전선으로 매단다

            enemyViews[i]     = t;
            viewKinds[i]      = kind;
            viewRenderers[i]  = r;
            viewAnimators[i]  = a;

            // 돌진몹: 지금(Instantiate 직후, Animator가 아직 안 돈 상태)이 프리팹 바인드 포즈라
            // 여기서 rest 자세를 캡처해 둔다. 이후 돌진 3단계에서 이 자세로 리셋하며 전신을 그린다.
            if (kind == ViewKind.Charge && a != null)
            {
                var ca = viewCharge[i];
                ca.BindRest(a);
                viewCharge[i] = ca;
            }
        }

        GameObject MakeCapsule()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.localScale = new Vector3(SimConfig.EnemyRadius * 2f, SimConfig.EnemyHeight * 0.5f,
                                                  SimConfig.EnemyRadius * 2f);
            go.GetComponent<Renderer>().material = Mat(ChaseColor);  // 개별 인스턴스
            return go;
        }

        static Material Mat(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh); m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }
    }
}
