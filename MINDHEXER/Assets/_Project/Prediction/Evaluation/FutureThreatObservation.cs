using UnityEngine;
using Game.Sim;

namespace Game.Prediction
{
    /// <summary>가중치와 분리된 미래 위험 관측값.</summary>
    public struct FutureThreatObservation
    {
        public bool playerAlive;
        public int playerHealth;
        public int aliveEnemyCount;
        public int attackWindupCount;
        public int nearestAttackActiveTicks;
        public int activeProjectileCount;
        public int nearestProjectileImpactTicks;
        public float nearestProjectileMissDistance;
        public int imminentChargeCount;
        public int nearestChargeImpactTicks;
        public int nearbyFlyingEnemyCount;       // 반경 내 공중 적 수(뷰/관측용). 감점 근거로는 더 안 쓴다.
        public int aimingFlyingEnemyCount;       // 그중 조준/발사 중(임박 대공 위협) — 감점 대상.
        public int strikeableFlyingEnemyCount;   // 런지 사거리·높이차 안(좌/우클릭 콤보로 처치 가능한 기회) — 가점 대상.

        /// <summary>
        /// [대공 등반, 2026-07-22] 아직 어떤 공중 액션으로도 닿지 않는(높이차 > LungeHeightTolerance)
        /// 공중 적에 대해, "얼마나 닿는 데 가까워졌는가"(0=한참 아래, 1=사거리 진입 직전).
        ///
        /// 왜 필요한가: 등반은 그 자체로는 피해도 처치도 안 낸다. 이 항이 없으면 올라가는 가지가
        /// 첫 스텝에서 점수 동률에 밀려 Beam에서 잘려나가고, AerialAscent 후보를 아무리 만들어도
        /// 최종 경로에는 절대 안 나타난다(형태 유도 항이 필요한 이유는 strikeableFlying과 같다).
        /// </summary>
        public float aerialAscentProgress01;
        /// <summary>닿지 않는 공중 적 수 — 등반 유도를 켤지 판단하는 데만 쓴다.</summary>
        public int unreachableFlyingEnemyCount;
        public int activeTraversalCount;
        public bool hasEscapeRoute;
    }

    public static class FutureThreatObserver
    {
        public static FutureThreatObservation Observe(in SimWorld world)
        {
            var result = new FutureThreatObservation
            {
                playerAlive = world.player.combat.hp > 0,
                playerHealth = world.player.combat.hp,
                nearestAttackActiveTicks = int.MaxValue,
                nearestProjectileImpactTicks = int.MaxValue,
                nearestProjectileMissDistance = float.MaxValue,
                nearestChargeImpactTicks = int.MaxValue,
            };

            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim enemy = ref world.enemies[i];
                if (!enemy.alive) continue;
                result.aliveEnemyCount++;

                bool isMeleeWindup = enemy.ai.combat == CombatType.Melee && enemy.ai.state == EnemyState.Windup;
                bool isRangedAim = enemy.ai.combat == CombatType.Ranged && enemy.ai.state == EnemyState.Aim;
                if (isMeleeWindup || isRangedAim)
                {
                    result.attackWindupCount++;
                    int windupTicks = isRangedAim ? AIConfig.RangedAimTicks : AIConfig.MeleeWindupTicks;
                    int ticks = Mathf.Max(0, windupTicks - enemy.ai.stateTicks);
                    if (ticks < result.nearestAttackActiveTicks)
                        result.nearestAttackActiveTicks = ticks;
                }

                ObserveSpecialMovement(in world, in enemy, ref result);
            }

            for (int i = 0; i < world.projectileCount; i++)
            {
                ref readonly Projectile projectile = ref world.projectiles[i];
                if (!projectile.alive) continue;
                result.activeProjectileCount++;
                ObserveClosestApproach(in world, in projectile, ref result);
            }
            return result;
        }

        static void ObserveSpecialMovement(
            in SimWorld world,
            in EnemySim enemy,
            ref FutureThreatObservation result)
        {
            if (enemy.traversalPhase != TraversalPhase.None)
                result.activeTraversalCount++;

            float horizontalDistance = FlatDistance(enemy.pos, world.player.pos);
            if (enemy.ai.mobility == MobilityType.Flying)
            {
                if (horizontalDistance <= PredictionScoreConfig.FlyingThreatRadius)
                {
                    result.nearbyFlyingEnemyCount++;
                    // 단순 부유는 회피 대상이 아니다 — 실제로 쏘려는(조준/발사) 공중 적만 임박 위협으로 감점.
                    if (enemy.ai.state == EnemyState.Aim || enemy.ai.state == EnemyState.Fire)
                        result.aimingFlyingEnemyCount++;
                }
                // 런지 사거리(수평) + 높이차 허용 안이면 "마무리 기회"로 집계 — Sim이 우클릭을
                // 대상 고도(enemy.y+LungeAimUp)로 착지시켜 좌클릭 사거리에 세우므로 실제로 처치 가능.
                // LOS는 서비스가 없어 여기선 생략(형태 유도용 근사 — 실제 판정은 ActionGenerator가 함).
                float heightGap = Mathf.Abs(enemy.pos.y - world.player.pos.y);
                if (horizontalDistance >= CombatConfig.LungeMinRange &&
                    horizontalDistance <= CombatConfig.LungeMaxRange &&
                    heightGap <= CombatConfig.LungeHeightTolerance)
                    result.strikeableFlyingEnemyCount++;

                // 닿지 않는 높이의 공중 적 — 등반 진행도를 잰다(위 aerialAscentProgress01 주석 참고).
                if (heightGap > CombatConfig.LungeHeightTolerance &&
                    horizontalDistance <= PredictionScoreConfig.AerialAscentRadius)
                {
                    result.unreachableFlyingEnemyCount++;
                    float deficit = heightGap - CombatConfig.LungeHeightTolerance;
                    float progress = Mathf.Clamp01(
                        1f - deficit / PredictionScoreConfig.AerialAscentReferenceGap);
                    if (progress > result.aerialAscentProgress01)
                        result.aerialAscentProgress01 = progress;
                }
            }

            if (enemy.ai.mobility != MobilityType.Charge ||
                (enemy.ai.state != EnemyState.Windup && enemy.ai.state != EnemyState.ChargeRun))
                return;

            Vector3 direction = enemy.ai.committedDir;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 1e-6f) return;
            direction.Normalize();

            Vector3 toPlayer = world.player.pos - enemy.pos;
            toPlayer.y = 0f;
            float along = Vector3.Dot(toPlayer, direction);
            if (along < 0f || along > AIConfig.ChargeMaxDist) return;

            float lateral = (toPlayer - direction * along).magnitude;
            float hitRadius = enemy.radius + SimConfig.PlayerRadius + 0.15f;
            if (lateral > hitRadius) return;

            int windup = enemy.ai.state == EnemyState.Windup
                ? Mathf.Max(0, AIConfig.ChargeWindupTicks - enemy.ai.stateTicks)
                : 0;
            int travel = Mathf.CeilToInt(along / AIConfig.ChargeSpeed * SimConfig.TickRate);
            int impactTicks = windup + travel;
            result.imminentChargeCount++;
            if (impactTicks < result.nearestChargeImpactTicks)
                result.nearestChargeImpactTicks = impactTicks;
        }

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x;
            float z = a.z - b.z;
            return Mathf.Sqrt(x * x + z * z);
        }

        static void ObserveClosestApproach(
            in SimWorld world,
            in Projectile projectile,
            ref FutureThreatObservation result)
        {
            // ProjectileSystem.Step의 실제 판정점(플레이어 몸통 구, 발밑이 아님)과 맞춘다.
            Vector3 torso = world.player.pos + Vector3.up * AIConfig.PlayerTorso;
            Vector3 relative = torso - projectile.pos;
            float speedSq = projectile.vel.sqrMagnitude;
            if (speedSq <= 1e-6f) return;
            float seconds = Mathf.Clamp(
                Vector3.Dot(relative, projectile.vel) / speedSq,
                0f,
                projectile.ttl * SimConfig.TickDelta);
            Vector3 closest = projectile.pos + projectile.vel * seconds;
            float missDistance = Vector3.Distance(closest, torso);
            int ticks = Mathf.CeilToInt(seconds * SimConfig.TickRate);
            if (missDistance < result.nearestProjectileMissDistance)
                result.nearestProjectileMissDistance = missDistance;
            if (missDistance <= AIConfig.ProjectileRadius + SimConfig.PlayerRadius &&
                ticks < result.nearestProjectileImpactTicks)
                result.nearestProjectileImpactTicks = ticks;
        }
    }
}
