using UnityEngine;
using Game.Sim;

namespace Game.Prediction
{
    /// <summary>
    /// 상황별 행동 후보 생성. docs/shared/PREDICTION_CONTRACT.md 10장 순서에서 Wait만
    /// 맨 뒤로 옮겼다 — Wait가 맨 앞이면 "아직 아무 일도 안 일어난" 동점 상황에서 항상
    /// Wait가 이겨서, 접근이 필요한 상황(적이 처음부터 사거리 밖)에서 Beam Search가 아예
    /// 안 다가가는 문제가 실제로 관찰됐다(평가 롤백만으로는 완전히 안 고쳐짐). 나머지
    /// 순서는 계약 그대로 — 이 순서 자체가 Beam Search 동률 해소 기준이 된다.
    /// 실전과 같은 공개 Sim 규칙(CombatMath, CombatConfig)만 재사용한다.
    /// LINQ/할당 없이 버퍼에 채운다.
    /// </summary>
    public static class ActionGenerator
    {
        static readonly MacroActionType[] Priority =
        {
            MacroActionType.MoveForward,
            MacroActionType.MoveLeft,
            MacroActionType.MoveRight,
            MacroActionType.Retreat,
            MacroActionType.Jump,
            MacroActionType.TerrainLeap,
            MacroActionType.AerialAscent,
            MacroActionType.DashForward,
            MacroActionType.DashBackward,
            MacroActionType.DashLeft,
            MacroActionType.DashRight,
            MacroActionType.JumpStrike,
            MacroActionType.AerialPursuit,
            MacroActionType.Attack,
            MacroActionType.Lunge,
            MacroActionType.Wait,
        };

        /// <summary>JumpStrike가 닿는 최대 높이차(플레이어 발밑 → 공중 적). 단일 점프가 매크로 안에서
        /// 오르는 정점(≈1.8m) + 좌클릭 높이차 허용(1m)보다 약간 보수적으로 잡아, 판정 시점에 확실히
        /// 사거리 안이게 한다. 표준 부유 고도(FlyHoverOffset, 개체별 편차 있음)를 넉넉히 포함.</summary>
        const float JumpStrikeReachHeight = 2.6f;

        /// <summary>buffer에 후보를 채우고 개수를 반환한다. buffer는 호출자가 재사용(풀링)한다.</summary>
        public static int Generate(in SimWorld world, in SimServices services, in PredictionSettings settings, MacroAction[] buffer)
        {
            int count = 0;
            int cap = Mathf.Min(settings.maxActionsPerNode, buffer.Length);
            ref readonly PlayerSim player = ref world.player;
            bool canStartAction = player.combat.hp > 0
                && player.combat.attackPhase == CombatConfig.PhNone
                && player.combat.lungePhase == CombatConfig.LgNone
                && player.combat.gloryPhase == CombatConfig.GlNone;

            for (int p = 0; p < Priority.Length && count < cap; p++)
            {
                switch (Priority[p])
                {
                    case MacroActionType.TerrainLeap:
                        if (TryBuildTerrainLeap(in world, in services, out MacroAction leap))
                            buffer[count++] = leap;
                        break;

                    case MacroActionType.AerialAscent:
                        if (TryBuildAerialAscent(in world, in services, out MacroAction ascent))
                            buffer[count++] = ascent;
                        break;

                    case MacroActionType.Jump:
                        // 동일 jump 입력을 실제 Sim이 grounded/jumpCount로 1단·2단 점프로 구분한다.
                        // 대시 중 입력 버퍼로 뒤늦게 발동하는 후보는 행동 의미가 불명확하므로 제외한다.
                        if (player.dashTicks == 0
                            && player.combat.hitStunTicks == 0
                            && player.combat.lungePhase != CombatConfig.LgTravel
                            && player.jumpCount < 2)
                            buffer[count++] = MacroAction.Simple(MacroActionType.Jump);
                        break;

                    case MacroActionType.JumpStrike:
                        // 얼어붙은 공중 슈터를 런지 없이 점프+좌클릭으로. 지상에서 솟구쳐 만나므로
                        // 점프 가능(지상·미대시·미경직·점프잔량) + 좌클릭 개시 가능 + 사거리 대상 필요.
                        if (canStartAction && player.grounded && player.dashTicks == 0
                            && player.jumpCount < 2 && HasJumpStrikeTarget(in world))
                            buffer[count++] = MacroAction.JumpStrikeAction();
                        break;

                    case MacroActionType.AerialPursuit:
                        if (canStartAction && player.grounded && player.jumpCount == 0
                            && player.dashTicks == 0 && player.combat.lungeCooldown == 0
                            && TryFindAerialPursuitTarget(in world, in services, out int pursuitTarget))
                            buffer[count++] = MacroAction.AerialPursuitTo(pursuitTarget);
                        break;

                    case MacroActionType.Attack:
                        if (canStartAction && HasAttackTarget(in world))
                            buffer[count++] = MacroAction.Simple(MacroActionType.Attack);
                        break;

                    case MacroActionType.DashForward:
                    case MacroActionType.DashBackward:
                    case MacroActionType.DashLeft:
                    case MacroActionType.DashRight:
                        if (player.dashTicks == 0 && player.dashCharges > 0)
                            buffer[count++] = MacroAction.Simple(Priority[p]);
                        break;

                    case MacroActionType.Lunge:
                        if (canStartAction && player.combat.lungeCooldown == 0)
                            count = AddLungeCandidates(in world, in player, in services, buffer, count, cap);
                        break;

                    default:
                        buffer[count++] = MacroAction.Simple(Priority[p]);
                        break;
                }
            }
            return count;
        }

        static bool TryBuildTerrainLeap(in SimWorld world, in SimServices services, out MacroAction action)
        {
            action = default;
            ref readonly PlayerSim player = ref world.player;
            if (player.combat.hp <= 0 || player.jumpCount != 0 || !player.grounded
                || player.dashTicks != 0 || player.combat.hitStunTicks != 0)
                return false;

            Vector3 escape = ComputeEscapeDirection(in world);
            PathStep step = services.Pathfinder.NextStep(player.pos, player.pos + escape * 12f, -1);
            if (step.kind != MoveKind.JumpUp) return false;
            Vector3 delta = step.next - player.pos;
            if (delta.sqrMagnitude <= 1e-6f) return false;
            float yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            action = MacroAction.TerrainLeapAt(yaw);
            return true;
        }

        /// <summary>
        /// 대공 등반 — <b>사거리 밖 공중 적</b>에게 닿는 고도까지 지형을 타고 오른다.
        ///
        /// 왜 필요한가: 공중 액션 3종은 전부 높이차 상한이 있다(JumpStrike 2.6m / AerialPursuit·
        /// Lunge는 <see cref="CombatConfig.LungeHeightTolerance"/>). 실제 맵에서 공중 몹은 발판
        /// 위를 부유해 플레이어보다 10m 넘게 높은 경우가 있는데, 그러면 어떤 후보도 생성되지 않아
        /// 예지가 공중 적을 통째로 무시한다. 기존 <see cref="TryBuildTerrainLeap"/>은 <b>도주
        /// 방향</b>으로만 JumpUp을 찾으므로 이 상황에 쓸 수 없다.
        ///
        /// 방법: 대상 바로 아래의 설 수 있는 면(<see cref="ICollision.SampleGround"/>)을 목표로 잡고
        /// 길찾기에 다음 한 걸음을 묻는다. 그래프가 "여기서 뛰어올라라"(JumpUp)라고 하면 도약을,
        /// 아직 걸어야 하면 그 방향 전진을 낸다. 즉 등반 경로 자체는 우리가 발명하지 않고
        /// <b>실제 몹이 쓰는 것과 같은 층이동 그래프</b>에 물어본다 — 그래야 예측과 실제가 안 어긋난다.
        /// </summary>
        static bool TryBuildAerialAscent(in SimWorld world, in SimServices services, out MacroAction action)
        {
            action = default;
            ref readonly PlayerSim player = ref world.player;
            if (player.combat.hp <= 0 || !player.grounded || player.jumpCount != 0
                || player.dashTicks != 0 || player.combat.hitStunTicks != 0)
                return false;

            if (!TryFindUnreachableAerialTarget(in world, out int targetId, out Vector3 targetPos))
                return false;

            // 대상 바로 아래에서 설 수 있는 면 = 올라가야 할 목표 고도.
            if (!services.Collision.SampleGround(targetPos, AerialAscentGroundProbe, out float standY))
                return false;
            if (standY - player.pos.y < AerialAscentMinGain) return false;   // 올라갈 게 없으면 의미 없음

            var goal = new Vector3(targetPos.x, standY, targetPos.z);
            PathStep step = services.Pathfinder.NextStep(player.pos, goal, -1);
            if (step.kind != MoveKind.JumpUp && step.kind != MoveKind.Walk) return false;

            Vector3 delta = step.next - player.pos;
            delta.y = 0f;
            if (delta.sqrMagnitude <= 1e-6f) return false;
            float yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;

            // JumpUp이면 도약, 아직 걸어가는 중이면 그 방향 전진(불필요한 점프로 공중 제어를 낭비하지 않는다).
            action = step.kind == MoveKind.JumpUp
                ? MacroAction.AerialAscentAt(yaw)
                : MacroAction.MoveTowardAt(yaw);
            return true;
        }

        /// <summary>지면 탐침 거리(m). 공중 적 위치에서 아래로 이만큼 훑어 발판 윗면을 찾는다.</summary>
        const float AerialAscentGroundProbe = 14f;
        /// <summary>이보다 적게 오르는 목표는 등반으로 치지 않는다(기존 액션이 이미 닿는 범위).</summary>
        const float AerialAscentMinGain = 1.5f;
        /// <summary>이 수평 거리 밖의 공중 적은 등반 대상으로 보지 않는다(맵 반대편까지 쫓지 않게).</summary>
        const float AerialAscentMaxFlatDistance = 25f;

        /// <summary>
        /// 지금 어떤 공중 액션으로도 닿지 않는(=높이차가 런지 허용치를 넘는) 공중 적 중 가장 가까운 것.
        /// 닿는 적이 있으면 그쪽이 우선이므로 등반은 만들지 않는다.
        /// </summary>
        static bool TryFindUnreachableAerialTarget(
            in SimWorld world, out int targetId, out Vector3 targetPos)
        {
            targetId = -1;
            targetPos = Vector3.zero;
            float best = float.MaxValue;

            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim enemy = ref world.enemies[i];
                if (!enemy.alive || enemy.combat.gloryStage > 0) continue;
                if (enemy.ai.mobility != MobilityType.Flying) continue;

                float heightGap = enemy.pos.y - world.player.pos.y;
                float flat = CombatMath.FlatDistance(world.player.pos, enemy.pos);
                if (flat > AerialAscentMaxFlatDistance) continue;
                // 이미 닿는 높이면 등반이 아니라 기존 공중 액션이 처리할 일이다.
                if (heightGap <= CombatConfig.LungeHeightTolerance) continue;
                if (flat >= best) continue;

                best = flat;
                targetId = enemy.id;
                targetPos = enemy.pos;
            }
            return targetId >= 0;
        }

        static Vector3 ComputeEscapeDirection(in SimWorld world)
        {
            Vector3 pressure = Vector3.zero;
            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim enemy = ref world.enemies[i];
                if (!enemy.alive) continue;
                Vector3 delta = enemy.pos - world.player.pos;
                delta.y = 0f;
                float distanceSq = delta.sqrMagnitude;
                if (distanceSq <= 1e-6f) continue;
                pressure += delta.normalized / Mathf.Max(1f, distanceSq);
            }

            if (pressure.sqrMagnitude > 1e-6f)
                return -pressure.normalized;
            return -CombatMath.Forward(world.player.yaw);
        }

        /// <summary>
        /// AerialPursuit 대상 탐색.
        ///
        /// ★ 판정 시점 = <b>우클릭이 실제로 나가는 순간</b>(더블점프 정점 부근)이지, 지금 서 있는
        ///   지상이 아니다. 예전엔 지상 자세로 높이차·사거리·LOS를 재서, "지금 그냥 우클릭해도
        ///   닿는 적"만 후보가 됐다 — 매크로의 존재 이유(점프해서 닿기)와 정반대라서, 더블점프
        ///   후엔 분명히 닿는 적인데도 후보가 아예 생성되지 않았다. 특히 지상에서 난간·처마에
        ///   시선이 막히는 위치가 그랬다(맵을 못 올라가는 것처럼 보이지만 실제론 후보 미생성).
        ///
        /// 그래서 <see cref="AerialPursuitRiseGain"/>·<see cref="AerialPursuitAdvanceGain"/>만큼
        /// 옮긴 가상 플레이어를 만들어 <see cref="CanLungeTarget"/>(=실제 Sim 게이트)에 물어본다.
        /// 상한(LungeHeightTolerance 등)을 여기서 다시 쓰지 않는 이유도 같다 — 규칙은 Sim 한 곳뿐.
        /// </summary>
        static bool TryFindAerialPursuitTarget(
            in SimWorld world, in SimServices services, out int targetId)
        {
            targetId = -1;
            float bestDistance = float.MaxValue;
            ref readonly PlayerSim player = ref world.player;

            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim enemy = ref world.enemies[i];
                if (!enemy.alive || enemy.combat.gloryStage > 0 || enemy.ai.mobility != MobilityType.Flying)
                    continue;

                // 지상 평타 사거리 안에 있는 낮은 적은 Attack/Lunge 후보가 처리한다 — 여긴 "위" 전용.
                if (enemy.pos.y - player.pos.y <= CombatConfig.AttackHeightTolerance) continue;

                // 매크로는 대상 쪽을 보고 전진한다(MacroAction.ResolveYaw가 lungeTargetId로 yaw를 푼다).
                // 그러니 전진 이득도 그 방향으로 준다 — 대상마다 방향이 다르므로 루프 안에서 만든다.
                PlayerSim atLunge = player;
                atLunge.pos += Vector3.up * AerialPursuitRiseGain
                             + CombatMath.FlatDirection(player.pos, enemy.pos) * AerialPursuitAdvanceGain;

                if (!CanLungeTarget(in world, in atLunge, in enemy, in services)) continue;

                float flat = CombatMath.FlatDistance(player.pos, enemy.pos);
                if (flat < bestDistance - 1e-5f
                    || (Mathf.Abs(flat - bestDistance) <= 1e-5f && enemy.id < targetId))
                {
                    bestDistance = flat;
                    targetId = enemy.id;
                }
            }
            return targetId >= 0;
        }

        // ── AerialPursuit 우클릭 시점의 자세 이득 ──
        // MacroAction.AerialPursuit의 입력 시퀀스(틱 0 점프 / 틱 7 2단점프 / 틱 11 우클릭)를
        // PlayerMovement의 적분(반암시적 오일러: vel.y += g·dt → pos.y += vel.y·dt)으로 푼 값이다.
        // 매크로 타이밍이나 이동 상수를 바꾸면 여기도 같이 봐야 한다.
        //
        // ※ 지형 충돌(천장·벽)을 무시한 낙관적 추정이다. 후보 생성 단계에선 이게 맞는 방향이다 —
        //   실제 발동 시점엔 PlayerCombat이 진짜 위치로 TryLockDestination을 다시 하므로,
        //   과대평가는 "헛스윙 후보 1개"로 끝나지만 과소평가는 "후보 자체가 사라짐"이 된다.

        /// <summary>틱 0~6 상승(1.118m) + 틱 7 속도 리셋 후 틱 7~10 상승(0.681m).</summary>
        const float AerialPursuitRiseGain = 1.80f;
        /// <summary>전진 11틱(9.17m/s → 1.681m) + 2단점프 수평 임펄스 4틱분(감쇠 포함 0.545m).</summary>
        const float AerialPursuitAdvanceGain = 2.23f;

        static bool HasAttackTarget(in SimWorld world)
        {
            Vector3 forward = CombatMath.Forward(world.player.yaw);
            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim enemy = ref world.enemies[i];
                if (!enemy.alive || enemy.combat.gloryStage > 0) continue;
                if (Mathf.Abs(enemy.pos.y - world.player.pos.y) > CombatConfig.AttackHeightTolerance) continue;
                if (CombatMath.InCone(world.player.pos, forward, enemy.pos,
                        CombatConfig.AttackConeRange + enemy.radius, CombatConfig.AttackConeHalfAngle))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// JumpStrike 대상: 조준/발사로 고도가 고정된(=점프로 따라잡을 수 있는) 공중 슈터가,
        /// 지상 평타 사거리보다 위(높이차 > AttackHeightTolerance)이면서 단일 점프 도달 높이 안에,
        /// 이미 좌클릭 부채꼴(수평) 안에 들어와 있는 경우. 점프는 수직이라 수평 접근은 못 한다 —
        /// 수평 진입은 일반 이동 후보가 담당하고, 이 후보는 "바로 위로 뛰어 치는" 순간만 만든다.
        /// </summary>
        static bool HasJumpStrikeTarget(in SimWorld world)
        {
            ref readonly PlayerSim player = ref world.player;
            Vector3 forward = CombatMath.Forward(player.yaw);
            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim enemy = ref world.enemies[i];
                if (!enemy.alive || enemy.combat.gloryStage > 0) continue;
                if (enemy.ai.mobility != MobilityType.Flying) continue;
                if (enemy.ai.state != EnemyState.Aim && enemy.ai.state != EnemyState.Fire) continue;
                float gap = enemy.pos.y - player.pos.y;
                if (gap <= CombatConfig.AttackHeightTolerance || gap > JumpStrikeReachHeight) continue;
                if (CombatMath.InCone(player.pos, forward, enemy.pos,
                        CombatConfig.AttackConeRange + enemy.radius, CombatConfig.AttackConeHalfAngle))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 계약(PREDICTION_CONTRACT.md 10장) "런지 후보 가까운 유효 대상 최대 2" 반영.
        /// 상위 2명을 각도→거리→id로 뽑은 뒤, 버퍼 삽입 순서는 계약이 명시한 대로
        /// targetId 오름차순으로 정렬한다(선정 우선순위와 삽입 순서는 별개).
        /// </summary>
        static int AddLungeCandidates(
            in SimWorld world, in PlayerSim player, in SimServices services,
            MacroAction[] buffer, int count, int cap)
        {
            FindTopLungeTargets(in world, in player, in services, out int first, out int second);
            if (first >= 0 && second >= 0 && second < first)
            {
                int swap = first; first = second; second = swap;
            }
            if (first >= 0 && count < cap) buffer[count++] = MakeLungeAction(in world, first);
            if (second >= 0 && count < cap) buffer[count++] = MakeLungeAction(in world, second);
            return count;
        }

        /// <summary>
        /// 공중(Flying) 대상이면 복합 콤보(LungeStrike: 우클릭 접근 → 착지 직후 좌클릭)를,
        /// 지상 대상이면 기존 단일 런지를 만든다. 공중 대상은 런지 임팩트 피해 1만으론 못 죽이고
        /// 착지 직후 낙하·bind 해제로 후속 좌클릭 창이 매크로 경계를 넘어가 닫히므로, 한 매크로
        /// 안에서 좌클릭까지 묶어야 실제 처치가 성립한다(원인 분석 B). 지상 대상은 착지 후 다음
        /// 매크로의 Attack 후보로 충분해 굳이 콤보로 묶지 않는다(후보 폭증 방지).
        /// </summary>
        static MacroAction MakeLungeAction(in SimWorld world, int targetId)
        {
            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim enemy = ref world.enemies[i];
                if (enemy.id != targetId) continue;
                return enemy.ai.mobility == MobilityType.Flying
                    ? MacroAction.LungeStrikeTo(targetId)
                    : MacroAction.LungeTo(targetId);
            }
            return MacroAction.LungeTo(targetId);
        }

        static void FindTopLungeTargets(
            in SimWorld world, in PlayerSim player, in SimServices services,
            out int first, out int second)
        {
            first = -1; second = -1;
            float firstDot = 0f, firstDist = 0f;
            float secondDot = 0f, secondDist = 0f;
            Vector3 forward = CombatMath.Forward(player.yaw);

            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim enemy = ref world.enemies[i];
                if (!CanLungeTarget(in world, in player, in enemy, in services)) continue;

                Vector3 direction = CombatMath.FlatDirection(player.pos, enemy.pos);
                float dot = Vector3.Dot(forward, direction);
                float distance = CombatMath.FlatDistance(player.pos, enemy.pos);

                if (IsBetterLungeTarget(dot, distance, enemy.id, first, firstDot, firstDist))
                {
                    second = first; secondDot = firstDot; secondDist = firstDist;
                    first = enemy.id; firstDot = dot; firstDist = distance;
                }
                else if (IsBetterLungeTarget(dot, distance, enemy.id, second, secondDot, secondDist))
                {
                    second = enemy.id; secondDot = dot; secondDist = distance;
                }
            }
        }

        static bool IsBetterLungeTarget(float dot, float distance, int id, int bestId, float bestDot, float bestDist)
        {
            if (bestId < 0) return true;
            if (dot > bestDot + 1e-6f) return true;
            if (Mathf.Abs(dot - bestDot) <= 1e-6f)
            {
                if (distance < bestDist - 1e-5f) return true;
                if (Mathf.Abs(distance - bestDist) <= 1e-5f && id < bestId) return true;
            }
            return false;
        }

        /// <summary>
        /// 런지 유효성 판정을 실제 Sim(PlayerCombat.CanLunge)에 그대로 위임한다 — 예측 전용
        /// 독립 재구현을 없애서 "예측 됨 → 실제 안 됨" 괴리 자체를 구조적으로 제거한다
        /// (docs/shared/AERIAL_LUNGE_SIM_API_PROPOSAL.md, KJH 구현 완료).
        ///
        /// CanLunge는 p.yaw/p.aimPitch(현재 조준) 기준으로 판정하므로, 이 후보를 정확히
        /// 바라보도록 조준만 맞춘 가상 PlayerSim(위치는 그대로)을 만들어 넣는다 — 그래야
        /// 실제 발동 경로(IsLungeable+TryLockDestination)와 완전히 같은 결과가 나온다.
        /// 실제 매크로 실행 시엔 MacroAction이 lungeTargetId를 명시 전달해 PlayerCombat.Step이
        /// FindLungeTarget(자동 조준 탐지)를 건너뛰므로, 사거리·높이차·LOS를 걸러내는 건
        /// 사실상 이 검색 단계가 유일한 관문이다 — 지금 정확히 그 검사를 real Sim 규칙으로
        /// 대체한 것.
        /// </summary>
        static bool CanLungeTarget(in SimWorld world, in PlayerSim player, in EnemySim enemy, in SimServices services)
        {
            if (!enemy.alive || enemy.combat.gloryStage > 0) return false;

            PlayerSim aimed = player;
            Vector3 eye = player.pos + Vector3.up * (SimConfig.PlayerHeight * 0.7f);
            Vector3 center = enemy.pos + Vector3.up * (enemy.height * 0.5f);
            Vector3 dir = center - eye;
            if (dir.sqrMagnitude > 1e-8f)
            {
                // Quaternion.Euler(pitch, yaw, 0)*forward와 정확히 왕복하는 유일한 안전한 방법 —
                // 회전 합성 순서를 직접 손으로 유도하면 부호를 틀리기 쉬워, LookRotation을
                // eulerAngles로 분해해 그대로 되돌린다(엔진이 왕복을 보장).
                Vector3 lookEuler = Quaternion.LookRotation(dir).eulerAngles;
                aimed.yaw = lookEuler.y;
                aimed.aimPitch = lookEuler.x;
            }
            return PlayerCombat.CanLunge(in world, in aimed, in services, enemy.id, out _);
        }
    }
}
