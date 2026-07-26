using UnityEngine;

namespace Game.Sim
{
    public struct CastHit
    {
        public bool hit;
        public float distance;
        public Vector3 normal;
    }

    public interface ICollision
    {
        CastHit CapsuleCast(Vector3 bottom, Vector3 top, float radius, Vector3 dir, float maxDist);
        CastHit Raycast(Vector3 origin, Vector3 dir, float maxDist);
        bool SampleGround(Vector3 feet, float maxDown, out float groundY);
        bool HasLineOfSight(Vector3 from, Vector3 to);
        bool CanOccupyCapsule(Vector3 feet, float radius, float height);

        /// <summary>
        /// 캡슐이 지오메트리에 파고들어 있으면 빠져나올 보정 벡터를 낸다(안 겹치면 zero).
        ///
        /// 스윕 캐스트만으로는 관통을 막을 수 없다 — 유니티 스윕은 <b>시작 시점에 이미 겹친
        /// 콜라이더를 무시</b>하기 때문에, 한 번 안에 들어가면 그 벽이 없는 것처럼 통과한다.
        /// 매 틱 이걸 불러 밀어내면 "낀 상태" 자체가 성립하지 않아 관통이 원천 차단된다.
        /// </summary>
        Vector3 Depenetrate(Vector3 feet, float radius, float height);
    }

    public enum MoveKind : byte { None, Walk, JumpUp, Drop, Boost }

    public struct PathStep
    {
        public MoveKind kind;
        public Vector3 traversalStart;
        public Vector3 next;
        public int currentNodeId;
        public int nextNodeId;
        public int destinationNodeId;
        public int linkId;
        public int floorId;
        public int destinationFloorId;
        public int traversalTicks;

        // ── 층이동 탄도 파라미터(마커 Bake가 링크에 구워둔 값) ──
        // 값이 아니라 파라미터를 넘겨, 평상시·예측·에디터 프리뷰가 같은 함수로 궤적을 풀게 한다.
        public float clearance;     // 정점 여유 높이(0 = 자동)
        public float gravity;       // 0 = SimConfig 기본
        public int   pauseTicks;    // 도약 전 주저
        public int   recoverTicks;  // 착지 후 멈칫
        public int   slotCount;     // 착지 슬롯 수(검증 통과분)
        public float slotSpread;    // 슬롯 링 반경
    }

    public interface IPathfinder
    {
        PathStep NextStep(Vector3 from, Vector3 to, int agentMask);
        int FloorIdAt(Vector3 position);

        /// <summary>런타임 NavMesh 안전망. 고정 그래프 구현은 false를 반환한다.</summary>
        bool ClampToWalkable(Vector3 pos, float maxDist, out Vector3 onMesh);
    }

    public readonly struct SimServices
    {
        public readonly ICollision Collision;
        public readonly IPathfinder Pathfinder;
        public SimServices(ICollision collision, IPathfinder pathfinder)
        {
            Collision = collision;
            Pathfinder = pathfinder;
        }
    }
}
