using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Game.Sim;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// 층이동 마커 자동 배치 창. 씬 지형을 훑어 도약할 만한 곳을 찾아 마커를 만든다.
    ///
    /// NavMesh를 쓰지 않고 <b>콜라이더 레이캐스트</b>로 지형을 읽는다 —
    /// 이 프로젝트의 NavMesh는 런타임 생성이라 에디터에는 없기 때문이다.
    ///
    /// 두 종류를 찾는다:
    ///  · <b>낙하형</b> — 위에서 아래로 뛰어내리는 곳(가장자리 → 아래 착지점)
    ///  · <b>간극형</b> — <b>거의 같은 높이인데 사이가 끊긴 곳</b>(구덩이·틈 건너뛰기)
    ///
    /// 값은 전부 창에서 조절하고 "분석"으로 개수만 먼저 확인할 수 있다(배치 전에 감을 잡으라고).
    /// </summary>
    public class TraversalAutoPlacer : EditorWindow
    {
        // ── 지형 훑기 ──
        float gridStep = 1.0f;        // 다리처럼 좁은 발판을 놓치지 않으려면 촘촘해야 한다

        // ── 낙하형 ──
        float minDrop = 0.8f;
        float maxDrop = 16f;

        // ── 간극형(같은 높이 틈) ──
        bool  enableGap     = true;
        float sameLevelTol  = 1.5f;   // 높이차가 이 이내면 "같은 높이"로 보고 간극형으로 판정
        float gapDepthMin   = 1.5f;   // 두 점 사이 바닥이 이만큼 아래거나 없으면 "틈"

        // ── 공통 ──
        float minHoriz = 1.5f;
        float maxHoriz = 11f;
        float edgeProbe   = 0.9f;     // 가장자리 판정: 이만큼 나가서
        float edgeDropMin = 0.5f;     // 이만큼 꺼지면 가장자리
        float minLinkSpacing = 2f;
        int   maxLinks = 250;

        // ── 다양성(비슷한 링크 난립 방지) ──
        float clusterSize   = 4.5f;   // 이 반경 안의 출발점들은 "같은 발판"으로 묶는다
        int   maxPerCluster = 2;      // 한 발판에서 뽑을 최대 링크 수
        float minDirSpread  = 55f;    // 같은 발판이면 방향이 이 각도 이상 달라야 함

        // ── 진단 표시 ──
        bool showSurfaces = true;     // 샘플된 면을 Scene 뷰에 점으로
        readonly List<Vector3> lastSurfaces = new List<Vector3>();
        readonly List<Cand>    lastChosen   = new List<Cand>();

        // ── 종류 분류 ──
        float floorHeight = 3.9f;     // 이 맵 층간 높이
        float kindThreshold = 1.5f;   // 낙차 > floorHeight × 이 값 → ② 상승제한

        string lastReport = "";

        [MenuItem("Tools/층이동 링크/자동 배치 창 열기", false, 60)]
        static void Open() => GetWindow<TraversalAutoPlacer>("층이동 자동배치").minSize = new Vector2(360, 560);

        void OnEnable()  { SceneView.duringSceneGui += OnScene; }
        void OnDisable() { SceneView.duringSceneGui -= OnScene; }

        /// <summary>
        /// 진단 표시. "다리에 아무것도 안 걸린다" 같은 문제는 <b>애초에 그 면이 샘플됐는지</b>부터
        /// 봐야 한다. 점이 안 찍히면 격자 간격을 줄여야 한다는 뜻이다.
        /// </summary>
        void OnScene(SceneView sv)
        {
            if (showSurfaces && lastSurfaces.Count > 0)
            {
                Handles.color = new Color(0.3f, 1f, 0.5f, 0.9f);
                Vector3 eye = sv.camera != null ? sv.camera.transform.position : Vector3.zero;
                foreach (var p in lastSurfaces)
                {
                    if ((p - eye).sqrMagnitude > 60f * 60f) continue;
                    Handles.DotHandleCap(0, p + Vector3.up * 0.05f, Quaternion.identity, 0.06f, EventType.Repaint);
                }
            }

            foreach (var c in lastChosen)
            {
                Handles.color = c.isGap ? new Color(1f, 0.6f, 0.1f, 0.95f) : new Color(0.3f, 0.8f, 1f, 0.95f);
                Handles.DrawLine(c.high, c.low);
                Handles.SphereHandleCap(0, c.high, Quaternion.identity, 0.3f, EventType.Repaint);
            }
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "씬 지형을 훑어 도약 지점을 찾습니다. NavMesh가 아니라 콜라이더를 봅니다.\n" +
                "먼저 [분석]으로 개수만 확인하고, 마음에 들면 [배치]를 누르십시오.", MessageType.Info);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("지형 훑기", EditorStyles.boldLabel);
            gridStep = EditorGUILayout.Slider("격자 간격(m)", gridStep, 0.5f, 4f);
            EditorGUILayout.LabelField(" ", "작을수록 촘촘히 찾지만 느려짐", EditorStyles.miniLabel);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("낙하형 (위 → 아래)", EditorStyles.boldLabel);
            minDrop = EditorGUILayout.Slider("최소 낙차(m)", minDrop, 0.4f, 5f);
            maxDrop = EditorGUILayout.Slider("최대 낙차(m)", maxDrop, 3f, 30f);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("간극형 (같은 높이, 사이가 끊긴 곳)", EditorStyles.boldLabel);
            enableGap    = EditorGUILayout.Toggle("간극형 찾기", enableGap);
            using (new EditorGUI.DisabledScope(!enableGap))
            {
                sameLevelTol = EditorGUILayout.Slider("같은 높이로 볼 오차(m)", sameLevelTol, 0.2f, 4f);
                gapDepthMin  = EditorGUILayout.Slider("틈으로 볼 깊이(m)", gapDepthMin, 0.5f, 6f);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("공통", EditorStyles.boldLabel);
            minHoriz = EditorGUILayout.Slider("최소 수평거리(m)", minHoriz, 0.5f, 5f);
            maxHoriz = EditorGUILayout.Slider("최대 수평거리(m)", maxHoriz, 3f, 25f);
            edgeProbe   = EditorGUILayout.Slider("가장자리 탐침 거리(m)", edgeProbe, 0.3f, 3f);
            edgeDropMin = EditorGUILayout.Slider("가장자리로 볼 낙차(m)", edgeDropMin, 0.2f, 3f);
            minLinkSpacing = EditorGUILayout.Slider("링크 최소 간격(m)", minLinkSpacing, 1f, 12f);
            maxLinks = EditorGUILayout.IntSlider("최대 개수", maxLinks, 5, 400);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("다양성 (비슷한 링크 난립 방지)", EditorStyles.boldLabel);
            clusterSize   = EditorGUILayout.Slider("같은 발판으로 볼 반경(m)", clusterSize, 1f, 12f);
            maxPerCluster = EditorGUILayout.IntSlider("한 발판당 최대 링크", maxPerCluster, 1, 6);
            minDirSpread  = EditorGUILayout.Slider("같은 발판 방향 최소차(도)", minDirSpread, 0f, 120f);
            EditorGUILayout.LabelField(" ", "패턴이 반복되면 반경↑ / 최대↓ / 각도↑", EditorStyles.miniLabel);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("종류 분류", EditorStyles.boldLabel);
            floorHeight   = EditorGUILayout.Slider("층간 높이(m)", floorHeight, 1.5f, 8f);
            kindThreshold = EditorGUILayout.Slider("상승제한 배수", kindThreshold, 1f, 3f);
            EditorGUILayout.LabelField(" ", $"낙차 {floorHeight * kindThreshold:0.0}m 초과 → ② 상승제한", EditorStyles.miniLabel);

            EditorGUILayout.Space(4);
            showSurfaces = EditorGUILayout.Toggle("샘플된 면 표시(초록 점)", showSurfaces);
            EditorGUILayout.LabelField(" ", "다리에 점이 없으면 격자 간격을 줄이십시오", EditorStyles.miniLabel);

            EditorGUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("분석 (개수만)")) Run(false);
                if (GUILayout.Button("배치")) Run(true);
            }
            if (GUILayout.Button("자동 배치한 링크 전부 삭제")) ClearAuto();

            if (!string.IsNullOrEmpty(lastReport))
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.HelpBox(lastReport, MessageType.None);
            }
        }

        // ── 실행 ──
        void Run(bool place)
        {
            var surfaces = CollectSurfaces(out Bounds b);
            if (surfaces.Count == 0)
            { lastReport = "걸을 수 있는 면을 못 찾았습니다. 콜라이더가 있는 씬인지 확인하십시오."; return; }

            lastSurfaces.Clear(); lastSurfaces.AddRange(surfaces);

            var cands = FindCandidates(surfaces);

            // ★ 종류별로 나눠 번갈아 뽑는다.
            //   점수(낙차÷수평)가 큰 낙차를 편애해서, 한 줄로 정렬해 위에서부터 고르면
            //   ② 상승제한이 간격·개수 한도를 다 먹고 ① 일반이 통째로 밀려난다.
            float bigLine = floorHeight * kindThreshold;
            var normalC = new List<Cand>();
            var restrictC = new List<Cand>();
            foreach (var c in cands) (c.Drop > bigLine ? restrictC : normalC).Add(c);
            normalC.Sort((x, y) => y.score.CompareTo(x.score));
            restrictC.Sort((x, y) => y.score.CompareTo(x.score));

            var chosen = new List<Cand>();
            int ni = 0, ri = 0;
            bool takeNormal = true;
            while (chosen.Count < maxLinks && (ni < normalC.Count || ri < restrictC.Count))
            {
                bool tookOne = false;
                // 한쪽이 소진되면 남은 쪽에서 계속 뽑는다
                if (takeNormal && ni < normalC.Count)
                {
                    while (ni < normalC.Count)
                    { var c = normalC[ni++]; if (!TooClose(chosen, c)) { chosen.Add(c); tookOne = true; break; } }
                }
                else if (!takeNormal && ri < restrictC.Count)
                {
                    while (ri < restrictC.Count)
                    { var c = restrictC[ri++]; if (!TooClose(chosen, c)) { chosen.Add(c); tookOne = true; break; } }
                }
                else if (ni < normalC.Count)
                {
                    while (ni < normalC.Count)
                    { var c = normalC[ni++]; if (!TooClose(chosen, c)) { chosen.Add(c); tookOne = true; break; } }
                }
                else
                {
                    while (ri < restrictC.Count)
                    { var c = restrictC[ri++]; if (!TooClose(chosen, c)) { chosen.Add(c); tookOne = true; break; } }
                }
                takeNormal = !takeNormal;
                if (!tookOne && ni >= normalC.Count && ri >= restrictC.Count) break;
            }
            lastChosen.Clear(); lastChosen.AddRange(chosen);
            SceneView.RepaintAll();

            int drops = 0, gaps = 0, normal = 0, restricted = 0;
            foreach (var c in chosen)
            {
                if (c.isGap) gaps++; else drops++;
                if (c.Drop > floorHeight * kindThreshold) restricted++; else normal++;
            }

            lastReport = $"면 {surfaces.Count}개 · 영역 {b.size.x:0}×{b.size.y:0}×{b.size.z:0}\n" +
                         $"후보 {cands.Count}개 (① 일반 {normalC.Count} · ② 상승제한 {restrictC.Count})\n" +
                         $"→ 선정 {chosen.Count}개\n" +
                         $"  낙하형 {drops} · 간극형 {gaps}\n" +
                         $"  ① 일반 {normal} · ② 상승제한 {restricted}";

            if (!place) { Debug.Log("[자동배치·분석]\n" + lastReport); return; }

            var root = new GameObject("[TraversalLinks_Auto]");
            Undo.RegisterCreatedObjectUndo(root, "층이동 링크 자동 배치");

            int made = 0, blocked = 0;
            for (int i = 0; i < chosen.Count; i++)
            {
                var c = chosen[i];
                bool big = c.Drop > floorHeight * kindThreshold;
                var kind = big ? TraversalLinkKind.AscendRestricted : TraversalLinkKind.Normal;

                string tag = c.isGap ? "간극" : (big ? "상승제한" : "일반");
                var go = new GameObject($"Link_{tag}_{i}");
                go.transform.SetParent(root.transform, false);
                go.transform.position = c.high;
                var link = go.AddComponent<TraversalLink>();
                link.kind = kind;
                link.endOffset = go.transform.InverseTransformPoint(c.low);

                if (link.IsBlocked) { Object.DestroyImmediate(go); blocked++; continue; }
                made++;
            }

            Selection.activeGameObject = root;
            lastReport += $"\n배치 완료 {made}개 (궤적 막혀 제외 {blocked})";
            Debug.Log("[자동배치]\n" + lastReport);
        }

        static void ClearAuto()
        {
            var root = GameObject.Find("[TraversalLinks_Auto]");
            if (root == null) { Debug.Log("[자동배치] 삭제할 것이 없습니다."); return; }
            Undo.DestroyObjectImmediate(root);
            Debug.Log("[자동배치] 자동 배치 링크를 삭제했습니다.");
        }

        // ── 지형 수집 ──
        List<Vector3> CollectSurfaces(out Bounds bounds)
        {
            bounds = new Bounds();
            bool init = false;
            foreach (var col in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                if (col.isTrigger) continue;
                if (!init) { bounds = col.bounds; init = true; } else bounds.Encapsulate(col.bounds);
            }
            var list = new List<Vector3>();
            if (!init) return list;

            float top = bounds.max.y + 5f;
            float r = SimConfig.EnemyRadius * SimConfig.EnemyNormalScale;
            float h = SimConfig.EnemyHeight * SimConfig.EnemyNormalScale;

            for (float x = bounds.min.x; x <= bounds.max.x; x += gridStep)
            for (float z = bounds.min.z; z <= bounds.max.z; z += gridStep)
            {
                var hits = Physics.RaycastAll(new Vector3(x, top, z), Vector3.down, bounds.size.y + 10f,
                                              Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                foreach (var hit in hits)
                {
                    if (hit.normal.y < 0.7f) continue;
                    Vector3 p = hit.point;
                    // ★ 캡슐을 바닥에서 살짝 띄운다. 안 띄우면 캡슐 밑면이 자기가 서 있는 바닥에 닿아
                    //   지점이 통째로 버려진다(부동소수점에 따라 들쭉날쭉 — 다리·좁은 발판이 통째로 사라지던 원인).
                    const float Lift = 0.15f;
                    Vector3 b = p + Vector3.up * (r + Lift);
                    Vector3 t2 = p + Vector3.up * Mathf.Max(r + Lift, h - r);
                    if (Physics.CheckCapsule(b, t2, r * 0.9f,
                                             Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                    list.Add(p);
                }
            }
            return list;
        }

        // ── 후보 ──
        class Cand
        {
            public Vector3 high, low;
            public float score;
            public bool isGap;
            public float Drop => high.y - low.y;
        }

        List<Cand> FindCandidates(List<Vector3> pts)
        {
            var buckets = new Dictionary<long, List<int>>();
            float cell = maxHoriz;
            for (int i = 0; i < pts.Count; i++)
            {
                long k = CellKey(pts[i], cell);
                if (!buckets.TryGetValue(k, out var l)) buckets[k] = l = new List<int>();
                l.Add(i);
            }

            var res = new List<Cand>();
            var nb = new List<int>();
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 a = pts[i];
                nb.Clear(); Gather(buckets, a, cell, nb);
                foreach (int j in nb)
                {
                    if (j <= i) continue;                 // 쌍 중복 방지
                    Vector3 b = pts[j];
                    Vector3 flat = b - a; flat.y = 0f;
                    float horiz = flat.magnitude;
                    if (horiz < minHoriz || horiz > maxHoriz) continue;
                    Vector3 dir = flat / horiz;

                    float dy = a.y - b.y;
                    float ady = Mathf.Abs(dy);

                    // ① 낙하형 — 높은 쪽이 가장자리여야 함
                    if (ady >= minDrop && ady <= maxDrop)
                    {
                        Vector3 hi = dy > 0 ? a : b, lo = dy > 0 ? b : a;
                        Vector3 d = dy > 0 ? dir : -dir;
                        if (!IsLedge(hi, d)) continue;
                        if (!ArcClear(hi, lo)) continue;
                        res.Add(new Cand { high = hi, low = lo, score = ady / Mathf.Max(1f, horiz), isGap = false });
                        continue;
                    }

                    // ② 간극형 — 거의 같은 높이인데 사이가 끊긴 곳
                    if (enableGap && ady <= sameLevelTol)
                    {
                        if (!HasGapBetween(a, b)) continue;          // 사이가 안 끊겼으면 그냥 걸어감
                        if (!IsLedge(a, dir) || !IsLedge(b, -dir)) continue;  // 양쪽 다 가장자리여야
                        Vector3 hi = a.y >= b.y ? a : b, lo = a.y >= b.y ? b : a;
                        if (!ArcClear(hi, lo)) continue;
                        // 간극은 넓을수록 값어치 있음(짧은 틈은 어차피 걸어서 돌아감)
                        res.Add(new Cand { high = hi, low = lo, score = horiz * 0.25f, isGap = true });
                    }
                }
            }
            return res;
        }

        /// <summary>두 점 사이가 실제로 끊겨 있는가 — 선을 따라가며 발밑이 꺼지는 구간을 찾는다.</summary>
        bool HasGapBetween(Vector3 a, Vector3 b)
        {
            const int N = 8;
            float lineY = Mathf.Min(a.y, b.y);
            for (int i = 1; i < N; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, (float)i / N);
                Vector3 origin = new Vector3(p.x, lineY + 0.5f, p.z);
                if (!Physics.Raycast(origin, Vector3.down, out var hit, gapDepthMin + 1f,
                                     Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    return true;                                    // 바닥이 아예 없음 = 틈
                if (lineY - hit.point.y >= gapDepthMin) return true; // 충분히 아래 = 틈
            }
            return false;
        }

        bool IsLedge(Vector3 from, Vector3 dir)
        {
            Vector3 probe = from + dir * edgeProbe + Vector3.up * 0.3f;
            if (!Physics.Raycast(probe, Vector3.down, out var hit, edgeDropMin + 1f,
                                 Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return true;
            return (from.y - hit.point.y) >= edgeDropMin;
        }

        /// <summary>궤적이 지형을 뚫는가. 점만 보면 사이의 얇은 벽을 놓치므로 <b>구간 스윕</b>까지 한다.</summary>
        static bool ArcClear(Vector3 hi, Vector3 lo)
        {
            float len = Vector3.Distance(hi, lo);
            float clearance = Mathf.Clamp(len * SimConfig.TraversalClearanceRatio,
                                          SimConfig.TraversalMinClearance, SimConfig.TraversalMaxClearance);
            var arc = TraversalBallistics.Solve(hi, lo, clearance);
            if (!arc.IsValid) return false;

            float r = SimConfig.EnemyRadius * SimConfig.EnemyNormalScale;
            float h = SimConfig.EnemyHeight * SimConfig.EnemyNormalScale;
            float halfH = Mathf.Max(r, h - r);
            const int S = 28;

            Vector3 prev = Vector3.zero; bool hasPrev = false;
            for (int i = 0; i <= S; i++)
            {
                float f = (float)i / S;
                if (f < 0.08f || f > 0.92f) { hasPrev = false; continue; }
                Vector3 p = arc.At(Mathf.RoundToInt(arc.flightTicks * f));
                if (Physics.CheckCapsule(p + Vector3.up * r, p + Vector3.up * halfH, r,
                                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;
                if (hasPrev)
                {
                    Vector3 seg = p - prev; float d = seg.magnitude;
                    if (d > 1e-4f && Physics.CapsuleCast(prev + Vector3.up * r, prev + Vector3.up * halfH, r,
                                                        seg / d, d, Physics.DefaultRaycastLayers,
                                                        QueryTriggerInteraction.Ignore)) return false;
                }
                prev = p; hasPrev = true;
            }
            return true;
        }

        /// <summary>
        /// 비슷한 링크를 걸러낸다. 양 끝이 다 가까운 "사실상 같은 링크"뿐 아니라,
        /// <b>같은 발판에서 비슷한 방향으로 뻗는 것</b>도 막는다 —
        /// 이게 없으면 한 가장자리에서 조금씩 다른 착지점으로 뻗는 거의 같은 링크가 잔뜩 생긴다.
        /// </summary>
        bool TooClose(List<Cand> placed, Cand c)
        {
            Vector3 cd = c.low - c.high; cd.y = 0f;
            if (cd.sqrMagnitude < 1e-4f) return true;
            cd.Normalize();

            int sameCluster = 0;
            foreach (var p in placed)
            {
                // ① 양 끝이 다 가까움 = 같은 링크
                if (Vector3.Distance(p.high, c.high) < minLinkSpacing &&
                    Vector3.Distance(p.low,  c.low)  < minLinkSpacing) return true;

                // ② 같은 발판(클러스터)이면 개수 제한 + 방향 다양성 요구
                if (Vector3.Distance(p.high, c.high) < clusterSize)
                {
                    if (++sameCluster >= maxPerCluster) return true;
                    Vector3 pd = p.low - p.high; pd.y = 0f;
                    if (pd.sqrMagnitude > 1e-4f && Vector3.Angle(pd.normalized, cd) < minDirSpread)
                        return true;
                }

                // ③ 착지점이 같은 자리에 몰리는 것도 막는다(여러 발판에서 한 곳으로 쏠림 방지)
                if (Vector3.Distance(p.low, c.low) < minLinkSpacing * 0.8f &&
                    Vector3.Angle((p.low - p.high).normalized, cd) < minDirSpread * 0.6f) return true;
            }
            return false;
        }

        static long CellKey(Vector3 p, float cell)
            => ((long)Mathf.Floor(p.x / cell) << 32) ^ ((long)Mathf.Floor(p.z / cell) & 0xffffffffL);

        static void Gather(Dictionary<long, List<int>> buckets, Vector3 p, float cell, List<int> outList)
        {
            long cx = (long)Mathf.Floor(p.x / cell), cz = (long)Mathf.Floor(p.z / cell);
            for (long dx = -1; dx <= 1; dx++)
            for (long dz = -1; dz <= 1; dz++)
                if (buckets.TryGetValue(((cx + dx) << 32) ^ ((cz + dz) & 0xffffffffL), out var l))
                    outList.AddRange(l);
        }
    }
}
