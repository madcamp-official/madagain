using System.Collections.Generic;
using UnityEngine;
using Game.Sim;

namespace Game.Prediction
{
    /// <summary>
    /// docs/shared/PREDICTION_CONTRACT.md 11장 "상위 후보만 최초 스냅샷에서 60Hz로 다시
    /// 실행" 담당. Beam 확장 중에는 매크로 스텝(15틱) 끝 상태만 보고 중간 프레임을 버리는데,
    /// 최종 후보로 뽑힌 것만 매 틱 단위로 다시 돌려 PredictedFrame/PredictedActionEvent/
    /// controls를 복원한다. macroTicks는 탐색에 쓴 PredictionSettings와 반드시 같아야 한다
    /// (다르면 candidate.actions의 매크로 스텝 경계가 실제와 어긋난다).
    /// </summary>
    public static class CandidateReplayer
    {
        /// <summary>
        /// candidate.predictedFrames/controls/actionEvents를 채운다. 계약 10장 "즉시 폐기
        /// 조건"(NaN/Infinity, mapVersion 불일치, 예상 밖 사망)에 걸리면 false를 반환한다 —
        /// 호출자는 OPTIMIZATION.md 8장대로 다음 순위 후보로 교체한다. candidate가 이미
        /// 사망 폴백(isDeadFallback)으로 표시돼 있으면 그 사망은 예상된 결과이므로 실패로
        /// 치지 않는다.
        /// </summary>
        public static bool Replay(in SimWorld initialSnapshot, in SimServices services, CandidatePath candidate, int macroTicks)
        {
            if (candidate.mapVersion != initialSnapshot.mapVersion)
            {
                candidate.predictedFrames = System.Array.Empty<PredictedFrame>();
                candidate.controls = System.Array.Empty<InputCmd>();
                candidate.actionEvents = System.Array.Empty<PredictedActionEvent>();
                candidate.defeatEvents = System.Array.Empty<PredictedDefeatEvent>();
                return false;
            }

            SimWorld world = Snapshot.Clone(in initialSnapshot);
            var frames = new List<PredictedFrame>(candidate.actions.Length * macroTicks + 1);
            var controls = new List<InputCmd>(candidate.actions.Length * macroTicks);
            var events = new List<PredictedActionEvent>();
            var defeatEvents = new List<PredictedDefeatEvent>();
            var defeated = new bool[world.enemyCount];
            for (int i = 0; i < world.enemyCount; i++)
                defeated[i] = IsDefeated(in world.enemies[i]);
            frames.Add(MakeFrame(0, in world, in services));

            bool ok = true;
            int tick = 0;

            for (int m = 0; m < candidate.actions.Length && ok; m++)
            {
                MacroAction action = candidate.actions[m];
                for (int t = 0; t < macroTicks; t++)
                {
                    SimWorld before = world;
                    float yaw = action.ResolveYaw(in world, BeamSearch.ComputeAimYaw(in world));
                    InputCmd cmd = action.ToInputCmd(yaw, t);
                    controls.Add(cmd);

                    SimStep.Run(ref world, in cmd, in services);
                    tick++;

                    DetectEvents(tick - 1, in before, in world, in cmd, events);
                    DetectDefeatEvents(tick - 1, in world, defeated, defeatEvents);
                    frames.Add(MakeFrame(tick, in world, in services));

                    if (!IsPhysicallyValid(in world))
                    {
                        ok = false;
                        break;
                    }
                    bool playerDead = world.player.combat.hp <= 0;
                    if (playerDead && !candidate.isDeadFallback) ok = false;
                    if (playerDead) break; // 사망(예상됐든 아니든) — 더 재생할 게 없다
                }
                if (world.player.combat.hp <= 0) break;
            }

            candidate.predictedFrames = frames.ToArray();
            candidate.controls = controls.ToArray();
            candidate.actionEvents = events.ToArray();
            candidate.defeatEvents = defeatEvents.ToArray();
            return ok;
        }

        static bool IsDefeated(in EnemySim enemy) =>
            !enemy.alive || enemy.combat.gloryStage > 0;

        static void DetectDefeatEvents(
            int tick, in SimWorld world, bool[] defeated, List<PredictedDefeatEvent> events)
        {
            for (int i = 0; i < world.enemyCount; i++)
            {
                bool nowDefeated = IsDefeated(in world.enemies[i]);
                if (!defeated[i] && nowDefeated)
                {
                    events.Add(new PredictedDefeatEvent
                    {
                        tick = tick,
                        enemyId = world.enemies[i].id,
                        worldPosition = world.enemies[i].pos,
                    });
                }
                defeated[i] = nowDefeated;
            }
        }

        static bool IsPhysicallyValid(in SimWorld world)
        {
            Vector3 p = world.player.pos;
            return !(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z)
                  || float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z));
        }

        static void DetectEvents(
            int tick, in SimWorld before, in SimWorld after, in InputCmd cmd,
            List<PredictedActionEvent> events)
        {
            if (after.player.jumpCount > before.player.jumpCount)
                events.Add(new PredictedActionEvent { tick = tick, type = PredictedActionType.Jump, targetId = -1 });

            if (before.player.combat.attackPhase == CombatConfig.PhNone
                && after.player.combat.attackPhase == CombatConfig.PhWindup)
                events.Add(new PredictedActionEvent { tick = tick, type = PredictedActionType.Attack, targetId = -1 });

            // 찌르기는 윈드업이 0틱이라 LgNone → LgTravel 로 직행한다.
            // LgWindup 만 보면 예지 결과에 찌르기가 아예 안 잡힌다.
            if (before.player.combat.lungePhase == CombatConfig.LgNone
                && after.player.combat.lungePhase != CombatConfig.LgNone)
                events.Add(new PredictedActionEvent
                {
                    tick = tick,
                    type = PredictedActionType.Lunge,
                    targetId = after.player.combat.lungeTargetId,
                });

            if (before.player.dashTicks == 0 && after.player.dashTicks > 0 && cmd.dash)
            {
                PredictedActionType dashType;
                switch (cmd.dashDirection)
                {
                    case DashDirection.Forward: dashType = PredictedActionType.DashForward; break;
                    case DashDirection.Backward: dashType = PredictedActionType.DashBackward; break;
                    case DashDirection.Left: dashType = PredictedActionType.DashLeft; break;
                    default: dashType = PredictedActionType.DashRight; break;
                }
                events.Add(new PredictedActionEvent { tick = tick, type = dashType, targetId = -1 });
            }
        }

        static PredictedFrame MakeFrame(int tick, in SimWorld world, in SimServices services) => new PredictedFrame
        {
            tick = tick,
            playerPosition = world.player.pos,
            playerYaw = world.player.yaw,
            playerAlive = world.player.combat.hp > 0,
            floorId = services.Pathfinder.FloorIdAt(world.player.pos),
        };
    }
}
