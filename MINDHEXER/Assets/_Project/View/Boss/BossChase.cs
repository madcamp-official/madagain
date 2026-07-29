using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 보스 추격 구동 — 걷기 애니메이션(하체·몸통·머리) 위에 <b>양팔 전방 움켜쥐기 IK</b>를 얹는다.
    /// (보스전_설계 §2·§4·§5)
    ///
    /// <para><b>왜 팔이 앞인가</b>: ×50 스케일에서 보스 몸(~45m)이 복도 폭(~50m)을 거의 채운다 —
    /// 옆벽을 짚을 공간 자체가 없다. 대신 팔을 플레이어 쪽으로 뻗어 <b>손바닥이 화면에 거의 항상
    /// 보이게</b> 한다. "쫓아오고 있다"를 시야로 전달하는 게 팔의 존재 이유다.</para>
    ///
    /// <para><b>고무줄 속도</b>(허기워기 추격 문법): 멀면 플레이어보다 빨라 따라붙고, 가까우면
    /// 살짝 느려져 "거의 잡힐 듯"을 유지한다. 잡는 건 속도가 아니라 <b>플레이어의 실수</b>(멈춤·
    /// 막힘)로만 일어난다 — 거리 < catchDistance가 되면 양손이 카메라로 모여 §16.2 사망 연출.</para>
    ///
    /// <para><b>내려찍기 = 파괴</b>: 손을 뻗어 찍는 지점은 <see cref="BossHandhold"/> 마커가 있으면
    /// 그곳(짚는 곳 = 부서지는 곳), 없으면 플레이어 주변 바닥을 절차적으로 고른다.</para>
    ///
    /// <para><b>팔 소유권</b>: FBX 프리셋 모션의 팔은 리그 불량(오른팔 본이 왼팔의 절반 길이)으로
    /// 못 쓴다. LateUpdate에서 팔 본을 통째로 덮어써 <b>IK가 팔 전체를 소유</b>한다. 트위스트 본도
    /// 매 프레임 중립화 — 안 하면 체인이 중간에서 접힌다(실측: 유효 길이 6.65→3.38m).</para>
    /// </summary>
    [DefaultExecutionOrder(50)]   // Animator 평가(Update) 뒤에 팔을 덮어쓴다.
    public class BossChase : MonoBehaviour
    {
        [Header("대상")]
        [Tooltip("추격 대상(플레이어 카메라/몸). 비우면 MainCamera를 찾는다.")]
        public Transform target;

        [Header("본 (비우면 이름으로 자동 탐색)")]
        public Transform lUpperarm, lForearm, lHand;
        public Transform rUpperarm, rForearm, rHand;

        [Header("고무줄 속도")]
        [Tooltip("멀 때 속도(m/s). 플레이어 달리기보다 빠르게 — 따라붙는 힘.")]
        public float speedFar = 16f;

        [Tooltip("가까울 때 속도(m/s). 플레이어보다 살짝 느리게 — '거의 잡힐 듯' 유지.")]
        public float speedNear = 5f;

        [Tooltip("이 거리 이상이면 speedFar로 전력 추격(m).")]
        public float farDistance = 150f;

        [Tooltip("이 거리 이하이면 speedNear까지 감속(m).")]
        public float nearDistance = 50f;

        [Tooltip("이 거리 안으로 들어오면 잡는다(m). 플레이어가 멈추거나 막혔다는 뜻.")]
        public float catchDistance = 15f;

        [Tooltip("걷기 클립이 자연스러워 보이는 기준 속도(m/s). Animator 재생 배속 = 현재속도/이 값.")]
        public float animBaseSpeed = 10f;

        [Header("팔 — 경계 자세(기본)")]
        [Tooltip("경계 자세에서 손이 어깨 기준 어디에 떠 있나(로컬: x=바깥, y=위, z=앞). 좌우 미러 적용.")]
        public Vector3 guardOffset = new Vector3(0.25f, -0.1f, 0.75f);

        [Tooltip("경계 자세 손 위치를 팔 길이의 몇 배 거리로 잡나(0~1). 1이면 완전히 뻗음.")]
        [Range(0.3f, 1f)] public float guardReach = 0.8f;

        [Header("팔 — 내려찍기")]
        [Tooltip("몇 초마다 한 손씩 내려찍나(좌우 교대).")]
        public float slamInterval = 2.2f;

        [Tooltip("뻗는 데 걸리는 시간(초).")]
        public float slamTime = 0.55f;

        [Tooltip("찍은 채 머무는 시간(초).")]
        public float slamHold = 0.5f;

        [Tooltip("거두는 시간(초).")]
        public float slamReturn = 0.7f;

        [Tooltip("마커가 없을 때 절차 찍기 지점: 플레이어 방향 좌우로 이만큼 비껴 바닥을 찍는다(m).")]
        public float slamSideOffset = 12f;

        [Header("IK")]
        [Tooltip("팔꿈치가 향할 방향(로컬: x=바깥). 좌우 미러 적용.")]
        public Vector3 elbowHint = new Vector3(1f, 0.4f, -0.2f);

        [Tooltip("손바닥 정렬 보정(오일러). 손 본 로컬 축이 모델마다 달라 손으로 맞춘다.")]
        public Vector3 palmEuler;

        [Range(0f, 1f)] public float weight = 1f;

        [Header("잡기")]
        [Tooltip("잡는 순간 양손이 대상까지 모이는 시간(초).")]
        public float catchTime = 0.8f;

        /// <summary>잡힘 확정 순간(양손이 카메라를 덮은 프레임). 사망 연출(§16.2)이 구독한다.</summary>
        public event System.Action OnCaught;

        /// <summary>현재 잡기 진행 중인가.</summary>
        public bool Catching { get; private set; }

        // ── 내부 ─────────────────────────────────────────────────────────

        class Arm
        {
            public Transform upper, lower, hand;
            public Transform[] twists;
            public Quaternion[] twistHome;
            public float sideSign;              // 왼 -1, 오른 +1
            public float len;                   // 유효 팔 길이(트위스트 중립화 후, 매 프레임 갱신)

            // 내려찍기 사이클
            public int phase;                   // 0=경계, 1=뻗는 중, 2=찍힘, 3=거두는 중
            public float t;
            public Vector3 slamPos, slamFrom;
            public BossHandhold hold;
        }

        Arm _l, _r;
        Arm _lastSlammed;
        float _slamTimer;
        float _catchT;
        Vector3 _catchFromL, _catchFromR;

        void Awake()
        {
            if (lUpperarm == null) AutoFind();
            _l = new Arm { upper = lUpperarm, lower = lForearm, hand = lHand, sideSign = -1f };
            _r = new Arm { upper = rUpperarm, lower = rForearm, hand = rHand, sideSign = +1f };
            CollectTwists(_l, "L_");
            CollectTwists(_r, "R_");

            var anim = GetComponentInChildren<Animator>();
            if (anim != null) anim.applyRootMotion = false;
            _anim = anim;

            if (target == null && Camera.main != null) target = Camera.main.transform;
        }

        Animator _anim;

        void AutoFind()
        {
            foreach (var t in GetComponentsInChildren<Transform>())
            {
                switch (t.name)
                {
                    case "L_Upperarm": lUpperarm = t; break;
                    case "L_Forearm": lForearm = t; break;
                    case "L_Hand": lHand = t; break;
                    case "R_Upperarm": rUpperarm = t; break;
                    case "R_Forearm": rForearm = t; break;
                    case "R_Hand": rHand = t; break;
                }
            }
            if (lUpperarm == null || rUpperarm == null)
                Debug.LogError("[보스] 팔 본을 찾지 못했습니다 — L_Upperarm 계열 이름 확인", this);
        }

        void CollectTwists(Arm a, string prefix)
        {
            var list = new System.Collections.Generic.List<Transform>();
            foreach (var t in GetComponentsInChildren<Transform>())
                if (t.name.StartsWith(prefix) && t.name.Contains("Twist") &&
                    (t.name.Contains("Upperarm") || t.name.Contains("Forearm")))
                    list.Add(t);
            a.twists = list.ToArray();
            a.twistHome = new Quaternion[list.Count];
            for (int i = 0; i < list.Count; i++) a.twistHome[i] = list[i].localRotation;
        }

        // ── 이동 (고무줄) ─────────────────────────────────────────────────

        void Update()
        {
            if (target == null || Catching) return;

            Vector3 to = target.position - transform.position;
            to.y = 0f;
            float dist = to.magnitude;

            // 잡기 판정 — 속도가 아니라 거리로. 플레이어가 멈추거나 막힌 상황에서만 도달한다.
            if (dist <= catchDistance) { BeginCatch(); return; }

            // 고무줄: near↔far 사이를 거리로 보간. 멀수록 빠르다.
            float u = Mathf.InverseLerp(nearDistance, farDistance, dist);
            float speed = Mathf.Lerp(speedNear, speedFar, u);

            // 진행은 대상 방향으로 완만히 조향(복도라 대부분 직진).
            Vector3 dir = to / Mathf.Max(0.001f, dist);
            Quaternion want = Quaternion.LookRotation(dir, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, want, 1f - Mathf.Exp(-2f * Time.deltaTime));
            transform.position += transform.forward * (speed * Time.deltaTime);

            // 걷기 배속 — 속도와 발이 따로 놀면 문워크가 된다.
            if (_anim != null) _anim.speed = Mathf.Max(0.2f, speed / Mathf.Max(0.1f, animBaseSpeed));
        }

        void BeginCatch()
        {
            Catching = true;
            _catchT = 0f;
            _catchFromL = _l.hand.position;
            _catchFromR = _r.hand.position;
            if (_anim != null) _anim.speed = 0.15f;   // 몸은 거의 멈추고 손만 덮친다
        }

        // ── 팔 ───────────────────────────────────────────────────────────

        void LateUpdate()
        {
            if (_l.upper == null || _r.upper == null || target == null) return;

            NeutralizeTwists(_l);
            NeutralizeTwists(_r);
            MeasureLen(_l);
            MeasureLen(_r);

            if (Catching)
            {
                TickCatch();
                return;
            }

            // 내려찍기 타이머 — 좌우 교대로 한 손씩.
            _slamTimer += Time.deltaTime;
            if (_slamTimer >= slamInterval)
            {
                Arm next = _lastSlammed == _l ? _r : _l;
                if (next.phase == 0 && TryBeginSlam(next)) { _slamTimer = 0f; _lastSlammed = next; }
            }

            TickSlam(_l);
            TickSlam(_r);

            SolveArm(_l, ArmTarget(_l), PalmToPlayer(_l));
            SolveArm(_r, ArmTarget(_r), PalmToPlayer(_r));
        }

        void NeutralizeTwists(Arm a)
        {
            if (a.twists == null) return;
            for (int i = 0; i < a.twists.Length; i++)
                if (a.twists[i] != null) a.twists[i].localRotation = a.twistHome[i];
        }

        void MeasureLen(Arm a)
        {
            a.len = Vector3.Distance(a.upper.position, a.lower.position)
                  + Vector3.Distance(a.lower.position, a.hand.position);
        }

        /// <summary>경계 자세 손 위치 — 어깨 앞쪽, 손바닥이 플레이어를 향해 떠 있다.</summary>
        Vector3 GuardPos(Arm a)
        {
            Vector3 local = new Vector3(guardOffset.x * a.sideSign, guardOffset.y, guardOffset.z);
            return a.upper.position + transform.TransformDirection(local.normalized) * (a.len * guardReach);
        }

        bool TryBeginSlam(Arm a)
        {
            // ① 마커 우선 — 앞쪽·자기편·미소모·(대략) 닿는 거리.
            BossHandhold best = null;
            float bestAhead = float.PositiveInfinity;
            Vector3 shoulder = a.upper.position;
            foreach (var h in BossHandhold.All)
            {
                if (h.consumed) continue;
                Vector3 to = h.transform.position - shoulder;
                float ahead = Vector3.Dot(to, transform.forward);
                if (ahead < 0f) continue;
                float side = Vector3.Dot(to, transform.right);
                if (h.side == BossHandhold.Side.Left && a.sideSign > 0f) continue;
                if (h.side == BossHandhold.Side.Right && a.sideSign < 0f) continue;
                if (h.side == BossHandhold.Side.Any && side * a.sideSign < 0f) continue;
                if (to.magnitude > a.len * 1.05f) continue;   // 살짝 못 미쳐도 몸이 전진하며 닿는다
                if (ahead < bestAhead) { bestAhead = ahead; best = h; }
            }
            if (best != null)
            {
                a.slamPos = best.transform.position;
                a.hold = best;
            }
            else
            {
                // ② 절차 — 플레이어 방향으로, 자기편으로 비껴서, 팔이 닿는 바닥.
                Vector3 toP = target.position - shoulder;
                toP.y = 0f;
                Vector3 dir = toP.sqrMagnitude > 1e-4f ? toP.normalized : transform.forward;
                float reach = Mathf.Min(a.len * 0.95f, toP.magnitude * 0.7f);
                Vector3 p = shoulder + dir * reach + transform.right * (a.sideSign * slamSideOffset);
                p.y = transform.position.y;   // 바닥
                a.slamPos = p;
                a.hold = null;
            }

            a.slamFrom = a.hand.position;
            a.phase = 1;
            a.t = 0f;
            return true;
        }

        void TickSlam(Arm a)
        {
            switch (a.phase)
            {
                case 1:   // 뻗는 중
                    a.t += Time.deltaTime / Mathf.Max(0.05f, slamTime);
                    if (a.t >= 1f)
                    {
                        a.phase = 2; a.t = 0f;
                        if (a.hold != null) a.hold.Consume();   // 닿는 순간 파괴 — 짚는 곳 = 부서지는 곳
                    }
                    break;
                case 2:   // 찍힌 채 유지
                    a.t += Time.deltaTime / Mathf.Max(0.05f, slamHold);
                    if (a.t >= 1f) { a.phase = 3; a.t = 0f; a.slamFrom = a.hand.position; }
                    break;
                case 3:   // 거두는 중
                    a.t += Time.deltaTime / Mathf.Max(0.05f, slamReturn);
                    if (a.t >= 1f) { a.phase = 0; a.t = 0f; a.hold = null; }
                    break;
            }
        }

        Vector3 ArmTarget(Arm a)
        {
            switch (a.phase)
            {
                case 1:
                {
                    float u = Mathf.SmoothStep(0f, 1f, a.t);
                    Vector3 p = Vector3.Lerp(a.slamFrom, a.slamPos, u);
                    p += Vector3.up * (Mathf.Sin(u * Mathf.PI) * a.len * 0.15f);   // 위로 들었다 내려찍는 호
                    return p;
                }
                case 2: return a.slamPos;
                case 3: return Vector3.Lerp(a.slamFrom, GuardPos(a), Mathf.SmoothStep(0f, 1f, a.t));
                default: return GuardPos(a);
            }
        }

        /// <summary>손바닥이 항상 플레이어를 향한다 — "쫓아온다"를 시야로 전달하는 핵심.</summary>
        Quaternion PalmToPlayer(Arm a)
        {
            Vector3 toP = target.position - a.hand.position;
            if (toP.sqrMagnitude < 1e-4f) toP = transform.forward;
            return Quaternion.LookRotation(toP.normalized, Vector3.up) * Quaternion.Euler(palmEuler);
        }

        void TickCatch()
        {
            _catchT += Time.deltaTime / Mathf.Max(0.05f, catchTime);
            float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_catchT));

            // 양손이 카메라 좌우에서 모여 시야를 덮는다(§16.2 — 팔이 화면을 가리는 틈에 리셋).
            Vector3 head = target.position;
            Vector3 right = target.right;
            SolveArm(_l, Vector3.Lerp(_catchFromL, head - right * 1.5f, u), PalmToPlayer(_l));
            SolveArm(_r, Vector3.Lerp(_catchFromR, head + right * 1.5f, u), PalmToPlayer(_r));

            if (_catchT >= 1f)
            {
                Catching = false;
                _catchT = 0f;
                OnCaught?.Invoke();
            }
        }

        // ── 두-본 해석해 ─────────────────────────────────────────────────

        void SolveArm(Arm a, Vector3 target_, Quaternion handRot)
        {
            Vector3 root = a.upper.position;
            float lenU = Vector3.Distance(root, a.lower.position);
            float lenL = Vector3.Distance(a.lower.position, a.hand.position);
            Vector3 to = target_ - root;
            float c = Mathf.Clamp(to.magnitude, 0.01f, lenU + lenL - 1e-3f);
            Vector3 n = to.normalized;

            Vector3 hintW = transform.TransformDirection(
                new Vector3(elbowHint.x * a.sideSign, elbowHint.y, elbowHint.z)).normalized;
            Vector3 elbowDir = hintW - Vector3.Dot(hintW, n) * n;
            elbowDir = elbowDir.sqrMagnitude > 1e-6f ? elbowDir.normalized
                     : Vector3.Cross(n, Vector3.up).normalized * a.sideSign;

            float cosU = Mathf.Clamp((lenU * lenU + c * c - lenL * lenL) / (2f * lenU * c), -1f, 1f);
            float sinU = Mathf.Sqrt(Mathf.Max(0f, 1f - cosU * cosU));
            Vector3 upperDir = n * cosU + elbowDir * sinU;

            Quaternion u0 = a.upper.rotation, l0 = a.lower.rotation, h0 = a.hand.rotation;

            a.upper.rotation = Quaternion.FromToRotation(a.lower.position - root, upperDir) * a.upper.rotation;
            a.lower.rotation = Quaternion.FromToRotation(a.hand.position - a.lower.position,
                                                         target_ - a.lower.position) * a.lower.rotation;
            a.hand.rotation = handRot;

            if (weight < 1f)
            {
                a.upper.rotation = Quaternion.Slerp(u0, a.upper.rotation, weight);
                a.lower.rotation = Quaternion.Slerp(l0, a.lower.rotation, weight);
                a.hand.rotation = Quaternion.Slerp(h0, a.hand.rotation, weight);
            }
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (target == null) return;
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, catchDistance);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, nearDistance);
            if (_l != null && _l.phase != 0) { Gizmos.color = new Color(1f, .5f, .2f); Gizmos.DrawSphere(_l.slamPos, 1f); }
            if (_r != null && _r.phase != 0) { Gizmos.color = new Color(.2f, .7f, 1f); Gizmos.DrawSphere(_r.slamPos, 1f); }
        }
#endif
    }
}
