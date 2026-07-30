using System;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 펫 거미 다리 IK — <b>다리 끝을 팔에 고정</b>하고 몸통이 움직이면 다리가 따라 푼다.
    /// 설계: `docs/KJH/design/플레이어_신체표현_설계.md` §6
    ///
    /// <para><b>보통의 IK와 방향이 반대다.</b> 손 IK는 몸이 고정이고 손끝이 목표를 좇지만,
    /// 여기서는 <b>발끝이 고정</b>이고 몸통이 움직인다. 결과적으로 몸통이 들썩일 때
    /// 다리가 알아서 굽었다 펴진다 — 그게 "팔을 붙잡고 버티는" 모양이 된다.</para>
    ///
    /// <para><see cref="HandIK"/>를 재사용하지 않는 이유: 저쪽은 인체 관절 제한(팔꿈치 과신전 금지,
    /// 어깨 원뿔, 손목 스윙·트위스트)이 들어 있다. 거미 다리엔 전부 방해만 된다.</para>
    ///
    /// <para><b>앵커는 팔 뼈의 자식</b>이라 팔이 움직이면 같이 간다. 거미 몸통은 별개로
    /// <see cref="SpiderRig"/>가 몬다. 둘의 차이가 곧 다리 각도다.</para>
    ///
    /// <para>실행 순서 −10 — SpiderRig(−20)가 몸통을 놓은 <b>뒤</b>에 다리를 푼다.</para>
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class SpiderLegs : MonoBehaviour
    {
        [Serializable]
        public class Leg
        {
            [Tooltip("표시용")]
            public string label = "";

            [Header("다리 체인 (몸통 쪽 → 끝)")]
            public Transform upper;
            public Transform lower;
            [Tooltip("발끝 뼈. 이 지점이 앵커에 붙는다.")]
            public Transform tip;

            [Tooltip("팔에 고정된 지점. 보통 팔 뼈의 자식으로 둔다.")]
            public Transform anchor;

            [Tooltip("무릎이 향할 방향(몸통 로컬 기준). 보통 바깥·위.")]
            public Vector3 poleLocalDir = new Vector3(0f, 1f, 0f);

            [NonSerialized] public float lenUpper, lenLower;
            [NonSerialized] public bool  measured;
        }

        [Header("다리 (4개)")]
        public Leg[] legs = new Leg[4];

        [Header("가중치")]
        [Tooltip("0이면 다리를 안 건드린다. 비행 중에는 SpiderRig가 0으로 내린다.")]
        [Range(0f, 1f)] public float weight = 1f;

        [Tooltip("가중치가 목표로 수렴하는 속도.")]
        public float weightSpeed = 10f;

        [Header("앵커 자동 생성")]
        [Tooltip("앵커가 비어 있으면 여기 아래에 만든다. 보통 R_Hand 또는 R_Forearm.")]
        public Transform anchorParent;

        [Tooltip("앵커를 팔 둘레에 배치하는 반지름(m).")]
        public float anchorRadius = 0.035f;

        [Tooltip("앵커를 팔 축 방향으로 벌리는 간격(m).")]
        public float anchorSpread = 0.05f;

        [Header("디버그")]
        public bool drawGizmos = true;

        float _w;
        bool _warned;

        public float TargetWeight { get; set; } = -1f;   // -1 = weight 필드를 그대로 씀

        void OnEnable() { _w = weight; Measure(); }

        /// <summary>뼈 길이를 잰다. 런타임에 한 번이면 충분하다(스케일이 안 변하므로).</summary>
        public void Measure()
        {
            if (legs == null) return;
            foreach (var l in legs)
            {
                if (l == null || l.upper == null || l.lower == null || l.tip == null) continue;
                l.lenUpper = Vector3.Distance(l.upper.position, l.lower.position);
                l.lenLower = Vector3.Distance(l.lower.position, l.tip.position);
                l.measured = l.lenUpper > 1e-5f && l.lenLower > 1e-5f;
            }
            CapturePolesFromRest();
        }

        /// <summary>
        /// 다리마다 <b>모델링된 자세에서 무릎이 접혀 있던 방향</b>을 재서 폴로 삼는다.
        ///
        /// <para><b>왜 필요한가</b> — 기본값은 네 다리가 전부 <c>(0,1,0)</c>이라 무릎이 모두 같은
        /// 방향(위)으로 접힌다. 그런데 모델의 다리는 바깥으로 벌어져 있어서, IK가 풀리는 순간
        /// 무릎이 한쪽으로 몰리며 <b>배치해 둔 자세와 다른 모양</b>이 된다(실측: 무릎이 1.4cm 이동).
        /// 발끝은 앵커에 정확히 맞는데도 실루엣이 달라 보이는 이유가 이것이다.</para>
        ///
        /// <para>어깨→발끝 축에서 무릎이 벗어난 성분이 곧 접히는 방향이다. 그걸 그대로 폴로 쓰면
        /// IK가 모델 자세를 재현한다.</para>
        /// </summary>
        [ContextMenu("무릎 방향을 모델 자세에서 캡처")]
        public void CapturePolesFromRest()
        {
            if (legs == null) return;
            foreach (var l in legs)
            {
                if (l == null || l.upper == null || l.lower == null || l.tip == null) continue;
                Vector3 axis = l.tip.position - l.upper.position;
                if (axis.sqrMagnitude < 1e-8f) continue;
                axis.Normalize();
                Vector3 kneeOff = l.lower.position - l.upper.position;
                Vector3 poleW = kneeOff - axis * Vector3.Dot(kneeOff, axis);   // 축에 수직인 성분
                if (poleW.sqrMagnitude < 1e-8f) continue;
                l.poleLocalDir = transform.InverseTransformDirection(poleW.normalized);
            }
        }

        /// <summary>
        /// 앵커가 없으면 팔 둘레에 4개를 만든다. 실제 위치는 실기에서 눈으로 맞춘다.
        /// 팔을 감싸듯 좌우 2쌍으로 배치한다.
        /// </summary>
        [ContextMenu("앵커 자동 생성")]
        public void CreateAnchors()
        {
            if (anchorParent == null)
            {
                Debug.LogWarning("[SpiderLegs] anchorParent가 비어 있습니다(보통 R_Hand).");
                return;
            }
            if (legs == null || legs.Length == 0) legs = new Leg[4];

            for (int i = 0; i < legs.Length; i++)
            {
                if (legs[i] == null) legs[i] = new Leg();
                if (legs[i].anchor != null) continue;

                // 앞뒤 2 × 좌우 2
                float fwd  = (i < 2 ? 1f : -1f) * anchorSpread;
                float side = ((i % 2 == 0) ? 1f : -1f) * anchorRadius;

                var go = new GameObject("[LegAnchor_" + i + "]");
                go.transform.SetParent(anchorParent, false);
                go.transform.localPosition = new Vector3(side, 0f, fwd);
                legs[i].anchor = go.transform;
                if (string.IsNullOrEmpty(legs[i].label)) legs[i].label = "Leg" + i;
            }
            Debug.Log("[SpiderLegs] 앵커 4개 생성 — 위치는 실기에서 맞추십시오.");
        }

        void LateUpdate()
        {
            float want = TargetWeight >= 0f ? TargetWeight : weight;
            _w = Mathf.Lerp(_w, want, 1f - Mathf.Exp(-weightSpeed * Time.deltaTime));
            if (_w <= 0.001f || legs == null) return;

            int solved = 0;
            foreach (var l in legs)
            {
                if (l == null || l.upper == null || l.lower == null || l.tip == null || l.anchor == null) continue;
                if (!l.measured) { Measure(); if (!l.measured) continue; }
                Solve(l, _w);
                solved++;
            }

            if (solved == 0 && !_warned)
            {
                _warned = true;
                Debug.Log("[SpiderLegs] 풀 다리가 없습니다. 리깅된 모델이 오면 체인을 연결하십시오. (지금은 무시해도 됩니다)");
            }
        }

        /// <summary>2본 IK 정석 — 코사인 법칙으로 무릎 각을 구하고 폴로 굽힘 평면을 확정한다.</summary>
        void Solve(Leg l, float w)
        {
            Quaternion u0 = l.upper.localRotation, d0 = l.lower.localRotation;

            Vector3 root = l.upper.position;
            Vector3 goal = l.anchor.position;
            Vector3 toGoal = goal - root;
            float dist = toGoal.magnitude;
            if (dist < 1e-5f) return;

            float a = l.lenUpper, b = l.lenLower;
            // 뻗을 수 있는 범위로 자른다 — 넘으면 다리가 찢어진다.
            dist = Mathf.Clamp(dist, Mathf.Abs(a - b) + 1e-4f, a + b - 1e-4f);
            Vector3 dir = toGoal.normalized;

            // 굽힘 평면 — 폴 방향이 다리 축과 나란하면 대체 축을 쓴다.
            Vector3 pole = transform.TransformDirection(l.poleLocalDir);
            Vector3 n = Vector3.Cross(dir, pole);
            if (n.sqrMagnitude < 1e-7f)
            {
                n = Vector3.Cross(dir, l.lower.position - root);
                if (n.sqrMagnitude < 1e-7f)
                    n = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right);
            }
            n.Normalize();

            float cos = Mathf.Clamp((a * a + dist * dist - b * b) / (2f * a * dist), -1f, 1f);
            float ang = Mathf.Acos(cos) * Mathf.Rad2Deg;

            Vector3 dirUpper = Quaternion.AngleAxis(ang, n) * dir;
            Vector3 knee = root + dirUpper * a;
            Vector3 end  = root + dir * dist;

            Aim(l.upper, l.lower.position, knee);
            Aim(l.lower, l.tip.position,   end);

            if (w < 1f)
            {
                l.upper.localRotation = Quaternion.Slerp(u0, l.upper.localRotation, w);
                l.lower.localRotation = Quaternion.Slerp(d0, l.lower.localRotation, w);
            }
        }

        static void Aim(Transform bone, Vector3 childPos, Vector3 wantPos)
        {
            Vector3 cur = childPos - bone.position;
            Vector3 want = wantPos - bone.position;
            if (cur.sqrMagnitude < 1e-10f || want.sqrMagnitude < 1e-10f) return;
            bone.rotation = Quaternion.FromToRotation(cur, want) * bone.rotation;
        }

        void OnDrawGizmosSelected()
        {
            if (!drawGizmos || legs == null) return;
            foreach (var l in legs)
            {
                if (l == null) continue;
                if (l.anchor != null)
                {
                    Gizmos.color = new Color(0.4f, 1f, 0.4f);
                    Gizmos.DrawWireSphere(l.anchor.position, 0.006f);
                }
                if (l.upper == null || l.lower == null || l.tip == null) continue;
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(l.upper.position, l.lower.position);
                Gizmos.DrawLine(l.lower.position, l.tip.position);
                if (l.anchor != null)
                {
                    Gizmos.color = Color.gray;
                    Gizmos.DrawLine(l.tip.position, l.anchor.position);
                }
            }
        }
    }
}
