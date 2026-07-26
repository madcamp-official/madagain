using System;

namespace Game.Sim
{
    /// <summary>월드 깊은 복사. 예측이 "현재를 복제해 미래를 굴리기" 위한 도구.</summary>
    public static class Snapshot
    {
        public static SimWorld Clone(in SimWorld src)
        {
            SimWorld dst = src;                            // 값 필드 복사
            dst.enemies = new EnemySim[src.enemies.Length];
            Array.Copy(src.enemies, dst.enemies, src.enemies.Length);
            if (src.pendingHits != null)                   // 히트 큐도 독립 배열로(예지 복제 안전)
            {
                dst.pendingHits = new PlayerHit[src.pendingHits.Length];
                Array.Copy(src.pendingHits, dst.pendingHits, src.pendingHits.Length);
            }
            if (src.projectiles != null)                   // 투사체도 독립 배열로
            {
                dst.projectiles = new Projectile[src.projectiles.Length];
                Array.Copy(src.projectiles, dst.projectiles, src.projectiles.Length);
            }
            return dst;
        }

        // >>> [예측 세션 추가, 2026-07-18] CopyTo 메서드 전체가 새로 추가된 것 — 원래 core-controls
        // 버전엔 Clone만 있었다. 예측의 WorldBufferPool이 후보 확장마다 new SimWorld를 만들지
        // 않고 기존 배열을 재사용하려면 이 "제자리 복사"가 필요해서 되살렸다. 되돌리려면 이
        // 메서드 전체를 지우면 된다(WorldBufferPool.CopyInto가 이걸 호출하니 그쪽도 같이 손봐야 함).
        /// <summary>dst의 배열을 재사용해 src를 깊은 복사(할당 없음). WorldBufferPool 전용 — 후보마다 new SimWorld를 만들지 않기 위함.</summary>
        public static void CopyTo(in SimWorld src, ref SimWorld dst)
        {
            if (dst.enemies == null || dst.enemies.Length < src.enemies.Length)
                dst.enemies = new EnemySim[src.enemies.Length];
            Array.Copy(src.enemies, dst.enemies, src.enemies.Length);

            if (src.pendingHits != null)
            {
                if (dst.pendingHits == null || dst.pendingHits.Length < src.pendingHits.Length)
                    dst.pendingHits = new PlayerHit[src.pendingHits.Length];
                Array.Copy(src.pendingHits, dst.pendingHits, src.pendingHits.Length);
            }
            if (src.projectiles != null)
            {
                if (dst.projectiles == null || dst.projectiles.Length < src.projectiles.Length)
                    dst.projectiles = new Projectile[src.projectiles.Length];
                Array.Copy(src.projectiles, dst.projectiles, src.projectiles.Length);
            }

            dst.tick = src.tick;
            dst.player = src.player;
            dst.enemyCount = src.enemyCount;
            dst.rngState = src.rngState;
            dst.pendingHitCount = src.pendingHitCount;
            dst.projectileCount = src.projectileCount;
            dst.prevPlayerPos = src.prevPlayerPos;
            dst.spawnLocked = src.spawnLocked;
            dst.waveId = src.waveId;
            dst.mapVersion = src.mapVersion;
        }
        // <<< [예측 세션 추가 끝]
    }
}
