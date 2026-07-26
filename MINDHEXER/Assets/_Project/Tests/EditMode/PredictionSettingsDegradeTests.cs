using NUnit.Framework;
using UnityEngine;
using Game.Prediction;

namespace Game.Sim.Tests
{
    /// <summary>
    /// docs/shared/OPTIMIZATION.md 12장 "시간 예산과 동적 축소"를 적 수 기반(결정론적)으로
    /// 구현한 PredictionSettings.Degrade 검증. 실제 걸린 시간이 아니라 적 수만 보므로,
    /// 같은 입력이면 항상 같은 설정이 나와야 한다(그게 이 방식을 고른 이유).
    /// </summary>
    public class PredictionSettingsDegradeTests
    {
        [TestCase(1)]
        [TestCase(8)]
        [TestCase(16)]
        public void Degrade_LeavesFullQualityUnchanged_AtOrBelowSixteenEnemies(int enemyCount)
        {
            PredictionSettings baseline = PredictionSettings.Full;
            PredictionSettings degraded = PredictionSettings.Degrade(baseline, enemyCount);

            Assert.AreEqual(baseline.beamWidth, degraded.beamWidth);
            Assert.AreEqual(baseline.macroDepth, degraded.macroDepth);
            Assert.AreEqual(baseline.maxActionsPerNode, degraded.maxActionsPerNode);
        }

        [TestCase(17)]
        [TestCase(31)]
        public void Degrade_ReducesBeamWidthOnly_Between17And31Enemies(int enemyCount)
        {
            PredictionSettings baseline = PredictionSettings.Full;
            PredictionSettings degraded = PredictionSettings.Degrade(baseline, enemyCount);

            Assert.AreEqual(8, degraded.beamWidth);
            Assert.AreEqual(baseline.macroDepth, degraded.macroDepth, "이 구간에선 깊이는 안 줄어야 함");
        }

        [TestCase(32)]
        [TestCase(33)]
        [TestCase(50)]
        public void Degrade_ReducesBeamAndDepth_AtAndAbove32Enemies(int enemyCount)
        {
            PredictionSettings baseline = PredictionSettings.Full;
            PredictionSettings degraded = PredictionSettings.Degrade(baseline, enemyCount);

            Assert.AreEqual(6, degraded.beamWidth);
            Assert.Less(degraded.macroDepth, baseline.macroDepth);
            Assert.LessOrEqual(degraded.maxActionsPerNode, baseline.maxActionsPerNode);
        }

        [Test]
        public void Degrade_ReducesDepthFurther_AtAndAbove50Enemies()
        {
            PredictionSettings baseline = PredictionSettings.Full;
            PredictionSettings at40 = PredictionSettings.Degrade(baseline, 40);   // >=32 tier only
            PredictionSettings at64 = PredictionSettings.Degrade(baseline, 64);   // >=50 tier too

            Assert.Less(at64.macroDepth, at40.macroDepth);
        }

        [Test]
        public void Degrade_ThirtyTwoAndFiftyAreInclusiveBoundaries()
        {
            // 32/50마리 실측(305ms/340ms)이 하드리밋(300ms)을 살짝 넘겨서, 경계를
            // 초과(>)가 아니라 이상(>=)으로 바꿔 정확히 32·50마리도 강한 축소를 받게 했다.
            PredictionSettings baseline = PredictionSettings.Full;

            PredictionSettings at32 = PredictionSettings.Degrade(baseline, 32);
            Assert.AreEqual(6, at32.beamWidth);
            Assert.Less(at32.macroDepth, baseline.macroDepth);

            PredictionSettings at50 = PredictionSettings.Degrade(baseline, 50);
            Assert.AreEqual(Mathf.RoundToInt(baseline.macroDepth * 0.5f), at50.macroDepth);
        }

        [Test]
        public void Degrade_IsDeterministic_SameEnemyCountAlwaysProducesSameSettings()
        {
            PredictionSettings baseline = PredictionSettings.Full;
            PredictionSettings first = PredictionSettings.Degrade(baseline, 40);
            PredictionSettings second = PredictionSettings.Degrade(baseline, 40);

            Assert.AreEqual(first.beamWidth, second.beamWidth);
            Assert.AreEqual(first.macroDepth, second.macroDepth);
            Assert.AreEqual(first.maxActionsPerNode, second.maxActionsPerNode);
        }

        /// <summary>
        /// 회귀 방지: ActionGenerator.Priority는 [이동4개, 대시4개, Attack, Lunge, Wait] 순서라서
        /// maxActionsPerNode가 10 밑으로 내려가면 대시 충전이 남아있는 한(거의 항상) Attack 자체가
        /// 후보 생성 단계에서 사라진다 — 실제로 이 버그가 있었다("공격이 안 들어간다"). 캡을
        /// 안 건드리는 지금 구현이 안전한지 모든 구간에서 확인한다.
        /// </summary>
        [TestCase(1)]
        [TestCase(20)]
        [TestCase(40)]
        [TestCase(64)]
        public void Degrade_NeverReducesActionCapBelowNine_SoAttackStaysReachable(int enemyCount)
        {
            PredictionSettings degraded = PredictionSettings.Degrade(PredictionSettings.Full, enemyCount);
            Assert.GreaterOrEqual(degraded.maxActionsPerNode, 10,
                "이동4+대시4=8칸이 항상 먼저 채워지므로, 9 미만이면 Attack이 후보에서 구조적으로 사라진다");
        }

        [Test]
        public void Degrade_NeverProducesZeroOrNegativeDepth()
        {
            PredictionSettings tiny = new PredictionSettings { macroTicks = 15, macroDepth = 1, beamWidth = 12, maxActionsPerNode = 12 };
            PredictionSettings degraded = PredictionSettings.Degrade(tiny, 64);
            Assert.GreaterOrEqual(degraded.macroDepth, 1);
        }
    }
}
