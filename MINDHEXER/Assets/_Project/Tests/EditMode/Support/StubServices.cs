using UnityEngine;
using Game.Sim;

namespace Game.Sim.Tests
{
    /// <summary>
    /// 평지·벽 없음 가정의 최소 ICollision. 실제 지형 충돌을 검증하는 용도가 아니라,
    /// Sim/Prediction 로직 자체의 결정론을 씬·Physics 없이 EditMode에서 확인하기 위한 스텁이다.
    /// </summary>
    public sealed class StubCollision : ICollision
    {
        public CastHit CapsuleCast(Vector3 bottom, Vector3 top, float radius, Vector3 dir, float maxDist)
            => default; // 항상 안 막힘

        public CastHit Raycast(Vector3 origin, Vector3 dir, float maxDist)
            => default; // 항상 안 막힘

        public bool SampleGround(Vector3 feet, float maxDown, out float groundY)
        {
            groundY = 0f;
            return true; // 바닥은 어디서나 y=0
        }

        public bool HasLineOfSight(Vector3 from, Vector3 to) => true;

        public bool CanOccupyCapsule(Vector3 feet, float radius, float height) => true;
        public Vector3 Depenetrate(Vector3 feet, float radius, float height) => Vector3.zero;   // 벽 없음 → 겹칠 일 없음
    }

    /// <summary>from→to 직선만 반환하는 최소 IPathfinder. 정적 그래프 대신 테스트용.</summary>
    public sealed class StubPathfinder : IPathfinder
    {
        public PathStep NextStep(Vector3 from, Vector3 to, int agentMask)
            => new PathStep { kind = MoveKind.Walk, next = to, currentNodeId = 0, nextNodeId = 0,
                destinationNodeId = 0, linkId = -1, floorId = 0, destinationFloorId = 0 };
        public int FloorIdAt(Vector3 position) => 0;
        public bool ClampToWalkable(Vector3 pos, float maxDist, out Vector3 onMesh)
        { onMesh = pos; return false; }
        public bool NextCorner(Vector3 from, Vector3 to, out Vector3 next) { next = to; return true; }
        public float PathLength(Vector3 from, Vector3 to) => Vector3.Distance(from, to);
        public bool NearestDropEdge(Vector3 from, out Vector3 edge, out Vector3 landing)
        {
            edge = from; landing = from; return false;
        }
    }

    public static class StubServices
    {
        public static SimServices Create() => new SimServices(new StubCollision(), new StubPathfinder());
    }
}
