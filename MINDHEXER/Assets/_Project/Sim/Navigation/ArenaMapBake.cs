using System;
using UnityEngine;

namespace Game.Sim
{
    [Flags]
    public enum MapAreaFlags : ushort
    {
        None = 0,
        Playable = 1 << 0,
        Blocked = 1 << 1,
        LungeForbidden = 1 << 2,
        FallHazard = 1 << 3,
        EscapeCandidate = 1 << 4,
    }

    public enum NavTraversalType : byte
    {
        Walk, RampUp, RampDown, StairUp, StairDown, JumpUp, BoostUp, DropDown, Blocked
    }

    [Serializable]
    public struct ArenaNavNode
    {
        public int nodeId;
        public Vector3 position;
        public int floorId;
        public MapAreaFlags areaFlags;
    }

    [Serializable]
    public struct ArenaNavLink
    {
        public int linkId;
        public int fromNodeId;
        public int toNodeId;
        public NavTraversalType traversalType;
        public int traversalTicks;
        public float heightDelta;
        public float dropHeight;
        public int agentMask;
        public Vector3 landingPosition;
        public int landingSlotCount;
        public float landingSpread;

        // ── 층이동 개편(탄도) — 마커 Bake가 채운다 ──
        // 궤적은 (fromNode 위치 → landingPosition, clearance, gravity)로 재구성한다.
        // 값을 통째로 굽지 않고 파라미터만 두는 이유: 평상시·예측이 같은 함수로 풀어 반드시 일치시키기 위해.
        public Vector3 traversalStartPosition;  // 도약 발판(출발 좌표). NavMesh 경로 코너와 매칭해 진입을 판정한다
        public float clearance;      // 정점 여유 높이(0이면 SimConfig 기본 비율로 자동)
        public float gravity;        // 0이면 SimConfig.TraversalGravity
        public int   pauseTicks;     // 도약 전 주저(길이 비례로 구워짐)
        public int   recoverTicks;   // 착지 후 멈칫
        public float costDistance;   // 길찾기 비용(= 소요시간을 걸은 거리로 환산). 0이면 직선거리 사용
    }

    [Serializable]
    public sealed class ArenaMapBake
    {
        public int mapVersion = 1;
        public ArenaNavNode[] nodes = Array.Empty<ArenaNavNode>();
        public ArenaNavLink[] links = Array.Empty<ArenaNavLink>();
    }
}
