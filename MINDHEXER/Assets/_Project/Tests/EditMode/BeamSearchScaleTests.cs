using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using Game.Sim;
using Game.Prediction;

namespace Game.Sim.Tests
{
    /// <summary>
    /// docs/shared/PREDICTION_INTEGRATION_PLAN.md 12장 본탐색 사양(3초/180틱, 매크로 15틱→
    /// 깊이 12, Beam 12, 적 4~8마리)으로 PredictionSettings.Full을 검증한다.
    /// </summary>
    public class BeamSearchScaleTests
    {
        static SimWorld BuildSixEnemySafeWorld()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(new Vector3(0f, 0f, 40f));
            world.AddEnemy(new Vector3(0f, 0f, 5f));
            world.AddEnemy(new Vector3(3f, 0f, 4f));
            world.AddEnemy(new Vector3(-3f, 0f, 4f));
            world.AddEnemy(new Vector3(5f, 0f, 6f));
            world.AddEnemy(new Vector3(-5f, 0f, 6f));
            world.AddEnemy(new Vector3(0f, 0f, 2f));
            return world;
        }

        /// <summary>거울대칭 쌍을 포함한 6마리, 전부 한 방향(-z)에 몰려있어 "가장 가까운 적에서
        /// 후퇴"만으로도 항상 안전 — 동률/중복 상태 스트레스 테스트에 집중하고 생존 여부는
        /// 우연에 맡기지 않는다.</summary>
        static SimWorld BuildSymmetricWorld()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(new Vector3(0f, 0f, 30f));
            world.AddEnemy(new Vector3(2f, 0f, 10f));
            world.AddEnemy(new Vector3(-2f, 0f, 10f));
            world.AddEnemy(new Vector3(5f, 0f, 8f));
            world.AddEnemy(new Vector3(-5f, 0f, 8f));
            world.AddEnemy(new Vector3(0f, 0f, 12f));
            world.AddEnemy(new Vector3(0f, 0f, 6f));
            return world;
        }

        [Test]
        public void FullSearch_SurvivesWithMultipleEnemies()
        {
            SimWorld world = BuildSixEnemySafeWorld();
            SimServices services = StubServices.Create();
            CandidatePath result = PredictionPlanner.Plan(in world, in services, PredictionSettings.Full)[0];

            Assert.IsNotNull(result);
            Assert.IsFalse(result.isDeadFallback, "위협이 먼 상황에서 사망 폴백이 나오면 안 됨");
            Assert.Greater(result.actions.Length, 0);
        }

        [Test]
        public void FullSearch_IsDeterministic_AcrossRepeatedRuns()
        {
            SimWorld world = BuildSixEnemySafeWorld();
            SimServices services = StubServices.Create();
            PredictionSettings settings = PredictionSettings.Full;

            CandidatePath first = PredictionPlanner.Plan(in world, in services, settings)[0];
            for (int i = 0; i < 3; i++)
            {
                CandidatePath repeat = PredictionPlanner.Plan(in world, in services, settings)[0];
                Assert.AreEqual(first.actions.Length, repeat.actions.Length, $"반복 {i}: 행동 개수 불일치");
                for (int a = 0; a < first.actions.Length; a++)
                {
                    Assert.AreEqual(first.actions[a].type, repeat.actions[a].type, $"반복 {i}, 인덱스 {a}: 행동 종류 불일치");
                    Assert.AreEqual(first.actions[a].lungeTargetId, repeat.actions[a].lungeTargetId, $"반복 {i}, 인덱스 {a}: 런지 타깃 불일치");
                }
                Assert.AreEqual(first.TotalScore, repeat.TotalScore, 1e-6f, $"반복 {i}: 점수 불일치");
            }
        }

        [Test]
        public void FullSearch_HandlesSymmetricEnemies_WithoutHangingOrCrashing()
        {
            // 스텁 서비스는 실제 Physics/NavMesh 비용을 반영하지 않으므로 여기서 문서의
            // 200ms 목표를 검증하는 게 아니다 — 무한루프/성능 폭주만 잡는 관대한 시간 가드.
            SimWorld world = BuildSymmetricWorld();
            SimServices services = StubServices.Create();

            var stopwatch = Stopwatch.StartNew();
            CandidatePath result = PredictionPlanner.Plan(in world, in services, PredictionSettings.Full)[0];
            stopwatch.Stop();

            Assert.IsNotNull(result);
            Assert.IsFalse(result.isDeadFallback);
            Assert.Greater(result.actions.Length, 0);
            Assert.Less(stopwatch.ElapsedMilliseconds, 5000, "탐색이 비정상적으로 오래 걸림(무한루프/폭주 의심)");
        }
    }
}
