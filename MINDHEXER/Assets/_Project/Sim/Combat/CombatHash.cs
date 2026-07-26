using System.Runtime.InteropServices;
using UnityEngine;

namespace Game.Sim
{
    /// <summary>
    /// 전투 상태의 결정론 해시. ★ combat 세션 소유.
    /// WorldHash가 이걸 호출한다. combat이 필드를 늘리면 여기만 고치면 되고,
    /// 공유 파일 WorldHash.cs는 안 건드린다. FNV-1a (WorldHash와 동일 상수).
    /// 다음 틱에 영향 주는 필드는 전부 포함(계약 6조).
    /// </summary>
    public static class CombatHash
    {
        [StructLayout(LayoutKind.Explicit)]
        struct FB { [FieldOffset(0)] public float f; [FieldOffset(0)] public uint u; }

        const ulong Prime = 1099511628211UL;
        static ulong Mix(ulong h, ulong v) { h ^= v; h *= Prime; return h; }
        static ulong MixF(ulong h, float f) { FB b; b.u = 0; b.f = f; return Mix(h, b.u); }
        static ulong MixV(ulong h, Vector3 v) { h = MixF(h, v.x); h = MixF(h, v.y); h = MixF(h, v.z); return h; }

        public static ulong MixPlayer(ulong h, in PlayerCombatState c)
        {
            h = Mix(h, c.attackPhase);
            h = Mix(h, (ulong)c.attackPhaseTicks);
            h = Mix(h, c.attackHitDone ? 1UL : 0UL);
            // ★ 콤보 상태도 sim 상태다 — 안 섞으면 "콤보 1단계"와 "초기 상태"가
            //   같은 해시로 잡혀 예지가 어긋난다.
            h = Mix(h, c.attackStep);
            h = Mix(h, c.comboStep);
            h = Mix(h, (ulong)c.comboWindow);
            h = Mix(h, c.attackBuffered ? 1UL : 0UL);
            h = Mix(h, c.attackHitMask0);
            h = Mix(h, c.attackHitMask1);
            h = Mix(h, (ulong)c.attackElapsed);
            h = Mix(h, (ulong)c.hp);
            h = Mix(h, (ulong)c.hitStunTicks);
            h = Mix(h, (ulong)c.invulnTicks);   // 다음 틱 피해판정에 영향 → 필수
            h = Mix(h, c.lungePhase);
            h = Mix(h, (ulong)c.lungeTicks);
            h = Mix(h, (ulong)(c.lungeTargetId + 1));   // -1 포함
            h = MixV(h, c.lungeStart);
            h = MixV(h, c.lungeDest);
            h = Mix(h, (ulong)c.lungeTravelTicks);
            h = Mix(h, c.lungeHitDone ? 1UL : 0UL);
            h = Mix(h, (ulong)c.lungeCooldown);
            h = Mix(h, (ulong)c.lungeStacks);
            h = Mix(h, (ulong)c.lungeBufferTicks);
            h = Mix(h, c.gloryPhase);
            h = Mix(h, (ulong)c.gloryTicks);
            h = Mix(h, (ulong)c.gloryTargetId);
            h = MixV(h, c.gloryDir);
            return h;
        }

        public static ulong MixEnemy(ulong h, in EnemyCombatState c)
        {
            h = Mix(h, (ulong)c.health);
            h = Mix(h, (ulong)c.stunTicks);
            h = Mix(h, (ulong)c.bindTicks);
            h = Mix(h, (ulong)c.deathTick);
            h = Mix(h, c.gloryStage);
            return h;
        }
    }
}
