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
        public enum Phase { Idle, Sealing, Wedged, Crushed, Failed, Done }

        [Header("회차")]
        [Tooltip("몇 번째 입구인가(0부터). 회차별 상승(설계 §7)에 쓴다 — 뒤로 갈수록 낑김 시간이 짧다.")]
        public int index;

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

        [Tooltip("머리에서 프레스가 닿을 지점 마커. 머리 찌그러짐 자세 목록에 넣어 두면 " +
                 "단계가 오를 때 같이 내려온다 — 회차마다 프레스가 더 깊이 들어간다.")]
        public Transform headContact;

        [Tooltip("입구에 물릴 머리 기준점. 비우면 headContact.")]
        public Transform headAnchor;

        [Tooltip("보스가 팔로 짚어 출구를 막는 지점.")]
        public Transform sealHandTarget;

        [Tooltip("막는 손. 출구가 보스 기준 어느 쪽인지에 맞춘다.")]
        public BossArmRig.Side sealHand = BossArmRig.Side.Left;

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
            LockPress();
            if (sealWall != null) sealWall.enabled = false;
        }

        void OnEnable()
        {
            if (wedge != null) wedge.OnTimedOut += HandleTimeout;
        }

        void OnDisable()
        {
            if (wedge != null) wedge.OnTimedOut -= HandleTimeout;
        }

        // ── 프레스 소유권 ──────────────────────────────────────────────────

        /// <summary>보스가 오기 전엔 아예 못 건드리게 한다 — 미리 내려놔서 낑길 자리를 막는 버그 방지.</summary>
        void LockPress()
        {
            if (pressHackable != null) pressHackable.enabled = false;
            if (pressActuator != null)
            {
                pressActuator.LimitT = 1f;
                pressActuator.Target = 0f;   // 홈으로 되돌린다 — 1막에 내려놨어도 여기서 정리된다
            }
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

        /// <summary>플레이어가 입구를 지났다. 트리거에서 부르거나 직접 불러도 된다.</summary>
        public void PlayerPassed()
        {
            if (Current != Phase.Idle) return;
            Begin();
        }

        void OnTriggerEnter(Collider other)
        {
            if (playerTrigger == null) return;                       // 트리거를 안 쓰면 수동 호출만
            if (other.GetComponentInParent<FirstPersonPlayer>() == null) return;
            PlayerPassed();
        }

        void Begin()
        {
            Current = Phase.Sealing;
            _phaseT = 0f;

            if (sealWall != null) sealWall.enabled = true;
            if (geo != null) geo.GoTo(sealedPose);
            if (chase != null) chase.move = false;                   // 자리를 잡았으니 걷기 정지

            // 보스 팔이 출구를 막는다. Relax까지 목표가 유지되므로 한 번만 부르면 된다.
            if (arms != null && sealHandTarget != null)
                arms.Aim(sealHand, sealHandTarget.position, Vector3.zero);
        }

        // ── 상태 진행 ─────────────────────────────────────────────────────

        void Update()
        {
            _phaseT += Time.deltaTime;

            switch (Current)
            {
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
            if (wedge != null) wedge.End();
            if (headCrush != null) headCrush.Crush();

            // 봉쇄 해제 — 팔을 놓고 벽을 치운다.
            if (arms != null) arms.Relax(sealHand);
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
            Finish(true);
        }

        void HandleTimeout()
        {
            if (Current != Phase.Wedged) return;

            // 낑긴 곳을 부수고 나온다.
            if (geo != null) geo.GoTo(brokenPose);
            if (arms != null) arms.Relax(sealHand);
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
            if (chase != null) chase.move = true;
            OnFinished?.Invoke(crushed);
        }

        // ── IRunResettable ────────────────────────────────────────────────
        public void ResetForRestart()
        {
            if (wedge != null) wedge.End();
            if (arms != null) arms.Relax(sealHand);
            if (geo != null) geo.ResetToHome();
            if (sealWall != null) sealWall.enabled = false;

            LockPress();
            Current = Phase.Idle;
            _phaseT = 0f;
        }
    }
}
