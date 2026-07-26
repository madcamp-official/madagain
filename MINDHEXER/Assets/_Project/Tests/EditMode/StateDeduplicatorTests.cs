using NUnit.Framework;
using UnityEngine;
using Game.Sim;
using Game.Prediction;

namespace Game.Sim.Tests
{
    /// <summary>
    /// StateDeduplicator의 0.5m 위치 격자 경계 및 상태 필드 민감도를 확인한다.
    /// 격자는 반올림(RoundToInt(x/0.5))이라 경계가 0.25m 배수에서 생긴다 — 두 지점이
    /// 격자 폭(0.5m) 미만 떨어져 있어도 경계에 걸치면 다른 키가 나올 수 있으므로,
    /// "같은 칸" 케이스는 경계에서 충분히 떨어진 값으로 검증한다.
    /// </summary>
    public class StateDeduplicatorTests
    {
        [Test]
        public void ChargeDirectionAndTraversalState_AffectKey()
        {
            SimWorld first = SimWorld.Create();
            first.player = PlayerSim.Spawn(Vector3.zero);
            first.AddEnemy(new Vector3(0f, 0f, 5f),
                CombatType.Melee, MobilityType.Charge, SizeClass.Normal);
            first.enemies[0].ai.state = EnemyState.ChargeRun;
            first.enemies[0].ai.committedDir = Vector3.back;

            SimWorld differentDirection = Snapshot.Clone(in first);
            differentDirection.enemies[0].ai.committedDir = Vector3.right;
            Assert.AreNotEqual(
                StateDeduplicator.ComputeKey(in first, 0),
                StateDeduplicator.ComputeKey(in differentDirection, 0));

            SimWorld traversing = Snapshot.Clone(in first);
            traversing.enemies[0].traversalPhase = TraversalPhase.Airborne;
            traversing.enemies[0].activeMoveKind = MoveKind.Drop;
            Assert.AreNotEqual(
                StateDeduplicator.ComputeKey(in first, 0),
                StateDeduplicator.ComputeKey(in traversing, 0));
        }
        static SimWorld BuildWorld(Vector3 playerPos, bool dashReady = true)
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(playerPos);
            if (!dashReady) world.player.dashCharges = 0;
            return world;
        }

        [Test]
        public void PositionsWithinSameGridCell_ProduceSameKey()
        {
            SimWorld a = BuildWorld(new Vector3(0f, 0f, 0f));
            SimWorld b = BuildWorld(new Vector3(0.2f, 0f, 0.2f)); // 격자 경계(±0.25)에서 충분히 안쪽
            Assert.AreEqual(StateDeduplicator.ComputeKey(in a, 0), StateDeduplicator.ComputeKey(in b, 0));
        }

        [Test]
        public void PositionsAcrossGridBoundary_ProduceDifferentKeys()
        {
            SimWorld a = BuildWorld(new Vector3(0f, 0f, 0f));
            SimWorld b = BuildWorld(new Vector3(0.6f, 0f, 0f)); // 0.5m 격자 하나 너머
            Assert.AreNotEqual(StateDeduplicator.ComputeKey(in a, 0), StateDeduplicator.ComputeKey(in b, 0));
        }

        [Test]
        public void DashAvailabilityDifference_ChangesKey_EvenWithSamePosition()
        {
            SimWorld a = BuildWorld(Vector3.zero, dashReady: true);
            SimWorld b = BuildWorld(Vector3.zero, dashReady: false);
            Assert.AreNotEqual(StateDeduplicator.ComputeKey(in a, 0), StateDeduplicator.ComputeKey(in b, 0));
        }

        [Test]
        public void KillCountDifference_ChangesKey_EvenWithIdenticalWorld()
        {
            SimWorld world = BuildWorld(Vector3.zero);
            Assert.AreNotEqual(
                StateDeduplicator.ComputeKey(in world, 0),
                StateDeduplicator.ComputeKey(in world, 1));
        }

        /// <summary>
        /// 공중 접근 시퀀스가 dedup에 뭉개지지 않도록, 같은 위치라도 접지 여부·남은 점프 횟수가
        /// 다르면 다른 키가 나와야 한다(점프 잔량이 남은 분기가 살아남아 마지막 고도 갭을 메움).
        /// </summary>
        [Test]
        public void JumpStateDifference_ChangesKey_EvenWithSamePosition()
        {
            SimWorld grounded = BuildWorld(Vector3.zero);   // grounded=true, jumpCount=0
            SimWorld airborneOneJumpLeft = BuildWorld(Vector3.zero);
            airborneOneJumpLeft.player.grounded = false;
            airborneOneJumpLeft.player.jumpCount = 1;
            Assert.AreNotEqual(
                StateDeduplicator.ComputeKey(in grounded, 0),
                StateDeduplicator.ComputeKey(in airborneOneJumpLeft, 0),
                "접지/점프 상태가 다르면 키가 달라야 함");

            SimWorld airborneNoJumpLeft = Snapshot.Clone(in airborneOneJumpLeft);
            airborneNoJumpLeft.player.jumpCount = 2;   // 더블점프 소진
            Assert.AreNotEqual(
                StateDeduplicator.ComputeKey(in airborneOneJumpLeft, 0),
                StateDeduplicator.ComputeKey(in airborneNoJumpLeft, 0),
                "점프 잔량만 달라도 키가 달라야 함");
        }
    }
}
