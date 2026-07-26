using UnityEngine;
using Game.Sim;

namespace Game.Prediction
{
    public readonly struct PlayerIntentContext
    {
        public readonly int salientEnemyId;
        public readonly int initialHealth;
        public readonly float initialDistance;

        public PlayerIntentContext(int salientEnemyId, int initialHealth, float initialDistance)
        {
            this.salientEnemyId = salientEnemyId;
            this.initialHealth = initialHealth;
            this.initialDistance = initialDistance;
        }
    }

    public readonly struct SafetyIntentContext
    {
        public readonly int criticalEnemyId;

        public SafetyIntentContext(int criticalEnemyId)
        {
            this.criticalEnemyId = criticalEnemyId;
        }
    }

    /// <summary>예측 시작 시 탈출을 방해하는 핵심 위협 한 명을 고정한다.</summary>
    public static class SafetyIntentEvaluator
    {
        public static SafetyIntentContext Capture(in SimWorld world)
        {
            int bestId = -1;
            float bestScore = float.NegativeInfinity;
            Vector3 forward = CombatMath.Forward(world.player.yaw);
            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim enemy = ref world.enemies[i];
                if (!enemy.alive || enemy.combat.gloryStage > 0) continue;
                Vector3 delta = enemy.pos - world.player.pos;
                delta.y = 0f;
                float distance = delta.magnitude;
                bool nearbyBlocker = distance <= PredictionScoreConfig.SurroundedRadius;
                bool imminent = enemy.ai.state == EnemyState.Windup
                             || enemy.ai.state == EnemyState.ChargeRun;
                bool aimingFlying = enemy.ai.mobility == MobilityType.Flying
                                 && (enemy.ai.state == EnemyState.Aim || enemy.ai.state == EnemyState.Fire);
                if (!nearbyBlocker && !imminent && !aimingFlying) continue;

                float facing = distance > 1e-5f ? Vector3.Dot(forward, delta / distance) : 1f;
                float score = (aimingFlying ? 200f : 0f)
                            + (imminent ? 140f : 0f)
                            + (nearbyBlocker ? 60f : 0f)
                            + facing * 12f - Mathf.Min(distance, 20f);
                if (score > bestScore
                    || (Mathf.Approximately(score, bestScore) && (bestId < 0 || enemy.id < bestId)))
                {
                    bestScore = score;
                    bestId = enemy.id;
                }
            }
            return new SafetyIntentContext(bestId);
        }

        public static float CriticalThreatRemoved(in SimWorld world, in SafetyIntentContext context)
        {
            if (context.criticalEnemyId < 0) return 0f;
            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim enemy = ref world.enemies[i];
                if (enemy.id != context.criticalEnemyId) continue;
                return !enemy.alive || enemy.combat.gloryStage > 0 ? 1f : 0f;
            }
            return 1f;
        }
    }

    /// <summary>
    /// 시작 시점의 주목 대상, 종료 위치 품질, 조작 난이도를 결정론적으로 평가한다.
    /// 후보 루프에서 Physics/NavMesh를 조회하지 않는다.
    /// </summary>
    public static class PlayerIntentEvaluator
    {
        public static PlayerIntentContext Capture(in SimWorld world)
        {
            int bestId = -1;
            int bestHealth = 0;
            float bestDistance = 0f;
            float bestScore = float.NegativeInfinity;
            Vector3 forward = Quaternion.Euler(0f, world.player.yaw, 0f) * Vector3.forward;
            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim enemy = ref world.enemies[i];
                if (!enemy.alive || enemy.combat.gloryStage > 0) continue;
                Vector3 delta = enemy.pos - world.player.pos;
                delta.y = 0f;
                float distance = delta.magnitude;
                float facing = distance > 1e-4f ? Vector3.Dot(forward, delta / distance) : 1f;
                float score = facing * 18f - Mathf.Min(distance, 20f);
                if (enemy.combat.health <= CombatConfig.Damage) score += 8f;
                if (score > bestScore || (Mathf.Approximately(score, bestScore) && enemy.id < bestId))
                {
                    bestScore = score;
                    bestId = enemy.id;
                    bestHealth = enemy.combat.health;
                    bestDistance = distance;
                }
            }
            return new PlayerIntentContext(bestId, bestHealth, bestDistance);
        }

        public static float SalientTargetProgress(in SimWorld world, in PlayerIntentContext context)
        {
            if (context.salientEnemyId < 0) return 0f;
            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim enemy = ref world.enemies[i];
                if (enemy.id != context.salientEnemyId) continue;
                bool defeated = !enemy.alive || enemy.combat.gloryStage > 0;
                int damage = defeated ? context.initialHealth : Mathf.Max(0, context.initialHealth - enemy.combat.health);
                float distance = CombatMath.FlatDistance(world.player.pos, enemy.pos);
                float approach = defeated ? 0f : Mathf.Clamp(context.initialDistance - distance, -4f, 6f) * 1.5f;
                return damage * 5f + (defeated ? 24f : 0f) + approach;
            }
            return context.initialHealth * 5f + 24f;
        }

        public static float TerminalPositionQuality(in SimWorld world)
        {
            if (world.player.combat.hp <= 0) return 0f;
            float nearest = PredictionScoreConfig.TerminalNearestDistanceCap;
            int nearby = 0;
            float heightAdvantage = 0f;
            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim enemy = ref world.enemies[i];
                if (!enemy.alive) continue;
                float distance = CombatMath.FlatDistance(world.player.pos, enemy.pos);
                nearest = Mathf.Min(nearest, distance);
                if (distance <= PredictionScoreConfig.SurroundedRadius) nearby++;
                if (distance <= 8f) heightAdvantage = Mathf.Max(heightAdvantage, world.player.pos.y - enemy.pos.y);
            }
            bool actionable = world.player.combat.attackPhase == CombatConfig.PhNone
                           && world.player.combat.lungePhase == CombatConfig.LgNone;
            FutureThreatObservation threats = FutureThreatObserver.Observe(in world);
            float score = Mathf.Min(nearest, PredictionScoreConfig.TerminalNearestDistanceCap) * 2f
                        - Mathf.Max(0, nearby - 2) * PredictionScoreConfig.TerminalNearbyPenalty
                        + Mathf.Clamp(heightAdvantage, 0f, 4f) * PredictionScoreConfig.TerminalHeightWeight
                        + CountOpenEscapeSectors(in world) * PredictionScoreConfig.TerminalOpenSectorWeight;
            if (actionable) score += PredictionScoreConfig.TerminalReadyToActBonus;
            if (world.player.grounded) score += PredictionScoreConfig.TerminalGroundedBonus;
            if (world.player.dashCharges > 0) score += PredictionScoreConfig.TerminalDashReserveBonus;
            if (threats.nearestProjectileImpactTicks < PredictionScoreConfig.ProjectileImpactHorizonTicks)
                score -= PredictionScoreConfig.TerminalImminentProjectilePenalty;
            if (threats.nearestChargeImpactTicks < PredictionScoreConfig.ChargeImpactHorizonTicks)
                score -= PredictionScoreConfig.TerminalImminentChargePenalty;
            return score;
        }

        /// <summary>
        /// 수평 8방향 중 가까운 적에게 바로 막히지 않은 방향 수. 런타임 NavMesh를 조회하지 않는
        /// 저비용 근사이며, 막다른 지형의 최종 판정은 베이크 그래프 행동 후보가 담당한다.
        /// </summary>
        static int CountOpenEscapeSectors(in SimWorld world)
        {
            int open = 0;
            for (int sector = 0; sector < 8; sector++)
            {
                float angle = sector * 45f * Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                bool blocked = false;
                for (int i = 0; i < world.enemyCount; i++)
                {
                    ref readonly EnemySim enemy = ref world.enemies[i];
                    if (!enemy.alive) continue;
                    Vector3 delta = enemy.pos - world.player.pos;
                    delta.y = 0f;
                    float distance = delta.magnitude;
                    if (distance > PredictionScoreConfig.TerminalOpenSectorRadius || distance <= 1e-5f)
                        continue;
                    float forward = Vector3.Dot(delta, direction);
                    float lateral = Mathf.Abs(Vector3.Dot(
                        delta, new Vector3(direction.z, 0f, -direction.x)));
                    if (forward > 0f && lateral < 1.5f)
                    {
                        blocked = true;
                        break;
                    }
                }
                if (!blocked) open++;
            }
            return open;
        }

        public static float ActionDifficulty(
            in MacroAction action, in SimWorld parentWorld, in MacroAction previousAction, bool hasPreviousAction)
        {
            float cost;
            switch (action.type)
            {
                case MacroActionType.Wait: cost = 0f; break;
                case MacroActionType.MoveForward:
                case MacroActionType.MoveLeft:
                case MacroActionType.MoveRight:
                case MacroActionType.Retreat: cost = 0.25f; break;
                case MacroActionType.Attack: cost = 0.5f; break;
                case MacroActionType.Jump: cost = 0.8f; break;
                case MacroActionType.DashForward:
                case MacroActionType.DashBackward:
                case MacroActionType.DashLeft:
                case MacroActionType.DashRight: cost = 1f; break;
                case MacroActionType.Lunge: cost = 1.2f; break;
                case MacroActionType.JumpStrike: cost = 1.7f; break;
                case MacroActionType.LungeStrike: cost = 2f; break;
                case MacroActionType.TerrainLeap: cost = 2.2f; break;
                case MacroActionType.AerialPursuit: cost = 3.2f; break;
                default: cost = 1f; break;
            }
            float resolvedYaw = action.ResolveYaw(in parentWorld, parentWorld.player.yaw);
            cost += Mathf.Abs(Mathf.DeltaAngle(parentWorld.player.yaw, resolvedYaw)) / 180f;
            if (hasPreviousAction && previousAction.lungeTargetId >= 0 && action.lungeTargetId >= 0
                && previousAction.lungeTargetId != action.lungeTargetId)
                cost += 1.5f;
            return cost;
        }
    }

    /// <summary>
    /// 후보 평가 프로필. 같은 ScoreBreakdown(safety/kill/difficulty)을 서로 다른 목적으로
    /// 재조정한다 — 새 판정 공식을 따로 만드는 게 아니라 기존 세 버킷의 가중치만 바꾼다.
    /// 값은 전부 잠정(첫 튜닝)이며, 실사용하며 조정 대상이다.
    /// </summary>
    public readonly struct ScoreProfile
    {
        /// <summary>UI 표시용 이름(한글).</summary>
        public readonly string label;

        /// <summary>safety 버킷 배율. 높을수록 위험 회피를 우선.</summary>
        public readonly float safetyMul;

        /// <summary>kill 버킷(처치+피해) 배율. 높을수록 적극적으로 교전.</summary>
        public readonly float killMul;

        /// <summary>minKillThreshold 이상 처치를 확보하면 kill 버킷에 더하는 고정 보너스.
        /// "안전하지만 아무것도 안 함"으로 정체되지 않도록 최소한의 교전을 유도한다.
        /// 0이면 미적용(문턱 없음).</summary>
        public readonly float minKillBonus;
        public readonly int minKillThreshold;

        /// <summary>true면 OpportunityObserver 기반 가점/감점(다음 처치 가능성·유리한 위치·
        /// 자원 보존 vs 현재 피해)을 kill 버킷에 추가로 더한다. ThreatEvaluator.OpportunityBonus 참고.</summary>
        public readonly bool useOpportunityBonus;
        public readonly float salientTargetMul;
        public readonly float terminalPositionMul;
        public readonly float difficultyPenaltyMul;
        public readonly bool difficultyTerminalOnly;
        public readonly float criticalThreatRemovedBonus;

        public ScoreProfile(
            string label, float safetyMul, float killMul, float minKillBonus, int minKillThreshold,
            bool useOpportunityBonus = false, float salientTargetMul = 0f,
            float terminalPositionMul = 0f, float difficultyPenaltyMul = 0f,
            bool difficultyTerminalOnly = false, float criticalThreatRemovedBonus = 0f)
        {
            this.label = label;
            this.safetyMul = safetyMul;
            this.killMul = killMul;
            this.minKillBonus = minKillBonus;
            this.minKillThreshold = minKillThreshold;
            this.useOpportunityBonus = useOpportunityBonus;
            this.salientTargetMul = salientTargetMul;
            this.terminalPositionMul = terminalPositionMul;
            this.difficultyPenaltyMul = difficultyPenaltyMul;
            this.difficultyTerminalOnly = difficultyTerminalOnly;
            this.criticalThreatRemovedBonus = criticalThreatRemovedBonus;
        }

        /// <summary>기존 단일 스코어와 동일 — 재조정 없음(1배, 문턱 없음). 3-인자 BeamSearch.Run/
        /// PredictionPlanner.Plan()이 내부적으로 위임하는 하위 호환 기준이라 값을 바꾸면 안 된다.</summary>
        public static readonly ScoreProfile Balanced = new ScoreProfile("절충형", 1f, 1f, 0f, 0);

        /// <summary>난이도 낮음·안전 우선. 다만 "완전히 소극적"으로 정체되지 않도록, 최소 1킬을
        /// 확보하면 확실한 보너스를 준다 — 안전만 추구하다 아무도 안 죽이는 경로를 막는다.</summary>
        public static readonly ScoreProfile Safe = new ScoreProfile(
            "안전형", 2f, 0.3f, 0f, 0,
            salientTargetMul: 0.7f, terminalPositionMul: 2.8f, difficultyPenaltyMul: 0.8f,
            difficultyTerminalOnly: true, criticalThreatRemovedBonus: 50f);

        /// <summary>안정형 S1~S4 단계 비교용. 실제 3경로 출력에는 Safe만 사용한다.</summary>
        public static readonly ScoreProfile SafeDiagnostic = new ScoreProfile(
            "안정형-무가중진단", 2f, 0.5f, 50f, 1);

        public static readonly ScoreProfile SafeTerminalOnly = new ScoreProfile(
            "안정형-1종료안전", 2f, 0.5f, 50f, 1,
            terminalPositionMul: 2.2f);

        public static readonly ScoreProfile SafeTerminalDifficulty = new ScoreProfile(
            "안정형-2입력난이도", 2f, 0.5f, 50f, 1,
            terminalPositionMul: 2.2f, difficultyPenaltyMul: 0.8f,
            difficultyTerminalOnly: true);

        /// <summary>난이도 높음·오버킬. 안전 비중을 크게 낮추고 처치·피해를 적극적으로 추구한다
        /// (완전히 0으로 두지 않는 건, 확실한 즉사 상황까지 무시하게 만들지 않기 위함).</summary>
        public static readonly ScoreProfile Aggressive = new ScoreProfile(
            "공격형", 0.35f, 2.2f, 0f, 0,
            salientTargetMul: 0.1f, terminalPositionMul: 0.15f, difficultyPenaltyMul: 0.1f,
            difficultyTerminalOnly: true);

        /// <summary>
        /// 공격형 A1~A4 기준선 수집 전용. 기존 공격형의 safety/kill 성향은 유지하되 새 평가축은
        /// Beam 순위에 반영하지 않는다. raw 관측값은 CandidatePath와 진단 로그에 별도로 남긴다.
        /// 플레이 후보 UI에는 사용하지 않는다.
        /// </summary>
        public static readonly ScoreProfile AggressiveDiagnostic = new ScoreProfile(
            "공격형-무가중진단", 0.35f, 2.2f, 0f, 0);

        public static readonly ScoreProfile AggressiveTargetOnly = new ScoreProfile(
            "공격형-1타깃", 0.35f, 2.2f, 0f, 0,
            salientTargetMul: 0.1f);

        public static readonly ScoreProfile AggressiveTargetTerminal = new ScoreProfile(
            "공격형-2종료위치", 0.35f, 2.2f, 0f, 0,
            salientTargetMul: 0.1f, terminalPositionMul: 0.15f);

        /// <summary>
        /// 유리한 다음 공격을 준비한다: 다음 처치 가능성 > 유리한 위치 > 자원 보존 > 현재 피해.
        /// killMul을 낮춰(현재 피해 비중 축소) 대신 OpportunityBonus를 얹는다 — "지금 때리는 양"보다
        /// "다음 1초에 강하게 때릴 수 있는 상태인가"를 우선한다. safetyMul은 중립(1)으로 두고
        /// 위치 관련 위험(포위·위험한 락)은 OpportunityBonus 쪽 감점으로 별도 반영한다.
        /// </summary>
        public static readonly ScoreProfile Opportunistic = new ScoreProfile(
            "기회형", 1f, 0.6f, 0f, 0, useOpportunityBonus: true,
            salientTargetMul: 1.1f, terminalPositionMul: 1.2f, difficultyPenaltyMul: 1.8f);
    }
}
