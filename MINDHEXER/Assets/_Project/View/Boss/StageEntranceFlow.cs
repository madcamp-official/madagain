using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 스테이지 입구 1회 사이클의 <b>유일한 지휘자</b>. (보스전_설계 §1·§6)
    ///
    /// <code>
    /// Idle     플레이어가 아직 안 지남. 대왕프레스는 <b>평상시 = 일반 유압프레스</b>
    /// Sealing  보스 팔이 출구를 막는다 → 출구가 찌그러지고 투명벽 ON
    /// Wedged   머리를 입구에 물린다. 부들부들 N초. 프레스 잠금 해제 + 플릭 전용 + 상한
    ///  ├ 프레스가 상한에 닿음 → Crushed
    ///  └ 시간 초과            → Failed
    /// Crushed  머리 단계 +1 → 봉쇄 해제 → 머리 빼며 비틀거림 → 후퇴
    /// Failed   낑긴 곳을 부수고 플레이어에게 돌진 → 즉사
    /// </code>
    ///
    /// <para><b>프레스 상태는 이 컴포넌트만 만진다</b> — <see cref="Hackable.enabled"/>(잠금),
    /// <see cref="ActuatorControl.allowHold"/>(플릭 전용), <see cref="TelescopingActuator.LimitT"/>(상한)
    /// 셋 다. 프레스나 <see cref="BossWedgePoint"/>가 각자 만지면 어느 쪽이 마지막에 썼는지로 버그가 난다.
    /// 그리고 <b>이 컴포넌트가 없으면 대왕프레스는 그냥 큰 유압프레스다</b> — 추격 로직이 프레스 코드로
    /// 새어 들어가지 않게 지킨 경계다.</para>
    ///
    /// <para><b>머리에 IK를 쓰지 않는다.</b> 머리는 척추 체인의 말단이라, 루트를 옮기면 머리가 그만큼
    /// 정확히 옮겨진다(오차 0). 낑겨 있는 동안 보스는 제자리라 루트 이동에 제약이 없고, 머리 <b>방향</b>은
    /// 이미 <see cref="BossSpineAim"/>이 소유한다. 상체에 IK를 새로 넣는 건 위험하다 — 상체 커브가
    /// 가슴 메시를 찌그러뜨려 클립에서 <c>/Waist</c> 이하를 빼낸 이력이 있다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(30)]   // BossSpineAim(45)·BossArmRig(50)보다 먼저 — 루트를 먼저 놓고 그 위에 자세가 얹힌다
    public class StageEntranceFlow : MonoBehaviour, IRunResettable
    {
        public enum Phase { Idle, Bursting, Sealing, Wedged, Crushed, Failed, Done }

        /// <summary>
        /// 이 입구의 사이클이 <b>무엇으로 시작하는가</b>. (기초_설계안 §0.4)
        ///
        /// <list type="bullet">
        /// <item><b>PressRaised</b> — 전환점 딱 한 곳. 플레이어가 막힌 프레스를 <b>살짝 들어올리는 순간</b>
        ///   보스가 틈으로 머리를 박는다. 이 모드에서는 프레스를 잠그지 않는다 — 플레이어가 열어야 할
        ///   <b>문</b>이기 때문이다.</item>
        /// <item><b>PlayerTrigger</b> — 후반부 도주 구간. 플레이어가 입구에 도달하면 보스가 앞질러 막는다.
        ///   프레스는 보스가 오기 전까지 잠긴다(미리 내려놔서 낑길 자리를 막는 버그 방지).</item>
        /// </list>
        /// </summary>
        public enum Entry { PlayerTrigger, PressRaised }

        [Header("회차")]
        [Tooltip("몇 번째 입구인가(0부터). 회차별 상승(설계 §7)에 쓴다 — 뒤로 갈수록 낑김 시간이 짧다.")]
        public int index;

        [Header("진입")]
        [Tooltip("이 입구가 무엇으로 시작하는가. 전환점 한 곳만 PressRaised, 나머지는 PlayerTrigger.")]
        public Entry entryMode = Entry.PlayerTrigger;

        [Tooltip("PressRaised 전용 — 프레스가 이 t 이하로 <b>살짝만</b> 올라가면 발동한다.\n" +
                 "프레스는 축이 아래(−Y)라 t=1이 내려온 상태(막힘), t=0이 완전히 올라간 상태다.\n" +
                 "0.85면 15%만 들어올려도 보스가 온다 — '살짝 여는 순간'이라 크게 잡는다.")]
        [Range(0f, 1f)] public float raisedThreshold = 0.85f;

        [Tooltip("PressRaised 전용 — 보스가 머리를 박으며 프레스를 밀어올리는 시간(초).\n" +
                 "이 시간 동안 플레이어는 프레스를 못 만진다. 머리 진입과 같은 타이머를 쓴다.")]
        public float burstTime = 0.25f;

        [Tooltip("낑김이 끝난 뒤 보스가 돌아갈 <b>걷기 높이</b>(루트 월드 y). 비워 두면(NaN) 사이클 시작 시 " +
                 "현재 y를 기억해 그리로 되돌린다.\n" +
                 "★ 걷기와 낑김은 높이가 크게 다르다 — 걸을 땐 머리가 천장 아래(루트 ≈ −8), 낑길 땐 머리가 " +
                 "입구 높이(루트 ≈ −37)로 40m 넘게 내려간다. 되돌리지 않으면 <b>다음 추격에서 보스가 " +
                 "땅속을 걸어온다.</b>")]
        public float walkRootY = float.NaN;

        [Tooltip("이 입구가 추격을 <b>시작시키는가</b>. 전환점 한 곳만 켠다 — 켜져 있으면 사이클이 시작될 때 " +
                 "BossChaseState.Begin()을 부른다. 나머지 입구는 추격 중일 때만 발동한다.")]
        public bool startsChase = false;

        [Tooltip("회차마다 낑김 시간에 곱할 비율. 0.85면 회차마다 15% 짧아진다. 1이면 상승 없음.")]
        public float wedgeSecondsPerIndex = 0.85f;

        [Header("입구")]
        [Tooltip("플레이어 통과 감지 트리거.")]
        public Collider playerTrigger;

        [Tooltip("보스 팔이 막는 동안 플레이어를 못 지나가게 하는 벽. 콜라이더만 두고 렌더러는 없앨 것.\n" +
                 "★ 임시방편입니다 — 최종적으로는 보스 팔 자체가 막는 것으로 보여야 합니다.")]
        public Collider sealWall;

        [Tooltip("출구·낑김부 지오메트리의 자세 담당. 찌그러짐·부서짐 자세를 여기에 굽는다.")]
        public PartsPoser geo;

        [Tooltip("봉쇄 시 갈 자세 이름.")]
        public string sealedPose = "찌그러짐";

        [Tooltip("실패 시 갈 자세 이름(낑긴 곳이 부서짐).")]
        public string brokenPose = "부서짐";

        [Header("프레스 — 이 컴포넌트가 단독 소유")]
        public Hackable pressHackable;
        public ActuatorControl pressControl;
        public TelescopingActuator pressActuator;

        [Tooltip("헤드에서 보스 머리에 닿는 지점. 비우면 헤드 자신의 위치를 쓴다(두께만큼 어긋난다).")]
        public Transform pressFace;

        [Header("보스")]
        public BossWedgePoint wedge;
        public BossChase chase;
        public BossHeadCrush headCrush;
        public BossArmRig arms;

        [Tooltip("보스 루트(옮겨서 머리를 입구에 물린다). 비우면 chase의 트랜스폼.")]
        public Transform bossRoot;

        [Tooltip("등장 전까지 숨길 보스 오브젝트. 비우면 bossRoot의 게임오브젝트를 쓴다.\n" +
                 "★ 렌더러가 아니라 게임오브젝트 전체를 끈다 — 콜라이더가 남으면 프레스 조준을 막는다.")]
        public GameObject bossVisual;

        [Tooltip("머리에서 프레스가 닿을 지점 마커. 머리 찌그러짐 자세 목록에 넣어 두면 " +
                 "단계가 오를 때 같이 내려온다 — 회차마다 프레스가 더 깊이 들어간다.")]
        public Transform headContact;

        [Tooltip("입구에 물릴 머리 기준점. 비우면 headContact.")]
        public Transform headAnchor;

        [Tooltip("보스가 팔로 짚어 출구를 막는 지점.")]
        public Transform sealHandTarget;

        [Tooltip("막는 손. 출구가 보스 기준 어느 쪽인지에 맞춘다. (armPoser를 쓰면 무시된다)")]
        public BossArmRig.Side sealHand = BossArmRig.Side.Left;

        [Tooltip("보스 <b>팔 봉쇄 자세</b>를 재생하는 포저(BossArmSealPose 애셋). 넣으면 IK(sealHandTarget) 대신 " +
                 "이 자세를 쓴다 — 본 로컬 값이라 입구마다 좌표를 안 잡아도 4곳에서 똑같이 나온다.")]
        public PartsPoser armPoser;

        [Tooltip("봉쇄 자세 이름.")]
        public string armSealPose = "봉쇄";

        [Tooltip("팔을 되돌릴 홈 자세 이름.")]
        public string armHomePose = "홈";

        [Tooltip("허리 굽힘. 낑겨 있는 동안은 꺼야 어깨가 움직여 봉쇄한 손이 딸려가지 않는다.")]
        public BossSpineAim spineAim;

        [Header("실패 — 돌진")]
        [Tooltip("플레이어에게 돌진하는 속도(m/s). BossChase.speedFar보다 훨씬 빨라야 '끝났다'가 느껴진다.")]
        public float chargeSpeed = 60f;

        [Tooltip("이 거리 안에 들어오면 즉사. 돌진은 피할 수 없는 각본이다.")]
        public float chargeKillDistance = 10f;

        [Tooltip("돌진이 이 시간을 넘으면(막혀서 못 닿는 등) 그냥 즉사시킨다 — 영원히 안 끝나는 것 방지.")]
        public float chargeTimeout = 4f;

        [Header("성공 — 머리 빼기·비틀거림")]
        [Tooltip("머리를 뒤로 빼는 거리(m).")]
        public float pullOutDistance = 15f;

        [Tooltip("빼는 데 걸리는 시간(초).")]
        public float pullOutTime = 0.7f;

        [Tooltip("비틀거림 진폭(도).")]
        public float staggerDeg = 5f;

        [Tooltip("비틀거림이 잦아드는 시간(초).")]
        public float staggerTime = 1.2f;

        /// <summary>지금 단계.</summary>
        public Phase Current { get; private set; } = Phase.Idle;

        /// <summary>사이클이 끝났을 때(성공·실패 무관). 다음 스테이지로 넘기는 쪽이 구독한다.</summary>
        public event System.Action<bool> OnFinished;   // true = 찍었다

        Transform Root => bossRoot != null ? bossRoot : (chase != null ? chase.transform : null);
        Transform Anchor => headAnchor != null ? headAnchor : headContact;
        Vector3 PressFaceWorld => pressFace != null ? pressFace.position
                                : (pressActuator != null && pressActuator.head != null ? pressActuator.head.position
                                : transform.position);

        float _phaseT;
        Vector3 _pullFrom, _pullTo;
        Quaternion _staggerBase;
        bool _pressWasHackable = true;

        void Awake()
        {
            // 평상시 상태를 기억해 두고 사이클이 끝나면 정확히 그리로 되돌린다.
            if (pressHackable != null) _pressWasHackable = pressHackable.enabled;

            // ★ 두 진입 모드 다 평상시 = 일반 유압프레스로 시작한다. PlayerTrigger 입구를 여기서
            //   잠그면 추격이 시작되기도 전인 전반부에서 대왕프레스를 아예 못 만지게 된다 —
            //   "보스가 오기 전까지 잠근다"는 추격이 시작된 뒤(HandleChaseActiveChanged가 이미
            //   끝까지 올리며 다시 잠근다)에만 의미가 있다.
            RestorePress();

            if (sealWall != null) sealWall.enabled = false;

            // 등장 전까지 보스는 존재하지 않는 것처럼 다뤄야 한다(§보스 숨김).
            if (!BossChaseState.Active) SetBossHidden(true);
        }

        // ── 보스 숨김 ─────────────────────────────────────────────────────

        /// <summary>
        /// 등장 전까지 보스를 <b>통째로 끈다</b>. (렌더러만 끄면 콜라이더가 남는다)
        ///
        /// <para><b>왜 게임오브젝트 전체인가</b>: 50배 스케일 보스의 콜라이더가 전환점 프레스를
        /// 통째로 감싸고 있어(x 474~511 안에 프레스 485.7이 들어간다), 살아 있으면 <b>해킹 조준
        /// 레이가 프레스에 닿기 전에 보스 몸에 먼저 막힌다.</b> 걷기 클립이 제자리에서 도는 것도
        /// 같이 멈춘다 — 세 증상이 한 원인이라 한 곳에서 끈다.</para>
        ///
        /// <para>입구가 넷이라 <see cref="Awake"/>에서 각자 부르지만, 이미 그 상태면 아무것도 하지
        /// 않으므로 순서에 상관없다.</para>
        /// </summary>
        void SetBossHidden(bool hidden)
        {
            GameObject go = bossVisual != null ? bossVisual : (Root != null ? Root.gameObject : null);
            if (go == null) return;
            if (go.activeSelf != hidden) return;   // 이미 원하는 상태다
            go.SetActive(!hidden);
        }

        void OnEnable()
        {
            if (wedge != null) wedge.OnTimedOut += HandleTimeout;
            BossChaseState.OnActiveChanged += HandleChaseActiveChanged;
        }

        void OnDisable()
        {
            if (wedge != null) wedge.OnTimedOut -= HandleTimeout;
            BossChaseState.OnActiveChanged -= HandleChaseActiveChanged;
        }

        /// <summary>
        /// 추격이 시작되는 순간, <b>모든 입구의 프레스를 끝까지 올린다.</b>
        ///
        /// <para>전환점 자신의 프레스는 이미 <see cref="BeginBurst"/>가 올리는 중이므로(Current가
        /// Idle을 벗어나 있다) 건드리지 않는다 — 나머지 세 입구만 이 이벤트로 올라간다. 전반부에
        /// 플레이어가 어디까지 열어 뒀든 상관없이, 추격 중에는 모든 대왕프레스가 열려 있어야
        /// 낑길 자리가 확보되고 후반부 도주로가 막히지 않는다.</para>
        /// </summary>
        void HandleChaseActiveChanged(bool active)
        {
            if (!active) return;
            if (Current != Phase.Idle) return;
            RaisePressForChase();
        }

        /// <summary>계속 잠근 채로(플레이어가 다시 내릴 수 없게) 끝까지 올린다 — 자동으로 열린 문이라 손댈 필요가 없다.</summary>
        void RaisePressForChase()
        {
            if (pressHackable != null) pressHackable.enabled = false;
            if (pressControl != null) pressControl.allowHold = true;
            if (pressActuator != null)
            {
                pressActuator.LimitT = 1f;
                pressActuator.Flick(0f);   // 플릭 = 끝에서 끝. t=0이 완전히 올라간 상태.
            }
        }

        // ── 프레스 소유권 ──────────────────────────────────────────────────

        /// <summary>보스가 오기 전엔 아예 못 건드리게 한다 — 미리 내려놔서 낑길 자리를 막는 버그 방지.</summary>
        void LockPress()
        {
            if (pressHackable != null) pressHackable.enabled = false;
            // ★ Target을 건드리지 않는다 — 대왕프레스는 내려와 입구를 막고 있는 게 시작 상태다
            //   (startExtension이 그 값을 소유한다). 여기서 Target=0을 넣으면 t=0(완전 상승)으로
            //   튀어 오르는데, 그러면 게임 시작하자마자 모든 프레스가 열려 버린다.
            if (pressActuator != null) pressActuator.LimitT = 1f;
            if (pressControl != null) pressControl.allowHold = true;
        }

        /// <summary>낑김 동안만 — 잠금 해제 + 플릭 전용 + 머리 높이 상한.</summary>
        void ArmPress()
        {
            if (pressHackable != null) pressHackable.enabled = true;
            if (pressControl != null) pressControl.allowHold = false;   // 한 방만
            UpdatePressLimit();
        }

        /// <summary>평상시로 — 일반 유압프레스와 완전히 같은 상태.</summary>
        void RestorePress()
        {
            if (pressHackable != null) pressHackable.enabled = _pressWasHackable;
            if (pressControl != null) pressControl.allowHold = true;
            if (pressActuator != null) pressActuator.LimitT = 1f;
        }

        /// <summary>머리 접촉 지점에서 상한을 다시 계산한다. 단계가 올라 머리가 낮아지면 상한도 내려간다.</summary>
        void UpdatePressLimit()
        {
            if (pressActuator == null || headContact == null) return;
            pressActuator.LimitT = pressActuator.TThatBrings(PressFaceWorld, headContact.position);
        }

        // ── 진입 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 플레이어가 입구를 지났다. 트리거에서 부르거나 직접 불러도 된다.
        ///
        /// <para><b>추격 중이 아니면 무시한다</b>(전환점 입구는 예외). 플레이어는 <b>같은 입구를 두 번
        /// 지나므로</b>(전반부 1→2→3, 후반부 3→2→1) 이 게이트가 없으면 전반부에 보스가 튀어나온다.</para>
        /// </summary>
        public void PlayerPassed()
        {
            if (Current != Phase.Idle) return;
            if (entryMode != Entry.PlayerTrigger) return;            // PressRaised 입구는 트리거로 안 열린다
            if (!startsChase && !BossChaseState.Active) return;      // 전반부 통과 — 아무 일도 없다
            Begin();
        }

        void OnTriggerEnter(Collider other)
        {
            if (playerTrigger == null) return;                       // 트리거를 안 쓰면 수동 호출만
            if (other.GetComponentInParent<FirstPersonPlayer>() == null) return;
            PlayerPassed();
        }

        /// <summary>
        /// PressRaised 모드 — 프레스가 살짝이라도 올라갔는지 본다.
        ///
        /// <para>폴링으로 충분하다. 액추에이터에 도달 이벤트가 없고, 이 판정은 <see cref="Phase.Idle"/>
        /// 동안 한 입구에서만 도는 값 하나 비교라 비용이 없다.</para>
        /// </summary>
        void TickIdle()
        {
            if (entryMode != Entry.PressRaised) return;
            if (pressActuator == null) return;
            if (pressActuator.Current > raisedThreshold) return;     // 아직 안 올렸다

            Debug.Log($"[입구{index}] 프레스가 열리기 시작 — 보스가 머리를 박는다 " +
                      $"(t={pressActuator.Current:F2} ≤ {raisedThreshold:F2})");
            BeginBurst();
        }

        /// <summary>
        /// 보스가 <b>머리로 프레스를 밀어올리며</b> 틈으로 들어온다. (§0.4 전환점)
        ///
        /// <para><b>조종권을 먼저 뺏는다.</b> 플레이어가 올리던 중이므로, 안 뺏으면 플레이어 입력과
        /// 강제 상승이 같은 값을 두고 싸운다 — 소유자가 하나여야 한다는 규칙이 여기에도 적용된다.</para>
        ///
        /// <para>프레스 상승과 머리 진입은 <b>같은 타이머</b>(<see cref="burstTime"/>)를 쓴다.
        /// 따로 돌리면 머리가 프레스를 뚫고 지나가거나 늦게 들어온다.</para>
        /// </summary>
        /// <summary>
        /// 이번 사이클이 끝나고 보스가 돌아갈 걷기 높이(루트 월드 y).
        ///
        /// <para>인스펙터에 값이 있으면 그걸 쓰고, 없으면 <b>사이클에 들어가기 직전의 높이</b>를 쓴다.
        /// 손으로 안 채워도 동작하고, 채우면 확정값으로 고정된다.</para>
        /// </summary>
        float _walkY = float.NaN;

        void RememberWalkHeight()
        {
            _walkY = float.IsNaN(walkRootY)
                   ? (Root != null ? Root.position.y : float.NaN)
                   : walkRootY;
        }

        /// <summary>걷기 높이로 되돌린다. 안 하면 다음 추격에서 보스가 땅속을 걷는다.</summary>
        void RestoreWalkHeight()
        {
            if (Root == null || float.IsNaN(_walkY)) return;
            Vector3 p = Root.position;
            p.y = _walkY;
            Root.position = p;
        }

        void BeginBurst()
        {
            Current = Phase.Bursting;
            _phaseT = 0f;

            SetBossHidden(false);   // ★ 첫 등장 — 루트를 옮기기 전에 켜야 그 프레임부터 보인다
            RememberWalkHeight();

            // 조종권 박탈 — 지금 이 프레스를 잡고 있었다면 놓게 만든다.
            if (pressHackable != null) pressHackable.enabled = false;
            if (pressControl != null) pressControl.allowHold = true;

            // 머리가 밀어올리는 것이므로 스크립트가 끝까지 올린다. Flick 경로라 빠르다("쭉").
            if (pressActuator != null)
            {
                pressActuator.LimitT = 1f;
                pressActuator.Flick(0f);
            }

            if (chase != null) chase.move = false;

            _burstFrom = Root != null ? Root.position : Vector3.zero;
            _burstTo = _burstFrom;
            if (Root != null && wedge != null && Anchor != null)
                _burstTo = _burstFrom + (wedge.Stop.position - Anchor.position);
        }

        void TickBursting()
        {
            float u = burstTime > 1e-3f ? Mathf.Clamp01(_phaseT / burstTime) : 1f;

            // 머리를 들이받는 느낌 — 뒤로 갈수록 빨라지는 곡선(가속 충돌).
            if (Root != null) Root.position = Vector3.LerpUnclamped(_burstFrom, _burstTo, u * u);

            if (u < 1f) return;
            Begin();
        }

        Vector3 _burstFrom, _burstTo;

        void Begin()
        {
            Current = Phase.Sealing;
            _phaseT = 0f;

            // PlayerTrigger 입구는 Bursting을 안 거치므로 등장·높이 기억을 여기서 해야 한다.
            SetBossHidden(false);
            if (float.IsNaN(_walkY)) RememberWalkHeight();

            // 전환점 입구가 추격을 연다. 이후 다른 입구의 트리거가 살아난다.
            if (startsChase) BossChaseState.Begin();

            if (sealWall != null) sealWall.enabled = true;
            if (geo != null) geo.GoTo(sealedPose);
            if (chase != null) chase.move = false;                   // 자리를 잡았으니 걷기 정지

            SealArms();
        }

        // ── 팔 봉쇄 — 소유권을 넘겨받는다 ────────────────────────────────

        /// <summary>
        /// 보스 팔이 출구를 막는다.
        ///
        /// <para><b>팔 본을 건드리는 컴포넌트가 셋</b>이라, 자세를 재생하기 전에 <b>전부 꺼서 소유권을
        /// 가져와야 한다.</b> 안 그러면 <see cref="BossArmRig"/>(order 50)가 매 LateUpdate에 IK로
        /// 덮어쓰고, <see cref="BossSpineAim"/>(45)이 허리를 굽혀 어깨째 손을 끌고 간다.
        /// 낑겨 있는 동안 보스는 제자리에 물려 있으므로 셋 다 꺼도 연출상 맞다.</para>
        ///
        /// <para>⚠️ <b>끄는 것이 먼저다.</b> <see cref="BossArmRig.OnDisable"/>이 홈 자세로 되돌리므로,
        /// 순서가 반대면 방금 재생한 봉쇄 자세가 홈으로 지워진다. 지금 순서면 홈에서 출발해
        /// 봉쇄로 보간되어 오히려 자연스럽다.</para>
        ///
        /// <para><see cref="armPoser"/>가 없으면 옛 IK 경로(<see cref="sealHandTarget"/>)로 떨어진다.</para>
        /// </summary>
        void SealArms()
        {
            if (armPoser != null)
            {
                if (chase != null) chase.enabled = false;
                if (spineAim != null) spineAim.enabled = false;
                if (arms != null) arms.enabled = false;   // ★ 여기서 홈으로 돌아간다 — 그게 출발점이다

                armPoser.GoTo(armSealPose);
                return;
            }

            // 자세 애셋이 없는 구성 — 예전 IK 경로. Relax까지 목표가 유지되므로 한 번만 부르면 된다.
            if (arms != null && sealHandTarget != null)
                arms.Aim(sealHand, sealHandTarget.position, Vector3.zero);
        }

        /// <summary>봉쇄를 푼다. 팔을 홈으로 되돌리고 소유권을 원래 컴포넌트들에 돌려준다.</summary>
        void ReleaseArms()
        {
            if (armPoser != null)
            {
                armPoser.GoTo(armHomePose);
                // 리그를 여기서 되살리지 않는다 — 보간이 끝나기 전에 켜면 IK가 도중에 낚아챈다.
                // 사이클이 끝나는 Finish()에서 한꺼번에 돌려준다.
                return;
            }
            if (arms != null) arms.Relax(sealHand);
        }

        /// <summary>봉쇄 동안 꺼 뒀던 컴포넌트를 되살린다.</summary>
        void RestoreArmOwners()
        {
            if (armPoser == null) return;
            if (arms != null) arms.enabled = true;
            if (spineAim != null) spineAim.enabled = true;
            if (chase != null) chase.enabled = true;
        }

        // ── 상태 진행 ─────────────────────────────────────────────────────

        void Update()
        {
            _phaseT += Time.deltaTime;

            switch (Current)
            {
                case Phase.Idle: TickIdle(); break;
                case Phase.Bursting: TickBursting(); break;
                case Phase.Sealing: TickSealing(); break;
                case Phase.Wedged: TickWedged(); break;
                case Phase.Crushed: TickCrushed(); break;
                case Phase.Failed: TickFailed(); break;
            }
        }

        /// <summary>출구 찌그러짐이 끝나면 머리를 물린다.</summary>
        void TickSealing()
        {
            if (geo != null && geo.Moving) return;

            Current = Phase.Wedged;
            _phaseT = 0f;

            if (wedge != null)
            {
                // 회차별 상승 — 뒤로 갈수록 짧아진다.
                wedge.wedgeSeconds *= Mathf.Pow(Mathf.Max(0.1f, wedgeSecondsPerIndex), index);
                wedge.Begin(Root, headContact);
            }

            SnapHeadToStop();
            ArmPress();
        }

        /// <summary>루트를 옮겨 머리를 입구에 맞춘다. IK가 아니라 평행 이동이라 오차가 없다.</summary>
        void SnapHeadToStop()
        {
            if (Root == null || wedge == null || Anchor == null) return;
            Root.position += wedge.Stop.position - Anchor.position;
        }

        void TickWedged()
        {
            UpdatePressLimit();   // 부들부들로 머리가 흔들리므로 매 프레임 따라간다

            if (pressActuator != null && pressActuator.AtLimit)
                Succeed();
        }

        void Succeed()
        {
            BossChaseState.CountCrush();
            if (wedge != null) wedge.End();
            if (headCrush != null) headCrush.Crush();

            // 봉쇄 해제 — 팔을 놓고 벽을 치운다.
            ReleaseArms();
            if (sealWall != null) sealWall.enabled = false;

            LockPress();   // 다시 잠근다. 남은 사이클 동안 못 건드린다

            Current = Phase.Crushed;
            _phaseT = 0f;

            Transform r = Root;
            if (r != null)
            {
                _pullFrom = r.position;
                _pullTo = r.position - r.forward * pullOutDistance;   // 머리를 뒤로 뺀다
                _staggerBase = r.rotation;
            }
        }

        /// <summary>머리를 빼며 비틀거린다. 감쇠 진동이라 자연히 잦아든다.</summary>
        void TickCrushed()
        {
            Transform r = Root;
            if (r != null)
            {
                float u = pullOutTime > 1e-4f ? Mathf.Clamp01(_phaseT / pullOutTime) : 1f;
                r.position = Vector3.Lerp(_pullFrom, _pullTo, u * u * (3f - 2f * u));   // smoothstep

                float k = staggerTime > 1e-4f ? Mathf.Clamp01(1f - _phaseT / staggerTime) : 0f;
                float wobble = Mathf.Sin(_phaseT * 12f) * staggerDeg * k * k;
                r.rotation = _staggerBase * Quaternion.Euler(0f, 0f, wobble);
            }

            if (_phaseT < Mathf.Max(pullOutTime, staggerTime)) return;

            if (r != null) r.rotation = _staggerBase;   // 진동 잔여를 남기지 않는다

            // ★ 걷기 높이로 복귀. 낑길 때 머리를 입구에 맞추려고 루트를 40m 넘게 내렸으므로,
            //   여기서 안 되돌리면 다음 입구까지 <b>땅속을 걸어온다</b>.
            RestoreWalkHeight();

            Finish(true);
        }

        void HandleTimeout()
        {
            if (Current != Phase.Wedged) return;

            // 낑긴 곳을 부수고 나온다.
            if (geo != null) geo.GoTo(brokenPose);
            ReleaseArms();
            LockPress();

            Current = Phase.Failed;
            _phaseT = 0f;
        }

        /// <summary>플레이어에게 돌진 → 즉사. 피할 수 없는 각본이다(설계 §1 실패 처리).</summary>
        void TickFailed()
        {
            Transform r = Root;
            Transform t = chase != null ? chase.target : null;

            if (r != null && t != null)
            {
                Vector3 to = t.position - r.position;
                to.y = 0f;
                if (to.sqrMagnitude > 1e-4f)
                {
                    r.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
                    r.position += to.normalized * chargeSpeed * Time.deltaTime;
                }

                if (to.magnitude <= chargeKillDistance) { Kill(); return; }
            }

            // 벽에 막혀 영원히 못 닿는 경우를 대비한 안전망.
            if (_phaseT >= chargeTimeout) Kill();
        }

        void Kill()
        {
            GameOverManager.Trigger("보스에게 잡힘");
            Finish(false);
        }

        void Finish(bool crushed)
        {
            Current = Phase.Done;
            RestorePress();
            if (sealWall != null) sealWall.enabled = false;
            RestoreArmOwners();                                  // 팔·허리·추격 소유권 반납
            if (chase != null) chase.move = true;
            OnFinished?.Invoke(crushed);
        }

        // ── IRunResettable ────────────────────────────────────────────────
        public void ResetForRestart()
        {
            if (wedge != null) wedge.End();
            RestoreArmOwners();                                  // 먼저 되살리고(끈 채로 방치되면 다음 판에 팔이 죽는다)
            if (armPoser != null) armPoser.ResetToHome();
            if (arms != null) arms.Relax(sealHand);
            if (geo != null) geo.ResetToHome();
            if (sealWall != null) sealWall.enabled = false;

            // Awake와 같은 이유 — 추격 중이 아니면 평상시(해킹 가능)로 되돌린다.
            RestorePress();
            if (!BossChaseState.Active) SetBossHidden(true);
            Current = Phase.Idle;
            _phaseT = 0f;
        }
    }
}
