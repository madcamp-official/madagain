using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 1인칭 뷰모델 절차 애니메이션 — 뷰모델 루트의 <b>유일한 작성자</b>.
    ///
    /// 레이어별 오프셋을 전부 합산해 한 번에 루트에 쓴다. 팔 뼈(클립)·손가락(FingerPoser)은
    /// 건드리지 않으므로 서로 싸우지 않는다.
    ///
    ///   최종 위치 = 기준위치 + Σ(레이어 위치 오프셋)
    ///   최종 회전 = 기준회전 × Euler(Σ(레이어 회전 오프셋))
    ///
    /// 원리는 셋뿐이다:
    ///   ① 사인파   — 숨쉬기·걷기 흔들림처럼 반복되는 것. 시간이 아니라 <b>위상을 적분</b>해
    ///                 속도가 변해도 끊기지 않게 한다.
    ///   ② 감쇠 스프링 — 착지·피격처럼 한 방 맞고 되돌아오는 것. 발동은 속도에 킥 한 줄.
    ///   ③ 지연 추적 — 시선 지연·점프 처짐처럼 몸이 늦게 따라오는 것.
    ///
    /// SwordView는 클립 재생만 담당한다(루트를 건드리지 않음).
    /// </summary>
    // 칼을 움직인 뒤 HandIK가 팔을 풀어야 하므로 IK(기본 0)보다 먼저 돈다.
    [DefaultExecutionOrder(-100)]
    public class ViewmodelMotion : MonoBehaviour
    {
        public static ViewmodelMotion Instance { get; private set; }

        Transform vmRoot;
        Camera    cam;

        [Header("구동 대상")]
        [Tooltip("칼을 움직이고 IK로 팔이 따라오게 한다(권장). 끄면 뷰모델 전체를 통째로 움직인다")]
        public bool driveKatana = true;
        Transform  katana;
        Vector3    katanaBasePos;
        Quaternion katanaBaseRot = Quaternion.identity;
        bool       katanaCaptured;

        /// <summary>칼을 찾았는가(F4 진단 표시용).</summary>
        public bool HasKatana => katana != null;
        [Header("기준 배치 (씬에서 잡아둔 값 — 절차 오프셋은 이 위에 얹힌다)")]
        public Vector3    basePos;
        public Quaternion baseRot = Quaternion.identity;
        bool       captured;

        // ── 레이어 on/off ──
        [Header("레이어 on/off")]
        public bool enableBreathe = true;
        public bool enableBob     = true;
        public bool enableStrafe  = true;
        public bool enableAir     = true;
        public bool enableLand    = true;
        public bool enableHit     = true;
        public bool enableSway    = true;

        // ── ① 숨쉬기 (HP 낮을수록 크고 빠르게) ──
        [Header("숨쉬기")]
        public float breatheAmpFull = 0.004f, breatheAmpLow = 0.020f;
        public float breatheSpdFull = 1.6f,   breatheSpdLow = 5.0f;
        [Tooltip("가슴이 들리는 느낌 — 미세 피치 회전(도)")]
        public float breathePitch = 0.35f;
        [Tooltip("0~1이면 그 HP 비율로 강제(테스트). 음수면 실제 HP")]
        [Range(-1f, 1f)] public float hpOverride = -1f;
        float breathePhase;

        // ── ② 달리기 bob + 하강 ──
        [Header("달리기 (W 이동)")]
        [Tooltip("달릴 때 손이 내려가는 양(m)")]
        public float runDrop = 0.044f;
        [Tooltip("후진할 때 기준보다 위로 올라가는 순수 상승량(m). runDrop과 독립이다")]
        public float backLift = 0.025f;
        [Tooltip("발걸음마다 상하 흔들림(m)")]
        public float bobVert = 0.030f;
        [Tooltip("두 걸음에 한 번 좌우 흔들림(m)")]
        public float bobHoriz = 0.040f;
        [Tooltip("터벅터벅 기울임(도)")]
        public float bobRoll = 2.0f;
        [Tooltip("걸음 주기 — 속도에 곱해 위상 진행")]
        public float bobRate = 0.85f;
        [Tooltip("이동 강도 진입/이탈 부드러움 (클수록 빠름)")]
        public float moveEnterSpeed = 7f;
        // ★ 이 속도에서 이동강도가 1.0이 된다. 플레이어 최대치(9.17)로 잡으면
        //   실제 주행 중엔 1에 못 미쳐 연출이 항상 약하게 나온다. 조금 낮게 잡아 포화시킨다.
        [Tooltip("이 속도면 이동강도 1.0 (낮출수록 빨리 최대 연출. 최대 속도는 9.17)")]
        public float refSpeed = 6.0f;
        [Tooltip("속도 상한(m/s). 대시·찌르기 블링크가 '초고속 달리기'로 읽히지 않게 자른다")]
        public float speedCap = 10.5f;   // 9.17 × 1.15
        [Tooltip("공중에 뜨면 흔들림이 사라지는 속도 (클수록 즉시)")]
        public float bobAirFade = 10f;
        float bobPhase, moveAmt;
        float bobAir = 1f;                // 1=접지(흔들림 켬), 0=공중(끔)
        Vector3 moveDir = Vector3.zero;   // 실제 이동 방향(틱 차분에서 산출)

        // 진단용 — F4 패널에 실시간 표시(레이어가 정말 도는지 눈으로 확인)
        public float MoveAmt   => moveAmt;
        public float Speed     { get; private set; }   // 실제 수평 속도(m/s)
        public float DashAmt   => dashAmt;
        public Vector3 LastPos { get; private set; }
        public Vector3 LastRot { get; private set; }

        // ── ③ 이동방향 기울임 (스트레이프) ──
        [Header("이동방향 기울임")]
        [Tooltip("좌우 이동 시 반대로 롤(도)")]
        public float strafeRoll = 2.2f;
        [Tooltip("좌우 이동 시 반대로 밀림(m)")]
        public float strafeShift = 0.020f;
        [Tooltip("전/후진 시 앞뒤 밀림(m)")]
        public float strafePush = 0.018f;
        public float strafeSpring = 9f;
        Vector3 strafeCur;

        // ── 대시 ──
        [Header("대시")]
        public bool enableDash = true;
        [Tooltip("대시 중 칼이 내려가는 양(m) — 아주 조금만")]
        public float dashDrop = 0.035f;
        [Tooltip("대시 중 칼이 뒤로 당겨지는 양(m)")]
        public float dashPull = 0.03f;
        [Tooltip("대시 방향으로 기울이는 각(도)")]
        public float dashRoll = 4f;
        [Tooltip("대시 진입/이탈 속도 (클수록 즉각적)")]
        public float dashSpring = 16f;
        [Tooltip("대시 중 손가락 쥐는 정도")]
        public float dashGrip = 0.60f;
        [Tooltip("대시 시작 순간 확 움켜쥐는 세기")]
        public float dashGripPulse = 0.55f;
        float dashAmt;
        int   prevDashTicks;

        // ── ④ 점프/공중 관성 ──
        [Header("공중 관성")]
        [Tooltip("수직 속도 → 반대 방향 처짐(m per m/s)")]
        public float airFactor = 0.006f;
        [Tooltip("최대 처짐(m)")]
        public float airMax = 0.07f;
        public float airSpring = 10f;
        float airCur;

        // ── ⑤ 착지 딥 ──
        [Header("착지 딥")]
        public float landKick = 0.06f;
        public float landMinSpeed = 3f;
        [Tooltip("착지 시 손가락을 꽉 쥐는 세기(0=안 함)")]
        public float landGripPulse = 0.5f;

        // ── 손가락 지속 그립 (상태가 유지되는 동안 계속 쥠) ──
        [Header("손가락 (달리기·공중)")]
        [Tooltip("손가락 연동 전체 on/off")]
        public bool enableFingers = true;
        [Tooltip("전력질주 시 쥐는 정도")]
        public float runGrip = 0.22f;
        [Tooltip("공중(점프·낙하) 중 쥐는 정도")]
        public float airGrip = 0.40f;
        [Tooltip("점프하는 순간 확 움켜쥐는 세기")]
        public float jumpGripPulse = 0.45f;

        // ── ⑥ 피격 휘청 ──
        [Header("피격 휘청")]
        public float hitPosKick = 0.05f;
        public float hitRotKick = 12f;
        public float hitGripPulse = 0.8f;

        // ── ⑦ 시선 지연 (mouse sway) ──
        [Header("시선 지연")]
        [Tooltip("시선 변화량 → 반대 방향 끌림(도 per 도)")]
        public float swayFactor = 0.35f;
        [Tooltip("최대 끌림(도)")]
        public float swayMax = 4.5f;
        [Tooltip("시선 변화 → 위치 밀림(m per 도)")]
        public float swayShift = 0.0025f;
        public float swaySpring = 12f;
        Vector3 swayRot, swayPos;

        // ── 충격 스프링(착지·피격 공용) ──
        [Header("충격 스프링 (클수록 빠르고 딱딱)")]
        public float posStiff = 180f, posDamp = 18f;
        public float rotStiff = 220f, rotDamp = 20f;
        Vector3 kickPos, kickPosVel, kickRot, kickRotVel;

        // 전이 감지
        bool  prevGrounded = true;
        float prevVelY;
        int   prevHp = int.MinValue;
        float prevYaw, prevPitch;
        bool  hasPrevLook;

        void Awake() { Instance = this; }

        bool Acquire()
        {
            if (vmRoot != null) return true;
            var main = Main.Instance;
            cam = main != null ? main.Cam : Camera.main;
            if (cam == null) return false;
            var t = cam.transform.Find("KatanaViewmodel");
            if (t == null && cam.transform.childCount > 0) t = cam.transform.GetChild(0);
            if (t == null) return false;
            vmRoot = t;
            if (katana == null) katana = vmRoot.Find("Katana");
            // 씬(혹은 프리팹)에 잡혀 있는 배치를 기준으로 삼는다. 한 번만.
            if (!captured) { basePos = vmRoot.localPosition; baseRot = vmRoot.localRotation; captured = true; }
            if (!katanaCaptured && katana != null)
            {
                katanaBasePos = katana.localPosition; katanaBaseRot = katana.localRotation; katanaCaptured = true;
            }
            return true;
        }

        /// <summary>현재 위치를 새 기준으로 삼는다(포즈를 적용한 직후 등).</summary>
        public void RecaptureBase()
        {
            if (vmRoot == null) return;
            basePos = vmRoot.localPosition; baseRot = vmRoot.localRotation;
            if (katana != null)
            {
                katanaBasePos = katana.localPosition; katanaBaseRot = katana.localRotation; katanaCaptured = true;
            }
        }

        /// <summary>기준 배치를 직접 지정(F4 패널의 화면 구도 조절용).</summary>
        public void SetBase(Vector3 pos) { basePos = pos; captured = true; }

        public Vector3 BasePos => basePos;

        // ── 외부 발동(콘솔·테스트) ──
        public void KickLand(float impact)
        {
            kickPosVel += Vector3.down * (Mathf.Abs(impact) * landKick);
            PulseFingers(landGripPulse);
        }

        public void KickHit()
        {
            kickPosVel += Random.insideUnitSphere * hitPosKick;
            kickRotVel += Random.insideUnitSphere * hitRotKick * 10f;
            PulseFingers(hitGripPulse);
        }

        public void ResetAll()
        {
            kickPos = kickPosVel = kickRot = kickRotVel = Vector3.zero;
            strafeCur = swayRot = swayPos = Vector3.zero;
            airCur = 0f; moveAmt = 0f; hpOverride = -1f;
        }

        static void PulseFingers(float amount)
        {
            if (amount <= 0f) return;
            var fp = FingerPoser.Instance;
            if (fp != null) fp.PulseGrip(amount);
        }

        void LateUpdate()
        {
            var main = Main.Instance;
            if (main == null || !Acquire()) return;
            // 포즈 재생 중이면 손대지 않는다(PosePlayer가 루트까지 제어).
            if (PosePlayer.Instance != null && PosePlayer.Instance.IsPlaying) return;

            float dt = Mathf.Max(0.0001f, Time.deltaTime);
            ref readonly PlayerSim p = ref main.World.player;

            Vector3 pos = Vector3.zero;   // 누적 위치 오프셋
            Vector3 rot = Vector3.zero;   // 누적 회전 오프셋(도)

            // ── 이동 속도 ──
            // ★ p.vel 에는 수직 성분만 들어 있다(PlayerMovement는 수평을 pos에 직접 적용한다).
            //   그래서 vel.x/z 로 재면 항상 0이라 이동 연출이 통째로 죽는다.
            //   대신 "직전 틱 → 현재 틱"의 위치 차분으로 실제 수평 속도를 낸다.
            //   렌더 프레임이 아니라 고정 틱으로 나누므로 프레임레이트와 무관하다.
            bool frozen = HitStop.FrozenTicks > 0;
            if (!frozen)
            {
                Vector3 d = main.World.player.pos - main.PrevWorld.player.pos;
                d.y = 0f;
                float raw = d.magnitude / SimConfig.TickDelta;
                // 대시·찌르기 블링크는 정상 주행보다 훨씬 빠르다 — 그대로 쓰면 이동 연출이 최대로 터진다.
                // 상한을 걸어 "그냥 전력질주"로 읽히게 하고, 대시 고유 연출은 dashDrop이 따로 담당한다.
                Speed = Mathf.Min(raw, Mathf.Max(0.1f, speedCap));
            }
            // 히트스톱 중에는 sim이 멈춰 차분이 0이 된다. 그대로 두면 타격 순간 자세가 풀리므로
            // 직전 속도를 유지한다(Speed 갱신을 건너뛴다).

            // 이동 방향(단위벡터)도 차분에서 얻는다. 멈추면 직전 방향을 유지해 기울임이 뚝 끊기지 않게.
            if (!frozen)
            {
                Vector3 dv = main.World.player.pos - main.PrevWorld.player.pos;
                dv.y = 0f;
                if (dv.sqrMagnitude > 1e-8f) moveDir = dv.normalized;
            }

            float speed = Speed;
            float target = Mathf.Clamp01(speed / Mathf.Max(0.1f, refSpeed));
            moveAmt = Mathf.Lerp(moveAmt, target, 1f - Mathf.Exp(-moveEnterSpeed * dt));

            // ── ① 숨쉬기 ──
            if (enableBreathe)
            {
                float hp = hpOverride >= 0f
                    ? hpOverride
                    : Mathf.Clamp01(p.combat.hp / (float)Mathf.Max(1, CombatConfig.PlayerMaxHp));
                float amp = Mathf.Lerp(breatheAmpLow, breatheAmpFull, hp);
                float spd = Mathf.Lerp(breatheSpdLow, breatheSpdFull, hp);
                breathePhase += dt * spd;
                // 달릴 땐 숨 연출을 줄인다(bob이 대신함)
                float w = 1f - moveAmt * 0.7f;
                pos.y += Mathf.Sin(breathePhase) * amp * w;
                // 살짝 다른 주기의 피치 — 기계적으로 안 보이게
                rot.x += Mathf.Sin(breathePhase * 0.73f) * breathePitch * w;
            }

            // ── ② 달리기 bob + 하강 ──
            if (enableBob && moveAmt > 0.001f)
            {
                pos.y -= runDrop * moveAmt;                                   // 이동하면 손이 내려감

                // 흔들림은 "발걸음"에서 나온다 — 공중에선 발이 땅에 안 닿으므로 끈다.
                // (하강은 유지: 공중에서도 앞으로 나아가는 중이니까)
                bobAir = Mathf.Lerp(bobAir, p.grounded ? 1f : 0f, 1f - Mathf.Exp(-bobAirFade * dt));
                if (bobAir > 0.001f)
                {
                    bobPhase += dt * speed * bobRate;
                    float k = moveAmt * bobAir;
                    pos.y += Mathf.Sin(bobPhase * 2f) * bobVert  * k;         // 발걸음마다
                    pos.x += Mathf.Sin(bobPhase)      * bobHoriz * k;         // 두 걸음에 한 번 → 8자
                    rot.z += Mathf.Sin(bobPhase)      * bobRoll  * k;         // 터벅터벅
                }
            }

            // ── ③ 이동방향 기울임 ──
            if (enableStrafe)
            {
                // 이동 방향을 시선 기준 로컬로
                float yawRad = p.yaw * Mathf.Deg2Rad;
                Vector3 fwd   = new Vector3(Mathf.Sin(yawRad), 0f, Mathf.Cos(yawRad));
                Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
                // 방향 × 강도 — 멈추면 moveAmt가 0으로 줄며 기울임도 함께 풀린다
                Vector3 dir   = speed > 0.05f ? moveDir * moveAmt : Vector3.zero;
                float localR = Vector3.Dot(dir, right);
                float localF = Vector3.Dot(dir, fwd);
                Vector3 want = new Vector3(Mathf.Clamp(localR, -1f, 1f), 0f, Mathf.Clamp(localF, -1f, 1f));
                strafeCur = Vector3.Lerp(strafeCur, want, 1f - Mathf.Exp(-strafeSpring * dt));

                pos.x -= strafeCur.x * strafeShift;      // 오른쪽 가면 왼쪽으로 뒤처짐
                pos.z -= strafeCur.z * strafePush;
                rot.z += strafeCur.x * strafeRoll;       // 반대로 롤

                // 후진하면 칼이 들린다(가드를 올리는 자세).
                // ★ backLift = "순수 상승량". 하강분(runDrop)을 먼저 되돌린 뒤 그만큼 더 올리므로,
                //   runDrop 을 바꿔도 후진 높이가 따라 변하지 않는다(예전엔 종속돼 있어 과하게 튀었다).
                if (strafeCur.z < 0f)
                {
                    float back = -strafeCur.z;                          // 0..1 (뒤로 갈수록 1)
                    pos.y += back * (runDrop * moveAmt + backLift);
                }
            }

            // ── 대시: 확 내려가며 뒤로 당겨지고, 손가락을 움켜쥔다 ──
            if (enableDash)
            {
                bool dashing = p.dashTicks > 0;
                if (dashing && prevDashTicks == 0 && enableFingers) PulseFingers(dashGripPulse);
                prevDashTicks = p.dashTicks;

                dashAmt = Mathf.Lerp(dashAmt, dashing ? 1f : 0f, 1f - Mathf.Exp(-dashSpring * dt));
                if (dashAmt > 0.001f)
                {
                    pos.y -= dashDrop * dashAmt;
                    pos.z -= dashPull * dashAmt;
                    // 대시 방향(좌우 성분)으로 기울임
                    float yawR = p.yaw * Mathf.Deg2Rad;
                    Vector3 rightV = new Vector3(Mathf.Cos(yawR), 0f, -Mathf.Sin(yawR));
                    rot.z += Vector3.Dot(p.dashDir, rightV) * dashRoll * dashAmt;
                }
            }

            // ── ④ 공중 관성 ──
            if (enableAir)
            {
                float want = Mathf.Clamp(-p.vel.y * airFactor, -airMax, airMax);
                if (p.grounded) want = 0f;
                airCur = Mathf.Lerp(airCur, want, 1f - Mathf.Exp(-airSpring * dt));
                pos.y += airCur;
            }

            // ── ⑤ 착지 감지 ──
            if (p.grounded && !prevGrounded && enableLand)
            {
                float impact = Mathf.Max(0f, -prevVelY);
                if (impact > landMinSpeed) KickLand(impact);
            }
            // 점프 순간(접지 → 공중, 위로 뜸) — 확 움켜쥔다
            if (!p.grounded && prevGrounded && p.vel.y > 0.1f && enableFingers)
                PulseFingers(jumpGripPulse);
            prevGrounded = p.grounded; prevVelY = p.vel.y;

            // ── 손가락 지속 그립: 달리는 동안·공중에 있는 동안 계속 쥐고 있는다 ──
            if (enableFingers)
            {
                var fp = FingerPoser.Instance;
                if (fp != null)
                {
                    float want = moveAmt * runGrip;                 // 빨리 달릴수록 세게
                    if (!p.grounded)     want = Mathf.Max(want, airGrip);
                    if (p.dashTicks > 0) want = Mathf.Max(want, dashGrip);   // 대시가 가장 강하게
                    fp.SetSustain(want);
                }
            }

            // ── ⑥ 피격 감지 ──
            if (prevHp != int.MinValue && p.combat.hp < prevHp && enableHit) KickHit();
            prevHp = p.combat.hp;

            // ── ⑦ 시선 지연 ──
            if (enableSway)
            {
                float yaw = main.LookYaw, pitch = main.LookPitch;
                if (hasPrevLook)
                {
                    float dYaw   = Mathf.DeltaAngle(prevYaw, yaw);
                    float dPitch = pitch - prevPitch;
                    swayRot.y = Mathf.Clamp(swayRot.y - dYaw   * swayFactor, -swayMax, swayMax);
                    swayRot.x = Mathf.Clamp(swayRot.x + dPitch * swayFactor, -swayMax, swayMax);
                    swayPos.x = Mathf.Clamp(swayPos.x - dYaw   * swayShift, -0.05f, 0.05f);
                    swayPos.y = Mathf.Clamp(swayPos.y + dPitch * swayShift, -0.05f, 0.05f);
                }
                prevYaw = yaw; prevPitch = pitch; hasPrevLook = true;

                float k = 1f - Mathf.Exp(-swaySpring * dt);   // 0으로 되돌림
                swayRot = Vector3.Lerp(swayRot, Vector3.zero, k);
                swayPos = Vector3.Lerp(swayPos, Vector3.zero, k);
                pos += swayPos; rot += swayRot;
            }

            // ── 충격 스프링(착지·피격) ──
            Spring(ref kickPos, ref kickPosVel, posStiff, posDamp, dt);
            Spring(ref kickRot, ref kickRotVel, rotStiff, rotDamp, dt);
            pos += kickPos; rot += kickRot;

            // ── 합산 적용 ──
            LastPos = pos; LastRot = rot;

            if (driveKatana && katana != null)
            {
                // ★ 칼만 움직인다. 그립(Grip_R/L)이 칼의 자식이라 IK가 팔을 알아서 따라오게 한다.
                //   루트를 통째로 옮기면 팔·칼이 뻣뻣하게 같이 밀리지만, 이 방식은 어깨가 고정된 채
                //   팔꿈치가 접히며 따라와 훨씬 자연스럽다.
                //   루트 스케일이 100이므로 월드 단위 오프셋을 칼의 로컬 단위로 환산해야 한다.
                float s = Mathf.Max(0.0001f, vmRoot.lossyScale.x);
                katana.localPosition = katanaBasePos + pos / s;
                katana.localRotation = katanaBaseRot * Quaternion.Euler(rot);
                // 루트는 기준 배치 유지(화면 구도)
                vmRoot.localPosition = basePos;
                vmRoot.localRotation = baseRot;
            }
            else
            {
                vmRoot.localPosition = basePos + pos;
                vmRoot.localRotation = baseRot * Quaternion.Euler(rot);
            }
        }

        /// <summary>감쇠 스프링 — 오프셋을 0으로 되돌린다. 발동은 vel에 킥.</summary>
        static void Spring(ref Vector3 x, ref Vector3 v, float stiff, float damp, float dt)
        {
            Vector3 a = -stiff * x - damp * v;
            v += a * dt;
            x += v * dt;
        }
    }

    /// <summary>Play 시 자동 부착.</summary>
    public static class ViewmodelMotionBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<ViewmodelMotion>() == null)
                new GameObject("[ViewmodelMotion]").AddComponent<ViewmodelMotion>();
        }
    }
}
