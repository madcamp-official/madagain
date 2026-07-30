using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 보스 추격 — 걷기 애니메이션(하체·몸통) 위에 <b>양팔 벽 짚기</b>를 얹어 전진한다.
    /// (보스전_설계 §2·§4)
    ///
    /// <para><b>IK를 직접 하지 않는다.</b> 팔은 <see cref="BossArmRig"/>가 소유하고, 여기서는
    /// "어느 지점을 짚을지"만 정해 <see cref="BossArmRig.Aim"/>으로 밀어넣는다. 동작이 늘어도
    /// IK 코드가 복사되지 않는다.</para>
    ///
    /// <para><b>★ 짚는 지점은 옆이 아니라 저 앞이다.</b> 복도 폭(50m)에 비해 팔이 길어서
    /// (어깨→벽 14m vs 팔 32m) 바로 옆을 짚으면 팔이 접혀 몸에 구겨진다. 그래서 벽면 위에서
    /// <b>어깨로부터의 거리가 팔 길이에 가까워지는 만큼 앞쪽</b>으로 지점을 잡는다 —
    /// 옆으로 14m면 앞으로 28m쯤 되어 팔이 거의 쭉 뻗은 채 유지된다.</para>
    ///
    /// <para><b>짚은 손은 월드에 고정</b>되고, 몸이 전진하면 어깨가 손 쪽으로 다가간다. 팔이
    /// <see cref="releaseExtension"/> 이하로 접히면 그때 떼고 다시 저 앞을 짚는다 — 즉
    /// "쭉 뻗어 짚고 → 몸이 그리로 밀려가고 → 접히기 전에 다시 앞을 짚는다"의 반복이다.
    /// 한 번에 한 손만 옮겨서 교차 보행이 저절로 나온다.</para>
    ///
    /// <para><b>짚는 곳 = 부서지는 곳</b>: 지점은 <see cref="BossHandhold"/> 마커가 우선이고,
    /// 없으면 옆벽을 레이캐스트해 찾는다(그레이박스 검증용). 손이 닿는 프레임에 파괴 그룹이 발동한다.</para>
    /// </summary>
    [DefaultExecutionOrder(40)]   // BossArmRig(50)보다 먼저 — 지시를 내리면 그 다음에 리그가 푼다
    [RequireComponent(typeof(BossArmRig))]
    public class BossChase : MonoBehaviour
    {
        [Header("이동")]
        [Tooltip("전진 속도(m/s). 추격 대상이 있으면 고무줄 속도로 대체된다.")]
        public float walkSpeed = 12f;

        [Tooltip("끄면 제자리 걸음(팔 짚기만 확인할 때).")]
        public bool move = true;

        [Tooltip("재생속도 1.0일 때 발이 안 미끄러지는 이동속도(m/s). 애니 배속 = 현재속도/이 값.\n" +
                 "★ 실측값: 접지 중 발이 뒤로 밀리는 속도로 재면 ×50 스케일에서 약 24 m/s다. " +
                 "(원본 클립의 루트 전진량으로 재면 38~42가 나오는데, 그 차이만큼 프리셋 자체에 " +
                 "발 미끄러짐이 들어 있다 — 발 기준이 미끄러짐이 적다.)")]
        public float animBaseSpeed = 24f;

        [Tooltip("애니 배속 하한. 보스가 아주 느릴 때 완전히 멈춘 것처럼 보이지 않게 최소한은 움직인다.\n" +
                 "올리면 걸음은 살아나지만 그만큼 발이 미끄러진다.")]
        public float minAnimSpeed = 0.05f;

        [Header("고무줄 추격 (대상이 있을 때만)")]
        public Transform target;
        public float speedFar = 16f;
        public float speedNear = 5f;
        public float farDistance = 150f;
        public float nearDistance = 50f;

        [Header("짚기 — 얼마나 앞을 짚나")]
        [Tooltip("짚는 순간 팔을 이 비율까지 뻗는다. 1에 가까울수록 더 앞을 짚고 더 쭉 펴진다.")]
        [Range(0.6f, 0.99f)] public float reachUse = 0.92f;

        [Tooltip("팔이 이 비율 이하로 접히면 떼고 다시 앞을 짚는다. 낮출수록 한 번 짚고 오래 간다.")]
        [Range(0.2f, 0.9f)] public float releaseExtension = 0.55f;

        [Tooltip("어깨보다 이만큼 아래를 짚는다(m). 0이면 어깨 높이.")]
        public float plantDrop = 4f;

        [Tooltip("앞으로 뻗는 성분의 최소값(m). 벽이 아주 가까워도 최소한 이만큼은 앞을 짚는다.")]
        public float minForward = 8f;

        [Header("짚기 — 손 옮기는 연출")]
        [Tooltip("한 번 옮겨 짚는 데 걸리는 시간(초).")]
        public float stepTime = 1.2f;

        [Tooltip("한 손을 다 옮긴 뒤 다른 손이 움직이기까지의 최소 대기(초). " +
                 "이게 없으면 조건이 맞는 즉시 반대 손이 따라 나서서 좌우가 촐싹댄다.")]
        public float minStepInterval = 1.5f;

        [Tooltip("옮기는 동안 손이 벽에서 이만큼 떨어져 호를 그린다(m).")]
        public float stepLift = 6f;

        [Tooltip("짚고 있을 때 손가락 쥐는 정도. 옮기는 동안은 편다.")]
        [Range(0f, 1f)] public float gripCurl = 0.7f;

        [Tooltip("켜면 <b>손등</b>으로 벽을 짚는다(손바닥은 벽 반대쪽을 본다). 끄면 손바닥으로 짚는다.\n" +
                 "손바닥을 벽에 붙이려면 IK가 어깨를 크게 비틀어야 해서 가슴 판때기가 끌려 뭉개졌다 — " +
                 "손등으로 짚으면 비트는 양이 줄고 연출도 '밀고 나아간다'에 가깝다.")]
        public bool braceWithBackOfHand = true;

        [Header("벽 찾기")]
        public LayerMask wallMask = ~0;

        [Header("진단 (읽기 전용)")]
        public float extensionL, extensionR;   // 팔이 얼마나 펴져 있나 (1 = 완전히 뻗음)

        BossArmRig _rig;
        Animator _anim;

        class Hand
        {
            public BossArmRig.Side side;
            public float sign;              // 왼 -1 / 오른 +1
            public bool planted;
            public Vector3 pos, normal;     // 짚은 월드 지점 / 벽의 바깥 법선
            public BossHandhold hold;
            public float stepT = -1f;       // 0~1 진행 중, -1 = 정지
            public Vector3 fromPos, fromNormal;
        }

        Hand _l, _r, _stepping, _lastStepped;
        float _lastStepEnd = -999f;   // 마지막 스텝이 끝난 시각 — minStepInterval 판정용

        void Awake()
        {
            _rig = GetComponent<BossArmRig>();
            _anim = GetComponentInChildren<Animator>();
            if (_anim != null) _anim.applyRootMotion = false;
            _l = new Hand { side = BossArmRig.Side.Left,  sign = -1f };
            _r = new Hand { side = BossArmRig.Side.Right, sign = +1f };
        }

        void Update()
        {
            if (!move) return;
            if (!BossChaseState.Active) return;   // ★ 전환점에서 추격이 시작되기 전엔 걷지 않는다

            // 플레이어는 씬에 배치된 오브젝트가 아니라 런타임에 스폰되는 프리팹이라
            // 인스펙터에서 미리 연결할 수 없다 — GameOverManager와 같은 방식으로 늦게 잡는다.
            if (target == null && FirstPersonPlayer.Instance != null)
                target = FirstPersonPlayer.Instance.transform;

            float speed = walkSpeed;
            if (target != null)
            {
                Vector3 to = target.position - transform.position; to.y = 0f;
                float d = to.magnitude;
                speed = Mathf.Lerp(speedNear, speedFar, Mathf.InverseLerp(nearDistance, farDistance, d));
                if (d > 1e-3f)
                {
                    Quaternion want = Quaternion.LookRotation(to / d, Vector3.up);
                    transform.rotation = Quaternion.Slerp(transform.rotation, want, 1f - Mathf.Exp(-2f * Time.deltaTime));
                }
            }

            transform.position += transform.forward * (speed * Time.deltaTime);
            if (_anim != null) _anim.speed = Mathf.Max(minAnimSpeed, speed / Mathf.Max(0.1f, animBaseSpeed));
        }

        void LateUpdate()
        {
            Tick(_l);
            Tick(_r);
            Push(_l);
            Push(_r);
        }

        // ── 짚기 사이클 ──────────────────────────────────────────────────

        void Tick(Hand h)
        {
            float ext = Extension(h);
            if (h.side == BossArmRig.Side.Left) extensionL = ext; else extensionR = ext;

            if (h.stepT >= 0f)
            {
                h.stepT += Time.deltaTime / Mathf.Max(0.05f, stepTime);
                if (h.stepT >= 1f)
                {
                    h.stepT = -1f;
                    h.planted = true;
                    _stepping = null;
                    _lastStepEnd = Time.time;
                    if (h.hold != null) h.hold.Consume();   // 짚는 곳 = 부서지는 곳
                }
                return;
            }

            if (!NeedsReplant(h)) return;
            if (_stepping != null) return;                              // 한 번에 한 손만
            if (Time.time - _lastStepEnd < minStepInterval) return;     // 좌우 교대 사이 간격

            // 교차 우선 — 직전에 옮긴 손은, 반대 손도 옮겨야 하는 상황이면 <b>양보한다</b>.
            // (부호를 반대로 뒀다가 한쪽이 순번을 독점해 반대 손이 한 번도 못 짚는 버그를 겪었다)
            if (_lastStepped == h && NeedsReplant(Other(h))) return;

            if (FindPlant(h, out Vector3 p, out Vector3 n, out BossHandhold hold))
            {
                h.fromPos = h.planted ? h.pos : _rig.PalmPos(h.side);
                h.fromNormal = h.planted ? h.normal : n;
                h.pos = p; h.normal = n; h.hold = hold;
                h.planted = false;
                h.stepT = 0f;
                _stepping = h;
                _lastStepped = h;
            }
        }

        Hand Other(Hand h) { return h == _l ? _r : _l; }

        /// <summary>팔이 얼마나 펴져 있나(0=완전히 접힘, 1=쭉 뻗음).</summary>
        float Extension(Hand h)
        {
            float reach = _rig.Reach(h.side);
            if (reach < 1e-3f) return 0f;
            return Vector3.Distance(_rig.ShoulderPos(h.side), _rig.PalmPos(h.side)) / reach;
        }

        bool NeedsReplant(Hand h)
        {
            if (!h.planted) return true;
            float reach = _rig.Reach(h.side);
            if (reach < 1e-3f) return false;
            Vector3 sh = _rig.ShoulderPos(h.side);
            float d = Vector3.Distance(h.pos, sh);
            // 접혔거나(몸이 손을 지나쳤거나) 팔 길이를 넘었으면 다시 짚는다.
            return d < reach * releaseExtension || d > reach * 0.99f
                || Vector3.Dot(h.pos - sh, transform.forward) < 0f;
        }

        /// <summary>
        /// 다음 짚을 곳. 핵심은 <b>앞으로 멀리</b>다 — 옆으로만 짚으면 팔이 접혀 구겨진다.
        /// 벽까지의 옆 거리를 재고, 어깨~지점 거리가 팔 길이의 <see cref="reachUse"/>가 되도록
        /// 앞 성분을 역산한다(피타고라스).
        /// </summary>
        bool FindPlant(Hand h, out Vector3 pos, out Vector3 normal, out BossHandhold hold)
        {
            Vector3 sh = _rig.ShoulderPos(h.side);
            Vector3 fwd = transform.forward, right = transform.right;
            float reach = _rig.Reach(h.side);
            float want = reach * reachUse;

            // ① 마커 — 닿는 것 중 <b>가장 앞선</b> 것을 고른다(최대한 뻗기 위해).
            BossHandhold best = null;
            float bestAhead = -1f;
            foreach (var m in BossHandhold.All)
            {
                if (m == null || m.consumed) continue;
                Vector3 to = m.transform.position - sh;
                if (m.side == BossHandhold.Side.Left && h.sign > 0f) continue;
                if (m.side == BossHandhold.Side.Right && h.sign < 0f) continue;
                if (m.side == BossHandhold.Side.Any && Vector3.Dot(to, right) * h.sign < 0f) continue;
                float ahead = Vector3.Dot(to, fwd);
                if (ahead <= 0f || to.magnitude > want) continue;
                if (ahead > bestAhead) { bestAhead = ahead; best = m; }
            }
            if (best != null)
            {
                pos = best.transform.position;
                normal = best.transform.forward;
                hold = best;
                return true;
            }

            // ② 폴백 — 옆으로 쏴서 벽까지의 거리를 잰다.
            Vector3 side = right * h.sign;
            RaycastHit hit;
            if (!Physics.Raycast(sh, side, out hit, reach, wallMask, QueryTriggerInteraction.Ignore))
            { pos = default; normal = default; hold = null; return false; }

            float lateral = hit.distance;
            float dy = -plantDrop;
            // want² = lateral² + dy² + dz²  →  앞으로 얼마나 나가야 팔이 다 펴지는가
            float rem = want * want - lateral * lateral - dy * dy;
            float dz = Mathf.Max(minForward, rem > 1f ? Mathf.Sqrt(rem) : minForward);

            // 그 앞 지점에서 다시 옆으로 쏴 실제 벽면을 잡는다(복도가 넓어지거나 좁아져도 맞게).
            Vector3 probe = sh + fwd * dz + Vector3.up * dy;
            if (Physics.Raycast(probe, side, out hit, reach, wallMask, QueryTriggerInteraction.Ignore))
            { pos = hit.point; normal = hit.normal; hold = null; return true; }

            // 앞쪽에 벽이 없으면(모서리 등) 옆에서 잡은 점을 앞으로 밀어 쓴다.
            pos = hit.point + fwd * dz + Vector3.up * dy;
            normal = -side;
            hold = null;
            return true;
        }

        // ── 리그에 지시 ──────────────────────────────────────────────────

        void Push(Hand h)
        {
            if (!h.planted && h.stepT < 0f) { _rig.Relax(h.side); _rig.Curl(h.side, 0f); return; }

            Vector3 p = h.pos, n = h.normal;

            if (h.stepT >= 0f)
            {
                float u = Mathf.SmoothStep(0f, 1f, h.stepT);
                n = Vector3.Slerp(h.fromNormal, h.normal, u);
                p = Vector3.Lerp(h.fromPos, h.pos, u) + n * (Mathf.Sin(u * Mathf.PI) * stepLift);
                _rig.Curl(h.side, gripCurl * u * u);
            }
            else _rig.Curl(h.side, gripCurl);

            // n = 벽에서 바깥으로 나오는 법선.
            //  손등으로 짚기 → 손바닥은 벽 반대쪽(= +n)을 본다.
            //  손바닥으로 짚기 → 손바닥이 벽을 마주본다(= -n).
            _rig.Aim(h.side, p, braceWithBackOfHand ? n : -n);
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (_l == null || _rig == null) return;
            Hand[] hs = { _l, _r };
            foreach (var h in hs)
            {
                if (!h.planted && h.stepT < 0f) continue;
                Gizmos.color = h.sign < 0f ? new Color(1f, 0.55f, 0.2f) : new Color(0.25f, 0.7f, 1f);
                Gizmos.DrawSphere(h.pos, 1.2f);
                Gizmos.DrawLine(_rig.ShoulderPos(h.side), h.pos);
            }
        }
#endif
    }
}
