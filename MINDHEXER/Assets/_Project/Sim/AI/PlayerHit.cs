using UnityEngine;

namespace Game.Sim
{
    /// <summary>
    /// 적이 플레이어에게 넣는 히트 1건. ★ AI 세션 소유.
    /// dir = 히트 진행 방향(적→플레이어). CombatResolve가 이 방향으로 정면 막기 판정.
    /// </summary>
    public struct PlayerHit
    {
        public Vector3 dir;
        public int     dmg;
    }
}
