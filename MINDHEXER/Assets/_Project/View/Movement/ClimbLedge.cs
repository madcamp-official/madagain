using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// "이 물체는 잡고 올라갈 수 있다"는 <b>표시</b>. 등반·시선 도약 판정의 유일한 권위다.
    ///
    /// <para>레이캐스트로 지형을 추측하지 않고 프리팹에 이 컴포넌트를 붙여 명시한다. BoxCollider에서
    /// 모서리(잡는 선)와 착지 지점을 자동 산출하므로 붙이기만 하면 된다. 부분 제한이 필요하면
    /// <see cref="grabRegions"/>에 자식 트리거 볼륨을 넣어 "이 구간만 잡을 수 있다"로 좁힌다.</para>
    ///
    /// <para>손 연출을 위해 잡는 지점을 정확히 내야 한다 — <see cref="GrabInfo"/>가 모서리 선 위의
    /// 잡는 중심·방향·범위를 전부 담는다. 오브젝트가 수직으로 서 있다고 가정한다(기울어진 벽 미지원).</para>
    ///
    /// <para>씬 인스턴스가 아니라 <b>프리팹 에셋</b>에 붙일 것.</para>
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class ClimbLedge : MonoBehaviour
    {
        [System.Flags]
        public enum Face { None = 0, PlusX = 1, MinusX = 2, PlusZ = 4, MinusZ = 8, All = 15 }

        [Tooltip("어느 면에서 접근했을 때 오를 수 있는지. 물체의 로컬 축 기준.")]
        public Face climbableFaces = Face.All;

        [Tooltip("켜면 콜라이더 윗면을 모서리로 쓴다(보통 이것). 끄면 아래 높이를 직접 지정.")]
        public bool useColliderTop = true;

        [Tooltip("모서리 높이(로컬 Y). useColliderTop이 꺼져 있을 때만 쓴다.")]
        public float customHeight = 1f;

        [Tooltip("올라선 뒤 면에서 안쪽으로 얼마나 들어가 서는지(m, 월드).")]
        public float landingInset = 0.45f;

        [Tooltip("비우면 모서리 전체를 잡을 수 있다. 넣으면 이 볼륨(트리거 콜라이더) 안의 구간만 잡을 수 있다.")]
        public Collider[] grabRegions;

        [Header("기즈모")]
        [Tooltip("면 앞 바닥에서 모서리까지가 이 높이를 넘으면 기즈모가 빨갛게. AutoTraversal.maxMantleUp과 맞출 것.\n" +
                 "※ 오브젝트 자체 높이가 아니라 '그 앞에 섰을 때 올라야 할 높이'다.")]
        public float gizmoMaxClimbHeight = 2f;

        /// <summary>씬에 살아 있는 모든 ClimbLedge — 시선 도약 후보 검색용.</summary>
        public static readonly List<ClimbLedge> All = new List<ClimbLedge>();

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        BoxCollider _box;
        BoxCollider Box => _box != null ? _box : (_box = GetComponent<BoxCollider>());

        /// <summary>월드 AABB — 후보 검색에서 비싼 판정 전에 거리로 먼저 걸러내는 용도.</summary>
        public Bounds WorldBounds =>
            Box != null ? Box.bounds : new Bounds(transform.position, Vector3.zero);

        /// <summary>이 모서리의 몸통. 궤적 검사에서 <b>오르려는 대상 자신</b>은 장애물이 아니므로 제외한다.</summary>
        public Collider Volume => Box;

        /// <summary>면 안쪽으로 이만큼까지는 '바깥'으로 인정한다(면에 바싹 붙어 선 경우).</summary>
        const float OutsideTolerance = 0.05f;

        static readonly Vector3[] Normals = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
        static readonly Face[] Flags = { Face.PlusX, Face.MinusX, Face.PlusZ, Face.MinusZ };

        /// <summary>잡기 판정 결과 — 손 연출까지 쓸 수 있게 모서리 정보를 전부 담는다.</summary>
        public struct GrabInfo
        {
            public Vector3 landingFeet;   // 올라선 뒤 발 위치(월드)
            public Vector3 faceNormal;    // 접근한 면의 바깥 법선(수평, 월드)
            public Vector3 edgeCenter;    // 잡는 중심 = 플레이어를 모서리 선에 투영한 점(월드)
            public Vector3 edgeMid;       // 모서리 선의 중점(월드) — 좌우 치우침 판단용
            public Vector3 edgeDir;       // 모서리 선 방향(월드, 정규화)
            public float halfExtent;      // 모서리 절반 길이(월드)
            public float topY;            // 모서리 높이(월드 Y)
        }

        /// <summary>이 발 위치·접근 방향에서 잡을 수 있는가. 가능하면 모서리·착지 정보를 낸다.</summary>
        public bool TryResolve(Vector3 feet, Vector3 approachDir, out GrabInfo grab)
        {
            grab = default;
            if (Box == null) return false;

            Transform t = transform;
            Vector3 lp = t.InverseTransformPoint(feet);
            Vector3 ld = t.InverseTransformDirection(approachDir);
            ld.y = 0f;
            if (ld.sqrMagnitude < 1e-6f) return false;
            ld.Normalize();

            Vector3 c = Box.center, e = Box.size * 0.5f;

            // 접근 중인 면 = 바깥 법선이 접근 방향과 가장 반대인 면(어느 정도는 마주봐야 인정).
            // 여기에 <b>"그 면 바깥에 서 있어야 한다"</b>를 더한다 — 없으면 상자에서 멀어지는 중에
            // 반대편(먼) 면이 잡혀 엉뚱하게 위로 끌려 올라간다.
            int best = -1, nearest = -1;
            float bestOpposed = 0.35f, bestSide = float.NegativeInfinity, maxSideAll = float.NegativeInfinity;

            for (int i = 0; i < 4; i++)
            {
                Vector3 nn = Normals[i];
                float side = Vector3.Dot(lp - c, nn) - Mathf.Abs(Vector3.Dot(e, nn));   // >0 = 그 면 바깥
                if (side > maxSideAll) maxSideAll = side;

                if ((climbableFaces & Flags[i]) == 0) continue;
                float opposed = -Vector3.Dot(ld, nn);                                   // 그 면을 마주보는 정도
                if (opposed <= 0.35f) continue;

                if (side >= -OutsideTolerance && opposed > bestOpposed) { bestOpposed = opposed; best = i; }
                if (side > bestSide) { bestSide = side; nearest = i; }
            }

            // 박스 footprint <b>안</b>에 서 있는 경우(콜라이더가 건물보다 크거나, 안쪽 발판 위)엔
            // 바깥인 면이 하나도 없다. 그때만 '가장 가까운 면'으로 떨어뜨린다 —
            // 밖에 서 있을 때는 이 완화를 쓰지 않으므로 위의 '먼 면' 오발동은 그대로 막힌다.
            if (best < 0 && maxSideAll < 0f) best = nearest;
            if (best < 0) return false;

            float topLocal = useColliderTop ? c.y + e.y : customHeight;
            Vector3 n = Normals[best];
            Vector3 along = new Vector3(Mathf.Abs(n.z), 0f, Mathf.Abs(n.x));   // 면을 따라가는 축

            // 모서리 선(로컬): 면 상단 가로선. 플레이어 투영 = 그 축으로만 클램프.
            Vector3 mid = new Vector3(c.x + n.x * e.x, topLocal, c.z + n.z * e.z);
            Vector3 half = Vector3.Scale(along, e);
            float axisPos = Vector3.Dot(lp - mid, along);
            float axisMax = Vector3.Dot(half, along);
            Vector3 centerLocal = mid + along * Mathf.Clamp(axisPos, -axisMax, axisMax);

            grab.edgeCenter = t.TransformPoint(centerLocal);
            grab.edgeMid = t.TransformPoint(mid);
            grab.edgeDir = t.TransformDirection(along).normalized;
            grab.halfExtent = t.TransformVector(along * axisMax).magnitude;
            grab.topY = grab.edgeCenter.y;

            grab.faceNormal = t.TransformDirection(n);
            grab.faceNormal.y = 0f;
            if (grab.faceNormal.sqrMagnitude < 1e-6f) return false;
            grab.faceNormal.Normalize();

            grab.landingFeet = grab.edgeCenter - grab.faceNormal * landingInset;

            // 부분 제한 볼륨 — 잡는 중심이 볼륨 안에 있어야 한다.
            if (grabRegions != null && grabRegions.Length > 0)
            {
                bool inside = false;
                for (int i = 0; i < grabRegions.Length; i++)
                {
                    var r = grabRegions[i];
                    if (r == null) continue;
                    if ((r.ClosestPoint(grab.edgeCenter) - grab.edgeCenter).sqrMagnitude < 0.05f * 0.05f)
                    { inside = true; break; }
                }
                if (!inside) return false;
            }
            return true;
        }

        // ── 기즈모: 잡는 선 + 착지 방향. 도달 불가 높이면 빨강. ──────────────

        void OnDrawGizmos()
        {
            if (Box == null) return;
            Transform t = transform;
            Vector3 c = Box.center, e = Box.size * 0.5f;
            float topY = useColliderTop ? c.y + e.y : customHeight;

            for (int i = 0; i < 4; i++)
            {
                if ((climbableFaces & Flags[i]) == 0) continue;

                Vector3 n = Normals[i];
                Vector3 along = new Vector3(Mathf.Abs(n.z), 0f, Mathf.Abs(n.x));
                Vector3 mid = new Vector3(c.x + n.x * e.x, topY, c.z + n.z * e.z);
                Vector3 half = Vector3.Scale(along, e);

                Vector3 a = t.TransformPoint(mid - half);
                Vector3 b = t.TransformPoint(mid + half);
                Vector3 m = (a + b) * 0.5f;

                Vector3 wn = t.TransformDirection(n); wn.y = 0f;
                bool hasNormal = wn.sqrMagnitude > 1e-6f;
                if (hasNormal) wn.Normalize();

                // ★ 오를 높이는 <b>이 면 앞에 섰을 때</b>의 높이다 — 오브젝트 자체 높이가 아니다.
                //   (5.81m 벽이라도 그 앞 바닥이 4m 지점이면 1.8m만 오르면 되는 정상 대상이다.)
                //   면 바깥에서 아래로 쏘아 서게 될 바닥을 찾고, 거기서 모서리까지를 잰다.
                float climbH = -1f;
                if (hasNormal)
                {
                    Vector3 stand = m + wn * (landingInset + 0.25f) + Vector3.up * 0.05f;
                    RaycastHit hit;
                    if (Physics.Raycast(stand, Vector3.down, out hit, 50f, ~0, QueryTriggerInteraction.Ignore))
                        climbH = m.y - hit.point.y;
                }

                bool known = climbH >= 0f;
                Color col = !known ? new Color(1f, 0.85f, 0.2f)          // 설 바닥을 못 찾음
                          : climbH <= gizmoMaxClimbHeight ? new Color(0.3f, 1f, 0.4f)
                                                          : new Color(1f, 0.25f, 0.2f);

                Gizmos.color = col;
                Gizmos.DrawLine(a, b);
                Gizmos.DrawLine(a + Vector3.up * 0.03f, b + Vector3.up * 0.03f);

                if (hasNormal)
                {
                    Gizmos.DrawLine(m, m - wn * landingInset);
                    Gizmos.DrawSphere(m - wn * landingInset, 0.06f);
                }

#if UNITY_EDITOR
                UnityEditor.Handles.color = col;
                UnityEditor.Handles.Label(m + Vector3.up * 0.12f,
                    known ? climbH.ToString("0.00") + "m" : "설 바닥 없음");
#endif
            }
        }
    }
}
