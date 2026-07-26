using System.Runtime.InteropServices;
using UnityEngine;

namespace Game.Sim
{
    [StructLayout(LayoutKind.Explicit)]
    struct FloatBits { [FieldOffset(0)] public float f; [FieldOffset(0)] public uint u; }

    /// <summary>월드 상태 → 64비트. 결정론 검증용(같은 상태 두 번 굴려 해시 비교).</summary>
    public static class WorldHash
    {
        const ulong Offset = 14695981039346656037UL;
        const ulong Prime  = 1099511628211UL;

        public static ulong Compute(in SimWorld w)
        {
            ulong h = Offset;
            h = Mix(h, (ulong)w.tick);
            h = Mix(h, w.rngState);
            h = Mix(h, (ulong)w.nextEnemyId);   // id 발급기(재사용 방지) — 데싱크 감지용
            h = Mix(h, w.spawnLocked ? 1UL : 0UL);
            h = Mix(h, (ulong)w.waveId);
            h = Mix(h, (ulong)w.mapVersion);

            h = MixV(h, w.player.pos);
            h = MixV(h, w.player.vel);
            h = MixF(h, w.player.yaw);
            h = MixF(h, w.player.aimPitch);
            h = Mix(h, w.player.grounded ? 1UL : 0UL);
            h = MixV(h, w.player.lastGroundedPos);   // 적 추적 목표 — 빠지면 데싱크를 못 잡는다
            h = Mix(h, (ulong)w.player.jumpCount);
            h = Mix(h, (ulong)w.player.jumpBufferTicks);
            h = Mix(h, (ulong)w.player.jumpBoostTicks);
            h = MixV(h, w.player.jumpBoostDir);
            h = Mix(h, (ulong)w.player.dashTicks);
            h = MixV(h, w.player.dashDir);
            h = MixF(h, w.player.dashSpeed);
            h = Mix(h, (ulong)w.player.dashCharges);
            h = Mix(h, (ulong)w.player.dashRecharge);
            h = Mix(h, (ulong)w.player.dashBufferTicks);
            h = CombatHash.MixPlayer(h, in w.player.combat);   // combat 소유 해시

            h = Mix(h, (ulong)w.enemyCount);
            for (int i = 0; i < w.enemyCount; i++)
            {
                ref readonly EnemySim e = ref w.enemies[i];
                h = Mix(h, (ulong)e.id);
                h = Mix(h, e.alive ? 1UL : 0UL);
                h = MixV(h, e.pos);
                h = MixV(h, e.vel);
                h = MixF(h, e.yaw);
                h = Mix(h, e.grounded ? 1UL : 0UL);
                h = MixF(h, e.radius);
                h = MixF(h, e.height);
                h = MixV(h, e.waypoint);
                h = Mix(h, e.hasWaypoint ? 1UL : 0UL);
                h = Mix(h, (ulong)e.repathTicks);
                h = Mix(h, (ulong)e.descentPhase);
                h = Mix(h, (ulong)e.descentTicks);
                h = MixV(h, e.descentStart);
                h = MixV(h, e.descentLanding);
                h = Mix(h, (ulong)(e.currentNavNodeId + 1));
                h = Mix(h, (ulong)(e.destinationNavNodeId + 1));
                h = Mix(h, (ulong)(e.nextNavNodeId + 1));
                h = Mix(h, (ulong)(e.activeTraversalLinkId + 1));
                h = Mix(h, (ulong)(e.currentFloorId + 1));
                h = Mix(h, (ulong)e.traversalPhase);
                h = Mix(h, (ulong)e.activeMoveKind);
                h = Mix(h, (ulong)e.traversalTicks);
                h = Mix(h, (ulong)e.jumpDuration);
                h = MixV(h, e.jumpStart);
                h = MixV(h, e.jumpEnd);
                h = Mix(h, (ulong)(e.traversalSlot + 1));   // 착지 슬롯 점유
                h = Mix(h, (ulong)e.traversalPauseTicks);
                h = Mix(h, (ulong)e.traversalRecoverTicks);
                h = MixF(h, e.traversalClearance);
                h = MixF(h, e.traversalGravity);
                h = Mix(h, (ulong)e.launchTicks);           // 스폰 펄스 진행 상태
                h = MixF(h, e.personality);                 // 개체 고정 개성값
                h = CombatHash.MixEnemy(h, in e.combat);   // combat 소유 해시
                h = AIHash.MixAI(h, in e.ai);              // AI 소유 해시
            }
            h = AIHash.MixProjectiles(h, in w);           // AI 소유 해시(투사체)
            return h;
        }

        static ulong Mix(ulong h, ulong v) { h ^= v; h *= Prime; return h; }
        static ulong MixF(ulong h, float f) { FloatBits b; b.u = 0; b.f = f; return Mix(h, b.u); }
        static ulong MixV(ulong h, Vector3 v) { h = MixF(h, v.x); h = MixF(h, v.y); h = MixF(h, v.z); return h; }
    }
}
