using UnityEngine;
using Game.Sim;

namespace Game.Prediction
{
    /// <summary>세 갈래 점수(안전·처치·난이도)와 Beam 정렬용 합산치.</summary>
    public struct ScoreBreakdown
    {
        public float safety;
        public float kill;
        public float difficulty;
        public float Total => safety + kill + difficulty;
    }

    /// <summary>
    /// 후보 점수화. docs/shared/PREDICTION_CONTRACT.md 12장 가중치(피격 -200/탈출경로 +80/
    /// 포위 무문턱 -30/남은 대시 +20/동일 행동 반복 -5)를 그대로 넣어봤다가, 시각화 도구로
    /// 재검증하는 과정에서 회귀를 발견했다: 적이 처음부터 사거리 밖(3~6마리 시나리오)이면
    /// Beam Search가 아예 접근을 안 함.
    ///
    /// 원인 두 가지:
    /// 1. 계약이 못박은 행동 순서상 Wait가 제일 앞이라, "아직 아무 일도 안 일어난" 동점
    ///    상황(이동/대기/후퇴가 전부 같은 점수)에서 Wait가 항상 동률 승리.
    /// 2. "남은 대시 1회 +20"은 대시를 쓰는 순간 사라지는 보상이라, 장거리 접근에 대시가
    ///    꼭 필요한 상황에서도 대시 자체를 안 쓰는 쪽으로 유도됨.
    ///
    /// 사용자와 상의 후, 평가 공식만 이전에 실제로 잘 동작했던 버전(HP·처치·피해·안전거리
    /// 포화·포위 문턱)으로 되돌리고 여기서부터 다시 발전시키기로 했다. 행동 생성 순서
    /// (ActionGenerator)와 반환 후보 개수(BeamSearch) 등 계약의 다른 부분은 그대로 둔다 —
    /// 이번 롤백은 평가 쪽에 한정한다.
    ///
    /// 사망 후보 점수는 그대로 유한값(PredictionScoreConfig.PlayerDeath)을 쓴다 — 이건
    /// 전멸 폴백에서 "가장 덜 나쁜" 후보를 고를 수 있게 해준 개선이라 이번 회귀와 무관하다.
    /// </summary>
    public static class ThreatEvaluator
    {
        public static ScoreBreakdown Score(
            in SimWorld world,
            int killCountNormal, int killCountMid, int damageDealt, int hitsTaken,
            bool isRepeatedAction)
        {
            int killCount = killCountNormal + killCountMid;
            var s = new ScoreBreakdown
            {
                kill = killCount * PredictionScoreConfig.KillWeight + damageDealt * PredictionScoreConfig.DamageWeight,
            };

            if (world.player.combat.hp <= 0)
            {
                s.safety = PredictionScoreConfig.PlayerDeath;
                return s;
            }

            // 미래 위험/기회 관측은 한 번만 계산해 감점·가점 양쪽에 재사용한다.
            FutureThreatObservation obs = FutureThreatObserver.Observe(in world);

            int scoredHp = Mathf.Min(world.player.combat.hp, PredictionScoreConfig.ScoredPlayerHpCap);
            if (world.player.combat.hp > PredictionScoreConfig.ScoredPlayerHpCap)
                scoredHp = Mathf.Max(0, PredictionScoreConfig.ScoredPlayerHpCap - hitsTaken);
            s.safety = scoredHp * PredictionScoreConfig.HpWeight
                     + SafetyBonus(in world) * PredictionScoreConfig.SafeDistanceWeight
                     - SurroundedExcess(in world) * PredictionScoreConfig.SurroundedWeight
                     - SpecialThreatPenalty(in obs);

            // 공중 적 마무리 기회(형태 유도): 아직 피해가 없는 접근/점프 중간 스텝이 Beam에서
            // 살아남아 실제 처치까지 이어지도록만 돕는 작은 가점. 실제 피해/처치가 항상 더 크다.
            s.kill += obs.strikeableFlyingEnemyCount * PredictionScoreConfig.AerialOpportunityWeight;

            // 대공 등반 유도: 닿는 공중 적이 이미 있으면(위 항) 그쪽이 우선이므로 등반은 안 민다.
            // 아무도 닿지 않을 때만 "고도를 얻는 방향"에 작은 가점을 줘서 그 가지가 Beam에서 살아남게 한다.
            if (obs.strikeableFlyingEnemyCount == 0 && obs.unreachableFlyingEnemyCount > 0)
                s.kill += obs.aerialAscentProgress01 * PredictionScoreConfig.AerialAscentProgressWeight;

            // difficulty는 이번 롤백에서 비움(계약의 대시 보존/반복 페널티가 회귀 원인이라 제외).
            return s;
        }

        /// <summary>
        /// 프로필별(안전형/절충형/공격형) 재조정. 기존 Score()가 계산한 세 버킷(safety/kill/
        /// difficulty)의 내부 로직은 그대로 재사용하고, safety·kill 버킷에 배율 + 최소 킬
        /// 확보 보너스만 얹는다 — 위험/기회 관측(FutureThreatObserver) 자체를 프로필마다
        /// 새로 만들지 않는다. 사망(hp≤0)이면 PlayerDeath 센티널을 그대로 보존한다(배율을
        /// 곱하면 프로필마다 다른 크기의 사망 점수가 나와 폴백 비교 로직이 왜곡된다).
        /// </summary>
        public static ScoreBreakdown Score(
            in SimWorld world,
            int killCountNormal, int killCountMid, int damageDealt, int hitsTaken,
            bool isRepeatedAction, in ScoreProfile profile)
            => Score(in world, killCountNormal, killCountMid, damageDealt, hitsTaken,
                isRepeatedAction, in profile, includeOpportunityTerminalBonus: true);

        /// <summary>
        /// Opportunity posture is a terminal-state objective. Intermediate search nodes must not
        /// earn it merely for preserving an action instead of taking that action.
        /// </summary>
        public static ScoreBreakdown Score(
            in SimWorld world,
            int killCountNormal, int killCountMid, int damageDealt, int hitsTaken,
            bool isRepeatedAction, in ScoreProfile profile, bool includeOpportunityTerminalBonus)
        {
            ScoreBreakdown s = Score(in world, killCountNormal, killCountMid, damageDealt, hitsTaken, isRepeatedAction);
            if (world.player.combat.hp <= 0) return s;

            s.safety *= profile.safetyMul;
            s.kill *= profile.killMul;

            if (profile.minKillThreshold > 0 && killCountNormal + killCountMid >= profile.minKillThreshold)
                s.kill += profile.minKillBonus;

            if (profile.useOpportunityBonus && includeOpportunityTerminalBonus)
                s.kill += OpportunityBonus(OpportunityObserver.Observe(in world));

            return s;
        }

        /// <summary>
        /// OpportunityObservation을 부호 있는 점수로 변환한다 — 기회형 프로필 전용.
        /// 우선순위(다음 처치 가능성 > 유리한 위치 > 자원 보존 > 현재 피해)를 값 크기로 반영:
        /// 처형 임박·런지 가능(다음 처치 가능성)이 가장 크고, 부채꼴·측후방·고지대(유리한 위치)가
        /// 중간, 자원 보존이 가장 작다. 감점 쪽은 "당장 피해는 됐지만 후속이 막힘"과 "위험한
        /// 위치에서 종료"를 하나(lockedInDangerousRecovery)로 묶어 반영했다(둘 다 본질적으로
        /// "조작이 묶인 채 위협에 노출"이라 별도 신호로 안 나눔).
        /// </summary>
        static float OpportunityBonus(in OpportunityObservation obs)
        {
            float bonus = 0f;

            // 다음 처치 가능성 (최우선)
            bonus += obs.lungeableCount * PredictionScoreConfig.OpportunityLungeableWeight;
            bonus += obs.executionReadyLargeCount * PredictionScoreConfig.OpportunityExecutionReadyWeight;

            // 유리한 위치
            bonus += obs.coneEnemyCount * PredictionScoreConfig.OpportunityConeEnemyWeight;
            bonus += obs.flankedRangedCount * PredictionScoreConfig.OpportunityFlankWeight;
            if (obs.hasHeightAdvantage) bonus += PredictionScoreConfig.OpportunityHeightAdvantageBonus;

            // 자원 보존
            if (obs.dashPreserved) bonus += PredictionScoreConfig.OpportunityResourcePreserveWeight;
            if (obs.lungePreserved) bonus += PredictionScoreConfig.OpportunityResourcePreserveWeight;
            if (obs.readyToActNow) bonus += PredictionScoreConfig.OpportunityReadyToActBonus;

            // 감점: 적 무리 중앙 종료, 모든 이동 자원 소진, 위험한 위치에서 조작 묶임
            bonus -= obs.surroundedExcessCount * PredictionScoreConfig.OpportunitySurroundedCenterPenalty;
            if (obs.allMobilityResourcesSpent) bonus -= PredictionScoreConfig.OpportunityAllResourcesSpentPenalty;
            if (obs.lockedInDangerousRecovery) bonus -= PredictionScoreConfig.OpportunityLockedInDangerPenalty;

            return bonus;
        }

        static float SafetyBonus(in SimWorld world)
        {
            float nearest = float.MaxValue;
            bool anyAlive = false;
            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim enemy = ref world.enemies[i];
                if (!enemy.alive) continue;
                anyAlive = true;
                float distance = CombatMath.FlatDistance(world.player.pos, enemy.pos);
                if (distance < nearest) nearest = distance;
            }
            if (!anyAlive) return PredictionScoreConfig.SafeDistanceCap;
            return Mathf.Min(nearest, PredictionScoreConfig.SafeDistanceCap);
        }

        /// <summary>
        /// 원거리 솔저 투사체가 명중 궤도(FutureThreatObserver 기준)면, 임박할수록 커지는 감점을 준다.
        /// 명중이 이번 매크로 스텝 시야 밖(더 뒤)이어도 미리 피하도록 유도 — 새 원거리 적 인지용 항목.
        /// </summary>
        static float SpecialThreatPenalty(in FutureThreatObservation obs)
        {
            float penalty = 0f;
            if (obs.nearestProjectileImpactTicks < PredictionScoreConfig.ProjectileImpactHorizonTicks)
            {
                int urgency = PredictionScoreConfig.ProjectileImpactHorizonTicks - obs.nearestProjectileImpactTicks;
                penalty += urgency * PredictionScoreConfig.ProjectileImpactWeight;
            }
            if (obs.nearestChargeImpactTicks < PredictionScoreConfig.ChargeImpactHorizonTicks)
            {
                int urgency = PredictionScoreConfig.ChargeImpactHorizonTicks - obs.nearestChargeImpactTicks;
                penalty += urgency * PredictionScoreConfig.ChargeImpactWeight;
            }
            // 조준/발사 중인 공중 적만 감점 — 단순 부유는 회피가 아니라 처치 대상(위 kill 가점 참고).
            penalty += obs.aimingFlyingEnemyCount * PredictionScoreConfig.FlyingThreatWeight;
            penalty += obs.activeTraversalCount * PredictionScoreConfig.TraversalCommitmentWeight;
            return penalty;
        }

        static int SurroundedExcess(in SimWorld world)
        {
            int count = 0;
            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim enemy = ref world.enemies[i];
                if (!enemy.alive) continue;
                if (CombatMath.FlatDistance(world.player.pos, enemy.pos) <= PredictionScoreConfig.SurroundedRadius)
                    count++;
            }
            return Mathf.Max(0, count - PredictionScoreConfig.SurroundedTolerance);
        }
    }
}
