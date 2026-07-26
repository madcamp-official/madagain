using System.Collections.Generic;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// 스킨 메시를 "제거할 본에 물린 부분"과 "나머지"로 가른다.
    ///
    /// 본 스케일을 0으로 줄이는 흔한 편법은 쓰지 않는다 — 어깨 근처 정점은 어깨와 척추에
    /// 반반씩 물려 있어서, 절반만 끌려가 <b>뾰족한 스파이크나 움푹 팬 자국</b>이 남는다.
    /// 삼각형을 통째로 걷어내야 절단면이 깨끗하다.
    ///
    /// 두 결과는 정확히 상보적이라, 걷어낸 조각이 곧 전선에 매달 부위가 된다.
    /// 에디터에서 미리 굽기 때문에 런타임 읽기 권한(isReadable)이 필요 없다.
    /// </summary>
    public static class MeshSplitter
    {
        /// <summary>
        /// 삼각형 하나가 "제거 대상"인지 판정하는 기준.
        /// 세 정점의 제거본 가중치 평균이 이 값을 넘으면 걷어낸다.
        /// 낮추면 더 많이 잘리고(어깨까지), 높이면 덜 잘린다(손끝만).
        /// </summary>
        public const float DefaultThreshold = 0.5f;

        public class Result
        {
            public Mesh kept;        // 몸에 남는 부분
            public Mesh removed;     // 떨어져 나간 부위(전선 끝에 매달림)
            public Vector3 cutCenterLocal;   // 절단면 중심(캡을 덧대는 위치)
            public float   cutRadius;        // 절단면 반경(캡 크기)
            public int keptTris, removedTris;
        }

        /// <summary>
        /// removeBoneIndices에 지배적으로 물린 삼각형을 걷어낸다.
        /// mesh는 반드시 읽기 가능해야 한다(에디터에서만 호출하므로 문제없다).
        /// </summary>
        public static Result Split(Mesh mesh, HashSet<int> removeBoneIndices, float threshold = DefaultThreshold)
        {
            var res = new Result();
            var weights = mesh.boneWeights;
            var verts   = mesh.vertices;
            if (weights == null || weights.Length == 0) return null;

            // ── 정점별 "제거본 가중치 합" ──
            var vw = new float[verts.Length];
            for (int i = 0; i < weights.Length; i++)
            {
                var w = weights[i];
                float s = 0f;
                if (removeBoneIndices.Contains(w.boneIndex0)) s += w.weight0;
                if (removeBoneIndices.Contains(w.boneIndex1)) s += w.weight1;
                if (removeBoneIndices.Contains(w.boneIndex2)) s += w.weight2;
                if (removeBoneIndices.Contains(w.boneIndex3)) s += w.weight3;
                vw[i] = s;
            }

            // ── 삼각형 분류 ──
            var keepTris   = new List<int>();
            var removeTris = new List<int>();
            var cutVerts   = new List<Vector3>();   // 경계에 걸친 정점 = 절단면

            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var tris = mesh.GetTriangles(sub);
                for (int t = 0; t < tris.Length; t += 3)
                {
                    int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                    float avg = (vw[a] + vw[b] + vw[c]) / 3f;
                    if (avg >= threshold)
                    {
                        removeTris.Add(a); removeTris.Add(b); removeTris.Add(c);
                        // 셋 중 하나라도 경계 근처면 절단면 후보
                        if (vw[a] < 0.85f) cutVerts.Add(verts[a]);
                        if (vw[b] < 0.85f) cutVerts.Add(verts[b]);
                        if (vw[c] < 0.85f) cutVerts.Add(verts[c]);
                    }
                    else
                    {
                        keepTris.Add(a); keepTris.Add(b); keepTris.Add(c);
                    }
                }
            }

            res.keptTris = keepTris.Count / 3;
            res.removedTris = removeTris.Count / 3;
            if (res.removedTris == 0) return null;   // 걷어낼 게 없으면 실패

            // ── 절단면 중심·반경 (캡을 덧댈 위치 / 조각의 새 원점) ──
            if (cutVerts.Count > 0)
            {
                Vector3 sum = Vector3.zero;
                foreach (var v in cutVerts) sum += v;
                res.cutCenterLocal = sum / cutVerts.Count;
                float maxD = 0f;
                foreach (var v in cutVerts) maxD = Mathf.Max(maxD, Vector3.Distance(v, res.cutCenterLocal));
                res.cutRadius = Mathf.Max(0.01f, maxD);
            }

            res.kept = BuildSub(mesh, keepTris, "_kept", Vector3.zero);
            // ★ 조각은 원점을 <b>절단면</b>으로 옮긴다.
            //   그대로 두면 정점이 "몸 원점 기준 어깨 높이"에 있어서, 전선 끝에 갖다 놓는 순간
            //   그 높이만큼 통째로 밀려나 팔이 엉뚱한 데 붕 뜬다.
            res.removed = BuildSub(mesh, removeTris, "_part", res.cutCenterLocal);
            return res;
        }

        /// <summary>지정 삼각형만 남긴 새 메시. 쓰이는 정점만 추려 인덱스를 다시 매긴다.
        /// origin이 0이 아니면 그만큼 빼서 원점을 옮긴다(조각을 전선 끝에 매달기 위함).</summary>
        static Mesh BuildSub(Mesh src, List<int> tris, string suffix, Vector3 origin)
        {
            var map = new Dictionary<int, int>();
            var order = new List<int>();
            foreach (int i in tris)
                if (!map.ContainsKey(i)) { map[i] = order.Count; order.Add(i); }

            var sv = src.vertices; var sn = src.normals; var st = src.tangents;
            var su = src.uv;       var sw = src.boneWeights;
            bool hasN = sn != null && sn.Length == sv.Length;
            bool hasT = st != null && st.Length == sv.Length;
            bool hasU = su != null && su.Length == sv.Length;
            bool hasW = sw != null && sw.Length == sv.Length;

            int n = order.Count;
            var verts = new Vector3[n];
            var norms = hasN ? new Vector3[n] : null;
            var tans  = hasT ? new Vector4[n] : null;
            var uvs   = hasU ? new Vector2[n] : null;
            var bws   = hasW ? new BoneWeight[n] : null;

            for (int k = 0; k < n; k++)
            {
                int s = order[k];
                verts[k] = sv[s] - origin;
                if (hasN) norms[k] = sn[s];
                if (hasT) tans[k]  = st[s];
                if (hasU) uvs[k]   = su[s];
                if (hasW) bws[k]   = sw[s];
            }

            var idx = new int[tris.Count];
            for (int k = 0; k < tris.Count; k++) idx[k] = map[tris[k]];

            var m = new Mesh { name = src.name + suffix };
            if (n > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.vertices = verts;
            if (hasN) m.normals = norms;
            if (hasT) m.tangents = tans;
            if (hasU) m.uv = uvs;
            if (hasW) { m.boneWeights = bws; m.bindposes = src.bindposes; }
            m.triangles = idx;
            m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// 절단면을 막는 캡(원반). 뚫린 구멍으로 몸 안쪽 뒷면이 보이는 걸 막는다.
        /// 로봇 장갑 색(회색)으로 칠해 "잘린 단면"처럼 보이게 한다.
        /// </summary>
        public static Mesh BuildCap(Vector3 center, float radius, Vector3 normal, int segments = 12)
        {
            var verts = new Vector3[segments + 1];
            var norms = new Vector3[segments + 1];
            var tris  = new int[segments * 3];

            Vector3 n = normal.sqrMagnitude < 1e-6f ? Vector3.up : normal.normalized;
            Vector3 refv = Mathf.Abs(Vector3.Dot(n, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
            Vector3 t1 = Vector3.Normalize(Vector3.Cross(n, refv));
            Vector3 t2 = Vector3.Cross(n, t1);

            verts[0] = center; norms[0] = n;
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                verts[i + 1] = center + (t1 * Mathf.Cos(a) + t2 * Mathf.Sin(a)) * radius;
                norms[i + 1] = n;
                tris[i * 3] = 0;
                tris[i * 3 + 1] = 1 + i;
                tris[i * 3 + 2] = 1 + (i + 1) % segments;
            }

            var m = new Mesh { name = "CutCap" };
            m.vertices = verts; m.normals = norms; m.triangles = tris;
            m.RecalculateBounds();
            return m;
        }
    }
}
