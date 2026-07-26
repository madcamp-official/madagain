using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Game.Sim;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// 예측용 맵 그래프를 굽는 창.
    ///
    /// 예측은 월드를 포크해 sim을 그대로 돌리는데, NavMesh는 결정론적 포크가 불가라(계약 §8.3)
    /// 고정 그래프가 필요하다. 기존엔 SampleScene용이 코드에 하드코딩돼 있어 다른 맵엔 그래프가 없었다.
    ///
    /// ★ NavMesh가 있어야 하므로 <b>Play 중에 굽는다</b>(이 프로젝트 NavMesh는 런타임 생성).
    ///
    /// 핵심 설계
    ///  · 마커 끝점을 <b>노드로 승격</b> — 안 하면 발판이 노드와 어긋나 엉뚱한 데서 도약한다.
    ///  · walk 링크는 <b>NavMesh.Raycast</b>로 "실제로 걸어서 직행 가능한가"를 확인하고 잇는다
    ///    (거리만 보고 이으면 협곡 건너편끼리 이어져 몹이 허공을 걷는다).
    ///  · 노드 수는 성능에 직결된다 — 예측은 NextStep을 적×틱마다 부른다.
    /// </summary>
    public class PredictionGraphBaker : EditorWindow
    {
        // ── 노드 생성 ──
        bool  promoteMarkers  = true;
        float mergeRadius     = 2.0f;
        float fillSpacing     = 12f;
        float floorSplit      = 1.5f;

        // ── 연결 ──
        float maxConnect      = 16f;
        bool  useNavRaycast   = true;
        int   maxNeighbours   = 6;

        // ── 성능 ──
        // (공간 색인은 GraphPathfinder가 노드 24개 이상이면 자동으로 켠다 — 여기선 예상치만 보여준다)

        // ── 마커 링크 ──
        bool includeNormal = true, includeRestricted = true, includeDescendOnly = true;
        bool useCostDistance = true;

        // ── 높이 상한 ──
        // [2026-07-22 추가] 천장·지붕 NavMesh 제외용. 아래 Run()의 "1-b) 높이 상한" 주석 참고.
        bool useMaxHeight = true;
        float maxNodeHeight = 10.5f;

        // ── 출력 ──
        string savePath = "Assets/_Project/View/Nav/Resources";
        string report = "";

        [MenuItem("Tools/층이동 링크/예측 그래프 굽기", false, 70)]
        static void Open() => GetWindow<PredictionGraphBaker>("예측 그래프 굽기").minSize = new Vector2(380, 560);

        void OnGUI()
        {
            // NavMesh만 있으면 굽는다. Tools/맵 굽기로 미리 구웠으면 정지 상태에서도 가능하고,
            // 안 구웠으면 Play 중에 런타임 베이크된 NavMesh를 쓰면 된다.
            bool navReady = HasNavMesh();
            if (!navReady)
                EditorGUILayout.HelpBox(
                    "NavMesh가 없어 구울 수 없습니다.\n" +
                    "Tools/맵 굽기 → ① NavMesh 굽기 를 먼저 실행하거나, Play 중에 구우십시오.",
                    MessageType.Warning);

            EditorGUILayout.LabelField("노드 생성", EditorStyles.boldLabel);
            promoteMarkers = EditorGUILayout.Toggle("마커 끝점 노드로 승격", promoteMarkers);
            if (!promoteMarkers)
                EditorGUILayout.HelpBox("끄면 발판이 노드와 어긋나 엉뚱한 지점에서 도약합니다. 권장: 켬", MessageType.Warning);
            mergeRadius = EditorGUILayout.Slider("끝점 병합 반경(m)", mergeRadius, 0f, 6f);
            useMaxHeight = EditorGUILayout.Toggle("높이 상한 사용", useMaxHeight);
            using (new EditorGUI.DisabledScope(!useMaxHeight))
                maxNodeHeight = EditorGUILayout.Slider("최대 노드 높이(m)", maxNodeHeight, 2f, 40f);
            if (!useMaxHeight)
                EditorGUILayout.HelpBox(
                    "NavMesh는 천장·지붕 콜라이더 위에도 깔립니다. 끄면 예측이 갈 수 없는 지붕 위 " +
                    "경로를 계획할 수 있습니다. 권장: 켬 + 플레이 영역 최고점보다 조금 높게",
                    MessageType.Warning);
            fillSpacing = EditorGUILayout.Slider("지형 보충 간격(m)", fillSpacing, 0f, 30f);
            EditorGUILayout.LabelField(" ", "0 = 보충 안 함(마커 끝점만). 작을수록 촘촘·느림", EditorStyles.miniLabel);
            floorSplit  = EditorGUILayout.Slider("층 분리 높이차(m)", floorSplit, 0.5f, 5f);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("연결 (walk 링크)", EditorStyles.boldLabel);
            maxConnect    = EditorGUILayout.Slider("최대 연결 거리(m)", maxConnect, 4f, 40f);
            useNavRaycast = EditorGUILayout.Toggle("직행 가능 검사", useNavRaycast);
            if (!useNavRaycast)
                EditorGUILayout.HelpBox("끄면 협곡 건너편끼리 이어져 몹이 허공을 걷습니다. 권장: 켬", MessageType.Warning);
            maxNeighbours = EditorGUILayout.IntSlider("노드당 최대 이웃", maxNeighbours, 2, 12);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("마커 링크", EditorStyles.boldLabel);
            includeNormal      = EditorGUILayout.Toggle("① 일반 포함", includeNormal);
            includeRestricted  = EditorGUILayout.Toggle("② 상승제한 포함", includeRestricted);
            includeDescendOnly = EditorGUILayout.Toggle("③ 하강전용 포함", includeDescendOnly);
            useCostDistance    = EditorGUILayout.Toggle("비용 환산 적용", useCostDistance);
            if (!useCostDistance)
                EditorGUILayout.HelpBox("끄면 도약이 공짜로 보여 예측이 과도하게 뛰어내립니다.", MessageType.Warning);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("출력", EditorStyles.boldLabel);
            savePath = EditorGUILayout.TextField("저장 폴더", savePath);

            EditorGUILayout.Space(8);
            using (new EditorGUI.DisabledScope(!navReady))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("분석 (개수만)")) Run(false);
                if (GUILayout.Button("굽기")) Run(true);
            }

            if (!string.IsNullOrEmpty(report))
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.HelpBox(report, MessageType.None);
            }
        }

        // NavMesh 존재 여부. CalculateTriangulation은 무거워서 1초 간격으로만 확인한다(매 리페인트 방지).
        double navCheckAt;
        bool   navCached;
        bool HasNavMesh()
        {
            if (EditorApplication.timeSinceStartup - navCheckAt > 1.0)
            {
                var t = NavMesh.CalculateTriangulation();
                navCached = t.vertices != null && t.vertices.Length > 0;
                navCheckAt = EditorApplication.timeSinceStartup;
            }
            return navCached;
        }

        // ── 실행 ──
        void Run(bool save)
        {
            var tri = NavMesh.CalculateTriangulation();
            if (tri.vertices == null || tri.vertices.Length == 0)
            { report = "NavMesh가 없습니다. Tools/맵 굽기 → ① NavMesh 굽기 를 먼저 실행하십시오."; return; }

            var markers = Object.FindObjectsByType<TraversalLink>(FindObjectsSortMode.None);

            // 1) 노드 수집
            var pts = new List<Vector3>();
            int markerNodes = 0;
            if (promoteMarkers)
            {
                foreach (var m in markers)
                {
                    if (m == null || m.IsBlocked) continue;
                    if (!KindIncluded(m.kind)) continue;
                    AddPoint(pts, Snap(m.High), markerNodes++ >= 0);
                    AddPoint(pts, Snap(m.Low),  true);
                }
                markerNodes = pts.Count;
            }
            int afterMarkers = pts.Count;

            if (fillSpacing > 0f) FillFromTriangulation(tri, pts);
            int total = pts.Count;

            // 1-b) 높이 상한 — NavMesh 베이크가 콜라이더 전체(collectObjects=All + PhysicsColliders)를
            // 걷는 면으로 굽기 때문에 <b>천장·지붕 위에도 NavMesh가 깔린다</b>. 그대로 구우면 예측이
            // 플레이어가 갈 수 없는 지붕 위 경로를 계획한다(Arena_3 실측: 노드 152개 중 33개가
            // 12~23m, 실제 플레이 영역 상단은 9.4m였다). 아래 상한으로 그 노드들을 잘라낸다.
            // NavMesh 자체는 안 건드린다 — 그건 실제 게임 길찾기가 쓰는 것이라 별개 문제다.
            int cutByHeight = 0;
            if (useMaxHeight)
            {
                // [지역 상한, 2026-07-23] 아레나마다 지형 기준 높이가 달라 전역 상한 하나로는 안 된다
                // (Arena_4 바닥이 8~32m — 전역 10.5로 자르면 전멸). GraphHeightCapRegion 박스 안(XZ)의
                // 점은 그 지역 상한을 쓴다. 지역이 하나도 없으면 기존 동작과 완전히 동일하다.
                var capRegions = Object.FindObjectsByType<GraphHeightCapRegion>(FindObjectsSortMode.None);
                for (int i = pts.Count - 1; i >= 0; i--)
                {
                    float cap = maxNodeHeight;
                    foreach (var r in capRegions)
                        if (r.ContainsXZ(pts[i])) { cap = r.maxNodeHeight; break; }
                    if (pts[i].y <= cap) continue;
                    pts.RemoveAt(i);
                    cutByHeight++;
                    if (i < afterMarkers) afterMarkers--;
                }
                total = pts.Count;
            }

            // 2) 노드 생성(층 id 부여)
            var nodes = new ArenaNavNode[pts.Count];
            for (int i = 0; i < pts.Count; i++)
                nodes[i] = new ArenaNavNode
                {
                    nodeId = i,
                    position = pts[i],
                    floorId = Mathf.RoundToInt(pts[i].y / Mathf.Max(0.5f, floorSplit)),
                    areaFlags = MapAreaFlags.Playable,
                };

            // 3) walk 링크
            var links = new List<ArenaNavLink>();
            int linkId = 0;
            int walkPairs = BuildWalkLinks(nodes, links, ref linkId);

            // 4) 마커 링크
            int markerLinks = BuildMarkerLinks(markers, nodes, links, ref linkId);

            // 5) 연결성 검사
            int components = CountComponents(nodes, links);
            int isolated = CountIsolated(nodes, links);

            long fw = (long)nodes.Length * nodes.Length * nodes.Length;
            report =
                $"노드 {nodes.Length}개 (마커 끝점 {afterMarkers} · 지형 보충 {total - afterMarkers})\n" +
                (useMaxHeight
                    ? $"높이 상한 {maxNodeHeight:0.0}m 초과로 제외 {cutByHeight}개 (천장·지붕)\n"
                    : "⚠ 높이 상한 꺼짐 — 천장 위 노드가 섞일 수 있습니다\n") +
                $"링크 {links.Count}개 (walk {walkPairs * 2} · 마커 {markerLinks})\n" +
                $"연결 성분 {components}개" + (components > 1 ? "  ⚠ 갈라진 영역이 있습니다" : "") + "\n" +
                (isolated > 0 ? $"⚠ 고립 노드 {isolated}개\n" : "") +
                $"Floyd-Warshall 예상 {fw / 1_000_000f:0.0}M 연산" +
                (nodes.Length > 400 ? "  ⚠ 노드가 많습니다(병합 반경↑ / 보충 간격↑ 권장)" : "");

            if (!save) { Debug.Log("[예측 그래프·분석]\n" + report); return; }

            // 6) 저장
            if (!AssetDatabase.IsValidFolder(savePath))
            { report += "\n저장 폴더가 없습니다: " + savePath; return; }

            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            string path = $"{savePath}/{PredictionGraphAsset.ResourceName(scene)}.asset";

            var asset = AssetDatabase.LoadAssetAtPath<PredictionGraphAsset>(path);
            bool isNew = asset == null;
            if (isNew) asset = ScriptableObject.CreateInstance<PredictionGraphAsset>();

            asset.sceneName = scene;
            asset.bakedAt = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            asset.markerCount = markers.Length;
            asset.Store(nodes, links.ToArray());

            if (isNew) AssetDatabase.CreateAsset(asset, path);
            else EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            report += $"\n저장: {path}\n(다음 Play부터 예측이 이 그래프를 씁니다)";
            Debug.Log("[예측 그래프·굽기]\n" + report);
            Selection.activeObject = asset;
        }

        bool KindIncluded(TraversalLinkKind k)
        {
            switch (k)
            {
                case TraversalLinkKind.Normal:           return includeNormal;
                case TraversalLinkKind.AscendRestricted: return includeRestricted;
                default:                                 return includeDescendOnly;
            }
        }

        static Vector3 Snap(Vector3 p)
            => NavMesh.SamplePosition(p, out var h, 3f, NavMesh.AllAreas) ? h.position : p;

        void AddPoint(List<Vector3> pts, Vector3 p, bool _)
        {
            // 병합 반경 안이면 기존 노드 재사용 → 같은 가장자리에서 뻗은 링크들이 노드를 공유
            if (mergeRadius > 0f)
                foreach (var q in pts)
                    if ((q - p).sqrMagnitude <= mergeRadius * mergeRadius) return;
            pts.Add(p);
        }

        /// <summary>삼각형 무게중심을 보충 간격으로 솎아 넓은 영역을 채운다.</summary>
        void FillFromTriangulation(NavMeshTriangulation tri, List<Vector3> pts)
        {
            int triCount = tri.indices.Length / 3;
            float sq = fillSpacing * fillSpacing;
            for (int t = 0; t < triCount; t++)
            {
                Vector3 c = (tri.vertices[tri.indices[t * 3]] +
                             tri.vertices[tri.indices[t * 3 + 1]] +
                             tri.vertices[tri.indices[t * 3 + 2]]) / 3f;
                bool near = false;
                foreach (var q in pts)
                    if ((q - c).sqrMagnitude < sq) { near = true; break; }
                if (!near) pts.Add(c);
            }
        }

        /// <summary>실제로 걸어서 직행 가능한 노드 쌍만 잇는다.</summary>
        int BuildWalkLinks(ArenaNavNode[] nodes, List<ArenaNavLink> links, ref int linkId)
        {
            int pairs = 0;
            var cand = new List<(int idx, float d)>();
            for (int i = 0; i < nodes.Length; i++)
            {
                cand.Clear();
                for (int j = 0; j < nodes.Length; j++)
                {
                    if (i == j) continue;
                    float d = Vector3.Distance(nodes[i].position, nodes[j].position);
                    if (d > maxConnect) continue;
                    cand.Add((j, d));
                }
                cand.Sort((a, b) => a.d.CompareTo(b.d));

                int made = 0;
                foreach (var (j, d) in cand)
                {
                    if (made >= maxNeighbours) break;
                    if (j < i) { made++; continue; }        // 쌍 중복 방지(양방향은 아래서 한 번에)
                    if (useNavRaycast &&
                        NavMesh.Raycast(nodes[i].position, nodes[j].position, out _, NavMesh.AllAreas))
                        continue;                            // 막힘 = 직행 불가
                    links.Add(Walk(ref linkId, nodes[i].nodeId, nodes[j].nodeId, d));
                    links.Add(Walk(ref linkId, nodes[j].nodeId, nodes[i].nodeId, d));
                    pairs++; made++;
                }
            }
            return pairs;
        }

        static ArenaNavLink Walk(ref int id, int a, int b, float dist) => new ArenaNavLink
        {
            linkId = id++, fromNodeId = a, toNodeId = b,
            traversalType = NavTraversalType.Walk, traversalTicks = 0, agentMask = -1,
            costDistance = dist,
        };

        int BuildMarkerLinks(TraversalLink[] markers, ArenaNavNode[] nodes,
                             List<ArenaNavLink> links, ref int linkId)
        {
            int made = 0;
            foreach (var m in markers)
            {
                if (m == null || m.IsBlocked || !KindIncluded(m.kind)) continue;
                Vector3 high = Snap(m.High), low = Snap(m.Low);

                made += AddMarkerLink(m, nodes, links, ref linkId, high, low, ascend: false);
                if (m.AscendAllowed)
                    made += AddMarkerLink(m, nodes, links, ref linkId, low, high, ascend: true);
            }
            return made;
        }

        int AddMarkerLink(TraversalLink m, ArenaNavNode[] nodes, List<ArenaNavLink> links,
                          ref int linkId, Vector3 from, Vector3 to, bool ascend)
        {
            int a = Nearest(nodes, from), b = Nearest(nodes, to);
            if (a < 0 || b < 0 || a == b) return 0;

            BallisticArc arc = TraversalBallistics.Solve(from, to, m.EffectiveClearance, m.gravity);
            int pause = m.PauseTicks, recover = m.RecoverTicks;
            int totalTicks = TraversalBallistics.TotalTicks(pause, arc.flightTicks, recover);

            int ground = 1 << (int)MobilityType.Ground;
            int charge = 1 << (int)MobilityType.Charge;
            int trav   = 1 << (int)MobilityType.Traversal;
            int mask = !ascend ? (ground | charge | trav)
                               : (m.AscendTraversalOnly ? trav : (ground | charge | trav));

            links.Add(new ArenaNavLink
            {
                linkId = linkId++,
                fromNodeId = nodes[a].nodeId,
                toNodeId = nodes[b].nodeId,
                traversalType = ascend ? NavTraversalType.JumpUp : NavTraversalType.DropDown,
                traversalTicks = arc.flightTicks,
                heightDelta = to.y - from.y,
                dropHeight = Mathf.Max(0f, from.y - to.y),
                agentMask = mask,
                landingPosition = to,
                traversalStartPosition = from,
                landingSlotCount = m.UsableSlotCount(to),
                landingSpread = m.SlotSpread,
                clearance = m.EffectiveClearance,
                gravity = m.gravity,
                pauseTicks = pause,
                recoverTicks = recover,
                costDistance = useCostDistance
                    ? TraversalBallistics.CostDistance(totalTicks, SimConfig.EnemyMoveSpeed)
                    : Vector3.Distance(from, to),
            });
            return 1;
        }

        static int Nearest(ArenaNavNode[] nodes, Vector3 p)
        {
            int best = -1; float bestSq = float.MaxValue;
            for (int i = 0; i < nodes.Length; i++)
            {
                float sq = (nodes[i].position - p).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = i; }
            }
            return best;
        }

        // ── 검증 ──
        static int CountIsolated(ArenaNavNode[] nodes, List<ArenaNavLink> links)
        {
            var has = new bool[nodes.Length];
            foreach (var l in links)
            {
                int a = IndexOf(nodes, l.fromNodeId), b = IndexOf(nodes, l.toNodeId);
                if (a >= 0) has[a] = true;
                if (b >= 0) has[b] = true;
            }
            int n = 0;
            foreach (var h in has) if (!h) n++;
            return n;
        }

        static int CountComponents(ArenaNavNode[] nodes, List<ArenaNavLink> links)
        {
            var adj = new List<int>[nodes.Length];
            for (int i = 0; i < nodes.Length; i++) adj[i] = new List<int>();
            foreach (var l in links)
            {
                int a = IndexOf(nodes, l.fromNodeId), b = IndexOf(nodes, l.toNodeId);
                if (a >= 0 && b >= 0) { adj[a].Add(b); adj[b].Add(a); }
            }
            var seen = new bool[nodes.Length];
            int comps = 0;
            var stack = new Stack<int>();
            for (int i = 0; i < nodes.Length; i++)
            {
                if (seen[i]) continue;
                comps++; stack.Push(i); seen[i] = true;
                while (stack.Count > 0)
                {
                    int cur = stack.Pop();
                    foreach (int nx in adj[cur]) if (!seen[nx]) { seen[nx] = true; stack.Push(nx); }
                }
            }
            return comps;
        }

        static int IndexOf(ArenaNavNode[] ns, int id)
        {
            for (int i = 0; i < ns.Length; i++) if (ns[i].nodeId == id) return i;
            return -1;
        }
    }
}
