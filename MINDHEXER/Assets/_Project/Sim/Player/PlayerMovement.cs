using UnityEngine;

namespace Game.Sim
{
    /// <summary>
    /// 플레이어 이동: WASD·더블점프(버퍼+공중 임펄스)·4방향 대시(둠식 임펄스).
    /// 벽은 CharacterMotor가 처리.
    /// </summary>
    public static class PlayerMovement
    {
        public static void Step(ref PlayerSim p, in InputCmd cmd, in SimServices svc, float dt)
        {
            p.yaw = cmd.yaw;
            p.aimPitch = cmd.pitch;   // 런지 조준 레이용(이동 상태 무관하게 매 틱 갱신)
            Vector3 fwd = Forward(p.yaw);
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);

            // 대시 충전 회복 (스택별 순차)
            if (p.dashRecharge > 0)
            {
                p.dashRecharge--;
                if (p.dashRecharge == 0 && p.dashCharges < SimConfig.DashMaxCharges)
                {
                    p.dashCharges++;
                    if (p.dashCharges < SimConfig.DashMaxCharges)
                        p.dashRecharge = SimConfig.DashRechargeTicks;
                }
            }

            // 점프 버퍼: 누른 순간 기록 → 착지/가능 시점에 소비 (선입력 손맛)
            if (p.jumpBufferTicks > 0) p.jumpBufferTicks--;
            if (cmd.jump) p.jumpBufferTicks = SimConfig.JumpBufferTicks;

            // 피격 경직: 수평 조작 제한(중력·착지만)
            if (p.combat.hitStunTicks > 0)
            {
                CharacterMotor.ResolveVertical(svc.Collision, ref p.pos, ref p.vel, dt, out p.grounded,
                                               SimConfig.PlayerRadius, SimConfig.PlayerHeight);
                if (p.grounded) p.jumpCount = 0;
                return;
            }

            // 런지 Travel 중엔 PlayerCombat이 pos를 구동 — 여기선 손대지 않음
            if (p.combat.lungePhase == CombatConfig.LgTravel) return;

            // 대시 예약 버퍼 감쇠
            if (p.dashBufferTicks > 0) p.dashBufferTicks--;
            // 대시 막판(남은 지속 ≤ 예약구간)에 입력하면 예약 — 현재 대시 끝날 때까지 유지
            if (p.dashTicks > 0 && cmd.dash && p.dashTicks <= SimConfig.DashReserveWindow)
                p.dashBufferTicks = SimConfig.DashReserveWindow + 2;

            // 4방향 대시 시작: 새 입력 또는 예약(막판 입력). 방향은 발동 순간 WASD. 스택 없으면 무시.
            if (p.dashTicks == 0 && (cmd.dash || p.dashBufferTicks > 0) && p.dashCharges > 0)
            {
                p.dashTicks = SimConfig.DashDurationTicks;
                p.dashDir = DashVector(cmd.dashDirection, fwd, right);
                p.dashSpeed = SimConfig.DashInitialSpeed;   // 임펄스: 초기 속도 부여(첫 틱이 가장 강함)
                p.dashCharges--;
                p.dashBufferTicks = 0;   // 예약 소비
                if (p.dashRecharge == 0) p.dashRecharge = SimConfig.DashRechargeTicks;
            }

            Vector3 horiz;
            if (p.dashTicks > 0)
            {
                // 진짜 임펄스: 현재 속도로 이동 → 드래그로 감쇠. 총 거리는 힘·드래그에서 자동.
                horiz = p.dashDir * (p.dashSpeed * dt);
                p.dashSpeed *= SimConfig.DashDecay;
                p.dashTicks--;
            }
            else
            {
                Vector3 wish = right * cmd.move.x + fwd * cmd.move.y;
                if (wish.sqrMagnitude > 1f) wish.Normalize();
                horiz = wish * SimConfig.PlayerMoveSpeed * dt;

                // 점프 (버퍼 소비). 2단 점프는 입력 방향 수평 임펄스 동반(둠식 공중 방향전환)
                if (p.jumpBufferTicks > 0 && p.jumpCount < 2)
                {
                    bool airJump = !p.grounded && p.jumpCount >= 1;
                    p.vel.y = SimConfig.PlayerJumpSpeed;
                    p.jumpCount++;
                    p.grounded = false;
                    p.jumpBufferTicks = 0;
                    if (airJump && wish.sqrMagnitude > 1e-4f)
                    {
                        p.jumpBoostTicks = SimConfig.AirJumpBoostTicks;
                        p.jumpBoostDir = wish.normalized;
                    }
                }
            }

            // 2단 점프 수평 임펄스(선형 감쇠) — 일반 이동에 덧셈
            if (p.jumpBoostTicks > 0)
            {
                float k = (float)p.jumpBoostTicks / SimConfig.AirJumpBoostTicks;
                horiz += p.jumpBoostDir * (SimConfig.AirJumpBoost * k * dt);
                p.jumpBoostTicks--;
            }

            // 수평(벽 슬라이드) → 수직(중력·착지)
            p.pos = CharacterMotor.MoveHorizontal(svc.Collision, p.pos, horiz,
                                                  SimConfig.PlayerRadius, SimConfig.PlayerHeight);
            CharacterMotor.ResolveVertical(svc.Collision, ref p.pos, ref p.vel, dt, out bool grounded,
                                           SimConfig.PlayerRadius, SimConfig.PlayerHeight);
            p.grounded = grounded;
            if (grounded) { p.jumpCount = 0; p.lastGroundedPos = p.pos; }   // 적 추적 목표 갱신
        }

        static Vector3 DashVector(DashDirection d, Vector3 fwd, Vector3 right)
        {
            switch (d)
            {
                case DashDirection.Backward: return -fwd;
                case DashDirection.Left:     return -right;
                case DashDirection.Right:    return right;
                default:                     return fwd;
            }
        }

        static Vector3 Forward(float yaw)
            => new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad));
    }
}
