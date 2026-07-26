using NUnit.Framework;
using UnityEngine;
using Game.Sim;
using Game.Prediction;

namespace Game.Sim.Tests
{
    /// <summary>
    /// docs/shared/PREDICTION_CONTRACT.md 10장 순서에서 Wait만 맨 뒤로 옮긴 버전을 확인한다
    /// — Wait가 맨 앞이면 "아직 아무 일도 안 일어난" 동점 상황에서 항상 이겨서, 접근이
    /// 필요한 상황에서 Beam Search가 아예 안 다가가는 회귀가 실제로 있었다(ActionGenerator.cs 주석 참고).
    /// </summary>
    public class ActionGeneratorTests
    {
        static readonly MacroActionType[] ExpectedOrder =
        {
            MacroActionType.MoveForward,
            MacroActionType.MoveLeft,
            MacroActionType.MoveRight,
            MacroActionType.Retreat,
            MacroActionType.Jump,
            MacroActionType.DashForward,
            MacroActionType.DashBackward,
            MacroActionType.DashLeft,
            MacroActionType.DashRight,
            MacroActionType.Attack,
        };

        /// <summary>
        /// 지상 근접몹으로 못박아 스폰한다. 인자 1개짜리 SimWorld.AddEnemy는 슬롯 인덱스로
        /// 아키타입을 굴리는데(ExperimentalAutoSpawn — 현재 10중 7이 공중 원거리), 런지 후보의
        /// <b>종류</b>를 따지는 테스트가 그 분포에 얹히면 밸런스 조정마다 같이 깨진다.
        /// 공중 대상은 단일 Lunge가 아니라 LungeStrike 콤보로 나오는 것이 설계이므로
        /// (ActionGenerator.MakeLungeAction, AerialTargetingTests 참고), 여기선 지상으로 고정한다.
        /// </summary>
        static void AddGroundMelee(ref SimWorld world, Vector3 at) =>
            world.AddEnemy(at, CombatType.Melee, MobilityType.Ground, SizeClass.Normal);

        [Test]
        public void Generate_FollowsWaitLastOrder_WhenEverythingValid()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            AddGroundMelee(ref world, new Vector3(0f, 0f, 1.5f)); // 정면, 평타·런지 사거리 둘 다 안
            SimServices services = StubServices.Create();
            PredictionSettings settings = PredictionSettings.Full;
            var buffer = new MacroAction[settings.maxActionsPerNode];

            int count = ActionGenerator.Generate(in world, in services, in settings, buffer);

            int i = 0;
            foreach (MacroActionType expected in ExpectedOrder)
            {
                Assert.Less(i, count, $"'{expected}'가 나와야 하는데 후보가 부족함");
                Assert.AreEqual(expected, buffer[i].type, $"인덱스 {i} 행동 순서가 예상과 다름");
                i++;
            }
            Assert.Less(i, count, "런지 후보가 하나도 없음(유효 타깃 1명 있는 상황)");
            Assert.AreEqual(MacroActionType.Lunge, buffer[i].type, "Attack 다음은 Lunge여야 함");
            i++;
            Assert.AreEqual(count - 1, i, "Wait는 맨 마지막이어야 함");
            Assert.AreEqual(MacroActionType.Wait, buffer[i].type, "Wait는 맨 마지막이어야 함");
        }

        [Test]
        public void Generate_IncludesJump_OnGround_AndDoubleJumpInAir()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            SimServices services = StubServices.Create();
            PredictionSettings settings = PredictionSettings.Full;
            var buffer = new MacroAction[settings.maxActionsPerNode];

            int groundedCount = ActionGenerator.Generate(in world, in services, in settings, buffer);
            Assert.IsTrue(Contains(buffer, groundedCount, MacroActionType.Jump), "지상에서 점프 후보가 있어야 함");

            world.player.grounded = false;
            world.player.jumpCount = 1;
            int airborneCount = ActionGenerator.Generate(in world, in services, in settings, buffer);
            Assert.IsTrue(Contains(buffer, airborneCount, MacroActionType.Jump), "공중 1회 점프 후 더블 점프 후보가 있어야 함");

            world.player.jumpCount = 2;
            int exhaustedCount = ActionGenerator.Generate(in world, in services, in settings, buffer);
            Assert.IsFalse(Contains(buffer, exhaustedCount, MacroActionType.Jump), "더블 점프 소진 뒤에는 점프 후보가 없어야 함");
        }

        [Test]
        public void JumpMacro_PulsesJumpOnlyOnFirstTick()
        {
            MacroAction jump = MacroAction.Simple(MacroActionType.Jump);
            Assert.IsTrue(jump.ToInputCmd(0f, 0).jump);
            Assert.IsFalse(jump.ToInputCmd(0f, 1).jump);
        }

        [Test]
        public void JumpMacro_UsesRealSimForFirstAndDoubleJump()
        {
            SimWorld world = SimWorld.Create();
            SimServices services = StubServices.Create();
            MacroAction jump = MacroAction.Simple(MacroActionType.Jump);

            InputCmd first = jump.ToInputCmd(0f, 0);
            SimStep.Run(ref world, in first, in services);
            Assert.AreEqual(1, world.player.jumpCount);
            Assert.IsFalse(world.player.grounded);

            InputCmd second = jump.ToInputCmd(0f, 0);
            SimStep.Run(ref world, in second, in services);
            Assert.AreEqual(2, world.player.jumpCount);
            Assert.Greater(world.player.vel.y, 0f);
        }

        [Test]
        public void TerrainLeap_IsGeneratedOnlyForJumpUpEscapeStep()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            PredictionSettings settings = PredictionSettings.Full;
            var buffer = new MacroAction[settings.maxActionsPerNode];
            var jumpServices = new SimServices(new StubCollision(), new JumpEscapePathfinder());

            int jumpCount = ActionGenerator.Generate(in world, in jumpServices, in settings, buffer);
            Assert.IsTrue(Contains(buffer, jumpCount, MacroActionType.TerrainLeap));

            SimServices walkServices = StubServices.Create();
            int walkCount = ActionGenerator.Generate(in world, in walkServices, in settings, buffer);
            Assert.IsFalse(Contains(buffer, walkCount, MacroActionType.TerrainLeap));
        }

        [Test]
        public void TerrainLeap_QueriesGraphAwayFromNearbyEnemyPressure()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.AddEnemy(
                new Vector3(6f, 0f, 0f),
                CombatType.Melee, MobilityType.Ground, SizeClass.Normal);
            var pathfinder = new CapturingJumpPathfinder();
            var services = new SimServices(new StubCollision(), pathfinder);
            PredictionSettings settings = PredictionSettings.Full;
            var buffer = new MacroAction[settings.maxActionsPerNode];

            ActionGenerator.Generate(in world, in services, in settings, buffer);

            Assert.Less(pathfinder.lastGoal.x, world.player.pos.x);
        }

        [Test]
        public void TerrainLeap_PulsesFirstAndDoubleJump_WhileMovingTowardLink()
        {
            MacroAction leap = MacroAction.TerrainLeapAt(90f);
            InputCmd first = leap.ToInputCmd(leap.targetYaw, 0);
            InputCmd second = leap.ToInputCmd(leap.targetYaw, 7);

            Assert.IsTrue(first.jump);
            Assert.IsTrue(second.jump);
            Assert.AreEqual(Vector2.up, first.move);
            Assert.AreEqual(90f, leap.targetYaw);
        }

        sealed class JumpEscapePathfinder : IPathfinder
        {
            public PathStep NextStep(Vector3 from, Vector3 to, int agentMask) =>
                new PathStep { kind = MoveKind.JumpUp, next = from + Vector3.left * 3f + Vector3.up * 2f };
            public int FloorIdAt(Vector3 position) => 0;
            public bool ClampToWalkable(Vector3 pos, float maxDist, out Vector3 onMesh)
            { onMesh = pos; return true; }
        }

        sealed class CapturingJumpPathfinder : IPathfinder
        {
            public Vector3 lastGoal;
            public PathStep NextStep(Vector3 from, Vector3 to, int agentMask)
            {
                lastGoal = to;
                return new PathStep { kind = MoveKind.JumpUp, next = from + Vector3.left + Vector3.up };
            }
            public int FloorIdAt(Vector3 position) => 0;
            public bool ClampToWalkable(Vector3 pos, float maxDist, out Vector3 onMesh)
            { onMesh = pos; return true; }
        }

        static bool Contains(MacroAction[] actions, int count, MacroActionType type)
        {
            for (int i = 0; i < count; i++) if (actions[i].type == type) return true;
            return false;
        }

        [Test]
        public void Generate_ProducesUpToTwoLungeCandidates_WhenTwoValidTargetsExist()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            AddGroundMelee(ref world, new Vector3(0f, 0f, 3f));
            AddGroundMelee(ref world, new Vector3(1f, 0f, 3f));
            SimServices services = StubServices.Create();
            PredictionSettings settings = PredictionSettings.Full;
            var buffer = new MacroAction[settings.maxActionsPerNode];

            int count = ActionGenerator.Generate(in world, in services, in settings, buffer);

            int lungeCount = 0;
            for (int i = 0; i < count; i++)
                if (buffer[i].type == MacroActionType.Lunge) lungeCount++;
            Assert.AreEqual(2, lungeCount, "유효 타깃 2명이면 런지 후보도 2개 나와야 함(계약 10장)");
        }

        /// <summary>
        /// 런지 유효성 판정을 real Sim(PlayerCombat.CanLunge)에 위임한 이후의 회귀 고정.
        /// 옛 독립 재구현은 "플레이어의 현재 정면 방향" 기준 cone으로 걸러서, 사거리·높이·LOS가
        /// 전부 유효해도 플레이어 뒤쪽 적은 후보에서 제외됐다 — 실제 게임에서 마우스로 뒤를
        /// 보고 런지하면 되는 상황과 어긋났다. CanLunge는 "이 대상을 정확히 바라본다면winner"을
        /// 가정하고 판정하므로, 현재 정면과 무관하게 사거리·높이·LOS만으로 유효해야 한다.
        /// </summary>
        [Test]
        public void Generate_DoesNotOfferLungeBeyondReducedRange()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.AddEnemy(
                new Vector3(0f, 0f, CombatConfig.LungeMaxRange + 3f),
                CombatType.Melee, MobilityType.Ground, SizeClass.Normal);
            SimServices services = StubServices.Create();
            PredictionSettings settings = PredictionSettings.Full;
            var buffer = new MacroAction[settings.maxActionsPerNode];

            int count = ActionGenerator.Generate(in world, in services, in settings, buffer);

            Assert.IsFalse(Contains(buffer, count, MacroActionType.Lunge),
                "줄인 최대 사거리 밖에서는 즉시 런지 후보가 열리면 안 된다.");
        }

        [Test]
        public void DashMacro_ClosesDistance_AndUnlocksLungeCandidate()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.AddEnemy(
                new Vector3(0f, 0f, CombatConfig.LungeMaxRange + 3f),
                CombatType.Melee, MobilityType.Ground, SizeClass.Normal);
            world.enemies[0].combat.bindTicks = PredictionSettings.MacroTicksPerStep + 1;
            SimServices services = StubServices.Create();
            PredictionSettings settings = PredictionSettings.Full;
            var buffer = new MacroAction[settings.maxActionsPerNode];

            int beforeCount = ActionGenerator.Generate(in world, in services, in settings, buffer);
            Assert.IsFalse(Contains(buffer, beforeCount, MacroActionType.Lunge),
                "대시 전에는 런지 사거리 밖이어야 한다.");

            MacroAction dash = MacroAction.Simple(MacroActionType.DashForward);
            for (int tick = 0; tick < PredictionSettings.MacroTicksPerStep; tick++)
            {
                InputCmd input = dash.ToInputCmd(0f, tick);
                SimStep.Run(ref world, in input, in services);
            }

            int afterCount = ActionGenerator.Generate(in world, in services, in settings, buffer);
            Assert.IsTrue(Contains(buffer, afterCount, MacroActionType.Lunge),
                "한 예측 구간의 전방 대시 뒤에는 런지 후보가 열려야 한다.");
        }

        [Test]
        public void Generate_ProducesLungeCandidate_ForValidTargetBehindPlayer()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);   // yaw=0 → 정면은 +Z
            AddGroundMelee(ref world, new Vector3(0f, 0f, -3f)); // 정반대 방향(-Z), 사거리·높이는 유효
            SimServices services = StubServices.Create();
            PredictionSettings settings = PredictionSettings.Full;
            var buffer = new MacroAction[settings.maxActionsPerNode];

            int count = ActionGenerator.Generate(in world, in services, in settings, buffer);

            bool hasLunge = false;
            for (int i = 0; i < count; i++)
                if (buffer[i].type == MacroActionType.Lunge) hasLunge = true;
            Assert.IsTrue(hasLunge, "사거리·높이·LOS가 유효하면 정면 밖(뒤쪽) 적도 런지 후보여야 함");
        }

        [Test]
        public void Generate_OrdersLungeCandidatesByAscendingTargetId()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            AddGroundMelee(ref world, new Vector3(0f, 0f, 3f));  // id 0
            AddGroundMelee(ref world, new Vector3(1f, 0f, 3f));  // id 1
            SimServices services = StubServices.Create();
            PredictionSettings settings = PredictionSettings.Full;
            var buffer = new MacroAction[settings.maxActionsPerNode];

            int count = ActionGenerator.Generate(in world, in services, in settings, buffer);

            int firstLungeIndex = -1;
            for (int i = 0; i < count; i++)
            {
                if (buffer[i].type != MacroActionType.Lunge) continue;
                firstLungeIndex = i;
                break;
            }
            Assert.GreaterOrEqual(firstLungeIndex, 0);
            Assert.AreEqual(0, buffer[firstLungeIndex].lungeTargetId);
            Assert.AreEqual(1, buffer[firstLungeIndex + 1].lungeTargetId);
        }
    }
}
