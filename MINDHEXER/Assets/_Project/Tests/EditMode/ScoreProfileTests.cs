using NUnit.Framework;
using UnityEngine;
using Game.Sim;
using Game.Prediction;

namespace Game.Sim.Tests
{
    /// <summary>
    /// 안전형/기회형/공격형 3-프로필 평가(ScoreProfile)의 재조정 규칙을 확인한다. 절충형
    /// (Balanced)은 이제 3-슬롯 UI엔 안 쓰이지만 3-인자 Run/Plan()의 하위 호환 기준으로
    /// 남아있어 계속 검증한다. 위험 관측(FutureThreatObserver) 자체는 손대지 않고 safety·kill
    /// 버킷 배율 + 안전형의 최소 킬 확보 보너스만 이 클래스의 검증 대상이다(기회형의
    /// OpportunityObserver 검증은 아래 OpportunityObserverTests).
    /// </summary>
    public class ScoreProfileTests
    {
        static SimWorld BuildSafeWorld()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.AddEnemy(new Vector3(0f, 0f, 8f));   // 멀리 있는 위협 없는 적 1마리
            return world;
        }

        [Test]
        public void PlayerIntent_PrefersFrontTarget_AndDoesNotRewardStandingStill()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.AddEnemy(new Vector3(0f, 0f, 5f));
            world.AddEnemy(new Vector3(0f, 0f, -2f));

            PlayerIntentContext intent = PlayerIntentEvaluator.Capture(in world);

            Assert.AreEqual(world.enemies[0].id, intent.salientEnemyId);
            Assert.AreEqual(0f, PlayerIntentEvaluator.SalientTargetProgress(in world, in intent), 1e-5f);
        }

        [Test]
        public void SafetyIntent_PicksFrontEscapeBlocker_AndRewardsOnlyItsRemoval()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.player.yaw = 0f;
            world.AddEnemy(new Vector3(0f, 0f, 2f),
                CombatType.Melee, MobilityType.Ground, SizeClass.Normal);
            world.AddEnemy(new Vector3(0f, 0f, -2f),
                CombatType.Melee, MobilityType.Ground, SizeClass.Normal);

            SafetyIntentContext context = SafetyIntentEvaluator.Capture(in world);
            Assert.AreEqual(world.enemies[0].id, context.criticalEnemyId);
            Assert.AreEqual(0f, SafetyIntentEvaluator.CriticalThreatRemoved(in world, in context));

            world.enemies[1].alive = false;
            Assert.AreEqual(0f, SafetyIntentEvaluator.CriticalThreatRemoved(in world, in context),
                "일반 적 제거는 안정형 핵심 위협 보너스를 받으면 안 된다.");
            world.enemies[0].alive = false;
            Assert.AreEqual(1f, SafetyIntentEvaluator.CriticalThreatRemoved(in world, in context));
        }

        [Test]
        public void SafetyIntent_PrioritizesAimingFlyingThreat_OverOrdinaryBlocker()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.AddEnemy(new Vector3(0f, 0f, 2f),
                CombatType.Melee, MobilityType.Ground, SizeClass.Normal);
            world.AddEnemy(new Vector3(0f, 3f, 4f),
                CombatType.Ranged, MobilityType.Flying, SizeClass.Normal);
            world.enemies[1].ai.state = EnemyState.Aim;

            SafetyIntentContext context = SafetyIntentEvaluator.Capture(in world);

            Assert.AreEqual(world.enemies[1].id, context.criticalEnemyId);
        }

        [Test]
        public void PlayerIntent_ValuesDamageSafeTerminalAndChargesComplexInput()
        {
            SimWorld initial = SimWorld.Create();
            initial.player = PlayerSim.Spawn(Vector3.zero);
            initial.AddEnemy(new Vector3(0f, 0f, 5f));
            PlayerIntentContext intent = PlayerIntentEvaluator.Capture(in initial);
            SimWorld damaged = Snapshot.Clone(in initial);
            damaged.enemies[0].combat.health--;
            Assert.Greater(
                PlayerIntentEvaluator.SalientTargetProgress(in damaged, in intent),
                PlayerIntentEvaluator.SalientTargetProgress(in initial, in intent));

            MacroAction attack = MacroAction.Simple(MacroActionType.Attack);
            MacroAction pursuit = MacroAction.AerialPursuitTo(7);
            pursuit.targetYaw = 180f;
            Assert.Greater(
                PlayerIntentEvaluator.ActionDifficulty(in pursuit, in initial, in attack, false),
                PlayerIntentEvaluator.ActionDifficulty(in attack, in initial, in attack, false));

            SimWorld trapped = Snapshot.Clone(in initial);
            trapped.player.combat.attackPhase = CombatConfig.PhRecovery;
            for (int i = 0; i < 4; i++)
                trapped.AddEnemy(new Vector3(i - 1.5f, 0f, 1f));
            SimWorld escaped = Snapshot.Clone(in trapped);
            escaped.player.pos = new Vector3(0f, 4f, 12f);
            escaped.player.combat.attackPhase = CombatConfig.PhNone;
            Assert.Greater(
                PlayerIntentEvaluator.TerminalPositionQuality(in escaped),
                PlayerIntentEvaluator.TerminalPositionQuality(in trapped));
        }

        [Test]
        public void SafeTerminal_PrefersOpenEscapeSectors_OverABoxedPosition()
        {
            SimWorld boxed = SimWorld.Create();
            boxed.player = PlayerSim.Spawn(Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                float angle = i * 45f * Mathf.Deg2Rad;
                boxed.AddEnemy(
                    new Vector3(Mathf.Sin(angle) * 4f, 0f, Mathf.Cos(angle) * 4f),
                    CombatType.Melee, MobilityType.Ground, SizeClass.Normal);
            }

            SimWorld open = Snapshot.Clone(in boxed);
            open.player.pos = new Vector3(0f, 0f, -10f);

            Assert.Greater(
                PlayerIntentEvaluator.TerminalPositionQuality(in open),
                PlayerIntentEvaluator.TerminalPositionQuality(in boxed));
        }

        [Test]
        public void SafeTerminal_PenalizesIncomingProjectileAtTheEndpoint()
        {
            SimWorld clear = SimWorld.Create();
            clear.player = PlayerSim.Spawn(Vector3.zero);
            SimWorld threatened = Snapshot.Clone(in clear);
            threatened.SpawnProjectile(
                new Vector3(0f, AIConfig.PlayerTorso, -4f),
                new Vector3(0f, 0f, 12f));

            Assert.Less(
                PlayerIntentEvaluator.TerminalPositionQuality(in threatened),
                PlayerIntentEvaluator.TerminalPositionQuality(in clear));
        }

        [Test]
        public void AggressiveDiagnostic_LeavesNewMultipliersZero_ButExportsRawObservations()
        {
            Assert.AreEqual(0f, ScoreProfile.AggressiveDiagnostic.salientTargetMul);
            Assert.AreEqual(0f, ScoreProfile.AggressiveDiagnostic.terminalPositionMul);
            Assert.AreEqual(0f, ScoreProfile.AggressiveDiagnostic.difficultyPenaltyMul);
            Assert.AreEqual(ScoreProfile.Aggressive.safetyMul, ScoreProfile.AggressiveDiagnostic.safetyMul);
            Assert.AreEqual(ScoreProfile.Aggressive.killMul, ScoreProfile.AggressiveDiagnostic.killMul);

            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.AddEnemy(new Vector3(0f, 0f, 3f));
            SimServices services = StubServices.Create();

            CandidatePath candidate = BeamSearch.Run(
                in world, in services, PredictionSettings.Mini, ScoreProfile.AggressiveDiagnostic)[0];

            Assert.IsFalse(float.IsNaN(candidate.rawExecutionDifficulty));
            Assert.IsFalse(float.IsNaN(candidate.rawSalientTargetProgress));
            Assert.IsFalse(float.IsNaN(candidate.rawTerminalPositionQuality));
            Assert.GreaterOrEqual(candidate.rawExecutionDifficulty, 0f);
        }

        [Test]
        public void AggressiveWeightStages_EnableExactlyOneAxisAtATime()
        {
            Assert.AreEqual(0.1f, ScoreProfile.AggressiveTargetOnly.salientTargetMul);
            Assert.AreEqual(0f, ScoreProfile.AggressiveTargetOnly.terminalPositionMul);
            Assert.AreEqual(0f, ScoreProfile.AggressiveTargetOnly.difficultyPenaltyMul);

            Assert.AreEqual(0.1f, ScoreProfile.AggressiveTargetTerminal.salientTargetMul);
            Assert.AreEqual(0.15f, ScoreProfile.AggressiveTargetTerminal.terminalPositionMul);
            Assert.AreEqual(0f, ScoreProfile.AggressiveTargetTerminal.difficultyPenaltyMul);

            Assert.AreEqual(0.1f, ScoreProfile.Aggressive.salientTargetMul);
            Assert.AreEqual(0.15f, ScoreProfile.Aggressive.terminalPositionMul);
            Assert.AreEqual(0.1f, ScoreProfile.Aggressive.difficultyPenaltyMul);
            Assert.IsTrue(ScoreProfile.Aggressive.difficultyTerminalOnly);
            Assert.AreEqual(0f, ScoreProfile.SafeDiagnostic.terminalPositionMul);
            Assert.AreEqual(2.2f, ScoreProfile.SafeTerminalOnly.terminalPositionMul);
            Assert.AreEqual(0f, ScoreProfile.SafeTerminalOnly.difficultyPenaltyMul);
            Assert.AreEqual(0.8f, ScoreProfile.SafeTerminalDifficulty.difficultyPenaltyMul);
            Assert.IsTrue(ScoreProfile.SafeTerminalDifficulty.difficultyTerminalOnly);
            Assert.AreEqual(0.3f, ScoreProfile.Safe.killMul);
            Assert.AreEqual(0, ScoreProfile.Safe.minKillThreshold);
            Assert.AreEqual(0f, ScoreProfile.Safe.minKillBonus);
            Assert.AreEqual(50f, ScoreProfile.Safe.criticalThreatRemovedBonus);
            Assert.AreEqual(2.8f, ScoreProfile.Safe.terminalPositionMul);
            Assert.AreEqual(0.8f, ScoreProfile.Safe.difficultyPenaltyMul);
            Assert.IsTrue(ScoreProfile.Safe.difficultyTerminalOnly);
            Assert.IsFalse(ScoreProfile.Opportunistic.difficultyTerminalOnly,
                "기회형은 쉬운 입력 가지를 중간 Beam에서도 보존해야 하므로 누적 난이도를 계속 적용한다.");
        }

        [Test]
        public void Score_Balanced_MatchesBaseScore_Exactly()
        {
            SimWorld world = BuildSafeWorld();

            ScoreBreakdown baseScore = ThreatEvaluator.Score(in world, 1, 0, 2, 1, false);
            ScoreBreakdown balanced = ThreatEvaluator.Score(in world, 1, 0, 2, 1, false, in ScoreProfile.Balanced);

            Assert.AreEqual(baseScore.safety, balanced.safety, 1e-5f, "절충형은 배율 1·보너스 0이라 기존 Score()와 완전히 같아야 함");
            Assert.AreEqual(baseScore.kill, balanced.kill, 1e-5f);
            Assert.AreEqual(baseScore.difficulty, balanced.difficulty, 1e-5f);
        }

        [Test]
        public void Score_Safe_AddsMinKillBonus_OnlyAtOrAboveThreshold()
        {
            SimWorld world = BuildSafeWorld();

            ScoreBreakdown zeroKills = ThreatEvaluator.Score(in world, 0, 0, 0, 0, false, in ScoreProfile.Safe);
            ScoreBreakdown oneKill = ThreatEvaluator.Score(in world, 1, 0, 0, 0, false, in ScoreProfile.Safe);

            float expectedKillDelta = PredictionScoreConfig.KillWeight * ScoreProfile.Safe.killMul + ScoreProfile.Safe.minKillBonus;
            Assert.AreEqual(expectedKillDelta, oneKill.kill - zeroKills.kill, 1e-4f,
                "킬 1개 확보 시 배율 적용분 + 최소 킬 보너스만큼 kill 버킷이 늘어야 함");
        }

        [Test]
        public void Score_Aggressive_FavorsKillOverSafety_RelativeToBalanced()
        {
            SimWorld world = BuildSafeWorld();

            ScoreBreakdown balanced = ThreatEvaluator.Score(in world, 1, 0, 1, 0, false, in ScoreProfile.Balanced);
            ScoreBreakdown aggressive = ThreatEvaluator.Score(in world, 1, 0, 1, 0, false, in ScoreProfile.Aggressive);

            Assert.Less(aggressive.safety, balanced.safety, "공격형은 안전 버킷을 절충형보다 덜 중요하게 봐야 함");
            Assert.Greater(aggressive.kill, balanced.kill, "공격형은 처치 버킷을 절충형보다 더 중요하게 봐야 함");
        }

        [Test]
        public void Score_Safe_FavorsSafetyOverKill_RelativeToBalanced()
        {
            SimWorld world = BuildSafeWorld();

            ScoreBreakdown balanced = ThreatEvaluator.Score(in world, 1, 0, 1, 0, false, in ScoreProfile.Balanced);
            ScoreBreakdown safe = ThreatEvaluator.Score(in world, 1, 0, 1, 0, false, in ScoreProfile.Safe);

            Assert.Greater(safe.safety, balanced.safety, "안전형은 안전 버킷을 절충형보다 더 크게 반영해야 함");
        }

        [Test]
        public void Score_DevelopmentInfiniteHp_IsCappedForPrediction()
        {
            SimWorld normal = BuildSafeWorld();
            normal.player.combat.hp = PredictionScoreConfig.ScoredPlayerHpCap;
            SimWorld development = Snapshot.Clone(in normal);
            development.player.combat.hp = 1_000_000;

            ScoreBreakdown normalScore = ThreatEvaluator.Score(in normal, 0, 0, 0, 0, false);
            ScoreBreakdown developmentScore = ThreatEvaluator.Score(in development, 0, 0, 0, 0, false);

            Assert.AreEqual(normalScore.safety, developmentScore.safety, 1e-5f);
            ScoreBreakdown hitDevelopment = ThreatEvaluator.Score(in development, 0, 0, 0, 1, false);
            Assert.Less(hitDevelopment.safety, developmentScore.safety);
        }

        /// <summary>사망 후보는 프로필과 무관하게 동일해야 한다 — 배율을 곱하면 프로필마다 다른
        /// 크기의 사망 점수가 나와 BeamSearch의 "가장 덜 나쁜 후보" 폴백 비교가 왜곡된다.</summary>
        [Test]
        public void Score_OpportunityBonus_IsAppliedOnlyAtTerminalDepth()
        {
            SimWorld world = BuildSafeWorld();

            ScoreBreakdown intermediate = ThreatEvaluator.Score(
                in world, 0, 0, 0, 0, false, in ScoreProfile.Opportunistic,
                includeOpportunityTerminalBonus: false);
            ScoreBreakdown terminal = ThreatEvaluator.Score(
                in world, 0, 0, 0, 0, false, in ScoreProfile.Opportunistic,
                includeOpportunityTerminalBonus: true);

            Assert.Greater(terminal.kill, intermediate.kill,
                "Opportunity posture/resource rewards must be terminal-only.");
        }

        [Test]
        public void Score_PlayerDeath_IsIdenticalAcrossAllProfiles()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.player.combat.hp = 0;

            ScoreBreakdown baseScore = ThreatEvaluator.Score(in world, 2, 0, 5, 3, false);
            ScoreBreakdown safe = ThreatEvaluator.Score(in world, 2, 0, 5, 3, false, in ScoreProfile.Safe);
            ScoreBreakdown balanced = ThreatEvaluator.Score(in world, 2, 0, 5, 3, false, in ScoreProfile.Balanced);
            ScoreBreakdown aggressive = ThreatEvaluator.Score(in world, 2, 0, 5, 3, false, in ScoreProfile.Aggressive);

            Assert.AreEqual(PredictionScoreConfig.PlayerDeath, baseScore.safety);
            Assert.AreEqual(baseScore.safety, safe.safety);
            Assert.AreEqual(baseScore.safety, balanced.safety);
            Assert.AreEqual(baseScore.safety, aggressive.safety);
            Assert.AreEqual(baseScore.kill, safe.kill);
            Assert.AreEqual(baseScore.kill, aggressive.kill);
        }
    }

    /// <summary>PredictionPlanner.PlanByProfile의 계약(항상 3개, 순서·라벨 고정) 확인.</summary>
    public class PredictionPlannerProfileTests
    {
        [Test]
        public void PlanByProfile_ReturnsExactlyThree_LabeledSafeOpportunisticAggressive_InOrder()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.AddEnemy(new Vector3(0f, 0f, 5f));
            SimServices services = StubServices.Create();

            CandidatePath[] results = PredictionPlanner.PlanByProfile(in world, in services, PredictionSettings.Mini);

            Assert.AreEqual(3, results.Length);
            Assert.AreEqual(ScoreProfile.Safe.label, results[0].profileLabel);
            Assert.AreEqual(ScoreProfile.Opportunistic.label, results[1].profileLabel,
                "가운데 슬롯은 더 이상 절충형이 아니라 기회형이어야 함");
            Assert.AreEqual(ScoreProfile.Aggressive.label, results[2].profileLabel);
        }

        /// <summary>기존 Plan()은 이번 변경(프로필 오버로드 추가)과 무관하게 그대로 동작해야 한다 —
        /// BeamSearch.Run(3-인자 오버로드)이 내부적으로 Balanced로 위임하는 것과 수치까지 일치.</summary>
        [Test]
        public void Plan_StaysNumericallyIdentical_ToBalancedProfileRun()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.AddEnemy(new Vector3(0f, 0f, 5f));
            SimServices services = StubServices.Create();

            CandidatePath viaPlan = PredictionPlanner.Plan(in world, in services, PredictionSettings.Mini)[0];
            CandidatePath viaProfile = BeamSearch.Run(in world, in services, PredictionSettings.Mini, ScoreProfile.Balanced)[0];

            Assert.AreEqual(viaProfile.TotalScore, viaPlan.TotalScore, 1e-6f);
            Assert.AreEqual(viaProfile.actions.Length, viaPlan.actions.Length);
        }

        [Test]
        public void PlanByProfile_SharedSearch_IsDeterministic()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.AddEnemy(new Vector3(0f, 0f, 5f));
            world.AddEnemy(new Vector3(3f, 0f, 4f));
            SimServices services = StubServices.Create();

            CandidatePath[] first = PredictionPlanner.PlanByProfile(
                in world, in services, PredictionSettings.Mini);
            CandidatePath[] repeat = PredictionPlanner.PlanByProfile(
                in world, in services, PredictionSettings.Mini);

            Assert.AreEqual(3, first.Length);
            Assert.AreEqual(first.Length, repeat.Length);
            for (int p = 0; p < first.Length; p++)
            {
                Assert.AreEqual(first[p].profileLabel, repeat[p].profileLabel);
                Assert.AreEqual(first[p].TotalScore, repeat[p].TotalScore, 1e-6f);
                Assert.AreEqual(first[p].actions.Length, repeat[p].actions.Length);
                for (int a = 0; a < first[p].actions.Length; a++)
                {
                    Assert.AreEqual(first[p].actions[a].type, repeat[p].actions[a].type);
                    Assert.AreEqual(first[p].actions[a].lungeTargetId, repeat[p].actions[a].lungeTargetId);
                }
            }
        }

        [Test]
        public void OpportunisticSearch_AttacksReachableEnemy_InsteadOfOnlyHoldingOpportunity()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.AddEnemy(new Vector3(0f, 0f, 3f));
            SimServices services = StubServices.Create();

            CandidatePath result = BeamSearch.Run(
                in world, in services, PredictionSettings.Full, ScoreProfile.Opportunistic)[0];

            Assert.Greater(result.damageDealt, 0,
                "Opportunistic search must realize damage instead of only preserving a lunge-ready posture.");
        }
    }

    /// <summary>
    /// 기회형 프로필의 관측 로직(OpportunityObserver) 개별 항목 확인. "다음 처치 가능성 >
    /// 유리한 위치 > 자원 보존 > 현재 피해" 우선순위를 이루는 신호 하나하나가 의도대로
    /// 켜지고 꺼지는지만 본다 — 가중치 합산(OpportunityBonus)은 ScoreProfileTests의
    /// Score_Aggressive/Safe 비교 테스트와 같은 패턴으로 이미 간접 검증됨.
    /// </summary>
    public class OpportunityObserverTests
    {
        [Test]
        public void Observe_CountsLungeableEnemy_WithinRangeAndHeight_NotWhenTooFar()
        {
            SimWorld near = SimWorld.Create();
            near.player = PlayerSim.Spawn(Vector3.zero);
            near.AddEnemy(new Vector3(0f, 0f, 3f));
            Assert.AreEqual(1, OpportunityObserver.Observe(in near).lungeableCount);

            SimWorld far = SimWorld.Create();
            far.player = PlayerSim.Spawn(Vector3.zero);
            far.AddEnemy(new Vector3(0f, 0f, CombatConfig.LungeMaxRange + 10f));
            Assert.AreEqual(0, OpportunityObserver.Observe(in far).lungeableCount);
        }

        [Test]
        public void Observe_CountsExecutionReadyLargeEnemy_OnlyAtExecuteReadyHealth()
        {
            SimWorld ready = SimWorld.Create();
            ready.player = PlayerSim.Spawn(Vector3.zero);
            ready.AddEnemy(new Vector3(0f, 0f, 3f), CombatType.Melee, MobilityType.Ground, SizeClass.Large);
            ready.enemies[0].combat.health = CombatConfig.Damage;   // 다음 한 대로 처형 진입
            Assert.AreEqual(1, OpportunityObserver.Observe(in ready).executionReadyLargeCount);

            SimWorld notReady = SimWorld.Create();
            notReady.player = PlayerSim.Spawn(Vector3.zero);
            notReady.AddEnemy(new Vector3(0f, 0f, 3f), CombatType.Melee, MobilityType.Ground, SizeClass.Large);
            notReady.enemies[0].combat.health = CombatConfig.Damage + 3;
            Assert.AreEqual(0, OpportunityObserver.Observe(in notReady).executionReadyLargeCount);
        }

        [Test]
        public void Observe_CountsEnemiesInAttackCone()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);   // yaw=0 → 정면 +Z
            world.AddEnemy(new Vector3(0f, 0f, 1.5f));       // 정면 안
            world.AddEnemy(new Vector3(0f, 0f, -1.5f));      // 완전히 반대쪽 — cone 밖

            Assert.AreEqual(1, OpportunityObserver.Observe(in world).coneEnemyCount);
        }

        [Test]
        public void Observe_CountsFlankedRangedEnemy_FacingAway_NotWhenFacingPlayer()
        {
            SimWorld flanked = SimWorld.Create();
            flanked.player = PlayerSim.Spawn(Vector3.zero);
            flanked.AddEnemy(new Vector3(0f, 0f, 3f), CombatType.Ranged, MobilityType.Ground, SizeClass.Normal);
            flanked.enemies[0].yaw = 0f;   // 플레이어를 등지고 정면(+Z)을 봄
            Assert.AreEqual(1, OpportunityObserver.Observe(in flanked).flankedRangedCount);

            SimWorld facing = SimWorld.Create();
            facing.player = PlayerSim.Spawn(Vector3.zero);
            facing.AddEnemy(new Vector3(0f, 0f, 3f), CombatType.Ranged, MobilityType.Ground, SizeClass.Normal);
            facing.enemies[0].yaw = 180f;   // 플레이어를 정면으로 봄
            Assert.AreEqual(0, OpportunityObserver.Observe(in facing).flankedRangedCount);
        }

        [Test]
        public void Observe_DetectsHeightAdvantage_OnlyWhenMeaningfullyHigherThanNearbyEnemies()
        {
            SimWorld high = SimWorld.Create();
            high.player = PlayerSim.Spawn(new Vector3(0f, 5f, 0f));
            high.AddEnemy(new Vector3(0f, 0f, 3f));
            Assert.IsTrue(OpportunityObserver.Observe(in high).hasHeightAdvantage);

            SimWorld level = SimWorld.Create();
            level.player = PlayerSim.Spawn(Vector3.zero);
            level.AddEnemy(new Vector3(0f, 0f, 3f));
            Assert.IsFalse(OpportunityObserver.Observe(in level).hasHeightAdvantage);
        }

        [Test]
        public void Observe_ReadyToActNow_FalseWhileAttackOrLungePhaseActive()
        {
            SimWorld ready = SimWorld.Create();
            ready.player = PlayerSim.Spawn(Vector3.zero);
            Assert.IsTrue(OpportunityObserver.Observe(in ready).readyToActNow);

            SimWorld busy = SimWorld.Create();
            busy.player = PlayerSim.Spawn(Vector3.zero);
            busy.player.combat.attackPhase = CombatConfig.PhWindup;
            Assert.IsFalse(OpportunityObserver.Observe(in busy).readyToActNow);
        }

        [Test]
        public void Observe_DetectsResourcePreservation_AndAllResourcesSpent()
        {
            SimWorld fresh = SimWorld.Create();
            fresh.player = PlayerSim.Spawn(Vector3.zero);   // 기본 대시 2·런지 스택 2
            OpportunityObservation freshObs = OpportunityObserver.Observe(in fresh);
            Assert.IsTrue(freshObs.dashPreserved);
            Assert.IsTrue(freshObs.lungePreserved);
            Assert.IsFalse(freshObs.allMobilityResourcesSpent);

            SimWorld spent = SimWorld.Create();
            spent.player = PlayerSim.Spawn(Vector3.zero);
            spent.player.dashCharges = 0;
            spent.player.combat.lungeStacks = 0;
            OpportunityObservation spentObs = OpportunityObserver.Observe(in spent);
            Assert.IsFalse(spentObs.dashPreserved);
            Assert.IsFalse(spentObs.lungePreserved);
            Assert.IsTrue(spentObs.allMobilityResourcesSpent);
        }

        [Test]
        public void Observe_DetectsSurroundedExcess_AndLockedInDangerousRecovery_WhenBothTrue()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            for (int i = 0; i < 5; i++)   // SurroundedTolerance(4) 초과
            {
                float angle = i / 5f * Mathf.PI * 2f;
                world.AddEnemy(new Vector3(Mathf.Cos(angle) * 2f, 0f, Mathf.Sin(angle) * 2f));
            }

            OpportunityObservation notLocked = OpportunityObserver.Observe(in world);
            Assert.AreEqual(1, notLocked.surroundedExcessCount);
            Assert.IsFalse(notLocked.lockedInDangerousRecovery, "포위됐어도 조작이 안 묶였으면 아직 위험한 락은 아님");

            world.player.combat.attackPhase = CombatConfig.PhRecovery;
            OpportunityObservation locked = OpportunityObserver.Observe(in world);
            Assert.IsTrue(locked.lockedInDangerousRecovery, "포위 + 조작 묶임이 겹치면 위험한 락으로 잡아야 함");
        }
    }
}
