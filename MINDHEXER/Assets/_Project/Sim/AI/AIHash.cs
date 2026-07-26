using System.Runtime.InteropServices;

namespace Game.Sim
{
    /// <summary>
    /// 몹 AI 상태의 결정론 해시. ★ AI 세션 소유. WorldHash가 위임 호출.
    /// AI가 필드를 늘리면 여기만 고치고 공유 WorldHash.cs는 안 건드린다.
    /// </summary>
    public static class AIHash
    {
        [StructLayout(LayoutKind.Explicit)]
        struct FB { [FieldOffset(0)] public float f; [FieldOffset(0)] public uint u; }

        const ulong Prime = 1099511628211UL;
        static ulong Mix(ulong h, ulong v) { h ^= v; h *= Prime; return h; }
        static ulong MixF(ulong h, float f) { FB b; b.u = 0; b.f = f; return Mix(h, b.u); }

        public static ulong MixAI(ulong h, in EnemyAI a)
        {
            h = Mix(h, (byte)a.combat);
            h = Mix(h, (byte)a.mobility);
            h = Mix(h, (byte)a.size);
            h = Mix(h, (byte)a.state);
            h = Mix(h, (ulong)a.stateTicks);
            h = MixF(h, a.committedDir.x);
            h = MixF(h, a.committedDir.y);
            h = MixF(h, a.committedDir.z);
            h = Mix(h, (ulong)a.attackCooldown);
            h = Mix(h, a.hitDone ? 1UL : 0UL);
            h = MixF(h, a.beamDir.x); h = MixF(h, a.beamDir.y); h = MixF(h, a.beamDir.z);   // 보스 빔 방향
            h = MixF(h, a.anchor.x); h = MixF(h, a.anchor.y); h = MixF(h, a.anchor.z);      // 보스 고정 좌표
            h = Mix(h, a.anchorSet ? 1UL : 0UL);
            return h;
        }

        public static ulong MixProjectiles(ulong h, in SimWorld w)
        {
            h = Mix(h, (ulong)w.projectileCount);
            for (int i = 0; i < w.projectileCount; i++)
            {
                ref readonly Projectile p = ref w.projectiles[i];
                h = Mix(h, p.alive ? 1UL : 0UL);
                if (!p.alive) continue;
                h = MixF(h, p.pos.x); h = MixF(h, p.pos.y); h = MixF(h, p.pos.z);
                h = MixF(h, p.vel.x); h = MixF(h, p.vel.y); h = MixF(h, p.vel.z);
                h = Mix(h, (ulong)p.ttl);
            }
            return h;
        }
    }
}
