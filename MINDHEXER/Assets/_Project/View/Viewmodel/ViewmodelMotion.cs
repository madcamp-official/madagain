using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 1인칭 뷰모델(팔) 절차 애니메이션 — 뷰모델 루트의 유일한 작성자.
    ///
    /// <para>Precog의 <c>ViewmodelMotion</c>(전투 게임, 카타나 전용)에서 원리만 가져와 새로 짰다.
    /// 원본은 대시·피격·HP·손가락 전부 삭제된 전투 시뮬레이션(<c>Main.Instance.World.player</c> 등)에
    /// 배선돼 있었는데, 우리 게임엔 그 시스템 자체가 없다(전투·HP·대시 없음). 그래서 데이터 출처를
    /// 전부 <see cref="FirstPersonPlayer"/>·<see cref="AutoTraversal"/>·<see cref="HackDriver"/>의
    /// 공개 상태로 새로 배선했다.</para>
    ///
    /// <para>최종 위치 = 기준위치 + Σ(레이어 위치 오프셋), 최종 회전도 마찬가지로 합산 후 한 번에 적용.
    /// 원리는 셋뿐이다:
    ///   ① 사인파(위상 적분) — 숨쉬기·걷기 흔들림처럼 반복되는 것.
    ///   ② 감쇠 스프링 — 착지처럼 한 방 맞고 되돌아오는 것.
    ///   ③ 지연 추적 — 시선 지연(마우스 스웨이)처럼 몸이 늦게 따라오는 것.</para>
    ///
    /// <para><b>잡고 올라가기(맨틀) 중엔 완전히 정지한다.</b> 그때는 손이 모서리에 고정돼야 하는데
    /// 이 레이어들(특히 시선 지연)을 계속 얹으면 고정된 손이 마우스 따라 흔들려서 서로 싸운다.
    /// <see cref="AutoTraversal.Busy"/>를 봐서 등반 중엔 <see cref="MantleRig"/>에게 전적으로 넘긴다.</para>
    ///
    /// <para>손가락은 전투(달리기·공중·대시) 대신 <b>해킹 조준/상호작용</b>을 트리거로 쓴다 —
    /// <see cref="HackDriver.Controlled"/>(조종 중)는 강하게, 단순 조준 중엔 약하게 쥔다.</para>
    /// </summary>
    [DefaultExecutionOrder(-50)]   // HandIK(팔 IK, 기본 실행순서 0)보다 먼저 — 절차 오프셋을 얹은 뒤 IK가 손을 마저 풀어야 함
    public class ViewmodelMotion : MonoBehaviour
    {
        public static ViewmodelMotion Instance { get; private set; }

        [Tooltip("찾을 뷰모델 루트 이름. 못 찾으면 이 오브젝트의 첫 자식으로 대체.")]
        public string viewmodelRootName = ViewmodelCamera.ViewmodelRootName;

        Transform vmRoot;

        /// <summary>진단용 — 뷰모델 루트가 카메라를 품고 있는가. <see cref="ViewmodelRoot"/>가
        /// 막으므로 정상이라면 항상 false다. GameBoot의 감사가 재발을 잡는 데 쓴다.</summary>
        public bool RootIsCamera => vmRoot != null && vmRoot.GetComponentInChildren<Camera>(true) != null;
        FirstPersonPlayer fpp;
        AutoTraversal auto;
        HackDriver hack;
        Camera cam;

        [Header("기준 배치 (씬에서 잡아둔 값 — 절차 오프셋은 이 위에 얹힌다)")]
        public Vector3    basePos;
        public Quaternion baseRot = Quaternion.identity;
        bool captured;

        [Header("레이어 on/off")]
        public bool enableBreathe = true;
        public bool enableBob     = true;
        public bool enableStrafe  = true;
        public bool enableAir     = true;
        public bool enableLand    = true;
        public bool enableSway    = true;
        public bool enableFingers = true;

        // ── ① 숨쉬기 ──
        [Header("숨쉬기")]
        public float breatheAmp = 0.006f;
        public float breatheSpeed = 2.2f;
        [Tooltip("가슴이 들리는 느낌 — 미세 피치 회전(도)")]
        public float breathePitch = 0.35f;
        float breathePhase;

        // ── ② 걷기 bob + 하강 ──
        [Header("걷기")]
        [Tooltip("걸을 때 손이 내려가는 양(m)")]
        public float runDrop = 0.03f;
        [Tooltip("후진할 때 위로 올라가는 순수 상승량(m) — runDrop과 독립")]
        public float backLift = 0.02f;
        public float bobVert = 0.022f;
        public float bobHoriz = 0.03f;
        public float bobRoll = 1.6f;
        public float bobRate = 0.85f;
        public float moveEnterSpeed = 7f;
        [Tooltip("이 속도면 이동강도 1.0")]
        public float refSpeed = 5.5f;
        [Tooltip("공중에 뜨면 흔들림이 사라지는 속도")]
        public float bobAirFade = 10f;
        float bobPhase, moveAmt, bobAir = 1f;

        // ── ③ 이동방향 기울임 ──
        [Header("이동방향 기울임")]
        public float strafeRoll = 1.8f;
        public float strafeShift = 0.016f;
        public float strafePush = 0.014f;
        public float strafeSpring = 9f;
        Vector3 strafeCur;

        // ── ④ 공중 관성 ──
        [Header("공중 관성")]
        public float airFactor = 0.006f;
        public float airMax = 0.06f;
        public float airSpring = 10f;
        float airCur;

        // ── ⑤ 착지 딥 (카메라 흔들림 = MotionFeel과 별개, 팔 전용) ──
        [Header("착지 딥 (팔 전용 — 카메라는 MotionFeel이 따로 함)")]
        public float landKick = 0.05f;
        public float landMinSpeed = 3f;
        public float posStiff = 180f, posDamp = 18f;
        Vector3 kickPos, kickPosVel;

        // ── ⑥ 시선 지연 ──
        [Header("시선 지연(마우스 스웨이)")]
        public float swayFactor = 0.3f;
        public float swayMax = 4f;
        public float swayShift = 0.002f;
        public float swaySpring = 12f;
        [Tooltip("VR에서의 시선 지연 배율. 기본 0 — VR엔 마우스 개념이 없고, 머리를 돌릴 때마다 팔이 " +
                 "늦게 따라오면 '내 팔이 내 것 같지 않은' 이질감과 멀미가 난다. " +
                 "MotionFeel.vrRollScale과 같은 취지의 게이트다.")]
        [Range(0f, 1f)] public float vrSwayScale = 0f;
        Vector3 swayRot, swayPos;
        float prevYaw, prevPitch;
        bool hasPrevLook;

        // ── ⑦ 손가락 — 조준/상호작용 ──
        [Header("손가락 (해킹 조준/상호작용)")]
        [Tooltip("Hackable을 그냥 조준만 하고 있을 때 쥐는 정도")]
        public float aimGrip = 0.25f;
        [Tooltip("조종(Controlled) 중일 때 쥐는 정도")]
        public float controlGrip = 0.7f;
        [Tooltip("조준 판정 레이어. 비우면 전체.")]
        public LayerMask aimMask = ~0;
        public float aimRayDistance = 100f;

        bool prevGrounded = true;
        float prevVelY;

        void Awake()
        {
            Instance = this;
            fpp = GetComponent<FirstPersonPlayer>();
            auto = GetComponent<AutoTraversal>();
            hack = GetComponent<HackDriver>();
            cam = GetComponent<Camera>() ?? Camera.main;
        }

        /// <summary>
        /// 뷰모델 루트 확보. 판정은 <see cref="ViewmodelRoot"/> 하나가 한다.
        ///
        /// <para><b>못 찾으면 스스로 꺼진다.</b> 예전엔 <c>transform.GetChild(0)</c>로 아무거나 집었는데,
        /// 이 컴포넌트는 <c>[PlayerBody]</c>에 붙으므로 그 첫 자식인 <b>Main Camera</b>가 잡혔다.
        /// 그 뒤로 매 LateUpdate마다 카메라의 위치·회전을 절대값으로 덮어써 PC에선 시점 회전이,
        /// VR에선 눈높이·위치가 죽었다. 팔이 안 움직이는 것보다 시점이 죽는 게 훨씬 나쁘다.</para>
        /// </summary>
        bool Acquire()
        {
            if (vmRoot != null) return true;
            if (_gaveUp) return false;

            Transform t = ViewmodelRoot.Find(transform);
            if (t == null)
            {
                _gaveUp = true;
                enabled = false;   // 매 프레임 헛도는 것도, 엉뚱한 걸 흔드는 것도 막는다
                Debug.LogWarning(
                    $"[뷰모델] '{viewmodelRootName}' 루트를 찾지 못해 절차 모션을 끕니다. " +
                    $"팔을 쓰려면 Main Camera 아래에 '{viewmodelRootName}' 오브젝트를 두십시오.", this);
                return false;
            }

            vmRoot = t;
            if (!captured) { basePos = vmRoot.localPosition; baseRot = vmRoot.localRotation; captured = true; }
            return true;
        }

        bool _gaveUp;

        /// <summary>현재 위치를 새 기준으로 삼는다(포즈 적용 직후 등). PosePlayer가 부른다.</summary>
        public void RecaptureBase()
        {
            if (vmRoot == null) return;
            basePos = vmRoot.localPosition; baseRot = vmRoot.localRotation;
        }

        public void SetBase(Vector3 pos) { basePos = pos; captured = true; }
        public Vector3 BasePos => basePos;

        public void KickLand(float impact) => kickPosVel += Vector3.down * (Mathf.Abs(impact) * landKick);

        public void ResetAll()
        {
            kickPos = kickPosVel = Vector3.zero;
            strafeCur = swayRot = swayPos = Vector3.zero;
            airCur = 0f; moveAmt = 0f;
        }

        void LateUpdate()
        {
            if (!Acquire() || fpp == null) return;

            // 포즈 재생 중이면 손대지 않는다(PosePlayer가 루트까지 제어).
            if (PosePlayer.Instance != null && PosePlayer.Instance.IsPlaying) return;

            // 잡고 올라가기 중엔 완전히 정지 — MantleRig가 손 위치를 전적으로 소유한다(§클래스 주석).
            if (auto != null && auto.Busy) return;

            float dt = Mathf.Max(0.0001f, Time.deltaTime);

            Vector3 pos = Vector3.zero;
            Vector3 rot = Vector3.zero;

            Vector2 vel = fpp.move.Output;
            float speed = vel.magnitude;
            bool grounded = fpp.Grounded;

            float target = Mathf.Clamp01(speed / Mathf.Max(0.1f, refSpeed));
            moveAmt = Mathf.Lerp(moveAmt, target, 1f - Mathf.Exp(-moveEnterSpeed * dt));

            // ── ① 숨쉬기 ──
            if (enableBreathe)
            {
                breathePhase += dt * breatheSpeed;
                float w = 1f - moveAmt * 0.7f;   // 걸을 땐 bob이 대신하므로 숨쉬기는 줄인다
                pos.y += Mathf.Sin(breathePhase) * breatheAmp * w;
                rot.x += Mathf.Sin(breathePhase * 0.73f) * breathePitch * w;   // 다른 주기 — 기계적으로 안 보이게
            }

            // ── ② 걷기 bob + 하강 ──
            if (enableBob && moveAmt > 0.001f)
            {
                pos.y -= runDrop * moveAmt;

                bobAir = Mathf.Lerp(bobAir, grounded ? 1f : 0f, 1f - Mathf.Exp(-bobAirFade * dt));
                if (bobAir > 0.001f)
                {
                    bobPhase += dt * speed * bobRate;
                    float k = moveAmt * bobAir;
                    pos.y += Mathf.Sin(bobPhase * 2f) * bobVert  * k;
                    pos.x += Mathf.Sin(bobPhase)      * bobHoriz * k;
                    rot.z += Mathf.Sin(bobPhase)      * bobRoll  * k;
                }
            }

            // ── ③ 이동방향 기울임 ──
            if (enableStrafe)
            {
                Vector3 dir = speed > 0.05f ? new Vector3(fpp.Wish.x, 0f, fpp.Wish.y) : Vector3.zero;
                // Wish는 월드 방향이라 시선 기준 로컬로 다시 투영한다.
                Vector3 fwd = fpp.FlatForward;
                Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
                float localR = Vector3.Dot(dir, right);
                float localF = Vector3.Dot(dir, fwd);
                Vector3 want = new Vector3(Mathf.Clamp(localR, -1f, 1f), 0f, Mathf.Clamp(localF, -1f, 1f));
                strafeCur = Vector3.Lerp(strafeCur, want, 1f - Mathf.Exp(-strafeSpring * dt));

                pos.x -= strafeCur.x * strafeShift;
                pos.z -= strafeCur.z * strafePush;
                rot.z += strafeCur.x * strafeRoll;

                if (strafeCur.z < 0f)
                {
                    float back = -strafeCur.z;
                    pos.y += back * (runDrop * moveAmt + backLift);
                }
            }

            // ── ④ 공중 관성 ──
            if (enableAir)
            {
                float want = Mathf.Clamp(-fpp.VerticalVelocity * airFactor, -airMax, airMax);
                if (grounded) want = 0f;
                airCur = Mathf.Lerp(airCur, want, 1f - Mathf.Exp(-airSpring * dt));
                pos.y += airCur;
            }

            // ── ⑤ 착지 감지 ──
            if (grounded && !prevGrounded && enableLand)
            {
                float impact = Mathf.Max(0f, -prevVelY);
                if (impact > landMinSpeed) KickLand(impact);
            }
            prevGrounded = grounded; prevVelY = fpp.VerticalVelocity;

            // ── ⑥ 시선 지연 ──
            // 시점 각도는 카메라가 갖는다. 이 컴포넌트가 붙은 [PlayerBody]는 회전하지 않으므로
            // (FirstPersonPlayer가 rotation=identity로 고정) 자기 트랜스폼을 읽으면 항상 0이라
            // 스웨이가 영영 안 걸린다 — view(카메라)를 읽어야 한다.
            //
            // ★ 로컬이 아니라 <b>월드</b> 각도를 읽는다. 리그가 [Head](yaw/pitch) → 카메라(롤)로
            //   나뉘어 있어서 카메라 로컬에는 롤밖에 없다 — 로컬을 읽으면 또 0만 나온다.
            float swayScale = VrMode.Enabled ? vrSwayScale : 1f;
            Transform look = fpp != null && fpp.view != null ? fpp.view : null;
            if (enableSway && swayScale > 0f && look != null)
            {
                Vector3 e = look.eulerAngles;
                float yaw = e.y, pitch = e.x > 180f ? e.x - 360f : e.x;   // pitch는 -85..85라 랩어라운드만 풀면 됨
                if (hasPrevLook)
                {
                    float dYaw   = Mathf.DeltaAngle(prevYaw, yaw) * swayScale;
                    float dPitch = (pitch - prevPitch) * swayScale;
                    swayRot.y = Mathf.Clamp(swayRot.y - dYaw   * swayFactor, -swayMax, swayMax);
                    swayRot.x = Mathf.Clamp(swayRot.x + dPitch * swayFactor, -swayMax, swayMax);
                    swayPos.x = Mathf.Clamp(swayPos.x - dYaw   * swayShift, -0.05f, 0.05f);
                    swayPos.y = Mathf.Clamp(swayPos.y + dPitch * swayShift, -0.05f, 0.05f);
                }
                prevYaw = yaw; prevPitch = pitch; hasPrevLook = true;

                float k = 1f - Mathf.Exp(-swaySpring * dt);
                swayRot = Vector3.Lerp(swayRot, Vector3.zero, k);
                swayPos = Vector3.Lerp(swayPos, Vector3.zero, k);
                pos += swayPos; rot += swayRot;
            }

            // ── 착지 충격 스프링 ──
            Spring(ref kickPos, ref kickPosVel, posStiff, posDamp, dt);
            pos += kickPos;

            // ── ⑦ 손가락 — 해킹 조준/상호작용 ──
            if (enableFingers)
            {
                var fp = FingerPoser.Instance;
                if (fp != null)
                {
                    float want = 0f;
                    if (hack != null && hack.Controlled != null) want = controlGrip;
                    else if (IsAimingHackable()) want = aimGrip;
                    fp.SetSustain(want);
                }
            }

            vmRoot.localPosition = basePos + pos;
            vmRoot.localRotation = baseRot * Quaternion.Euler(rot);
        }

        /// <summary>
        /// 지금 조준선 위에 Hackable이 있는가 — HackDriver를 건드리지 않고 독립적으로 판정한다
        /// (같은 판정을 두 곳에서 하지만, 손가락 연출 하나 때문에 다른 세션 파일을 고칠 이유는 없다).
        /// </summary>
        bool IsAimingHackable()
        {
            Camera c = hack != null && hack.cam != null ? hack.cam : cam;
            if (c == null) return false;
            var ray = new Ray(c.transform.position, c.transform.forward);
            if (!Physics.Raycast(ray, out RaycastHit hitInfo, aimRayDistance, aimMask)) return false;
            var hb = hitInfo.collider.GetComponentInParent<Hackable>();
            return hb != null && hitInfo.distance <= hb.hackRange;
        }

        static void Spring(ref Vector3 x, ref Vector3 v, float stiff, float damp, float dt)
        {
            Vector3 a = -stiff * x - damp * v;
            v += a * dt;
            x += v * dt;
        }
    }

    /// <summary>
    /// Play 시 자동 부착 — <b>뷰모델 루트가 실제로 있는 씬에서만</b>.
    ///
    /// <para>예전엔 무조건 붙였다. 뷰모델이 없는 씬(HackSandbox 등)에서도 붙어서 흔들 대상을
    /// 찾다가 카메라를 집었다. 흔들 것이 없으면 컴포넌트 자체가 생기지 않는 게 맞다.</para>
    /// </summary>
    public static class ViewmodelMotionBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var fpp = Object.FindFirstObjectByType<FirstPersonPlayer>();
            if (fpp == null || fpp.GetComponent<ViewmodelMotion>() != null) return;
            if (ViewmodelRoot.Find(fpp.transform) == null) return;   // 흔들 팔이 없으면 붙이지 않는다
            fpp.gameObject.AddComponent<ViewmodelMotion>();
        }
    }
}
