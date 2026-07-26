using System.Collections.Generic;
using UnityEngine;
using Unity.AI.Navigation;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 씬의 <see cref="TraversalLink"/> 마커를 실제 길찾기 데이터로 굽는다.
    ///
    /// 마커 1개 → 방향별로
    ///   · <b>NavMeshLink</b>(평상시) — Area로 몹별 게이팅, 비용은 환산거리
    ///   · <b>ArenaNavLink</b>(예측 그래프) — agentMask로 게이팅, 같은 탄도 파라미터·같은 비용
    /// 둘을 <b>같은 소스에서</b> 만들기 때문에 평상시와 예측이 구조적으로 어긋나지 않는다.
    ///
    /// 게이트는 "링크"가 아니라 <b>진행 방향</b>에 걸리므로(내려갈 땐 누구나, 올라갈 땐 제한)
    /// 양방향 마커는 <b>단방향 링크 2개</b>로 굽는다.
    /// </summary>
    public static class TraversalBaker
    {
        /// <summary>NavMesh Area 이름. 프로젝트 Navigation 설정에 같은 이름으로 만들어 두면 자동으로 잡힌다.</summary>
        public const string AreaLeap          = "Leap";
        public const string AreaLeapTraversal = "LeapTraversal";

        /// <summary>런타임 NavMeshLink들을 담을 부모(정리·재베이크 편의).</summary>
        const string LinkRootName = "[TraversalLinks]";

        /// <summary>씬의 모든 마커를 굽는다. 반환 = 예측 그래프에 넣을 링크 목록.</summary>
        public static List<ArenaNavLink> Bake(IList<TraversalLink> markers, IList<ArenaNavNode> nodes,
                                              bool buildNavMeshLinks = true)
        {
            var result = new List<ArenaNavLink>();
            if (markers == null || markers.Count == 0) return result;

            Transform root = null;
            if (buildNavMeshLinks)
            {
                var existing = GameObject.Find(LinkRootName);
                if (existing != null) Object.Destroy(existing);
                root = new GameObject(LinkRootName).transform;
            }

            int nextLinkId = 0;
            int skipped = 0;
            foreach (var m in markers)
            {
                if (m == null) continue;

                // ★ 끝점을 NavMesh 위로 당긴다.
                // NavMesh는 에이전트 반경만큼 가장자리가 깎여 있어, 발판 끝에 찍은 좌표는 NavMesh 밖일 수 있다.
                // 그 상태로 링크를 만들면 연결이 안 되고, 몹이 발판까지 가서 맴돌기만 한다(부들부들).
                Vector3 high = SnapToNavMesh(m.High);
                Vector3 low  = SnapToNavMesh(m.Low);
                if ((high - low).sqrMagnitude < 1e-6f) continue;

                // 최소 clearance로도 궤적이 구조물을 뚫으면 굽지 않는다(무효 마커).
                // 이런 링크를 살려 두면 몹이 벽을 통과해 날아간다.
                if (m.IsBlocked)
                {
                    Debug.LogWarning($"[층이동] 무효 마커 건너뜀: {m.name} — 최소 높이로도 궤적이 막힘 " +
                                     $"(막힌 지점 {m.BlockPoint:F2}). 위치를 옮기거나 종류를 바꾸십시오.", m);
                    skipped++;
                    continue;
                }

                // ── 하강(High→Low): 항상 전 지상몹 허용 ──
                result.Add(MakeGraphLink(ref nextLinkId, m, high, low, ascend: false, nodes));
                if (buildNavMeshLinks) MakeNavMeshLink(root, m, high, low, ascend: false);

                // ── 상승(Low→High): 종류가 허용할 때만 ──
                if (m.AscendAllowed)
                {
                    result.Add(MakeGraphLink(ref nextLinkId, m, low, high, ascend: true, nodes));
                    if (buildNavMeshLinks) MakeNavMeshLink(root, m, low, high, ascend: true);
                }
            }
            if (skipped > 0)
                Debug.LogWarning($"[층이동] 무효 마커 {skipped}개를 건너뛰었습니다(궤적이 구조물을 뚫음).");
            return result;
        }

        // ── 그래프 링크(예측) ──
        static ArenaNavLink MakeGraphLink(ref int nextLinkId, TraversalLink m, Vector3 from, Vector3 to,
                                          bool ascend, IList<ArenaNavNode> nodes)
        {
            BallisticArc arc = TraversalBallistics.Solve(from, to, m.EffectiveClearance, m.gravity);
            int pause = m.PauseTicks, recover = m.RecoverTicks;
            int total = TraversalBallistics.TotalTicks(pause, arc.flightTicks, recover);

            return new ArenaNavLink
            {
                linkId = nextLinkId++,
                fromNodeId = NearestNodeId(nodes, from),
                toNodeId   = NearestNodeId(nodes, to),
                traversalType = ascend ? NavTraversalType.JumpUp : NavTraversalType.DropDown,
                traversalTicks = arc.flightTicks,
                heightDelta = to.y - from.y,
                dropHeight  = Mathf.Max(0f, from.y - to.y),
                agentMask = AgentMaskFor(m, ascend),
                landingPosition = to,
                traversalStartPosition = from,
                landingSlotCount = m.UsableSlotCount(to),   // 검증 통과한 슬롯만
                landingSpread = m.SlotSpread,
                clearance = m.EffectiveClearance,
                gravity   = m.gravity,
                pauseTicks = pause,
                recoverTicks = recover,
                costDistance = TraversalBallistics.CostDistance(total, SimConfig.EnemyMoveSpeed),
            };
        }

        /// <summary>
        /// 방향별 허용 몹 마스크. 하강은 전부, 상승은 종류에 따라 Traversal만.
        /// (공중몹은 마커를 쓰지 않으므로 어느 쪽에도 넣지 않는다 — 별도 항법은 이번 범위 밖.)
        /// </summary>
        static int AgentMaskFor(TraversalLink m, bool ascend)
        {
            int ground   = 1 << (int)MobilityType.Ground;
            int charge   = 1 << (int)MobilityType.Charge;
            int traversal= 1 << (int)MobilityType.Traversal;

            if (!ascend) return ground | charge | traversal;                 // 하강 = 전 지상몹
            return m.AscendTraversalOnly ? traversal : (ground | charge | traversal);
        }

        /// <summary>
        /// 좌표를 NavMesh 위로 당긴다. 링크 끝점이 NavMesh 밖이면 연결 자체가 안 되므로 필수.
        /// 못 찾으면 원래 좌표(그 경우 그 마커는 사실상 무효라 로그로 알린다).
        /// </summary>
        static Vector3 SnapToNavMesh(Vector3 p)
        {
            if (UnityEngine.AI.NavMesh.SamplePosition(p, out var hit, 3f, UnityEngine.AI.NavMesh.AllAreas))
                return hit.position;
            Debug.LogWarning($"[층이동] 끝점 {p:F2} 근처에 NavMesh가 없습니다 — 링크가 연결되지 않을 수 있습니다.");
            return p;
        }

        static int NearestNodeId(IList<ArenaNavNode> nodes, Vector3 p)
        {
            if (nodes == null || nodes.Count == 0) return -1;
            int best = 0; float bestSq = float.MaxValue;
            for (int i = 0; i < nodes.Count; i++)
            {
                float sq = (nodes[i].position - p).sqrMagnitude;
                if (sq < bestSq - 1e-5f || (Mathf.Abs(sq - bestSq) <= 1e-5f && nodes[i].nodeId < nodes[best].nodeId))
                { best = i; bestSq = sq; }
            }
            return nodes[best].nodeId;
        }

        // ── NavMeshLink(평상시) ──
        static void MakeNavMeshLink(Transform root, TraversalLink m, Vector3 from, Vector3 to, bool ascend)
        {
            var go = new GameObject((ascend ? "Up_" : "Down_") + m.name);
            go.transform.SetParent(root, false);
            go.transform.position = from;

            var link = go.AddComponent<NavMeshLink>();
            link.agentTypeID = 0;
            link.bidirectional = false;                    // 방향별 권한이 달라 항상 단방향 2개로 굽는다
            link.startPoint = Vector3.zero;
            link.endPoint   = go.transform.InverseTransformPoint(to);
            // ★ 폭 0 = 점 링크. 폭이 있으면 NavMesh가 진입점을 변 위 아무 곳에나 잡아
            //   경로 코너가 링크 좌표와 최대 폭/2만큼 어긋나고, 그러면 진입 판정이 되다 말다 한다.
            link.width = 0f;
            link.area = AreaIndex(ascend && m.AscendTraversalOnly ? AreaLeapTraversal : AreaLeap);

            // 비용: 도약의 실제 소요시간을 걸은 거리로 환산 → NavMesh가 "돌아갈까 뛸까"를 공정하게 비교.
            BallisticArc arc = TraversalBallistics.Solve(from, to, m.EffectiveClearance, m.gravity);
            int total = TraversalBallistics.TotalTicks(m.PauseTicks, arc.flightTicks, m.RecoverTicks);
            float costMeters = TraversalBallistics.CostDistance(total, SimConfig.EnemyMoveSpeed);
            float straight = Mathf.Max(0.01f, Vector3.Distance(from, to));
            link.costModifier = costMeters / straight;     // 배율로 지정

            link.UpdateLink();
        }

        /// <summary>이름으로 NavMesh Area 인덱스를 찾는다. 없으면 0(Walkable)으로 폴백.</summary>
        static int AreaIndex(string areaName)
        {
            var names = UnityEngine.AI.NavMesh.GetAreaNames();
            for (int i = 0; i < names.Length; i++)
                if (names[i] == areaName) return UnityEngine.AI.NavMesh.GetAreaFromName(areaName);
            return 0;
        }

        // 몹별 areaMask 유도는 NavMeshPathfinder가 소유한다(agentMask에서 직접 계산) — 진실을 한 곳에만 둔다.
    }
}
