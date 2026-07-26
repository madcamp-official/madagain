using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 평면 메시 슬라이서. ★ combat 소유·독립. 순수 유틸(정적).
    /// 메시를 평면(점+법선, 메시 로컬 공간)으로 두 조각으로 가른다.
    /// 걸치는 삼각형은 교점에서 쪼개고, 단면(cross-section)을 채워 "잘린 속살"을 만든다.
    /// 출력 메시는 서브메시 2개: 0=겉면(shell), 1=단면(cap) → 각기 다른 머티리얼.
    /// 볼록 메시(캡슐 등)에서 단면이 단일 볼록 폴리곤이라 부채꼴 삼각화로 충분.
    /// </summary>
    public static class MeshSlicer
    {
        class Frag
        {
            public readonly List<Vector3> verts = new();
            public readonly List<Vector3> norms = new();
            // ★ UV를 반드시 들고 가야 한다. 없으면 시체가 텍스처의 한 점(0,0)만 샘플링해
            //   <b>단색 회색 덩어리</b>가 되고, 빨간 라인을 골라 빛내는 셰이더도 무력해진다.
            public readonly List<Vector2> uvs = new();
            public readonly List<int> shell = new();   // 서브메시0
            public readonly List<int> cap   = new();   // 서브메시1

            public int Add(Vector3 v, Vector3 n, Vector2 uv)
            { verts.Add(v); norms.Add(n); uvs.Add(uv); return verts.Count - 1; }
            public bool Empty => shell.Count == 0;

            public Mesh Build()
            {
                var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                m.SetVertices(verts);
                m.SetNormals(norms);
                m.SetUVs(0, uvs);
                m.subMeshCount = 2;
                m.SetTriangles(shell, 0);
                m.SetTriangles(cap, 1);
                m.RecalculateBounds();
                m.RecalculateTangents();   // 노멀맵이 뒤집히지 않게
                return m;
            }
        }

        /// <summary>메시를 로컬 평면으로 슬라이스. 실제로 갈리면 true.</summary>
        public static bool Slice(Mesh src, Vector3 planePoint, Vector3 planeNormal,
                                 out Mesh above, out Mesh below)
        {
            above = below = null;
            if (src == null) return false;
            planeNormal = planeNormal.normalized;

            Vector3[] sv = src.vertices;
            Vector3[] sn = src.normals;
            Vector2[] su = src.uv;
            int[] tri = src.triangles;
            bool hasN = sn != null && sn.Length == sv.Length;
            bool hasU = su != null && su.Length == sv.Length;

            var A = new Frag();   // planeNormal 쪽(+)
            var B = new Frag();   // 반대쪽(-)
            // 교점을 점이 아니라 <b>선분</b>으로 모은다 — 나중에 닫힌 고리로 엮어야 하기 때문
            var cut = new List<(Vector3 a, Vector3 b)>();

            for (int t = 0; t < tri.Length; t += 3)
            {
                int i0 = tri[t], i1 = tri[t + 1], i2 = tri[t + 2];
                ClipTriangle(sv[i0], sv[i1], sv[i2],
                             hasN ? sn[i0] : Vector3.up, hasN ? sn[i1] : Vector3.up, hasN ? sn[i2] : Vector3.up,
                             hasU ? su[i0] : Vector2.zero, hasU ? su[i1] : Vector2.zero, hasU ? su[i2] : Vector2.zero,
                             planePoint, planeNormal, A, B, cut);
            }

            BuildCaps(cut, planeNormal, A, B);

            if (A.Empty || B.Empty) return false;   // 평면이 메시를 실제로 안 가름
            above = A.Build();
            below = B.Build();
            return true;
        }

        static float SD(Vector3 v, Vector3 p, Vector3 n) => Vector3.Dot(v - p, n);

        /// <summary>삼각형을 평면으로 클립 → A/B 각각의 폴리곤을 부채꼴 삼각화. 교점은 cut에 수집.</summary>
        static void ClipTriangle(Vector3 a, Vector3 b, Vector3 c,
                                 Vector3 na, Vector3 nb, Vector3 nc,
                                 Vector2 ua, Vector2 ub, Vector2 uc,
                                 Vector3 p, Vector3 n,
                                 Frag A, Frag B, List<(Vector3 a, Vector3 b)> cut)
        {
            Vector3[] vs = { a, b, c };
            Vector3[] ns = { na, nb, nc };
            Vector2[] us = { ua, ub, uc };
            float[] d = { SD(a, p, n), SD(b, p, n), SD(c, p, n) };

            var aPoly = new List<(Vector3 v, Vector3 nn, Vector2 uv)>(4);
            var bPoly = new List<(Vector3 v, Vector3 nn, Vector2 uv)>(4);

            // 평면을 가로지르는 삼각형은 교점이 정확히 2개 — 그 둘이 단면의 한 변이 된다
            Vector3 ip0 = Vector3.zero, ip1 = Vector3.zero;
            int hits = 0;

            for (int i = 0; i < 3; i++)
            {
                int j = (i + 1) % 3;
                float di = d[i], dj = d[j];
                if (di >= 0f) aPoly.Add((vs[i], ns[i], us[i])); else bPoly.Add((vs[i], ns[i], us[i]));

                if ((di >= 0f) != (dj >= 0f))   // 에지가 평면을 가로지름
                {
                    float tt = di / (di - dj);
                    Vector3 ip = Vector3.Lerp(vs[i], vs[j], tt);
                    Vector3 inn = Vector3.Lerp(ns[i], ns[j], tt).normalized;
                    Vector2 iuv = Vector2.Lerp(us[i], us[j], tt);   // 교점 UV도 보간
                    aPoly.Add((ip, inn, iuv));
                    bPoly.Add((ip, inn, iuv));
                    if (hits == 0) ip0 = ip; else if (hits == 1) ip1 = ip;
                    hits++;
                }
            }
            if (hits == 2 && (ip0 - ip1).sqrMagnitude > 1e-10f) cut.Add((ip0, ip1));

            FanShell(aPoly, A);
            FanShell(bPoly, B);
        }

        static void FanShell(List<(Vector3 v, Vector3 nn, Vector2 uv)> poly, Frag f)
        {
            if (poly.Count < 3) return;
            int i0 = f.Add(poly[0].v, poly[0].nn, poly[0].uv);
            for (int k = 1; k < poly.Count - 1; k++)
            {
                int ia = f.Add(poly[k].v, poly[k].nn, poly[k].uv);
                int ib = f.Add(poly[k + 1].v, poly[k + 1].nn, poly[k + 1].uv);
                f.shell.Add(i0); f.shell.Add(ia); f.shell.Add(ib);
            }
        }

        /// <summary>
        /// 단면 캡.
        ///
        /// ★ 예전엔 교점을 전부 모아 <b>하나의 중심에서 부채꼴</b>로 이었다. 캡슐처럼 단면이
        ///   볼록한 고리 하나일 땐 맞지만, 사람형 로봇은 한 평면에 <b>여러 개의 고리</b>(몸통·양팔 등)가
        ///   생기고 각 고리도 오목하다. 그걸 한 중심으로 이으면 삼각형이 서로 가로질러
        ///   <b>별(바람개비) 모양</b>이 된다 — 바로 그 증상이다.
        ///
        /// 그래서 교점을 <b>선분</b>으로 모아 닫힌 고리로 엮은 뒤, 고리마다 따로 삼각화한다.
        /// 오목해도 되도록 귀 자르기(ear clipping)를 쓴다.
        /// </summary>
        static void BuildCaps(List<(Vector3 a, Vector3 b)> segs, Vector3 n, Frag A, Frag B)
        {
            if (segs.Count < 3) return;

            // 평면 기저 — 2D로 눕혀야 고리 연결·삼각화가 쉽다
            Vector3 u = Vector3.Cross(n, Vector3.up);
            if (u.sqrMagnitude < 1e-4f) u = Vector3.Cross(n, Vector3.right);
            u.Normalize();
            Vector3 v = Vector3.Cross(n, u);

            var loops = ChainLoops(segs);
            foreach (var loop in loops)
            {
                if (loop.Count < 3) continue;
                var tri = EarClip(loop, u, v);
                if (tri.Count == 0) continue;
                AddCapLoop(B, loop, tri,  n);   // -측 조각의 단면은 +n 향함
                AddCapLoop(A, loop, tri, -n);   // +측 조각의 단면은 -n 향함
            }
        }

        /// <summary>선분들을 끝점 맞물림으로 이어 닫힌 고리 여러 개를 만든다.</summary>
        static List<List<Vector3>> ChainLoops(List<(Vector3 a, Vector3 b)> segs)
        {
            const float Eps = 1e-4f;
            var used = new bool[segs.Count];
            var loops = new List<List<Vector3>>();

            for (int s = 0; s < segs.Count; s++)
            {
                if (used[s]) continue;
                used[s] = true;
                var loop = new List<Vector3> { segs[s].a, segs[s].b };
                Vector3 tail = segs[s].b, head = segs[s].a;

                // 꼬리에 이어 붙일 선분을 계속 찾는다
                bool grew = true;
                while (grew)
                {
                    grew = false;
                    for (int i = 0; i < segs.Count; i++)
                    {
                        if (used[i]) continue;
                        if ((segs[i].a - tail).sqrMagnitude < Eps) { tail = segs[i].b; loop.Add(tail); used[i] = true; grew = true; }
                        else if ((segs[i].b - tail).sqrMagnitude < Eps) { tail = segs[i].a; loop.Add(tail); used[i] = true; grew = true; }
                        if (grew) break;
                    }
                    // 고리가 닫히면 끝
                    if ((tail - head).sqrMagnitude < Eps) { loop.RemoveAt(loop.Count - 1); break; }
                }
                if (loop.Count >= 3) loops.Add(loop);
            }
            return loops;
        }

        /// <summary>평면에 눕힌 뒤 귀 자르기로 삼각화. 오목 다각형도 처리된다.</summary>
        static List<int> EarClip(List<Vector3> loop, Vector3 u, Vector3 v)
        {
            int n = loop.Count;
            var p = new Vector2[n];
            for (int i = 0; i < n; i++) p[i] = new Vector2(Vector3.Dot(loop[i], u), Vector3.Dot(loop[i], v));

            // 면적 부호로 감김 방향을 맞춘다(반시계로 통일)
            float area2 = 0f;
            for (int i = 0; i < n; i++) { int j = (i + 1) % n; area2 += p[i].x * p[j].y - p[j].x * p[i].y; }
            var idx = new List<int>(n);
            if (area2 < 0f) for (int i = n - 1; i >= 0; i--) idx.Add(i);
            else            for (int i = 0; i < n; i++)      idx.Add(i);

            var tris = new List<int>();
            int guard = 0;
            while (idx.Count > 3 && guard++ < n * n)
            {
                bool clipped = false;
                for (int k = 0; k < idx.Count; k++)
                {
                    int i0 = idx[(k - 1 + idx.Count) % idx.Count], i1 = idx[k], i2 = idx[(k + 1) % idx.Count];
                    Vector2 a = p[i0], b = p[i1], c = p[i2];
                    // 볼록한 꼭짓점인가
                    if ((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x) <= 0f) continue;
                    // 그 삼각형 안에 다른 점이 들어오면 귀가 아니다
                    bool bad = false;
                    foreach (int m in idx)
                    {
                        if (m == i0 || m == i1 || m == i2) continue;
                        if (InTri(p[m], a, b, c)) { bad = true; break; }
                    }
                    if (bad) continue;
                    tris.Add(i0); tris.Add(i1); tris.Add(i2);
                    idx.RemoveAt(k);
                    clipped = true;
                    break;
                }
                if (!clipped) break;   // 자기교차 등 — 남은 건 아래에서 부채꼴로 마감
            }
            if (idx.Count == 3) { tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]); }
            else if (idx.Count > 3)
                for (int k = 1; k < idx.Count - 1; k++) { tris.Add(idx[0]); tris.Add(idx[k]); tris.Add(idx[k + 1]); }
            return tris;
        }

        static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool pos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(neg && pos);
        }

        static void AddCapLoop(Frag f, List<Vector3> loop, List<int> tris, Vector3 capN)
        {
            // 단면은 별도 서브메시(1번)에 별도 머티리얼을 쓰므로 UV는 의미 없다.
            int baseIdx = f.verts.Count;
            for (int i = 0; i < loop.Count; i++) f.Add(loop[i], capN, Vector2.zero);
            for (int i = 0; i < tris.Count; i++) f.cap.Add(baseIdx + tris[i]);
        }
    }
}
