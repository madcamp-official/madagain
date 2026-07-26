using NUnit.Framework;
using Game.Prediction;

namespace Game.Sim.Tests
{
    /// <summary>예측 게이지 메커닉(1~5초)이 macroDepth로 정확히 매핑되는지 확인한다.</summary>
    public class PredictionSettingsTests
    {
        [TestCase(1f, 4)]
        [TestCase(2f, 8)]
        [TestCase(3f, 12)]
        [TestCase(4f, 16)]
        [TestCase(5f, 20)]
        public void ForDuration_ProducesExpectedMacroDepth(float seconds, int expectedDepth)
        {
            PredictionSettings settings = PredictionSettings.ForDuration(seconds);

            Assert.AreEqual(expectedDepth, settings.macroDepth);
            Assert.AreEqual(15, settings.macroTicks);
            Assert.AreEqual(seconds, settings.DurationSeconds, 1e-4f);
        }

        [Test]
        public void ForDuration_MatchesFullSettings_AtFiveSeconds()
        {
            PredictionSettings fromGauge = PredictionSettings.ForDuration(5f);
            PredictionSettings full = PredictionSettings.Full;

            Assert.AreEqual(full.macroTicks, fromGauge.macroTicks);
            Assert.AreEqual(full.macroDepth, fromGauge.macroDepth);
            Assert.AreEqual(full.beamWidth, fromGauge.beamWidth);
            Assert.AreEqual(full.maxActionsPerNode, fromGauge.maxActionsPerNode);
        }

        [TestCase(0f)]
        [TestCase(0.5f)]
        [TestCase(-3f)]
        public void ForDuration_ClampsBelowMinimum_ToOneSecond(float tooShort)
        {
            PredictionSettings settings = PredictionSettings.ForDuration(tooShort);
            Assert.AreEqual(PredictionSettings.ForDuration(PredictionSettings.MinDurationSeconds).macroDepth, settings.macroDepth);
        }

        [TestCase(6f)]
        [TestCase(100f)]
        public void ForDuration_ClampsAboveMaximum_ToFiveSeconds(float tooLong)
        {
            PredictionSettings settings = PredictionSettings.ForDuration(tooLong);
            Assert.AreEqual(PredictionSettings.ForDuration(PredictionSettings.MaxDurationSeconds).macroDepth, settings.macroDepth);
        }
    }
}
