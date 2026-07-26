using UnityEngine;
using Game.Sim;

namespace Game.Prediction
{
    /// <summary>
    /// 기회형(Opportunistic) 프로필 전용 관측값 — "다음 1초의 강한 공격 가능성"을 이루는
    /// 요소들을 가중치와 분리해서 담는다. FutureThreatObservation과 같은 패턴(관측/가중치 분리).
    ///
    /// 알려진 한계(구현 범위 밖): "엄폐 확보"·"층 이동 출구 확보"는 현재 Sim에 엄폐 플래그나
    /// Score() 시그니처에서 접근 가능한 경로찾기 질의가 없어 관측하지 못한다 — 새로 추가하려면
    /// ThreatEvaluator.Score 시그니처에 IPathfinder/SimServices를 새로 꿰어야 하는 더 큰 변경이라
    /// 이번엔 생략했다. "고지대"는 순수 높이 비교로 근사한다.
    /// 런지 가능 여부는 LOS 없이 사거리·높이차만으로 근사한다(FutureThreatObservation의
    /// strikeableFlyingEnemyCount와 동일한 근사 방식 — "형태 유도용", 실제 판정은 ActionGenerator/
    /// PlayerCombat.CanLunge가 함).
    /// </summary>
    public struct OpportunityObservation
    {
        public int lungeableCount;           // 사거리·높이 안(대략) 런지 가능해 보이는 적 수
        public int executionReadyLargeCount; // 다음 한 대로 처형 진입하는 Large 적 수
        public int coneEnemyCount;           // 평타 부채꼴 안에 든 적 수(2명 이상이면 더 유리)
        public int flankedRangedCount;       // 측면·후방을 잡은 원거리(솔저/카코) 적 수
        public bool hasHeightAdvantage;      // 근처 적보다 확연히 높은 곳에 있는가
        public bool dashPreserved;           // 대시 자원(1회 이상) 남음
        public bool lungePreserved;          // 런지 자원(1회 이상) 남음
        public bool readyToActNow;           // 평타/런지/글로리 전부 비어있어 즉시 다음 행동 가능
        public int surroundedExcessCount;    // 포위 허용치 초과분(안전 버킷의 것과 같은 기준 재사용)
        public bool allMobilityResourcesSpent; // 대시·런지 자원 전부 소진
        public bool lockedInDangerousRecovery; // 평타/런지로 조작이 묶인 채 위협 근접
    }

    public static class OpportunityObserver
    {
        public static OpportunityObservation Observe(in SimWorld world)
        {
            var obs = new OpportunityObservation();
            ref readonly PlayerSim player = ref world.player;
            Vector3 attackForward = CombatMath.Forward(player.yaw);

            int surroundedCount = 0;
            float nearestDistance = float.MaxValue;

            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim enemy = ref world.enemies[i];
                if (!enemy.alive || enemy.combat.gloryStage > 0) continue;

                float flatDistance = CombatMath.FlatDistance(player.pos, enemy.pos);
                if (flatDistance < nearestDistance) nearestDistance = flatDistance;
                if (flatDistance <= PredictionScoreConfig.OpportunitySurroundedRadius) surroundedCount++;

                // 런지 가능 근사: LOS 없이 사거리·높이차만(형태 유도용, 위 클래스 주석 참고).
                float heightGap = Mathf.Abs(enemy.pos.y - player.pos.y);
                if (heightGap <= CombatConfig.LungeHeightTolerance
                    && flatDistance >= CombatConfig.LungeMinRange
                    && flatDistance <= CombatConfig.LungeMaxRange + enemy.radius)
                    obs.lungeableCount++;

                if (enemy.ai.size == SizeClass.Large && enemy.combat.health <= CombatConfig.Damage)
                    obs.executionReadyLargeCount++;

                if (heightGap <= CombatConfig.AttackHeightTolerance
                    && CombatMath.InCone(player.pos, attackForward, enemy.pos,
                        CombatConfig.AttackConeRange + enemy.radius, CombatConfig.AttackConeHalfAngle))
                    obs.coneEnemyCount++;

                if (enemy.ai.combat == CombatType.Ranged)
                {
                    Vector3 enemyForward = CombatMath.Forward(enemy.yaw);
                    Vector3 towardPlayer = CombatMath.FlatDirection(enemy.pos, player.pos);
                    float facingDot = Vector3.Dot(enemyForward, towardPlayer);
                    if (facingDot < PredictionScoreConfig.OpportunityFlankDotThreshold)
                        obs.flankedRangedCount++;
                }
            }

            obs.surroundedExcessCount = Mathf.Max(0, surroundedCount - PredictionScoreConfig.SurroundedTolerance);
            obs.hasHeightAdvantage = nearestDistance < float.MaxValue
                && HasHeightAdvantage(in world, in player);

            obs.dashPreserved = player.dashCharges > 0;
            obs.lungePreserved = player.combat.lungeStacks > 0;
            obs.allMobilityResourcesSpent = player.dashCharges == 0 && player.combat.lungeStacks == 0;

            bool locked = player.combat.attackPhase != CombatConfig.PhNone
                || player.combat.lungePhase != CombatConfig.LgNone;
            obs.readyToActNow = !locked && player.combat.gloryPhase == CombatConfig.GlNone;
            obs.lockedInDangerousRecovery = locked && obs.surroundedExcessCount > 0;

            return obs;
        }

        /// <summary>"고지대": 반경 내 살아있는 적 전부보다 플레이어가 확연히(문턱 이상) 높은가.
        /// 엄폐·층 이동 출구는 관측 못 함(클래스 주석 참고) — 순수 높이 비교로만 근사.</summary>
        static bool HasHeightAdvantage(in SimWorld world, in PlayerSim player)
        {
            bool anyNearby = false;
            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim enemy = ref world.enemies[i];
                if (!enemy.alive || enemy.combat.gloryStage > 0) continue;
                if (CombatMath.FlatDistance(player.pos, enemy.pos) > PredictionScoreConfig.OpportunityHeightCheckRadius)
                    continue;
                anyNearby = true;
                if (player.pos.y - enemy.pos.y < PredictionScoreConfig.OpportunityHeightAdvantageMin)
                    return false;
            }
            return anyNearby;
        }
    }
}
