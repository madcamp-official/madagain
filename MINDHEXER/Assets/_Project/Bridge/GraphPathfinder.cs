using System;
using System.Collections.Generic;
using UnityEngine;
using Game.Sim;

namespace Game.Bridge
{
    /// <summary>불변 ArenaMapBake에서 만든 결정론적 유향 경로표.</summary>
    public sealed class GraphPathfinder : IPathfinder
    {
        const int Infinity = 1_000_000_000;
        readonly ArenaNavNode[] nodes;
        readonly ArenaNavLink[] links;
        readonly int[,] next;
        readonly int[,] distance;
        readonly int[,] directLink;
        public int MapVersion { get; }

        // ── 공간 색인 ──
        // NearestNode는 예측에서 적×틱마다 불린다(≈337k 규모). 노드가 늘면 전체 선형 스캔이
        // 그대로 병목이 되므로, 격자 해시로 주변 셀만 본다. 결과·동률 해소는 선형 스캔과 동일.
        readonly Dictionary<long, List<int>> grid;
        readonly float gridCell;
        readonly int gridMaxRing;

        GraphPathfinder(ArenaNavNode[] nodes, ArenaNavLink[] links, int[,] next, int[,] distance, int[,] directLink, int mapVersion)
        {
            this.nodes = nodes;
            this.links = links;
            this.next = next;
            this.distance = distance;
            this.directLink = directLink;
            MapVersion = mapVersion;

            // 노드가 적으면 색인이 오히려 손해라 그냥 선형 스캔
            if (nodes.Length >= 24)
            {
                gridCell = 8f;
                grid = new Dictionary<long, List<int>>(nodes.Length);
                var min = new Vector3(float.MaxValue, 0f, float.MaxValue);
                var max = new Vector3(float.MinValue, 0f, float.MinValue);
                for (int i = 0; i < nodes.Length; i++)
                {
                    Vector3 p = nodes[i].position;
                    if (p.x < min.x) min.x = p.x; if (p.x > max.x) max.x = p.x;
                    if (p.z < min.z) min.z = p.z; if (p.z > max.z) max.z = p.z;
                    long k = CellKey(p);
                    if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>();
                    l.Add(i);
                }
                float span = Mathf.Max(max.x - min.x, max.z - min.z);
                gridMaxRing = Mathf.Max(2, Mathf.CeilToInt(span / gridCell) + 1);
            }
        }

        long CellKey(Vector3 p)
            => ((long)Mathf.FloorToInt(p.x / gridCell) << 32) ^ ((long)Mathf.FloorToInt(p.z / gridCell) & 0xffffffffL);

        public static GraphPathfinder FromBake(ArenaMapBake bake, int agentMask = -1)
        {
            if (bake == null) throw new ArgumentNullException(nameof(bake));
            ValidateBake(bake);
            ArenaNavNode[] ns = (ArenaNavNode[])bake.nodes.Clone();
            ArenaNavLink[] ls = (ArenaNavLink[])bake.links.Clone();
            Array.Sort(ns, (a, b) => a.nodeId.CompareTo(b.nodeId));
            FillTraversalDefaults(ns, ls);
            int n = ns.Length;
            var dist = new int[n, n];
            var first = new int[n, n];
            var direct = new int[n, n];
            for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                dist[i, j] = i == j ? 0 : Infinity;
                first[i, j] = i == j ? i : -1;
                direct[i, j] = -1;
            }

            for (int li = 0; li < ls.Length; li++)
            {
                ArenaNavLink l = ls[li];
                if (l.traversalType == NavTraversalType.Blocked || (l.agentMask & agentMask) == 0) continue;
                int a = IndexOf(ns, l.fromNodeId), b = IndexOf(ns, l.toNodeId);
                if (a < 0 || b < 0) continue;
                // 비용: 도약 링크는 Bake가 구워둔 "환산거리"(주저+비행+멈칫을 걸은 거리로 환산)를 쓴다.
                // 이걸 안 쓰고 직선거리로 두면 도약이 공짜로 보여, 평상시(NavMesh, 보정됨)와
                // 정반대 판단을 하게 된다 — 예측이 거짓말하는 상태가 된다.
                float costMeters = l.costDistance > 0f
                    ? l.costDistance
                    : Vector3.Distance(ns[a].position, ns[b].position);
                int cost = Mathf.Max(1, Mathf.RoundToInt(costMeters * 100f));
                if (cost < dist[a, b] || (cost == dist[a, b] && (direct[a, b] < 0 || l.linkId < ls[direct[a, b]].linkId)))
                {
                    dist[a, b] = cost;
                    first[a, b] = b;
                    direct[a, b] = li;
                }
            }

            for (int k = 0; k < n; k++)
            for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                if (k == i || k == j) continue;   // 자기 경유 배제 — first[i,i]=i 자기참조가 최소 nodeId로 next를 오염시킴(층이동 정지 버그)
                if (dist[i, k] >= Infinity || dist[k, j] >= Infinity) continue;
                int candidate = dist[i, k] + dist[k, j];
                int hop = first[i, k];
                if (candidate < dist[i, j] ||
                    (candidate == dist[i, j] && hop >= 0 &&
                     (first[i, j] < 0 || ns[hop].nodeId < ns[first[i, j]].nodeId)))
                {
                    dist[i, j] = candidate;
                    first[i, j] = hop;
                }
            }
            return new GraphPathfinder(ns, ls, first, dist, direct, bake.mapVersion);
        }

        static void ValidateBake(ArenaMapBake bake)
        {
            if (bake.mapVersion <= 0)
                throw new ArgumentException("ArenaMapBake.mapVersion must be positive.", nameof(bake));
            if (bake.nodes == null || bake.nodes.Length == 0)
                throw new ArgumentException("ArenaMapBake must contain at least one node.", nameof(bake));
            if (bake.links == null)
                throw new ArgumentException("ArenaMapBake.links cannot be null.", nameof(bake));

            for (int i = 0; i < bake.nodes.Length; i++)
            {
                for (int j = i + 1; j < bake.nodes.Length; j++)
                    if (bake.nodes[i].nodeId == bake.nodes[j].nodeId)
                        throw new ArgumentException($"Duplicate arena node id {bake.nodes[i].nodeId}.", nameof(bake));
            }

            for (int i = 0; i < bake.links.Length; i++)
            {
                ArenaNavLink link = bake.links[i];
                if (IndexOf(bake.nodes, link.fromNodeId) < 0 || IndexOf(bake.nodes, link.toNodeId) < 0)
                    throw new ArgumentException($"Arena link {link.linkId} references a missing node.", nameof(bake));
            }
        }

        /// <summary>
        /// 탄도 파라미터를 안 채운 링크에 길이 기반 기본값을 넣는다.
        /// ★ 손으로 짠 그래프(CreateArena 등)는 이 필드들이 0이라 그대로 두면
        ///   "주저·멈칫 없이 즉시 도약 + 최소 clearance"가 되어 기존 동작이 바뀐다.
        ///   마커 Bake는 clearance를 항상 0보다 크게 채우므로, clearance<=0을 "미지정"으로 본다.
        /// </summary>
        static void FillTraversalDefaults(ArenaNavNode[] ns, ArenaNavLink[] ls)
        {
            for (int i = 0; i < ls.Length; i++)
            {
                var l = ls[i];
                if (l.traversalType == NavTraversalType.Walk ||
                    l.traversalType == NavTraversalType.Blocked) continue;
                if (l.clearance > 0f) continue;   // 이미 구워진 링크

                int a = IndexOf(ns, l.fromNodeId), b = IndexOf(ns, l.toNodeId);
                if (a < 0 || b < 0) continue;
                Vector3 from = ns[a].position;
                Vector3 to = l.landingPosition != default ? l.landingPosition : ns[b].position;
                float len = Vector3.Distance(from, to);

                l.clearance = Mathf.Clamp(len * SimConfig.TraversalClearanceRatio,
                                          SimConfig.TraversalMinClearance, SimConfig.TraversalMaxClearance);
                if (l.pauseTicks <= 0)
                    l.pauseTicks = TraversalBallistics.LengthToTicks(len,
                        SimConfig.TraversalPauseMin, SimConfig.TraversalPauseMax,
                        SimConfig.TraversalLengthRef, SimConfig.TraversalLengthExp);
                if (l.recoverTicks <= 0)
                    l.recoverTicks = TraversalBallistics.LengthToTicks(len,
                        SimConfig.TraversalRecoverMin, SimConfig.TraversalRecoverMax,
                        SimConfig.TraversalLengthRef, SimConfig.TraversalLengthExp);
                if (l.landingSlotCount <= 0) l.landingSlotCount = 1;
                ls[i] = l;
            }
        }

        public PathStep NextStep(Vector3 from, Vector3 to, int agentMask)
        {
            if (nodes.Length == 0) return default;
            int a = NearestNode(from), destination = NearestNode(to);
            if (a == destination)
                return MakeStep(MoveKind.Walk, to, a, a, destination, -1, 0);
            int b = next[a, destination];
            if (b < 0) return new PathStep { kind = MoveKind.None, currentNodeId = nodes[a].nodeId, destinationNodeId = nodes[destination].nodeId, floorId = nodes[a].floorId, destinationFloorId = nodes[destination].floorId, linkId = -1 };
            int li = directLink[a, b];
            if (li < 0) return default;
            ArenaNavLink link = links[li];
            if ((link.agentMask & agentMask) == 0) return default;
            MoveKind kind = ToMoveKind(link.traversalType);
            Vector3 target = kind == MoveKind.Drop || kind == MoveKind.Boost ? link.landingPosition : nodes[b].position;
            PathStep s = MakeStep(kind, target, a, b, destination, link.linkId, link.traversalTicks);
            // 탄도 파라미터를 실어 보낸다 — 실행부가 에디터 프리뷰와 같은 함수로 궤적을 푼다.
            s.clearance    = link.clearance;
            s.gravity      = link.gravity;
            s.pauseTicks   = link.pauseTicks;
            s.recoverTicks = link.recoverTicks;
            s.slotCount    = link.landingSlotCount;
            s.slotSpread   = link.landingSpread;
            return s;
        }

        PathStep MakeStep(MoveKind kind, Vector3 target, int a, int b, int destination, int linkId, int ticks)
            => new PathStep { kind = kind, traversalStart = nodes[a].position, next = target, currentNodeId = nodes[a].nodeId,
                nextNodeId = nodes[b].nodeId, destinationNodeId = nodes[destination].nodeId,
                linkId = linkId, floorId = nodes[a].floorId, destinationFloorId = nodes[b].floorId,
                traversalTicks = ticks };

        public int FloorIdAt(Vector3 position) => nodes.Length == 0 ? -1 : nodes[NearestNode(position)].floorId;

        public bool ClampToWalkable(Vector3 pos, float maxDist, out Vector3 onMesh)
        {
            onMesh = pos;
            return false;
        }

        public bool NextCorner(Vector3 from, Vector3 to, out Vector3 nextPos)
        {
            PathStep step = NextStep(from, to, -1);
            nextPos = step.next;
            return step.kind != MoveKind.None;
        }

        public float PathLength(Vector3 from, Vector3 to)
        {
            if (nodes.Length == 0) return -1f;
            int a = NearestNode(from), b = NearestNode(to);
            return distance[a, b] >= Infinity ? -1f : distance[a, b] / 100f;
        }

        public bool NearestDropEdge(Vector3 from, out Vector3 edge, out Vector3 landing)
        {
            int a = NearestNode(from);
            for (int b = 0; b < nodes.Length; b++)
            {
                int li = directLink[a, b];
                if (li >= 0 && links[li].traversalType == NavTraversalType.DropDown)
                { edge = nodes[a].position; landing = links[li].landingPosition; return true; }
            }
            edge = landing = from; return false;
        }

        int NearestNode(Vector3 p)
        {
            if (grid == null) return NearestLinear(p);

            int best = -1; float bestSq = float.MaxValue;
            int cx = Mathf.FloorToInt(p.x / gridCell), cz = Mathf.FloorToInt(p.z / gridCell);

            for (int ring = 0; ring <= gridMaxRing; ring++)
            {
                // 이번 고리의 셀들만 훑는다(이미 본 안쪽 고리는 건너뜀)
                for (int dx = -ring; dx <= ring; dx++)
                for (int dz = -ring; dz <= ring; dz++)
                {
                    if (ring > 0 && Mathf.Abs(dx) != ring && Mathf.Abs(dz) != ring) continue;
                    long key = ((long)(cx + dx) << 32) ^ ((long)(cz + dz) & 0xffffffffL);
                    if (!grid.TryGetValue(key, out var bucket)) continue;
                    for (int b = 0; b < bucket.Count; b++)
                    {
                        int i = bucket[b];
                        float sq = (nodes[i].position - p).sqrMagnitude;
                        // 동률 해소는 선형 스캔과 동일하게 nodeId 오름차순 → 결정론 보존
                        if (sq < bestSq - 1e-5f ||
                            (best >= 0 && Mathf.Abs(sq - bestSq) <= 1e-5f && nodes[i].nodeId < nodes[best].nodeId))
                        { best = i; bestSq = sq; }
                    }
                }
                // 이 고리 안쪽이 보장하는 반경보다 이미 가까우면 더 볼 필요 없음
                if (best >= 0)
                {
                    float guaranteed = ring * gridCell;
                    if (bestSq <= guaranteed * guaranteed) break;
                }
            }
            return best >= 0 ? best : NearestLinear(p);
        }

        int NearestLinear(Vector3 p)
        {
            int best = 0; float bestSq = float.MaxValue;
            for (int i = 0; i < nodes.Length; i++)
            {
                float sq = (nodes[i].position - p).sqrMagnitude;
                if (sq < bestSq - 1e-5f || (Mathf.Abs(sq - bestSq) <= 1e-5f && nodes[i].nodeId < nodes[best].nodeId))
                { best = i; bestSq = sq; }
            }
            return best;
        }

        static int IndexOf(ArenaNavNode[] ns, int id)
        {
            for (int i = 0; i < ns.Length; i++) if (ns[i].nodeId == id) return i;
            return -1;
        }

        static MoveKind ToMoveKind(NavTraversalType t)
        {
            switch (t)
            {
                case NavTraversalType.DropDown: return MoveKind.Drop;
                case NavTraversalType.BoostUp: return MoveKind.Boost;
                case NavTraversalType.JumpUp: return MoveKind.JumpUp;
                case NavTraversalType.Blocked: return MoveKind.None;
                default: return MoveKind.Walk;
            }
        }

        public static GraphPathfinder CreatePrototypeArena()
        {
            var bake = new ArenaMapBake
            {
                nodes = new[]
                {
                    N(0,-14,0,14,0), N(1,-12,0,14,0), N(2,-6.5f,4,14,1),
                    N(3,0,4,14,1), N(4,5,4,14,1), N(5,-4,4,16,1), N(6,4,4,12,1)
                },
                links = new[]
                {
                    L(0,0,1,NavTraversalType.Walk), L(1,1,0,NavTraversalType.Walk),
                    L(2,2,1,NavTraversalType.DropDown),
                    L(3,2,3,NavTraversalType.Walk), L(4,3,2,NavTraversalType.Walk),
                    L(5,3,4,NavTraversalType.Walk), L(6,4,3,NavTraversalType.Walk),
                    L(7,3,5,NavTraversalType.Walk), L(8,5,3,NavTraversalType.Walk),
                    L(9,3,6,NavTraversalType.Walk), L(10,6,3,NavTraversalType.Walk)
                }
            };
            bake.links[2].landingPosition = bake.nodes[1].position;
            return FromBake(bake);
        }

        /// <summary>실제 3층 아레나 그래프(17노드). 경사로·2F·3F·반층 Walk(양방향) + 다리→1F북 절벽 드롭.
        /// 손 authoring(기본). corridor 폭·agentMask 정밀 튜닝은 후속(전부 -1=모든 몹, Walk).</summary>
        public static ArenaMapBake CreateArenaBake()
        {
            var nodes = new[]
            {
                N(0, 0f,0f,0f, 0),      N(1, 14f,0f,-6f, 0),    N(2, -14f,0f,6f, 0),
                N(3, 10f,0f,-2f, 0),    N(4, -10f,0f,2f, 0),    N(5, -6f,0f,17f, 0),
                N(6, 6f,0f,-17f, 0),    N(7, 10f,4.5f,6f, 2),   N(8, 10f,4.5f,18f, 2),
                N(9, -10f,4.5f,-6f, 2), N(10, -10f,4.5f,-18f, 2),
                N(11, 18f,9f,20f, 3),   N(12, -18f,9f,-20f, 3), N(13, 0f,9f,22f, 3),
                N(14, -14f,3f,17f, 1),  N(15, 14f,3f,-17f, 1),  N(16, 0f,0f,20f, 0),
            };
            var links = new List<ArenaNavLink>();
            int id = 0;
            void W(int a, int b)
            {
                links.Add(new ArenaNavLink { linkId = id++, fromNodeId = a, toNodeId = b, traversalType = NavTraversalType.Walk, traversalTicks = 30, agentMask = -1 });
                links.Add(new ArenaNavLink { linkId = id++, fromNodeId = b, toNodeId = a, traversalType = NavTraversalType.Walk, traversalTicks = 30, agentMask = -1 });
            }
            W(0,1); W(0,2); W(0,3); W(0,4); W(0,5); W(0,6); W(1,3); W(2,4);   // 1F
            W(3,7); W(4,9);            // 1F→2F 경사로
            W(7,8); W(9,10);           // 2F
            W(8,11); W(10,12);         // 2F→3F 경사로
            W(11,13); W(12,13);        // 3F 다리
            W(5,14); W(6,15);          // 1F→반층 경사로
            W(0,16); W(5,16);          // 1F 북
            links.Add(new ArenaNavLink { linkId = id++, fromNodeId = 13, toNodeId = 16,
                traversalType = NavTraversalType.DropDown, traversalTicks = 30, agentMask = -1,
                landingPosition = nodes[16].position, landingSlotCount = 1 });   // 다리→1F북 절벽
            return new ArenaMapBake { mapVersion = 2, nodes = nodes, links = links.ToArray() };
        }

        public static GraphPathfinder CreateArena() => FromBake(CreateArenaBake());

        static ArenaNavNode N(int id,float x,float y,float z,int floor) => new ArenaNavNode { nodeId=id, position=new Vector3(x,y,z), floorId=floor, areaFlags=MapAreaFlags.Playable };
        static ArenaNavLink L(int id,int a,int b,NavTraversalType t) => new ArenaNavLink { linkId=id, fromNodeId=a, toNodeId=b, traversalType=t, traversalTicks=30, agentMask=-1, landingPosition=default };
    }
}
