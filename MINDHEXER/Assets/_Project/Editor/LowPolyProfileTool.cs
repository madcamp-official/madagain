using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// 실루엣 압출 저폴리화 — 원본 메시에서 <b>단면을 떠서</b> 저폴리 통짜 메시를 다시 세운다.
    ///
    /// <para><b>왜 감축(decimation)이 아니라 이것인가</b>: Tripo 같은 AI 생성 모델은 표면이
    /// 이어져 있지 않고 <b>서로 떨어진 껍데기 수천 개</b>로 되어 있다(레일 = 12,430정점에
    /// 연결 요소 1,987개). QEM 감축은 이어진 표면에서 모서리를 접는 방식이라 접을 대상이 없어
    /// 금방 바닥을 친다 — 실측으로 12,430 → 5,408에서 더 안 내려갔고, 품질을 0.025까지 낮춰도
    /// 같은 값이었다. 그래서 감축이 아니라 <b>실루엣만 다시 뽑는</b> 방식을 쓴다.</para>
    ///
    /// <para><b>절차</b>
    /// <list type="number">
    ///  <item>정점을 긴 축을 따라 얇은 슬랩으로 나눈다.</item>
    ///  <item>슬랩마다 정점을 단면 평면에 투영하고, 고정 각도 P방향으로 <b>가장 바깥 정점</b>을
    ///        골라 링을 만든다(볼록 껍질을 P각형으로 근사. 실제 표면 위의 점이라 부풀지 않는다).</item>
    ///  <item>링 후보 중 <b>형태 변화가 큰 곳</b>부터 골라 ringCount개만 남긴다(양 끝은 항상 포함).</item>
    ///  <item>링을 이어 옆면을 만들고 양 끝에 캡을 붙인다.</item>
    /// </list></para>
    ///
    /// <para><b>한계</b>: 볼록 근사라 <b>오목한 홈·리벳·패널 이음새는 사라진다.</b> 남는 것은
    /// 바깥 실루엣뿐이다. 표면 무늬로 승부하는 모델에는 맞지 않는다.</para>
    ///
    /// <para>정점 수 = <c>ringCount × pointsPerRing + 2</c>(캡 중심). 두 값이 곧 예산이다.</para>
    /// </summary>
    public static class LowPolyProfileTool
    {
        /// <summary>슬랩 후보 개수. 링은 여기서 골라 뽑으므로 ringCount보다 넉넉해야 한다.</summary>
        const int SlabSamples = 96;

        /// <param name="axis">긴 축(0=X,1=Y,2=Z). 음수면 바운즈에서 자동 판정.</param>
        public static Mesh Build(Mesh src, int ringCount, int pointsPerRing, int axis, out string report)
        {
            return Build(src, ringCount, pointsPerRing, axis, 1f, out report);
        }

        /// <param name="outlierPercentile">
        /// 방향마다 <b>가장 바깥 정점</b> 대신 이 백분위의 정점을 집는다(0~1, 1이면 최댓값 = 자르지 않음).
        /// 원본에 툭 튀어나온 정점이 하나라도 있으면 그게 링 꼭짓점이 되어 <b>뾰족한 가시</b>로 남는데,
        /// 0.95~0.99로 두면 그런 이상치가 잘린다. 낮출수록 실루엣이 안쪽으로 깎인다.
        /// </param>
        public static Mesh Build(Mesh src, int ringCount, int pointsPerRing, int axis,
                                 float outlierPercentile, out string report)
        {
            return Build(src, ringCount, pointsPerRing, axis, outlierPercentile, 0.005f, out report);
        }

        /// <param name="axisTrim">
        /// 긴 축 방향으로 <b>양 끝에서 잘라낼 정점 비율</b>(0~0.2). 0.005면 위아래 0.5%씩.
        ///
        /// <para>AI 생성 모델은 본체에서 <b>뚝 떨어진 부스러기</b>를 달고 있는 경우가 많다.
        /// 레일이 그랬다 — 본체가 끝난 뒤 빈 구간이 이어지다가 점 6개짜리 조각이 두 군데 있었고,
        /// 마지막 슬랩을 링으로 쓰는 바람에 본체에서 거기까지 <b>길게 이어 붙인 가시</b>가 생겼다.</para>
        ///
        /// <para>"점이 많은 연속 구간"으로 본체를 찾는 방법도 써 봤는데, 중간에 점이 적은 슬랩
        /// 하나가 구간을 끊어 본체 절반이 날아갔다(길이 0.81 → 0.50). 그래서 <b>연속성이 아니라
        /// 정점 분포의 백분위</b>로 양 끝만 다듬는다 — 가운데가 성겨도 잘리지 않는다.</para>
        /// </param>
        public static Mesh Build(Mesh src, int ringCount, int pointsPerRing, int axis,
                                 float outlierPercentile, float axisTrim, out string report)
        {
            report = "";
            if (src == null) { report = "원본 메시가 없습니다."; return null; }

            ringCount = Mathf.Max(2, ringCount);
            pointsPerRing = Mathf.Max(3, pointsPerRing);

            Vector3[] verts = src.vertices;
            if (verts.Length < 4) { report = "정점이 너무 적습니다."; return null; }

            if (axis < 0 || axis > 2)
            {
                Vector3 sz = src.bounds.size;
                axis = (sz.x >= sz.y && sz.x >= sz.z) ? 0 : (sz.y >= sz.z ? 1 : 2);
            }
            int u = (axis + 1) % 3, v = (axis + 2) % 3;   // 단면 평면의 두 축 (오른손 순서 유지)

            // 양 끝의 떨어진 부스러기를 백분위로 다듬는다 (0이면 원본 그대로).
            axisTrim = Mathf.Clamp(axisTrim, 0f, 0.2f);
            var axisAll = new float[verts.Length];
            for (int i = 0; i < verts.Length; i++) axisAll[i] = verts[i][axis];
            System.Array.Sort(axisAll);
            int loI = Mathf.Clamp(Mathf.FloorToInt(axisTrim * (axisAll.Length - 1)), 0, axisAll.Length - 1);
            int hiI = Mathf.Clamp(axisAll.Length - 1 - loI, 0, axisAll.Length - 1);
            float minA = axisAll[loI], maxA = axisAll[hiI];
            if (maxA - minA < 1e-6f) { report = "긴 축 방향으로 두께가 없습니다."; return null; }

            // ── 1) 슬랩별 링 후보 ──────────────────────────────────────────
            var slabPts = new List<Vector2>[SlabSamples];
            for (int i = 0; i < SlabSamples; i++) slabPts[i] = new List<Vector2>();

            float span = maxA - minA;
            int dropped = 0;
            for (int i = 0; i < verts.Length; i++)
            {
                float a = verts[i][axis];
                if (a < minA || a > maxA) { dropped++; continue; }   // 다듬어 낸 바깥쪽 부스러기
                int s = Mathf.Clamp(Mathf.FloorToInt((a - minA) / span * SlabSamples), 0, SlabSamples - 1);
                slabPts[s].Add(new Vector2(verts[i][u], verts[i][v]));
            }

            // 점이 너무 적은 슬랩은 링으로 쓰지 않는다 — 방향마다 같은 정점이 뽑혀 바늘이 된다.
            var nonEmpty = new List<int>();
            for (int s = 0; s < SlabSamples; s++) if (slabPts[s].Count > 0) nonEmpty.Add(slabPts[s].Count);
            if (nonEmpty.Count == 0) { report = "정점이 슬랩에 하나도 안 들어갔습니다."; return null; }
            nonEmpty.Sort();
            int minPts = Mathf.Max(3, Mathf.RoundToInt(nonEmpty[nonEmpty.Count / 2] * 0.1f));

            var candA = new List<float>();              // 각 후보 링의 축 좌표
            var candRing = new List<Vector2[]>();       // 각 후보 링의 P개 점
            for (int s = 0; s < SlabSamples; s++)
            {
                if (slabPts[s].Count < minPts) continue;
                candA.Add(minA + (s + 0.5f) / SlabSamples * span);
                candRing.Add(SupportRing(slabPts[s], pointsPerRing, outlierPercentile));
            }
            if (candRing.Count < 2) { report = "유효한 단면이 2개 미만입니다."; return null; }

            string trimNote = dropped > 0
                ? string.Format(" / 양 끝 다듬어 낸 정점 {0}, 축 범위 {1:0.000}~{2:0.000}", dropped, minA, maxA)
                : "";

            // ── 2) 형태 변화가 큰 곳부터 링 선택 ───────────────────────────
            List<int> keep = PickRings(candRing, Mathf.Min(ringCount, candRing.Count));

            // ── 3) 링을 이어 메시로 ────────────────────────────────────────
            int R = keep.Count, P = pointsPerRing;
            var outV = new List<Vector3>(R * P + 2);
            var outUV = new List<Vector2>(R * P + 2);

            for (int r = 0; r < R; r++)
            {
                int ci = keep[r];
                float a = candA[ci];
                float vt = (a - minA) / span;
                for (int j = 0; j < P; j++)
                {
                    Vector2 p = candRing[ci][j];
                    Vector3 w = Vector3.zero;
                    w[axis] = a; w[u] = p.x; w[v] = p.y;
                    outV.Add(w);
                    outUV.Add(new Vector2((float)j / P, vt));
                }
            }

            var tris = new List<int>((R - 1) * P * 6 + P * 6);
            for (int r = 0; r < R - 1; r++)
                for (int j = 0; j < P; j++)
                {
                    int j2 = (j + 1) % P;
                    int a0 = r * P + j, b0 = r * P + j2, c0 = (r + 1) * P + j, d0 = (r + 1) * P + j2;
                    tris.Add(a0); tris.Add(c0); tris.Add(b0);
                    tris.Add(b0); tris.Add(c0); tris.Add(d0);
                }

            // 캡 — 각 끝 링의 중심에서 부채꼴
            int capStart = AddCapCenter(outV, outUV, candRing[keep[0]], candA[keep[0]], axis, u, v, 0f);
            for (int j = 0; j < P; j++)
            {
                int j2 = (j + 1) % P;
                tris.Add(capStart); tris.Add(j); tris.Add(j2);
            }
            int lastBase = (R - 1) * P;
            int capEnd = AddCapCenter(outV, outUV, candRing[keep[R - 1]], candA[keep[R - 1]], axis, u, v, 1f);
            for (int j = 0; j < P; j++)
            {
                int j2 = (j + 1) % P;
                tris.Add(capEnd); tris.Add(lastBase + j2); tris.Add(lastBase + j);
            }

            var mesh = new Mesh();
            mesh.name = src.name + "_LowPoly";
            mesh.SetVertices(outV);
            mesh.SetUVs(0, outUV);
            mesh.SetTriangles(tris, 0);

            // 감는 방향이 뒤집혔으면 안이 보인다 — 부호 있는 부피로 판정해 자동 교정한다.
            if (SignedVolume(outV, tris) < 0f) FlipWinding(mesh, tris);

            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            report = string.Format(
                "축 {0} / 링 {1}(후보 {2}) × 점 {3} → 정점 {4} / 삼각형 {5}   원본 정점 {6} 대비 {7:0.0}%{8}",
                "XYZ"[axis], R, candRing.Count, P, mesh.vertexCount, tris.Count / 3,
                verts.Length, 100f * mesh.vertexCount / verts.Length, trimNote);
            return mesh;
        }

        /// <summary>
        /// 고정 각도마다 바깥쪽 실제 정점을 골라 P각형 링을 만든다.
        /// 지지선(supporting line) 대신 실제 점을 쓰므로 원본보다 부풀지 않는다.
        ///
        /// <para><paramref name="pct"/>가 1보다 작으면 그 방향 투영값의 백분위로 잘라 <b>이상치를
        /// 버린다</b> — 정점 하나가 툭 튀어나와 있으면 그게 꼭짓점이 되어 가시로 남기 때문이다.
        /// 자른 뒤에도 "그 이하 중 가장 먼 실제 정점"을 쓰므로 점은 여전히 표면 위에 있다.</para>
        /// </summary>
        static Vector2[] SupportRing(List<Vector2> pts, int P, float pct)
        {
            Vector2 c = Vector2.zero;
            for (int i = 0; i < pts.Count; i++) c += pts[i];
            c /= pts.Count;

            var ring = new Vector2[P];
            var proj = new float[pts.Count];
            var sorted = new float[pts.Count];

            for (int j = 0; j < P; j++)
            {
                float th = 2f * Mathf.PI * j / P;
                var dir = new Vector2(Mathf.Cos(th), Mathf.Sin(th));

                for (int i = 0; i < pts.Count; i++) proj[i] = Vector2.Dot(pts[i] - c, dir);

                float limit = float.MaxValue;
                if (pct < 1f)
                {
                    System.Array.Copy(proj, sorted, proj.Length);
                    System.Array.Sort(sorted);
                    limit = sorted[Mathf.Clamp(Mathf.FloorToInt(pct * (sorted.Length - 1)), 0, sorted.Length - 1)];
                }

                float best = float.MinValue;
                Vector2 bestP = c + dir * 1e-4f;   // 후보가 하나도 없을 때의 안전값
                for (int i = 0; i < pts.Count; i++)
                {
                    if (proj[i] > limit || proj[i] <= best) continue;
                    best = proj[i]; bestP = pts[i];
                }
                ring[j] = bestP;
            }
            return ring;
        }

        /// <summary>
        /// 양 끝을 먼저 잡고, 이웃 링을 선형 보간했을 때 <b>가장 크게 벗어나는</b> 링부터
        /// 하나씩 추가한다. 같은 예산으로 형태가 바뀌는 곳에 링을 몰아 준다.
        /// </summary>
        static List<int> PickRings(List<Vector2[]> cand, int want)
        {
            var keep = new List<int> { 0, cand.Count - 1 };
            while (keep.Count < want)
            {
                int bestIdx = -1; float bestErr = -1f;
                for (int k = 0; k < keep.Count - 1; k++)
                {
                    int lo = keep[k], hi = keep[k + 1];
                    for (int i = lo + 1; i < hi; i++)
                    {
                        float t = (float)(i - lo) / (hi - lo);
                        float err = 0f;
                        for (int j = 0; j < cand[i].Length; j++)
                        {
                            Vector2 lerp = Vector2.Lerp(cand[lo][j], cand[hi][j], t);
                            float d = (cand[i][j] - lerp).sqrMagnitude;
                            if (d > err) err = d;
                        }
                        if (err > bestErr) { bestErr = err; bestIdx = i; }
                    }
                }
                if (bestIdx < 0) break;              // 더 넣을 자리가 없다
                keep.Add(bestIdx);
                keep.Sort();
            }
            return keep;
        }

        static int AddCapCenter(List<Vector3> outV, List<Vector2> outUV, Vector2[] ring,
                                float a, int axis, int u, int v, float vt)
        {
            Vector2 c = Vector2.zero;
            for (int i = 0; i < ring.Length; i++) c += ring[i];
            c /= ring.Length;

            Vector3 w = Vector3.zero;
            w[axis] = a; w[u] = c.x; w[v] = c.y;
            outV.Add(w);
            outUV.Add(new Vector2(0.5f, vt));
            return outV.Count - 1;
        }

        /// <summary>발산 정리로 부호 있는 부피. 음수면 삼각형이 안쪽을 향하고 있다.</summary>
        static float SignedVolume(List<Vector3> v, List<int> t)
        {
            float sum = 0f;
            for (int i = 0; i < t.Count; i += 3)
                sum += Vector3.Dot(Vector3.Cross(v[t[i]], v[t[i + 1]]), v[t[i + 2]]);
            return sum / 6f;
        }

        static void FlipWinding(Mesh mesh, List<int> t)
        {
            for (int i = 0; i < t.Count; i += 3) { int tmp = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = tmp; }
            mesh.SetTriangles(t, 0);
        }

        // ── 메뉴 ──────────────────────────────────────────────────────────

        [MenuItem("Tools/최적화/선택 메시를 실루엣 저폴리로 (링8 × 점12)")]
        static void BuildFromSelection()
        {
            var src = Selection.activeObject as Mesh;
            if (src == null) { Debug.LogWarning("[LowPoly] 메시 에셋을 선택하십시오."); return; }

            string report;
            Mesh m = Build(src, 8, 12, -1, out report);
            if (m == null) { Debug.LogWarning("[LowPoly] " + report); return; }

            string dir = "Assets/_Project/Art/Models/Generated";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/_Project/Art/Models", "Generated");
            AssetDatabase.CreateAsset(m, dir + "/" + m.name + ".asset");
            AssetDatabase.SaveAssets();
            Debug.Log("[LowPoly] " + report);
            Selection.activeObject = m;
        }
    }
}
