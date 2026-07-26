using NUnit.Framework;
using UnityEngine;
using Game.Sim;
using Game.Prediction;

namespace Game.Sim.Tests
{
    /// <summary>
    /// 공중(Flying) 적 타겟팅 보완의 인수/재현 테스트.
    ///
    /// 배경(원인 분석): 예측은 이미 Jump·Lunge(우클릭)·Attack(좌클릭)을 후보로 생성하고,
    /// 실제 Sim의 런지는 대상과 같은 고도(enemy.y + LungeAimUp=0.4m)로 착지시켜 좌클릭
    /// 사거리·높이차(1m) 안에 세운다. 그런데도 공중 적을 못 잡는 이유:
    ///   (A) ThreatEvaluator가 공중 적 근접을 "순수 감점"으로만 봐서, 처치가 성사되기 전에
    ///       Beam이 접근 가지를 쳐낸다.
    ///   (B) 매크로 15틱·1펄스 구조로는 "런지 → (곧바로) 좌클릭/더블점프" 서브틱 콤보를
    ///       한 매크로가 표현하지 못한다(다음 매크로 경계 +15틱이면 이미 낙하·bind 해제).
    ///
    /// 이 파일의 EngagesLoneReachableFlyingEnemy가 전체 보완(Phase 1 스코어 + Phase 2 복합
    /// 매크로)의 인수 기준이다 — 보완 전에는 red, 후에는 green이어야 한다.
    /// </summary>
    public class AerialTargetingTests
    {
        /// <summary>
        /// 사람이라면 "우클릭 접근 → 좌클릭"으로 확실히 잡는 배치: 공중 원거리 1마리가
        /// hover 고도(플레이어 y + FlyHoverOffset, 개체별 ±FlyHoverJitter), 런지 사거리(1.2~7m) 안, 정면.
        /// StubCollision은 지면 y=0·LOS 통과·캡슐 점유 가능이라 런지 게이팅이 성립한다.
        /// </summary>
        static SimWorld BuildLoneFlyerWorld()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.AddEnemy(new Vector3(0f, AIConfig.FlyHoverOffset, 5f),
                CombatType.Ranged, MobilityType.Flying, SizeClass.Normal);
            return world;
        }

        /// <summary>인수 기준: 도달 가능한 단독 공중 적을 본탐색이 최소한 교전(피해 1 이상)해야 한다.
        /// 보완(Phase 1 스코어 + Phase 2 복합 매크로) 전에는 실패하는 것이 정상이다.</summary>
        [Test]
        public void FullSearch_EngagesLoneReachableFlyingEnemy()
        {
            SimWorld world = BuildLoneFlyerWorld();
            SimServices services = StubServices.Create();

            CandidatePath[] results = PredictionPlanner.Plan(in world, in services, PredictionSettings.Full);
            CandidatePath best = results[0];

            Assert.GreaterOrEqual(best.damageDealt, 1,
                "본탐색이 도달 가능한 단독 공중 적을 전혀 못 때리고 있다 — 공중 타겟팅 보완 필요.");
        }

        /// <summary>
        /// 원인 분석 검증(하네스): 예측의 런지 유효성 판정이 공중 적을 대상으로 인정하는지 확인.
        /// 이게 false면 애초에 런지 계열 후보 자체가 안 생겨 복합 매크로도 성립하지 못한다.
        /// 공중 대상은 Phase 2부터 단일 Lunge가 아니라 LungeStrike로 나오므로(아래
        /// ActionGenerator_UsesLungeStrike_ForFlyingTarget이 그 구체 종류를 엄밀히 검증한다),
        /// 여기선 "런지 계열이 하나라도 생성됐는가"만 넓게 확인한다.
        /// </summary>
        [Test]
        public void ActionGenerator_ProducesLungeCandidate_ForReachableFlyingEnemy()
        {
            SimWorld world = BuildLoneFlyerWorld();
            SimServices services = StubServices.Create();
            PredictionSettings settings = PredictionSettings.Full;
            var buffer = new MacroAction[settings.maxActionsPerNode];

            int count = ActionGenerator.Generate(in world, in services, in settings, buffer);

            bool hasLunge = false;
            for (int i = 0; i < count; i++)
                if (buffer[i].type == MacroActionType.Lunge || buffer[i].type == MacroActionType.LungeStrike)
                    { hasLunge = true; break; }

            Assert.IsTrue(hasLunge,
                "hover 고도·런지 사거리 안의 공중 적인데 런지 계열 후보가 생성되지 않았다.");
        }

        /// <summary>공중 적 대상 런지 후보는 복합 콤보(LungeStrike)여야 한다(지상은 단일 Lunge).</summary>
        [Test]
        public void ActionGenerator_UsesLungeStrike_ForFlyingTarget()
        {
            SimWorld world = BuildLoneFlyerWorld();
            SimServices services = StubServices.Create();
            PredictionSettings settings = PredictionSettings.Full;
            var buffer = new MacroAction[settings.maxActionsPerNode];

            int count = ActionGenerator.Generate(in world, in services, in settings, buffer);

            bool hasStrike = false, hasPlainLunge = false;
            for (int i = 0; i < count; i++)
            {
                if (buffer[i].type == MacroActionType.LungeStrike) hasStrike = true;
                if (buffer[i].type == MacroActionType.Lunge) hasPlainLunge = true;
            }
            Assert.IsTrue(hasStrike, "공중 대상엔 LungeStrike 콤보가 나와야 함");
            Assert.IsFalse(hasPlainLunge, "공중 단독 대상이면 단일 Lunge가 아니라 콤보로 나와야 함");
        }

        /// <summary>
        /// 핵심 검증: LungeStrike 콤보 한 매크로(15틱)를 실제 SimStep으로 재생하면 공중 적을
        /// 처치한다(런지 임팩트 1 + 착지 직후 좌클릭 1 = 2hp 소진). Beam Search와 동일하게
        /// 매 틱 가장 가까운 적을 향하도록 조준(ComputeAimYaw)한다.
        /// </summary>
        [Test]
        public void LungeStrikeMacro_KillsLoneFlyingEnemy_WithinOneMacro()
        {
            SimWorld world = BuildLoneFlyerWorld();
            SimServices services = StubServices.Create();
            var strike = MacroAction.LungeStrikeTo(world.enemies[0].id);

            for (int t = 0; t < PredictionSettings.Full.macroTicks; t++)
            {
                float yaw = BeamSearch.ComputeAimYaw(in world);
                InputCmd cmd = strike.ToInputCmd(yaw, t);
                SimStep.Run(ref world, in cmd, in services);
            }

            Assert.IsFalse(world.enemies[0].alive,
                "LungeStrike 한 매크로로 2hp 공중 적을 처치해야 함(런지 임팩트+좌클릭).");
        }

        /// <summary>지상에서 조준 중(고도 고정) 공중 슈터가 점프 사거리 안이면 JumpStrike 후보가 나오고,
        /// 조준하지 않는(움직이는) 공중 적에겐 나오지 않는다.</summary>
        [Test]
        public void ActionGenerator_ProducesJumpStrike_OnlyForFrozenAerialShooterInReach()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.AddEnemy(new Vector3(0f, AIConfig.FlyHoverOffset, 1.2f),
                CombatType.Ranged, MobilityType.Flying, SizeClass.Normal);
            world.enemies[0].ai.state = EnemyState.Aim;   // 고도 고정
            SimServices services = StubServices.Create();
            PredictionSettings settings = PredictionSettings.Full;
            var buffer = new MacroAction[settings.maxActionsPerNode];

            int aimingCount = ActionGenerator.Generate(in world, in services, in settings, buffer);
            Assert.IsTrue(Contains(buffer, aimingCount, MacroActionType.JumpStrike),
                "조준 중(고도 고정) 공중 슈터엔 JumpStrike 후보가 나와야 함");

            world.enemies[0].ai.state = EnemyState.Reposition;   // 움직이는 중
            int movingCount = ActionGenerator.Generate(in world, in services, in settings, buffer);
            Assert.IsFalse(Contains(buffer, movingCount, MacroActionType.JumpStrike),
                "움직이는 공중 적엔 점프로 못 따라잡으므로 JumpStrike를 만들면 안 됨");
        }

        /// <summary>JumpStrike 한 매크로(점프+정점 근처 좌클릭)가 지상에서 조준 중인 공중 슈터에
        /// 실제로 좌클릭 피해를 입힌다(런지 없이 대공).</summary>
        [Test]
        public void JumpStrikeMacro_HitsFrozenAerialShooter_FromGround()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.AddEnemy(new Vector3(0f, AIConfig.FlyHoverOffset, 1.2f),
                CombatType.Ranged, MobilityType.Flying, SizeClass.Normal);
            world.enemies[0].ai.state = EnemyState.Aim;
            world.enemies[0].ai.committedDir = new Vector3(0f, 0f, -1f);
            SimServices services = StubServices.Create();

            int hp0 = world.enemies[0].combat.health;
            var strike = MacroAction.JumpStrikeAction();
            for (int t = 0; t < PredictionSettings.MacroTicksPerStep; t++)
            {
                float yaw = BeamSearch.ComputeAimYaw(in world);
                InputCmd cmd = strike.ToInputCmd(yaw, t);
                SimStep.Run(ref world, in cmd, in services);
            }

            Assert.Less(world.enemies[0].combat.health, hp0,
                "JumpStrike 한 매크로로 얼어붙은 공중 슈터에게 좌클릭 피해를 줘야 함");
        }

        [Test]
        public void AerialPursuit_LocksFlyingTarget_AndSequencesDoubleJumpThenLunge()
        {
            SimWorld world = BuildLoneFlyerWorld();
            SimServices services = StubServices.Create();
            PredictionSettings settings = PredictionSettings.Full;
            var buffer = new MacroAction[settings.maxActionsPerNode];

            int count = ActionGenerator.Generate(in world, in services, in settings, buffer);
            MacroAction pursuit = default;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                if (buffer[i].type != MacroActionType.AerialPursuit) continue;
                pursuit = buffer[i];
                found = true;
                break;
            }

            Assert.IsTrue(found);
            Assert.AreEqual(world.enemies[0].id, pursuit.lungeTargetId);
            Assert.IsTrue(pursuit.ToInputCmd(0f, 0).jump);
            Assert.IsTrue(pursuit.ToInputCmd(0f, 7).jump);
            InputCmd lunge = pursuit.ToInputCmd(0f, 11);
            Assert.IsTrue(lunge.lunge);
            Assert.AreEqual(world.enemies[0].id, lunge.lungeTargetId);
        }

        /// <summary>
        /// 회귀 고정: <b>지상에선 안 닿지만 더블점프 후엔 닿는</b> 공중 적에게도 AerialPursuit
        /// 후보가 나와야 한다.
        ///
        /// 예전 TryFindAerialPursuitTarget은 지상 자세로 높이차를 재서 LungeHeightTolerance(6m)를
        /// 넘으면 잘라냈다 — 즉 "지금 그냥 우클릭해도 닿는 적"만 후보가 됐고, 정작 이 매크로가
        /// 존재하는 이유인 "점프해서 닿는 적"은 통째로 빠졌다. 아래 6.6m는 그 옛 상한(6m) 위,
        /// 새 판정 기준인 우클릭 시점 고도(+AerialPursuitRiseGain ≈ 1.8m) 아래다.
        /// </summary>
        [Test]
        public void ActionGenerator_ProducesAerialPursuit_ForTargetReachableOnlyAfterDoubleJump()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.AddEnemy(new Vector3(0f, 6.6f, 3.2f),
                CombatType.Ranged, MobilityType.Flying, SizeClass.Normal);

            Assert.Greater(world.enemies[0].pos.y - world.player.pos.y,
                CombatConfig.LungeHeightTolerance,
                "전제 조건: 지상에선 런지 높이차 허용을 넘어야 이 회귀를 검증할 수 있다.");

            SimServices services = StubServices.Create();
            PredictionSettings settings = PredictionSettings.Full;
            var buffer = new MacroAction[settings.maxActionsPerNode];

            int count = ActionGenerator.Generate(in world, in services, in settings, buffer);

            Assert.IsTrue(Contains(buffer, count, MacroActionType.AerialPursuit),
                "더블점프 후엔 런지가 성립하는 높이인데 AerialPursuit 후보가 생성되지 않았다 — " +
                "판정이 다시 지상 자세로 되돌아갔는지 확인할 것.");
        }

        static bool Contains(MacroAction[] actions, int count, MacroActionType type)
        {
            for (int i = 0; i < count; i++) if (actions[i].type == type) return true;
            return false;
        }

        /// <summary>
        /// StubCollision은 SampleGround/CanOccupyCapsule이 항상 성공이라, "실제 아레나에서 착지점
        /// 아래 지면이 안 잡힘"(구덩이·다층 지형·hover 고도가 raycast 한계치에 걸림) 상황을 재현하지
        /// 못한다. 실전에서 "공중 유닛에 접근 시도 자체를 안 함"으로 관측된 버그가 바로 이 실패
        /// 모드였다 — 예측의 옛 독립 재구현(ActionGenerator.CanTargetForLunge, 지금은 삭제되고
        /// PlayerCombat.CanLunge 위임으로 교체됨)이 그런 조건을 요구했는데, 그 조건은 real Sim
        /// (PlayerCombat.TryLockDestination)엔 애초에 없다. 이 스텁으로 그 상황을 강제해서
        /// 회귀를 고정한다 — 지금은 CanLunge가 애초에 지면을 안 보므로 항상 통과해야 정상.
        /// </summary>
        sealed class NoGroundBeneathCollision : ICollision
        {
            public CastHit CapsuleCast(Vector3 bottom, Vector3 top, float radius, Vector3 dir, float maxDist) => default;
            public CastHit Raycast(Vector3 origin, Vector3 dir, float maxDist) => default;
            public bool SampleGround(Vector3 feet, float maxDown, out float groundY) { groundY = 0f; return false; }
            public bool HasLineOfSight(Vector3 from, Vector3 to) => true;
            public bool CanOccupyCapsule(Vector3 feet, float radius, float height) => false;
            public Vector3 Depenetrate(Vector3 feet, float radius, float height) => Vector3.zero;
        }

        /// <summary>회귀 고정: 착지점 아래 지면이 안 잡히는(구덩이 위 hover 등) 상황에서도
        /// 공중 적 런지 계열 후보는 생성돼야 한다 — real Sim은 애초에 지면을 요구하지 않는다.</summary>
        [Test]
        public void ActionGenerator_ProducesLungeStrike_EvenWhenNoGroundBeneathLandingSpot()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.AddEnemy(new Vector3(0f, AIConfig.FlyHoverOffset, 5f),
                CombatType.Ranged, MobilityType.Flying, SizeClass.Normal);
            var services = new SimServices(new NoGroundBeneathCollision(), new StubPathfinder());
            PredictionSettings settings = PredictionSettings.Full;
            var buffer = new MacroAction[settings.maxActionsPerNode];

            int count = ActionGenerator.Generate(in world, in services, in settings, buffer);

            Assert.IsTrue(Contains(buffer, count, MacroActionType.LungeStrike),
                "착지점 아래 지면이 없어도(real Sim은 지면을 요구하지 않으므로) LungeStrike 후보가 나와야 함");
        }
    }
}
