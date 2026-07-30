using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 경비병 전용 절차적 회전 — 몸통이 먼저 돌고, 발이 한쪽씩 떼서 각을 맞춘다.
    ///
    /// <para><b>순서</b>(정지 시간 전체를 3구간으로 나눔):
    /// ① <b>리드</b> — 가슴 본이 목표 방향으로 먼저(더 빨리, 더 크게) 돌아간다. 엉덩이·발은 그대로.
    /// ② <b>스텝A</b> — 첫 번째 발이 들려(포물선) 전체 회전의 절반 지점 각도로 옮겨 붙는다.
    ///    엉덩이(=이 오브젝트의 회전)도 그 절반까지 같이 돈다. 반대쪽 발은 <b>고정</b>.
    /// ③ <b>스텝B</b> — 나머지 발이 마저 옮겨 붙어 최종 각도를 맞춘다. 엉덩이도 완료.
    ///    가슴의 "리드분"은 이 구간에서 0으로 수렴 — 다리가 따라잡는 만큼 몸통이 풀린다.</para>
    ///
    /// <para><b>왜 발에 IK가 필요한가</b>: 몸 전체(이 transform)가 회전하면 스킨 메시인 발도 같이
    /// 돌아 미끄러진다. 발을 <see cref="HandIK"/>(범용 2본 IK, 원래 손용이지만 뼈 이름만 다를 뿐
    /// 다리에도 그대로 맞는다)로 <b>월드 고정 타깃</b>에 묶어두면, 그 발이 "고정" 차례일 때는
    /// 몸이 돌아도 실제로 그 자리에 붙어 있는다.</para>
    ///
    /// <para>경비병 전용 — 범용화하지 않는다. 걷기·대기 중엔 다리 IK를 끄고(weight=0)
    /// 원래 애니메이션 그대로 재생한다. 이 컴포넌트가 관여하는 건 정지 중 회전뿐이다.</para>
    /// </summary>
    // HandIK(기본 순서 0)보다 먼저 LateUpdate가 돌아야 이번 프레임 타깃을 HandIK가 그대로 읽는다.
    [DefaultExecutionOrder(-10)]
    [RequireComponent(typeof(Animator))]
    public class GuardTurnStep : MonoBehaviour
    {
        enum Phase { Idle, Lead, StepA, StepB }
        enum Foot { Left, Right }

        [Header("몸통 리드")]
        [Tooltip("가슴이 먼저 도는 각도(도). 실제 전체 회전량을 넘지 않게 자동으로 잘린다.")]
        public float leadAngle = 35f;

        [Tooltip("정지 시간 중 '리드'가 차지하는 비율. 나머지를 스텝A/B가 반씩 나눠 쓴다.")]
        [Range(0.05f, 0.5f)] public float leadFraction = 0.18f;

        [Header("발 스텝")]
        [Tooltip("발이 들리는 높이(m).")]
        public float footLiftHeight = 0.12f;

        [Tooltip("어느 발이 먼저 떼는지. Auto = 도는 방향에 따라 자동(왼쪽으로 돌면 오른발 먼저).")]
        public FirstFootMode firstFoot = FirstFootMode.Auto;
        public enum FirstFootMode { Auto, LeftFirst, RightFirst }

        [Header("다리 IK 한계 (도) — HandIK 재사용, 팔이 아니라 다리 기준으로 재설정")]
        [Range(0f, 30f)] public float kneeMinFlex = 2f;
        [Range(60f, 170f)] public float kneeMaxFlex = 155f;
        [Range(30f, 120f)] public float hipMaxCone = 70f;

        [Header("바닥")]
        [Tooltip("발을 붙일 때 이 레이어에서 바닥 높이를 다시 잰다. 평평하면 안 맞아도 무방.")]
        public LayerMask groundMask = ~0;

        [Header("디버그")]
        public bool logTurns;

        [Tooltip("한쪽 다리가 찌그러지는 증상 진단용 — 1초마다 좌우 무릎 굽힘·IK weight·회전 단계를 찍는다.\n" +
                 "가장 먼저 켜진 경비병 하나만 찍는다(웨이브 8기가 동시에 스팸하지 않게).")]
        public bool logLegDiag;

        static GuardTurnStep _diagOwner;   // 여러 기가 동시에 찍지 않도록 첫 번째만 소유
        float _diagT;

        Animator _anim;
        Transform _chest;
        HandIK _legL, _legR;
        Transform _targetL, _targetR;

        bool _hasLegs;
        Vector3 _offsetL, _offsetR;         // 루트→발 오프셋. 회전만 벗겨낸 값이고 단위는 월드(스케일 안 나눔) — Capture() 주석 참조
        Quaternion _localRotL, _localRotR;  // 루트 로컬 발 회전 — 회전 시작마다 매번 새로 캡처
        Vector3 _plantedPosL, _plantedPosR; // 지금 각 발이 실제로 붙어 있는 월드 위치
        Quaternion _plantedRotL, _plantedRotR;

        Phase _phase = Phase.Idle;
        float _t, _leadDur, _stepDur;
        Quaternion _turnFrom, _turnTo, _midRot;
        float _leadSigned;
        Foot _first;
        System.Action _onComplete;

        public bool Busy => _phase != Phase.Idle;

        void Awake()
        {
            _anim = GetComponent<Animator>();
            _chest = _anim.GetBoneTransform(HumanBodyBones.Chest);

            _legL = BuildLeg("[GuardLegIK_L]", HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, out _targetL);
            _legR = BuildLeg("[GuardLegIK_R]", HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, out _targetR);
            _hasLegs = _legL != null && _legR != null;
        }

        HandIK BuildLeg(string name, HumanBodyBones upperB, HumanBodyBones lowerB, HumanBodyBones footB, out Transform target)
        {
            target = null;
            Transform upper = _anim.GetBoneTransform(upperB);
            Transform lower = _anim.GetBoneTransform(lowerB);
            Transform foot = _anim.GetBoneTransform(footB);
            if (upper == null || lower == null || foot == null) return null;

            var holder = new GameObject(name).transform;
            holder.SetParent(transform, false);   // transform.parent 기준으로 pole 방향이 우리 회전을 따라오게

            var targetGo = new GameObject(name + "_Target").transform;
            targetGo.SetParent(null, true);   // 월드 고정 타깃 — 몸 회전에 안 딸려간다
            target = targetGo;

            var ik = holder.gameObject.AddComponent<HandIK>();
            ik.upper = upper; ik.lower = lower; ik.end = foot;
            ik.target = target;
            ik.matchRotation = true;
            ik.poleLocalDir = new Vector3(0f, -0.25f, 1f);   // 무릎 = 대체로 앞·약간 아래
            ik.elbowMinFlex = kneeMinFlex;
            ik.elbowMaxFlex = kneeMaxFlex;
            ik.shoulderMaxCone = hipMaxCone;
            ik.weight = 0f;   // 걷기/대기 중엔 꺼둠 — 회전할 때만 켠다
            return ik;
        }

        /// <summary>
        /// 다리 IK가 기준으로 삼을 "지금 이 순간의" 발 자세를 캡처한다. 매 회전 시작마다 새로
        /// 캡처한다 — 예전엔 최초 1회만 캡처해서, 그 1회가 하필 걷기 도중(스트라이드 중간) 프레임이면
        /// 이후 모든 회전의 발 오프셋이 잘못된 자세를 기준으로 계산됐다. 지금은 GuardPatrol이
        /// Idle 크로스페이드가 완전히 끝난 뒤(Settling 종료)에만 BeginTurn을 부르므로, 여기서 읽는
        /// 다리 IK weight=0 상태의 Animator 포즈는 항상 방금 자리 잡은 진짜 대기 자세다.
        /// </summary>
        void Capture()
        {
            if (!_hasLegs) return;
            // 회전만 벗겨낸 오프셋 — 스케일로 나누지 않는다. InverseTransformPoint를 쓰면 값이
            // lossyScale로 나뉘어 나오는데, TickStep은 이걸 `transform.position + landRot * offset`
            // 으로 월드 단위처럼 그대로 더한다(스케일을 다시 곱하지 않는다). 그래서 스케일 2인
            // 경비병(HackSandbox·Stage_1)에서는 발 목표가 실제 거리의 절반, 즉 골반 바로 아래에
            // 찍혔고, HandIK가 dist를 dMin(=kneeMaxFlex 155°에서의 도달거리)으로 클램프하면서
            // 무릎을 155°까지 접고 hipMaxCone 70°로 허벅지를 비틀었다 — 한쪽 다리가 찌그러져
            // 말리는 증상의 원인. 아래 _localRot* 가 이미 쓰는 "회전만" 방식으로 통일한다.
            _offsetL = Quaternion.Inverse(transform.rotation) * (_legL.end.position - transform.position);
            _offsetR = Quaternion.Inverse(transform.rotation) * (_legR.end.position - transform.position);
            _localRotL = Quaternion.Inverse(transform.rotation) * _legL.end.rotation;
            _localRotR = Quaternion.Inverse(transform.rotation) * _legR.end.rotation;
            _plantedPosL = _legL.end.position; _plantedRotL = _legL.end.rotation;
            _plantedPosR = _legR.end.position; _plantedRotR = _legR.end.rotation;
        }

        /// <summary>정지-회전 시작. <paramref name="duration"/>은 GuardPatrol의 정지 시간(중간 0.65s / 끝 1.3s)과 같다.</summary>
        public void BeginTurn(Quaternion from, Quaternion to, float duration, System.Action onComplete)
        {
            Capture();
            if (!_hasLegs) { onComplete?.Invoke(); return; }   // 다리 IK 배선 실패 — 그냥 즉시 완료 처리(호출자가 폴백)

            _turnFrom = from; _turnTo = to;
            _onComplete = onComplete;

            float totalDeg = Mathf.DeltaAngle(from.eulerAngles.y, to.eulerAngles.y);
            _leadSigned = Mathf.Clamp(leadAngle, 0f, Mathf.Abs(totalDeg)) * Mathf.Sign(totalDeg == 0f ? 1f : totalDeg);

            _first = firstFoot switch
            {
                FirstFootMode.LeftFirst => Foot.Left,
                FirstFootMode.RightFirst => Foot.Right,
                _ => totalDeg > 0f ? Foot.Left : Foot.Right,
            };

            _midRot = Quaternion.Slerp(from, to, Ease(0.5f));

            _leadDur = duration * leadFraction;
            _stepDur = (duration - _leadDur) * 0.5f;

            _legL.weight = 1f; _legR.weight = 1f;
            transform.rotation = from;
            _phase = Phase.Lead;
            _t = 0f;

            if (logTurns) Debug.Log($"[GuardTurn] 시작 — {totalDeg:F0}° 회전, 먼저 뗄 발={_first}");
        }

        void LateUpdate()
        {
            TickLegDiag();   // Idle(=걷는 중)에도 찍어야 하므로 아래 early return 앞에 둔다
            if (_phase == Phase.Idle) return;
            _t += Time.deltaTime;

            switch (_phase)
            {
                case Phase.Lead: TickLead(); break;
                case Phase.StepA: TickStep(true); break;
                case Phase.StepB: TickStep(false); break;
            }

            ApplyChestLead(CurrentLeadDeg());
            _targetL.position = _plantedPosL; _targetL.rotation = _plantedRotL;
            _targetR.position = _plantedPosR; _targetR.rotation = _plantedRotR;
            // 지금 움직이는 중인 발은 아래 Tick*에서 _plantedPos/Rot를 직접 매 프레임 덮어써 반영한다.
        }

        /// <summary>
        /// 한쪽 다리 찌그러짐 진단용 계측. 정적 분석으로는 원인을 특정할 수 없어(리그·클립·Avatar·
        /// 레이어 마스크 전부 좌우 대칭으로 확인됨) 실제 값을 찍는다. 판독법:
        ///
        /// <list type="bullet">
        ///  <item><b>weight가 0이 아닌데 phase=Idle</b> → IK weight 고착. 회전을 끝내지 못한 경로가 있다.</item>
        ///  <item><b>weight 0인데 knee L/R 차이가 큼</b> → IK 무관. Animator 출력 자체가 비대칭
        ///        = 휴머노이드 리타게팅 문제(Avatar T-포즈·본 축).</item>
        ///  <item><b>reach &gt; 1.0</b> → 발 목표가 다리 길이보다 멀어 HandIK가 dMax로 클램프 중
        ///        (다리가 억지로 펴진다). scale 값이 1이 아니면 스케일 경로도 같이 의심한다.</item>
        /// </list>
        /// knee는 허벅지 방향과 정강이 방향 사이 각도다 — 0=완전히 편 상태, 클수록 접힌 상태.
        /// </summary>
        void TickLegDiag()
        {
            if (!logLegDiag || !_hasLegs) return;
            if (_diagOwner == null) _diagOwner = this;
            if (_diagOwner != this) return;

            _diagT += Time.deltaTime;
            if (_diagT < 1f) return;
            _diagT = 0f;

            var clips = _anim.GetCurrentAnimatorClipInfo(0);
            string clip = clips.Length > 0 && clips[0].clip != null ? clips[0].clip.name : "(없음)";

            Debug.Log($"[GuardLegDiag] {name} clip={clip} phase={_phase} scale={transform.lossyScale.x:F2}" +
                      $" | weight L={_legL.weight:F2} R={_legR.weight:F2}" +
                      $" | knee L={KneeFlex(_legL):F0}° R={KneeFlex(_legR):F0}°" +
                      $" | reach L={ReachRatio(_legL):F2} R={ReachRatio(_legR):F2}");
        }

        /// <summary>허벅지 방향과 정강이 방향 사이 각도. 0=편 상태, 155에 가까우면 최대 굴곡 클램프에 닿은 것.</summary>
        static float KneeFlex(HandIK leg)
        {
            Vector3 thigh = leg.lower.position - leg.upper.position;
            Vector3 shin  = leg.end.position   - leg.lower.position;
            if (thigh.sqrMagnitude < 1e-8f || shin.sqrMagnitude < 1e-8f) return 0f;
            return Vector3.Angle(thigh, shin);
        }

        /// <summary>골반→발목표 거리 ÷ 다리 전체 길이. 1을 넘으면 다리가 닿을 수 없는 곳을 가리키는 중.</summary>
        static float ReachRatio(HandIK leg)
        {
            if (leg.target == null) return 0f;
            float len = Vector3.Distance(leg.upper.position, leg.lower.position)
                      + Vector3.Distance(leg.lower.position, leg.end.position);
            if (len < 1e-5f) return 0f;
            return Vector3.Distance(leg.upper.position, leg.target.position) / len;
        }

        float CurrentLeadDeg()
        {
            switch (_phase)
            {
                case Phase.Lead: return _leadSigned * Ease(Mathf.Clamp01(_t / Mathf.Max(0.001f, _leadDur)));
                case Phase.StepA: return _leadSigned;
                case Phase.StepB: return _leadSigned * (1f - Ease(Mathf.Clamp01(_t / Mathf.Max(0.001f, _stepDur))));
                default: return 0f;
            }
        }

        void ApplyChestLead(float deg)
        {
            if (_chest == null) return;
            _chest.localRotation = _chest.localRotation * Quaternion.Euler(0f, deg, 0f);
        }

        void TickLead()
        {
            if (_t >= _leadDur) { _phase = Phase.StepA; _t = 0f; }
        }

        void TickStep(bool isA)
        {
            float u = Ease(Mathf.Clamp01(_t / Mathf.Max(0.001f, _stepDur)));

            // 이번 스텝에서 엉덩이(=본체 회전)가 이동할 s 구간: A=0→0.5, B=0.5→1
            float s0 = isA ? 0f : 0.5f, s1 = isA ? 0.5f : 1f;
            transform.rotation = Quaternion.Slerp(_turnFrom, _turnTo, Ease(Mathf.Lerp(s0, s1, u)));

            Foot moving = isA ? _first : Other(_first);
            Quaternion landRot = isA ? _midRot : _turnTo;

            Vector3 offset = moving == Foot.Left ? _offsetL : _offsetR;
            Quaternion localRot = moving == Foot.Left ? _localRotL : _localRotR;
            Vector3 startPos = moving == Foot.Left ? _plantedPosL : _plantedPosR;
            Quaternion startRot = moving == Foot.Left ? _plantedRotL : _plantedRotR;

            Vector3 endPos = SnapToGround(transform.position + landRot * offset);
            Quaternion endRot = landRot * localRot;

            Vector3 pos = Vector3.Lerp(startPos, endPos, u);
            pos.y += Mathf.Sin(u * Mathf.PI) * footLiftHeight;
            Quaternion rot = Quaternion.Slerp(startRot, endRot, u);

            if (moving == Foot.Left) { _plantedPosL = pos; _plantedRotL = rot; }
            else                     { _plantedPosR = pos; _plantedRotR = rot; }

            if (_t >= _stepDur)
            {
                // 착지 확정 — 다음 스텝의 시작점이 되도록 최종 지점으로 스냅.
                if (moving == Foot.Left) { _plantedPosL = endPos; _plantedRotL = endRot; }
                else                     { _plantedPosR = endPos; _plantedRotR = endRot; }

                _t = 0f;
                if (isA) { _phase = Phase.StepB; }
                else EndTurn();
            }
        }

        /// <summary>
        /// 회전을 <b>중간에 포기</b>하고 다리를 애니메이션에 돌려준다.
        ///
        /// <para><b>왜 필요한가</b>: 다리 IK는 이 컴포넌트가 만든 <b>별도 <see cref="HandIK"/>
        /// 컴포넌트</b>가 매 프레임 적용한다. 그래서 이 컴포넌트만 <c>enabled = false</c>로 끄면
        /// IK는 <b>계속 돌면서</b> 마지막 weight·타깃을 유지한다 — 회전 도중(발이 들린 순간)에 껐다면
        /// 그 발이 <b>영구히 공중에 뜬 채로 남는다</b>(실제로 겪은 버그: 해킹당한 경비병이 한쪽 발을
        /// 들고 굳음).</para>
        ///
        /// <para>그래서 끄기 전에 반드시 이걸 불러 weight를 0으로 되돌려야 한다. 완료 콜백은
        /// 부르지 않는다 — 완료가 아니라 포기이므로 대기 중인 쪽이 오해하면 안 된다.</para>
        /// </summary>
        public void ReleaseLegs()
        {
            if (_legL != null) _legL.weight = 0f;
            if (_legR != null) _legR.weight = 0f;
            _phase = Phase.Idle;
            _t = 0f;
            _onComplete = null;
        }

        /// <summary>
        /// 다리 IK를 <b>영구히 죽인다</b>(고장 등). 되돌리는 경로는 두지 않는다.
        ///
        /// <para><b>왜 weight=0으로는 부족한가</b>: <see cref="HandIK"/>는 weight가 0이면
        /// <b>아무것도 안 할 뿐 이미 써 놓은 뼈 회전을 되돌리지 않는다</b>(0&lt;weight&lt;1 구간에서만
        /// 원래 자세로 Slerp한다). 게다가 <see cref="HandIK"/>는 <b>별도 컴포넌트</b>라
        /// <see cref="GuardTurnStep"/>만 꺼도 계속 살아 있어서, 누군가 weight를 다시 올리면
        /// 그대로 되살아난다 — 실제로 해킹당한 경비병이 한쪽 발을 든 채 굳었다.</para>
        ///
        /// <para>그래서 컴포넌트와 홀더 오브젝트까지 꺼서 <b>되살아날 경로 자체를 없앤다.</b>
        /// 이후 뼈는 Animator가 온전히 소유한다.</para>
        /// </summary>
        public void ShutDown()
        {
            ReleaseLegs();

            if (_legL != null) { _legL.enabled = false; _legL.gameObject.SetActive(false); }
            if (_legR != null) { _legR.enabled = false; _legR.gameObject.SetActive(false); }
            if (_targetL != null) _targetL.gameObject.SetActive(false);
            if (_targetR != null) _targetR.gameObject.SetActive(false);

            _hasLegs = false;   // 남은 코드가 실수로 다시 손대지 못하게
        }

        void EndTurn()
        {
            transform.rotation = _turnTo;
            _legL.weight = 0f; _legR.weight = 0f;   // 걷기로 돌아가면 다리는 다시 애니메이션이 맡는다
            _phase = Phase.Idle;
            if (logTurns) Debug.Log("[GuardTurn] 완료");
            var cb = _onComplete; _onComplete = null;
            cb?.Invoke();
        }

        Vector3 SnapToGround(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 0.6f, Vector3.down, out RaycastHit hit, 1.5f,
                                groundMask, QueryTriggerInteraction.Ignore))
                return new Vector3(p.x, hit.point.y, p.z);
            return p;
        }

        static Foot Other(Foot f) => f == Foot.Left ? Foot.Right : Foot.Left;
        static float Ease(float u) => u * u * (3f - 2f * u);   // smoothstep

        void OnDestroy()
        {
            if (_targetL != null) Destroy(_targetL.gameObject);
            if (_targetR != null) Destroy(_targetR.gameObject);
        }
    }
}
