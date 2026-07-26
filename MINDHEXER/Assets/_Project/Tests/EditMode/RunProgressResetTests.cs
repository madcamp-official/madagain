using Game.View;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public class RunProgressResetTests
    {
        [Test]
        public void WaveRunner_ResetProgress_ReturnsToIdleBeforeFirstWave()
        {
            var go = new GameObject("wave-reset-test");
            try
            {
                var config = go.AddComponent<ArenaWaves>();
                config.waves = new[] { new Wave() };
                var runner = go.AddComponent<WaveRunner>();
                runner.config = config;

                runner.StartFrom(0, true);
                Assert.AreNotEqual(WaveRunner.State.Idle, runner.CurrentState);

                runner.ResetProgress();

                Assert.AreEqual(WaveRunner.State.Idle, runner.CurrentState);
                Assert.AreEqual(-1, runner.CurrentWave);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ArenaGate_ResetToStartState_RestoresConfiguredState()
        {
            var go = new GameObject("gate-reset-test");
            try
            {
                var gate = go.AddComponent<ArenaGate>();
                gate.startState = ArenaGate.StartState.Open;
                gate.Close();

                gate.ResetToStartState();

                Assert.IsTrue(gate.IsOpen);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
