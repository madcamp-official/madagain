using NUnit.Framework;
using UnityEngine;
using Game.Sim;
using Game.Prediction;

namespace Game.Sim.Tests
{
    /// <summary>
    /// docs/shared/PREDICTION_CONTRACT.md 11장: 최종 후보를 최초 스냅샷에서 60Hz로
    /// 다시 실행해 PredictedFrame/PredictedActionEvent/controls를 만드는 CandidateReplayer 검증.
    /// </summary>
    public class CandidateReplayerTests
    {
        [Test]
        public void Replay_ProducesOneFramePerTick_PlusInitialFrame()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(new Vector3(0f, 0f, 20f));
            world.AddEnemy(new Vector3(0f, 0f, 5f));
            SimServices services = StubServices.Create();

            var settings = PredictionSettings.Mini;
            CandidatePath candidate = PredictionPlanner.Plan(in world, in services, settings)[0];

            bool ok = CandidateReplayer.Replay(in world, in services, candidate, settings.macroTicks);

            Assert.IsTrue(ok, "정상 후보는 재생이 성공해야 함");
            Assert.AreEqual(candidate.durationTicks + 1, candidate.predictedFrames.Length,
                "프레임 수 = 실제 진행 틱 수 + 시작(tick 0) 프레임");
            Assert.AreEqual(candidate.predictedFrames.Length - 1, candidate.controls.Length,
                "controls는 시작 프레임 없이 틱당 1개");
            Assert.AreEqual(0, candidate.predictedFrames[0].tick);
            Assert.AreEqual(candidate.durationTicks, candidate.predictedFrames[candidate.predictedFrames.Length - 1].tick);
        }

        [Test]
        public void Replay_IsDeterministic_AcrossRepeatedCalls()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(new Vector3(0f, 0f, 20f));
            world.AddEnemy(new Vector3(0f, 0f, 5f));
            world.AddEnemy(new Vector3(3f, 0f, 4f));
            SimServices services = StubServices.Create();

            var settings = PredictionSettings.Mini;
            CandidatePath candidate = PredictionPlanner.Plan(in world, in services, settings)[0];

            var first = new CandidatePath { candidateId = candidate.candidateId, actions = candidate.actions, mapVersion = candidate.mapVersion, isDeadFallback = candidate.isDeadFallback };
            var second = new CandidatePath { candidateId = candidate.candidateId, actions = candidate.actions, mapVersion = candidate.mapVersion, isDeadFallback = candidate.isDeadFallback };

            CandidateReplayer.Replay(in world, in services, first, settings.macroTicks);
            CandidateReplayer.Replay(in world, in services, second, settings.macroTicks);

            Assert.AreEqual(first.predictedFrames.Length, second.predictedFrames.Length);
            for (int i = 0; i < first.predictedFrames.Length; i++)
            {
                Assert.AreEqual(first.predictedFrames[i].playerPosition, second.predictedFrames[i].playerPosition, $"틱 {i}: 위치 불일치");
                Assert.AreEqual(first.predictedFrames[i].playerYaw, second.predictedFrames[i].playerYaw, $"틱 {i}: yaw 불일치");
            }
            Assert.AreEqual(first.defeatEvents.Length, second.defeatEvents.Length);
            for (int i = 0; i < first.defeatEvents.Length; i++)
            {
                Assert.AreEqual(first.defeatEvents[i].tick, second.defeatEvents[i].tick);
                Assert.AreEqual(first.defeatEvents[i].enemyId, second.defeatEvents[i].enemyId);
                Assert.AreEqual(first.defeatEvents[i].worldPosition, second.defeatEvents[i].worldPosition);
            }
        }

        [Test]
        public void Replay_RecordsDefeatAtSameTickAndPosition_AsLegacySecondReplay()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.AddEnemy(new Vector3(0f, 0f, 3f));
            world.enemies[0].combat.health = 1;
            SimServices services = StubServices.Create();
            var candidate = new CandidatePath
            {
                actions = new[] { MacroAction.LungeTo(world.enemies[0].id) },
                mapVersion = world.mapVersion,
            };

            const int replayTicks = 30;
            Assert.IsTrue(CandidateReplayer.Replay(in world, in services, candidate, replayTicks));

            SimWorld legacy = Snapshot.Clone(in world);
            bool wasDefeated = !legacy.enemies[0].alive || legacy.enemies[0].combat.gloryStage > 0;
            int expectedTick = -1;
            Vector3 expectedPosition = default;
            for (int tick = 0; tick < replayTicks; tick++)
            {
                MacroAction action = candidate.actions[0];
                float yaw = action.ResolveYaw(in legacy, BeamSearch.ComputeAimYaw(in legacy));
                InputCmd cmd = action.ToInputCmd(yaw, tick);
                SimStep.Run(ref legacy, in cmd, in services);
                bool defeated = !legacy.enemies[0].alive || legacy.enemies[0].combat.gloryStage > 0;
                if (!wasDefeated && defeated)
                {
                    expectedTick = tick;
                    expectedPosition = legacy.enemies[0].pos;
                    break;
                }
                wasDefeated = defeated;
            }

            Assert.GreaterOrEqual(expectedTick, 0, "테스트 픽스처가 실제 처치를 만들어야 함");
            Assert.AreEqual(1, candidate.defeatEvents.Length);
            Assert.AreEqual(expectedTick, candidate.defeatEvents[0].tick);
            Assert.AreEqual(world.enemies[0].id, candidate.defeatEvents[0].enemyId);
            Assert.AreEqual(expectedPosition, candidate.defeatEvents[0].worldPosition);
        }

        /// <summary>
        /// DetectEvents가 평타 발동(attackPhase PhNone→PhWindup)을 잡는지 본다.
        /// 후보를 <b>직접 만들어</b> 넣는 게 핵심이다 — 옛 버전은 PredictionPlanner.Plan(...)[0]을
        /// 썼는데, 같은 픽스처에서 런지도 대형몹 글로리킬을 트리거하게 된 뒤로 1위 경로가
        /// Lunge로 바뀌어 "평타 이벤트가 없다"고 실패했다. 이 테스트가 검증하려는 건
        /// 플래너의 선택이 아니라 리플레이어의 이벤트 검출이므로 입력을 고정한다
        /// (바로 아래 Replay_RecordsLungeEvent_AtRealTriggerTick과 같은 방식).
        /// </summary>
        [Test]
        public void Replay_RecordsAttackEvent_AtRealTriggerTick()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.AddEnemy(new Vector3(0f, 0f, 1f));
            world.enemies[0] = EnemySim.Spawn(0, new Vector3(0f, 0f, 1f),
                CombatType.Melee, MobilityType.Ground, SizeClass.Large);
            world.enemies[0].combat.health = 1;
            SimServices services = StubServices.Create();

            var candidate = new CandidatePath
            {
                actions = new[] { MacroAction.Simple(MacroActionType.Attack) },
                mapVersion = world.mapVersion,
            };

            bool ok = CandidateReplayer.Replay(in world, in services, candidate, 15);

            Assert.IsTrue(ok);
            bool hasAttackEvent = false;
            foreach (PredictedActionEvent e in candidate.actionEvents)
                if (e.type == PredictedActionType.Attack) hasAttackEvent = true;
            Assert.IsTrue(hasAttackEvent, "글로리킬을 트리거한 평타 이벤트가 기록돼야 함");
        }

        [Test]
        public void Replay_RecordsLungeEvent_AtRealTriggerTick()
        {
            // LungeWindupTicks=0이라 PlayerCombat.Step은 LgNone에서 곧장 LgTravel로 넘어간다
            // (LgWindup을 절대 거치지 않는다) — DetectEvents가 이 실제 전이를 잡는지 검증.
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.AddEnemy(new Vector3(0f, 0f, 3f));
            SimServices services = StubServices.Create();

            var candidate = new CandidatePath
            {
                actions = new[] { MacroAction.LungeTo(world.enemies[0].id) },
                mapVersion = world.mapVersion,
            };

            bool ok = CandidateReplayer.Replay(in world, in services, candidate, 15);

            Assert.IsTrue(ok);
            bool hasLungeEvent = false;
            foreach (PredictedActionEvent e in candidate.actionEvents)
                if (e.type == PredictedActionType.Lunge) hasLungeEvent = true;
            Assert.IsTrue(hasLungeEvent, "런지(우클릭) 발동 이벤트가 기록돼야 함 — LgNone→LgTravel 전이를 잡아야 함");
        }

        [Test]
        public void Replay_Fails_WhenMapVersionMismatches()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            SimServices services = StubServices.Create();

            var candidate = new CandidatePath
            {
                actions = System.Array.Empty<MacroAction>(),
                mapVersion = world.mapVersion + 1, // 일부러 불일치
            };

            bool ok = CandidateReplayer.Replay(in world, in services, candidate, 15);

            Assert.IsFalse(ok);
            Assert.AreEqual(0, candidate.predictedFrames.Length);
            Assert.AreEqual(0, candidate.defeatEvents.Length);
        }

        /// <summary>
        /// 경계가 양쪽 다 <b>이하(inclusive)</b>인지 본다: |Δ| ≤ Perfect창이면 Perfect,
        /// 그 바로 밖부터 Good창까지는 Good.
        /// 창 크기는 체감 난이도 피드백으로 계속 조정되는 값이라(3→5→8, 8→11→16→22)
        /// 리터럴로 박지 않고 상수에서 유도한다 — 튜닝할 때마다 테스트가 같이 깨지면 안 된다.
        /// </summary>
        [TestCase(-RhythmJudge.PerfectWindowTicks, RhythmJudgement.Perfect)]
        [TestCase(RhythmJudge.PerfectWindowTicks, RhythmJudgement.Perfect)]
        [TestCase(-(RhythmJudge.PerfectWindowTicks + 1), RhythmJudgement.Good)]
        [TestCase(RhythmJudge.PerfectWindowTicks + 1, RhythmJudgement.Good)]
        [TestCase(-RhythmJudge.GoodWindowTicks, RhythmJudgement.Good)]
        [TestCase(RhythmJudge.GoodWindowTicks, RhythmJudgement.Good)]
        public void RhythmJudge_UsesInclusivePerfectAndGoodBoundaries(
            int offset, RhythmJudgement expected)
        {
            const int eventTick = 100;   // Good창이 넓어져도 제출 틱이 음수로 안 가게 넉넉히
            var judge = new RhythmJudge(new[]
            {
                new PredictedActionEvent { tick = eventTick, type = PredictedActionType.Attack, targetId = -1 }
            });

            Assert.AreEqual(expected, judge.Submit(PredictedActionType.Attack, eventTick + offset));
            Assert.AreEqual(expected, judge.GetJudgement(0));
        }

        [Test]
        public void RhythmJudge_WrongInputDoesNotConsume_AndMissingInputMissesAtGoodWindowEnd()
        {
            var judge = new RhythmJudge(new[]
            {
                new PredictedActionEvent { tick = 10, type = PredictedActionType.Jump, targetId = -1 }
            });

            Assert.AreEqual(RhythmJudgement.Pending,
                judge.Submit(PredictedActionType.Attack, 10));
            Assert.AreEqual(-1, judge.CompleteTick(10 + RhythmJudge.GoodWindowTicks - 1));
            Assert.AreEqual(0, judge.CompleteTick(10 + RhythmJudge.GoodWindowTicks));
            Assert.AreEqual(RhythmJudgement.Miss, judge.GetJudgement(0));
        }

        [Test]
        public void RhythmJudge_EarlyInputBuffersOnlyOneNearestMatchingEvent()
        {
            var judge = new RhythmJudge(new[]
            {
                new PredictedActionEvent { tick = 10, type = PredictedActionType.Attack, targetId = -1 },
                new PredictedActionEvent { tick = 14, type = PredictedActionType.Attack, targetId = -1 },
            });

            Assert.AreEqual(RhythmJudgement.Perfect,
                judge.Submit(PredictedActionType.Attack, 12));
            Assert.AreEqual(RhythmJudgement.Perfect, judge.GetJudgement(0));
            Assert.AreEqual(RhythmJudgement.Pending, judge.GetJudgement(1));
        }

        [Test]
        public void RhythmJudge_DisplayTimeMapping_AlignsExactVisualTargetWithEventTick()
        {
            const int eventTick = 120;
            const float targetRealTime = 10f;
            const float lateGoodSeconds = 0.42f;

            Assert.AreEqual(eventTick, RhythmJudge.MapDisplayTimeToTick(
                targetRealTime, targetRealTime, eventTick, lateGoodSeconds));
            Assert.AreEqual(eventTick - RhythmJudge.PerfectWindowTicks,
                RhythmJudge.MapDisplayTimeToTick(
                    targetRealTime - RhythmJudge.PerfectWindowTicks / (float)SimConfig.TickRate,
                    targetRealTime, eventTick, lateGoodSeconds));
            Assert.AreEqual(eventTick + RhythmJudge.GoodWindowTicks,
                RhythmJudge.MapDisplayTimeToTick(
                    targetRealTime + lateGoodSeconds,
                    targetRealTime, eventTick, lateGoodSeconds));
        }
    }
}
