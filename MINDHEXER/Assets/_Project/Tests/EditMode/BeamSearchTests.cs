using NUnit.Framework;
using UnityEngine;
using Game.Sim;
using Game.Prediction;

namespace Game.Sim.Tests
{
    /// <summary>
    /// docs/shared/PREDICTION_INTEGRATION_PLAN.md 15장 G3 미니 탐색 기준:
    /// 사망 후보 제거, 생존 경로 1개 이상 반환, 반복 실행 시 같은 후보 반환.
    /// docs/shared/PREDICTION_CONTRACT.md 10장 "반환 후보 최대 3"도 함께 확인한다.
    /// </summary>
    public class BeamSearchTests
    {
        static SimWorld BuildSafeWorld()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(new Vector3(0f, 0f, 20f));
            world.AddEnemy(new Vector3(0f, 0f, 5f));
            world.AddEnemy(new Vector3(3f, 0f, 4f));
            return world;
        }

        /// <summary>플레이어 HP 1 + 적이 이미 공격 Active 직전(다음 틱 확정 명중)인, 회피 불가능한 상황.</summary>
        static SimWorld BuildLethalWorld()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.player.combat.hp = 1;
            // 아키타입을 굴리는 AddEnemy(at) 오버로드를 쓰면 공중 원거리로 뽑혀(ExperimentalAutoSpawn
            // 분포상 10중 7) 근접 스윙이 아예 안 나가고 플레이어가 살아버린다 — 지상 근접으로 못박는다.
            world.AddEnemy(new Vector3(0f, 0f, 0.3f),
                CombatType.Melee, MobilityType.Ground, SizeClass.Normal);

            ref EnemySim enemy = ref world.enemies[0];
            enemy.ai.state = EnemyState.Windup;
            enemy.ai.stateTicks = AIConfig.MeleeWindupTicks - 1;
            enemy.ai.committedDir = new Vector3(0f, 0f, -1f);
            return world;
        }

        /// <summary>플레이어를 원형으로 둘러싼 count마리. SimWorld.AddEnemy가 슬롯 인덱스로
        /// 아키타입을 결정론적 배분하므로(ExperimentalAutoSpawn) count가 커지면 아키타입이 섞인다.
        /// 여기선 "섞인 군중에서 탐색이 안 터진다"만 보므로 구체 분포에 의존하지 않는다.</summary>
        static SimWorld BuildRingWorld(int count, float radius)
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            for (int i = 0; i < count; i++)
            {
                float angle = (float)i / count * Mathf.PI * 2f;
                world.AddEnemy(new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }
            return world;
        }

        /// <summary>
        /// docs/shared/OPTIMIZATION.md가 목표로 잡은 규모(적 8~64마리)의 아래쪽 끝(4~8마리),
        /// 근접·원거리·대형몹이 섞인 상황에서 본탐색(Full: Beam 12/3초)이 예외 없이 sane한
        /// 결과를 내는지 확인한다. 성능(ms) 자체는 에디터 환경에 따라 들쭉날쭉해서 여기선
        /// 안 재고, PredictionVisualizer의 스트레스 시나리오로 사용자가 직접 확인한다.
        /// </summary>
        [TestCase(4)]
        [TestCase(6)]
        [TestCase(8)]
        public void FullSearch_HandlesMixedArchetypeEnemyCounts_WithoutBreaking(int enemyCount)
        {
            SimWorld world = BuildRingWorld(enemyCount, 8f);
            SimServices services = StubServices.Create();

            CandidatePath[] results = null;
            Assert.DoesNotThrow(() =>
                results = PredictionPlanner.Plan(in world, in services, PredictionSettings.Full));

            Assert.GreaterOrEqual(results.Length, 1);
            Assert.LessOrEqual(results.Length, 3, "계약 10장 \"반환 후보 최대 3\"");
            foreach (CandidatePath result in results)
            {
                Assert.IsFalse(float.IsNaN(result.TotalScore));
                Assert.GreaterOrEqual(result.killCount, 0);
                Assert.LessOrEqual(result.killCount, enemyCount, "킬 수가 실제 적 수를 넘을 수 없음");
            }
        }

        /// <summary>같은 다수 적 스냅샷 + 같은 입력이면 반복 실행해도 같은 결과가 나와야 한다 —
        /// 적 수가 늘어난다고 StateDeduplicator/BeamSearch의 결정론이 깨지면 안 됨.</summary>
        [Test]
        public void FullSearch_IsDeterministic_WithEightMixedArchetypeEnemies()
        {
            SimWorld world = BuildRingWorld(8, 8f);
            SimServices services = StubServices.Create();

            CandidatePath first = PredictionPlanner.Plan(in world, in services, PredictionSettings.Full)[0];
            CandidatePath repeat = PredictionPlanner.Plan(in world, in services, PredictionSettings.Full)[0];

            Assert.AreEqual(first.actions.Length, repeat.actions.Length);
            for (int i = 0; i < first.actions.Length; i++)
                Assert.AreEqual(first.actions[i].type, repeat.actions[i].type, $"인덱스 {i}: 행동 종류 불일치");
            Assert.AreEqual(first.TotalScore, repeat.TotalScore, 1e-6f);
        }

        [Test]
        public void MiniSearch_SurvivesAndReturnsPlan_WhenNoImmediateThreat()
        {
            SimWorld world = BuildSafeWorld();
            SimServices services = StubServices.Create();
            CandidatePath[] results = PredictionPlanner.Plan(in world, in services, PredictionSettings.Mini);
            CandidatePath result = results[0];

            Assert.IsNotNull(result);
            Assert.IsFalse(result.isDeadFallback, "위협이 먼 상황에서 사망 폴백이 나오면 안 됨");
            Assert.Greater(result.actions.Length, 0, "생존 가능한 상황이면 최소 1개 행동은 계획돼야 함");
            Assert.AreNotEqual(float.NegativeInfinity, result.TotalScore);
        }

        [Test]
        public void Plan_ReturnsUpToThreeCandidates_SortedByScoreDescending()
        {
            SimWorld world = BuildSafeWorld();
            SimServices services = StubServices.Create();
            CandidatePath[] results = PredictionPlanner.Plan(in world, in services, PredictionSettings.Mini);

            Assert.GreaterOrEqual(results.Length, 1);
            Assert.LessOrEqual(results.Length, 3, "계약 10장 \"반환 후보 최대 3\"");
            for (int i = 1; i < results.Length; i++)
                Assert.GreaterOrEqual(results[i - 1].TotalScore, results[i].TotalScore, "점수 내림차순이어야 함");
        }

        [Test]
        public void MiniSearch_IsDeterministic_AcrossRepeatedRuns()
        {
            SimWorld world = BuildSafeWorld();
            SimServices services = StubServices.Create();
            PredictionSettings settings = PredictionSettings.Mini;

            CandidatePath first = PredictionPlanner.Plan(in world, in services, settings)[0];

            for (int i = 0; i < 5; i++)
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
        public void MiniSearch_FallsBackToBestScoringDead_WhenAllCandidatesDie()
        {
            SimWorld world = BuildLethalWorld();
            SimServices services = StubServices.Create();
            CandidatePath result = PredictionPlanner.Plan(in world, in services, PredictionSettings.Mini)[0];

            Assert.IsNotNull(result);
            Assert.IsTrue(result.isDeadFallback, "회피 불가능한 즉사 상황이면 사망 폴백이어야 함");
            Assert.Greater(result.actions.Length, 0);
            Assert.Greater(result.durationTicks, 0);
        }

        /// <summary>대형몹 처형(글로리킬)은 트리거 즉시 gloryStage>0로 결과가 잠기고 alive=false는
        /// ~77틱(컷신) 뒤에야 따라온다. 짧은 탐색 지평(매크로 1스텝 15틱)은 컷신 완료를 못 보므로,
        /// 트리거 시점에 즉시 킬로 인정하는지 확인한다.
        /// `PredictionSettings.Mini`는 못 쓴다 — maxActionsPerNode=4라 ActionGenerator의
        /// Priority 순서상 앞 4개(MoveForward/Left/Right/Retreat)로 캡이 이미 다 차서
        /// Attack 자체가 후보로 안 나온다(설계상 의도된 "행동 4개" 제한, 계약 문서 참고).
        /// 그래서 대신 짧은 지평(깊이 1) + 전체 행동 세트를 쓰는 별도 설정으로 검증한다.</summary>
        [Test]
        public void ShortHorizonSearch_CreditsGloryKill_AssoonAsTriggered()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.AddEnemy(new Vector3(0f, 0f, 1f));
            world.enemies[0] = EnemySim.Spawn(0, new Vector3(0f, 0f, 1f),
                CombatType.Melee, MobilityType.Ground, SizeClass.Large);
            world.enemies[0].combat.health = 1; // 다음 평타 한 대로 처형 트리거

            SimServices services = StubServices.Create();
            var settings = new PredictionSettings
            {
                macroTicks = 15,     // 처형 트리거(~6틱)는 담지만 컷신 완주(~77틱)는 못 담는 짧은 지평
                macroDepth = 1,
                beamWidth = 4,
                maxActionsPerNode = 12,   // Attack까지 후보에 오르도록 전체 행동 세트 사용
            };
            CandidatePath result = PredictionPlanner.Plan(in world, in services, settings)[0];

            Assert.GreaterOrEqual(result.killCount, 1,
                "짧은 탐색 지평 안에서도 글로리킬 트리거는 즉시 킬로 인정돼야 함");
        }

        [Test]
        public void FullSearch_DoesNotReturnConsecutiveWait_WhenScoresTie()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            SimServices services = StubServices.Create();

            CandidatePath[] results = PredictionPlanner.Plan(
                in world, in services, PredictionSettings.Full);

            for (int r = 0; r < results.Length; r++)
            {
                bool previousWait = false;
                for (int i = 0; i < results[r].actions.Length; i++)
                {
                    bool currentWait = results[r].actions[i].type == MacroActionType.Wait;
                    Assert.IsFalse(previousWait && currentWait,
                        "계약 1.2: 전술적 근거 없는 연속 Wait는 반환하면 안 된다.");
                    previousWait = currentWait;
                }
            }
        }
    }
}
