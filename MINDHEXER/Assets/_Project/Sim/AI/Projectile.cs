using UnityEngine;

namespace Game.Sim
{
    /// <summary>플라즈마 투사체. ★ AI 세션 소유. 유도 없음 → 회피 가능.</summary>
    public struct Projectile
    {
        public Vector3 pos;
        public Vector3 vel;
        public int     ttl;
        public bool    alive;
    }

    /// <summary>
    /// 투사체 갱신. ★ AI 세션 소유. SimStep이 적 AI 다음, CombatResolve 앞에 호출.
    /// 지형 맞으면 소멸, 플레이어 몸통 구에 닿으면 히트 큐잉(방어판정은 CombatResolve).
    /// </summary>
    public static class ProjectileSystem
    {
        public static void Step(ref SimWorld w, in SimServices svc, float dt)
        {
            Vector3 pc = w.player.pos + Vector3.up * AIConfig.PlayerTorso;
            float hitR = SimConfig.PlayerRadius + AIConfig.ProjectileRadius;

            for (int i = 0; i < w.projectileCount; i++)
            {
                ref Projectile pr = ref w.projectiles[i];
                if (!pr.alive) continue;

                Vector3 step = pr.vel * dt;
                float dist = step.magnitude;
                if (dist > 1e-6f)
                {
                    Vector3 dir = step / dist;
                    // 지형 충돌 → 소멸
                    if (svc.Collision.Raycast(pr.pos, dir, dist).hit) { pr.alive = false; continue; }
                    // 플레이어 충돌(선분 vs 구) → 히트 큐 + 소멸
                    if (CombatHit.SegmentHitsSphere(pr.pos, pr.pos + step, pc, hitR))
                    {
                        w.QueuePlayerHit(dir, AIConfig.RangedDamage);
                        pr.alive = false;
                        continue;
                    }
                    pr.pos += step;
                }

                if (--pr.ttl <= 0) pr.alive = false;
            }
        }
    }
}
