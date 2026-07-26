using NUnit.Framework;
using UnityEngine;
using Game.Sim;
using Game.Prediction;

namespace Game.Sim.Tests
{
    /// <summary>
    /// core-controls의 원거리 솔저(투사체) 인지 여부를 확인한다.
    /// FutureThreatObserver가 실제로 ThreatEvaluator.Score에 반영되는지가 핵심.
    /// </summary>
    public class ThreatEvaluatorTests
    {
        [Test]
        public void Score_PenalizesImminentProjectileImpact()
        {
            SimWorld safe = SimWorld.Create();
            safe.player = PlayerSim.Spawn(Vector3.zero);

            SimWorld threatened = SimWorld.Create();
            threatened.player = PlayerSim.Spawn(Vector3.zero);
            // 플레이어 몸통을 향해 곧장 날아오는 투사체 (명중까지 수 틱 이내).
            threatened.SpawnProjectile(new Vector3(0f, 0.7f, 3f), new Vector3(0f, 0f, -30f));

            ScoreBreakdown safeScore = ThreatEvaluator.Score(in safe, 0, 0, 0, 0, false);
            ScoreBreakdown threatScore = ThreatEvaluator.Score(in threatened, 0, 0, 0, 0, false);

            Assert.Less(threatScore.safety, safeScore.safety,
                "명중 궤도의 투사체는 안전 점수를 깎아야 함 (원거리 솔저 인지)");
        }

        [Test]
        public void Score_IgnoresProjectile_WhenNotOnHitCourse()
        {
            SimWorld safe = SimWorld.Create();
            safe.player = PlayerSim.Spawn(Vector3.zero);

            SimWorld sideways = SimWorld.Create();
            sideways.player = PlayerSim.Spawn(Vector3.zero);
            // 플레이어와 무관하게 옆으로 지나가는 투사체 — 위협으로 잡히면 안 됨.
            sideways.SpawnProjectile(new Vector3(20f, 0.7f, 0f), new Vector3(0f, 0f, 30f));

            ScoreBreakdown safeScore = ThreatEvaluator.Score(in safe, 0, 0, 0, 0, false);
            ScoreBreakdown sidewaysScore = ThreatEvaluator.Score(in sideways, 0, 0, 0, 0, false);

            Assert.AreEqual(safeScore.safety, sidewaysScore.safety, 1e-4f,
                "명중 궤도가 아닌 투사체는 안전 점수에 영향을 주면 안 됨");
        }

        [Test]
        public void Score_PenalizesCommittedChargePath()
        {
            SimWorld safe = SimWorld.Create();
            safe.player = PlayerSim.Spawn(Vector3.zero);

            SimWorld threatened = SimWorld.Create();
            threatened.player = PlayerSim.Spawn(Vector3.zero);
            threatened.AddEnemy(new Vector3(0f, 0f, 5f),
                CombatType.Melee, MobilityType.Charge, SizeClass.Normal);
            threatened.enemies[0].ai.state = EnemyState.ChargeRun;
            threatened.enemies[0].ai.committedDir = Vector3.back;

            ScoreBreakdown safeScore = ThreatEvaluator.Score(in safe, 0, 0, 0, 0, false);
            ScoreBreakdown threatScore = ThreatEvaluator.Score(in threatened, 0, 0, 0, 0, false);

            Assert.Less(threatScore.safety, safeScore.safety);
            Assert.AreEqual(1, FutureThreatObserver.Observe(in threatened).imminentChargeCount);
        }

        [Test]
        public void Score_DoesNotPenalizeChargeMovingAway()
        {
            SimWorld baseline = SimWorld.Create();
            baseline.player = PlayerSim.Spawn(Vector3.zero);
            baseline.AddEnemy(new Vector3(0f, 0f, 5f),
                CombatType.Melee, MobilityType.Ground, SizeClass.Normal);

            SimWorld movingAway = Snapshot.Clone(in baseline);
            movingAway.enemies[0].ai.mobility = MobilityType.Charge;
            movingAway.enemies[0].ai.state = EnemyState.ChargeRun;
            movingAway.enemies[0].ai.committedDir = Vector3.forward;

            ScoreBreakdown baselineScore = ThreatEvaluator.Score(in baseline, 0, 0, 0, 0, false);
            ScoreBreakdown movingAwayScore = ThreatEvaluator.Score(in movingAway, 0, 0, 0, 0, false);

            Assert.AreEqual(baselineScore.safety, movingAwayScore.safety, 1e-4f);
            Assert.AreEqual(0, FutureThreatObserver.Observe(in movingAway).imminentChargeCount);
        }

        /// <summary>조준/발사 중인 공중 적만 임박 대공 위협으로 안전 점수를 깎는다(단순 부유는 아님).</summary>
        [Test]
        public void Score_PenalizesAimingFlyingEnemy()
        {
            SimWorld ground = SimWorld.Create();
            ground.player = PlayerSim.Spawn(Vector3.zero);
            ground.AddEnemy(new Vector3(0f, 4f, 6f),
                CombatType.Ranged, MobilityType.Ground, SizeClass.Normal);

            SimWorld flying = Snapshot.Clone(in ground);
            flying.enemies[0].ai.mobility = MobilityType.Flying;
            flying.enemies[0].ai.state = EnemyState.Aim;   // 실제로 쏘려는 중

            ScoreBreakdown groundScore = ThreatEvaluator.Score(in ground, 0, 0, 0, 0, false);
            ScoreBreakdown flyingScore = ThreatEvaluator.Score(in flying, 0, 0, 0, 0, false);
            Assert.Less(flyingScore.safety, groundScore.safety,
                "조준 중인 공중 적은 안전 점수를 깎아야 함");
            Assert.AreEqual(1, FutureThreatObserver.Observe(in flying).aimingFlyingEnemyCount);
        }

        /// <summary>
        /// 예전엔 "반경 내 아무 공중 적"이 감점이라 예측이 공중 적에게 접근조차 안 했다.
        /// 이제 조준하지 않는 단순 부유 공중 적은 감점이 아니라, 런지 사거리 안이면
        /// "마무리 기회"로 kill 점수에 가점된다.
        /// </summary>
        [Test]
        public void Score_TreatsRepositioningFlyingEnemyAsOpportunity_NotThreat()
        {
            SimWorld ground = SimWorld.Create();
            ground.player = PlayerSim.Spawn(Vector3.zero);
            ground.AddEnemy(new Vector3(0f, AIConfig.FlyHoverOffset, 5f),
                CombatType.Ranged, MobilityType.Ground, SizeClass.Normal);

            SimWorld flying = Snapshot.Clone(in ground);
            flying.enemies[0].ai.mobility = MobilityType.Flying;   // 기본 상태(Reposition) — 조준 아님

            ScoreBreakdown groundScore = ThreatEvaluator.Score(in ground, 0, 0, 0, 0, false);
            ScoreBreakdown flyingScore = ThreatEvaluator.Score(in flying, 0, 0, 0, 0, false);

            Assert.AreEqual(groundScore.safety, flyingScore.safety, 1e-4f,
                "조준하지 않는 단순 부유 공중 적은 안전 점수를 깎으면 안 됨");
            Assert.Greater(flyingScore.kill, groundScore.kill,
                "런지 사거리 안의 공중 적은 마무리 기회로 kill 점수에 가점돼야 함");
            Assert.AreEqual(1, FutureThreatObserver.Observe(in flying).strikeableFlyingEnemyCount);
        }
    }
}
