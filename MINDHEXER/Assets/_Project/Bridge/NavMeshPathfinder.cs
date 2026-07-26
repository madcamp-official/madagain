using UnityEngine;
using UnityEngine.AI;
using Game.Sim;

namespace Game.Bridge
{
    /// <summary>
    /// 평상시 길찾기 = NavMesh.CalculatePath → 다음 코너. 연속 메시라 기둥·벽 우회.
    ///
    /// 층이동 개편(docs/shared/층이동_개편_설계.md):
    ///  - 층 전환은 <b>기하 자동판정이 아니라 마커(NavMeshLink)</b>가 담당한다 → DropDetect 폐기.
    ///  - 게이팅은 <b>몹별 areaMask</b>로. 못 쓰는 링크는 경로 계산 단계에서 원천 배제된다.
    ///  - "지금 마커 링크에 진입하는가"는 <b>코너 좌표 매칭</b>으로 판정한다.
    ///    NavMeshPath가 corners/status만 노출해 링크 메타데이터가 <b>원천적으로 없기 때문</b>이다.
    ///    (실측: 링크 구간은 시작점→끝점이 연속 코너 쌍으로 나오고, 출발점과 겹치면 좌표가 중복된다.)
    /// </summary>
    public class NavMeshPathfinder : IPathfinder
    {
        /// <summary>Bake가 채워 주는 마커 링크 표(진입 판정·탄도 파라미터 조회용).</summary>
        public ArenaNavLink[] links = System.Array.Empty<ArenaNavLink>();

        /// <summary>큰 상승 링크에 붙는 Area 이름. 이 Area는 Traversal 특성 몹만 통과한다.</summary>
        public const string AreaLeapTraversal = "LeapTraversal";
        static int restrictedArea = -2;   // -2 = 아직 조회 안 함, -1 = 프로젝트에 없음

        const float SampleRadius = 4f;
        // 코너 ↔ 마커 출발점 허용 오차.
        // ★ NavMesh는 링크 진입점을 링크 "변" 위 아무 곳에나 잡는다(폭이 있으면 중심에서 ±폭/2).
        //   실측에서 0.50m 어긋났고, 오차를 0.35로 두었더니 접근 각도에 따라 매칭이 되다 말다 했다
        //   (= 간헐적으로 도약을 안 하던 원인). Baker가 폭 0으로 굽지만 여유를 넉넉히 둔다.
        const float MatchEpsilon   = 0.9f;
        const float FootEpsilon    = 1.2f;   // 몹이 발판 위에 섰다고 볼 수평 반경(도착 판정 0.6보다 넉넉히)
        const float LandingEpsilon = 1.5f;   // 다음 코너 ↔ 링크 착지점 허용 오차(착지점은 슬롯만큼 벌어짐)

        readonly NavMeshPath path = new NavMeshPath();

        public PathStep NextStep(Vector3 from, Vector3 to, int agentMask)
        {
            var step = new PathStep { kind = MoveKind.None, next = to, currentNodeId = -1,
                nextNodeId = -1, destinationNodeId = -1, linkId = -1, floorId = -1, destinationFloorId = -1 };
            int areaMask = AreaMaskFor(agentMask);
            if (!Calc(from, to, areaMask)) return step;

            Vector3[] c = path.corners;
            if (c.Length < 1) return step;

            // 연속 중복 코너 병합 — 출발점과 링크 시작점이 겹치면 같은 좌표가 두 번 나온다(실측).
            int nextIdx = 1;
            while (nextIdx < c.Length && (c[nextIdx] - c[0]).sqrMagnitude < 1e-4f) nextIdx++;
            Vector3 nc = nextIdx < c.Length ? c[nextIdx] : c[c.Length - 1];

            step.next = nc;
            step.traversalStart = from;

            // ── 마커 진입 판정 ──
            // (1) 이미 발판 위에 서 있는 경우. 이 경우 다음 코너는 링크 "출발점"이 아니라 "착지점"이라
            //     출발점만 보면 매칭이 실패해, 몹이 건널 수 없는 착지점으로 걸으려다 가장자리에서 떤다.
            int li = FindLinkAtFoot(from, nc, agentMask);
            // (2) 아직 접근 중인 경우 — 다음 코너가 링크 출발점.
            if (li < 0) li = FindLinkStartingNear(nc, agentMask);
            if (li >= 0) return TraversalStep(step, links[li], from);

            step.kind = MoveKind.Walk;
            return step;
        }

        /// <summary>링크 하나를 PathStep으로 변환(탄도 파라미터·슬롯 정보까지 실어 보냄).</summary>
        static PathStep TraversalStep(PathStep step, ArenaNavLink l, Vector3 from)
        {
            step.kind = l.traversalType == NavTraversalType.JumpUp ? MoveKind.JumpUp : MoveKind.Drop;
            step.traversalStart = l.traversalStartPosition;   // 발판까지 걸어간 뒤 도약
            step.next = l.landingPosition;
            step.linkId = l.linkId;
            step.traversalTicks = l.traversalTicks;
            step.clearance = l.clearance;
            step.gravity = l.gravity;
            step.pauseTicks = l.pauseTicks;
            step.recoverTicks = l.recoverTicks;
            step.slotCount = l.landingSlotCount;
            step.slotSpread = l.landingSpread;
            return step;
        }

        /// <summary>
        /// 몹이 이미 어떤 링크의 발판 위에 서 있는가. 오판을 막기 위해
        /// "현재 위치가 출발점 근처" + "다음 코너가 그 링크의 착지점"을 <b>동시에</b> 요구한다.
        /// </summary>
        int FindLinkAtFoot(Vector3 from, Vector3 nextCorner, int agentMask)
        {
            for (int i = 0; i < links.Length; i++)
            {
                if ((links[i].agentMask & agentMask) == 0) continue;
                Vector3 flat = links[i].traversalStartPosition - from; flat.y = 0f;
                if (flat.sqrMagnitude > FootEpsilon * FootEpsilon) continue;
                if ((links[i].landingPosition - nextCorner).sqrMagnitude > LandingEpsilon * LandingEpsilon) continue;
                return i;
            }
            return -1;
        }

        /// <summary>좌표가 어떤 마커 링크의 출발점과 (오차 내) 일치하고 그 몹이 쓸 수 있으면 그 인덱스.</summary>
        int FindLinkStartingNear(Vector3 p, int agentMask)
        {
            float bestSq = MatchEpsilon * MatchEpsilon;
            int best = -1;
            for (int i = 0; i < links.Length; i++)
            {
                if ((links[i].agentMask & agentMask) == 0) continue;
                float sq = (links[i].traversalStartPosition - p).sqrMagnitude;
                if (sq <= bestSq) { bestSq = sq; best = i; }
            }
            return best;
        }

        /// <summary>
        /// 몹 기동타입(agentMask) → 통과 가능한 NavMesh areaMask.
        /// Traversal이 아니면 큰 상승 링크(Area = LeapTraversal)를 배제 → NavMesh가 알아서 우회한다.
        /// 게이팅이 <b>경로 계산 단계</b>에서 끝나므로 실행 중 재검사가 필요 없다.
        /// </summary>
        static int AreaMaskFor(int agentMask)
        {
            if ((agentMask & (1 << (int)MobilityType.Traversal)) != 0) return NavMesh.AllAreas;
            if (restrictedArea == -2) restrictedArea = NavMesh.GetAreaFromName(AreaLeapTraversal);
            return restrictedArea > 0 ? (NavMesh.AllAreas & ~(1 << restrictedArea)) : NavMesh.AllAreas;
        }

        public int FloorIdAt(Vector3 position) => -1;

        bool Calc(Vector3 from, Vector3 to, int areaMask)
        {
            // 시작점이 폴리곤 모서리에 걸리면 질의가 실패할 수 있어(실측) 항상 면 위로 당긴다.
            if (!NavMesh.SamplePosition(from, out var f, SampleRadius, areaMask)) return false;
            if (!NavMesh.SamplePosition(to,   out var t, SampleRadius, areaMask)) return false;
            return NavMesh.CalculatePath(f.position, t.position, areaMask, path);
        }

        /// <summary>가장 가까운 navmesh 점(반경 안). navmesh는 에이전트 반경만큼 가장자리가 깎여 있어
        /// 이 점으로 당기면 몹이 얇은 다리 밖으로 안 나간다.</summary>
        public bool ClampToWalkable(Vector3 pos, float maxDist, out Vector3 onMesh)
        {
            if (NavMesh.SamplePosition(pos, out var hit, maxDist, NavMesh.AllAreas))
            { onMesh = hit.position; return true; }
            onMesh = pos; return false;
        }
    }
}
