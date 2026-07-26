using UnityEngine;
using Game.Sim;

namespace Game.Prediction
{
    public enum MacroActionType : byte
    {
        Wait,
        MoveForward,
        MoveLeft,
        MoveRight,
        Retreat,
        Jump,
        DashForward,
        DashBackward,
        DashLeft,
        DashRight,
        Attack,
        Lunge,
        LungeStrike,   // 공중 마무리 콤보: 우클릭 접근 → (착지 직후) 좌클릭. 한 매크로 안에서 서브틱 시퀀싱.
        JumpStrike,    // 대공 콤보(런지 없이): 점프로 솟구쳐 얼어붙은(조준/발사 중) 공중 슈터 고도에서 좌클릭.
        AerialPursuit, // 공중 추격: 대상 방향 전진 → 점프 → 더블 점프 → 우클릭 접근.
        TerrainLeap,   // 안전형 지형 도약: 그래프의 JumpUp 방향으로 점프 → 더블 점프.
        AerialAscent,  // 대공 등반: 사거리 밖 공중 적 아래의 발판을 향해 그래프 JumpUp을 타고 고도를 얻는다.
    }

    /// <summary>
    /// 탐색 후보 1개 = "macroTicks 동안 유지하는 InputCmd 패턴" 하나.
    /// 대시·공격·런지처럼 한 번만 트리거하면 되는 행동은 매크로의 첫 틱에만 펄스를 준다
    /// (이후는 Sim이 자체 상태머신으로 지속시킨다 — PlayerMovement/PlayerCombat 참고).
    /// </summary>
    public struct MacroAction
    {
        public MacroActionType type;

        /// <summary>Lunge일 때만 유효. 매크로 생성 시점에 확정해 Travel 중 재계산하지 않는다.</summary>
        public int lungeTargetId;
        public float targetYaw;

        public static MacroAction Simple(MacroActionType type) => new MacroAction { type = type, lungeTargetId = -1, targetYaw = float.NaN };

        public static MacroAction LungeTo(int targetId) => new MacroAction { type = MacroActionType.Lunge, lungeTargetId = targetId, targetYaw = float.NaN };

        public static MacroAction LungeStrikeTo(int targetId) => new MacroAction { type = MacroActionType.LungeStrike, lungeTargetId = targetId, targetYaw = float.NaN };

        public static MacroAction AerialPursuitTo(int targetId) =>
            new MacroAction { type = MacroActionType.AerialPursuit, lungeTargetId = targetId, targetYaw = float.NaN };

        public static MacroAction TerrainLeapAt(float yaw) =>
            new MacroAction { type = MacroActionType.TerrainLeap, lungeTargetId = -1, targetYaw = yaw };

        /// <summary>대공 등반 도약 — 지정 방향으로 전진하며 1단·2단 점프로 발판에 오른다.</summary>
        public static MacroAction AerialAscentAt(float yaw) =>
            new MacroAction { type = MacroActionType.AerialAscent, lungeTargetId = -1, targetYaw = yaw };

        /// <summary>
        /// 지정 방향으로 걸어가는 전진. 기존 <see cref="MacroActionType.MoveForward"/>는 targetYaw가
        /// NaN이라 <see cref="ResolveYaw"/>가 "지금 보는 방향"으로 폴백하는데, 등반 경로처럼 그래프가
        /// 정해준 방향으로 가야 할 때는 그 방향을 명시해야 한다(같은 타입, yaw만 지정).
        /// </summary>
        public static MacroAction MoveTowardAt(float yaw) =>
            new MacroAction { type = MacroActionType.MoveForward, lungeTargetId = -1, targetYaw = yaw };

        /// <summary>
        /// LungeStrike 콤보에서 좌클릭을 넣는 서브틱. 우클릭 블링크(LungeTravelTicks)가 끝나고
        /// LgRecovery까지 해제된 다음 틱이어야 좌클릭이 실제로 개시된다(그 전엔 StepLunge가 틱을
        /// 소비해 무시됨). +2 여유 = Travel + Recovery 종료 직후. 실전 규칙(CombatConfig)에서 파생해
        /// 런지 튜닝이 바뀌어도 따라가게 한다.
        /// </summary>
        // ★ LungeTravelTicks(예전 3틱 상수)가 아니라 실제로 쓰이는 LungeTravel을 봐야 한다.
        //   둠식(8틱)으로 바꾼 뒤에도 3틱을 가정하면 좌클릭 서브틱이 5틱 빨라져,
        //   예지가 "우클릭 후 바로 좌클릭"을 실제보다 이르게 예측한다.
        public static int LungeStrikeAttackTick =>
            CombatConfig.LungeTravel + CombatConfig.LungeRecoveryTicks + 2;

        public static MacroAction JumpStrikeAction() => new MacroAction { type = MacroActionType.JumpStrike, lungeTargetId = -1, targetYaw = float.NaN };

        public float ResolveYaw(in SimWorld world, float fallbackYaw)
        {
            if (!float.IsNaN(targetYaw)) return targetYaw;
            if (lungeTargetId < 0) return fallbackYaw;
            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim enemy = ref world.enemies[i];
                if (!enemy.alive || enemy.id != lungeTargetId) continue;
                Vector3 delta = enemy.pos - world.player.pos;
                return Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            }
            return fallbackYaw;
        }

        /// <summary>
        /// JumpStrike에서 좌클릭 서브틱. 점프는 t0, 좌클릭 판정(윈드업 후 Active)이 매크로 안에서
        /// 가장 높은 지점(단일 점프는 매크로 끝에 apex 근처)에 떨어지도록 역산한다:
        /// (매크로 마지막 틱) − 윈드업 = 판정이 마지막 틱에 걸리는 개시 틱. 얼어붙은 공중 슈터의
        /// 고도(플레이어+FlyHoverOffset)에 가장 가깝게 붙는다.
        /// </summary>
        public static int JumpStrikeAttackTick =>
            Mathf.Max(1, (PredictionSettings.MacroTicksPerStep - 1) - CombatConfig.AttackWindupTicks);

        public InputCmd ToInputCmd(float yaw, int tickWithinMacro)
        {
            var cmd = new InputCmd { yaw = yaw };
            bool first = tickWithinMacro == 0;

            switch (type)
            {
                case MacroActionType.Wait:
                    break;
                case MacroActionType.MoveForward:
                    cmd.move = new Vector2(0f, 1f);
                    break;
                case MacroActionType.MoveLeft:
                    cmd.move = new Vector2(-1f, 0f);
                    break;
                case MacroActionType.MoveRight:
                    cmd.move = new Vector2(1f, 0f);
                    break;
                case MacroActionType.Retreat:
                    cmd.move = new Vector2(0f, -1f);
                    break;
                case MacroActionType.Jump:
                    if (first) cmd.jump = true;
                    break;
                case MacroActionType.DashForward:
                    if (first) { cmd.dash = true; cmd.dashDirection = DashDirection.Forward; }
                    break;
                case MacroActionType.DashBackward:
                    if (first) { cmd.dash = true; cmd.dashDirection = DashDirection.Backward; }
                    break;
                case MacroActionType.DashLeft:
                    if (first) { cmd.dash = true; cmd.dashDirection = DashDirection.Left; }
                    break;
                case MacroActionType.DashRight:
                    if (first) { cmd.dash = true; cmd.dashDirection = DashDirection.Right; }
                    break;
                case MacroActionType.Attack:
                    if (first) cmd.attack = true;
                    break;
                case MacroActionType.Lunge:
                    if (first) { cmd.lunge = true; cmd.lungeTargetId = lungeTargetId; }
                    break;
                case MacroActionType.LungeStrike:
                    // 우클릭으로 대상 고도(enemy.y+LungeAimUp)에 붙은 뒤, 착지 직후 좌클릭으로 마무리.
                    // 공중 적을 좌클릭 사거리·높이차(1m) 안에서 실제로 처치하는 유일한 원자적 콤보.
                    if (first) { cmd.lunge = true; cmd.lungeTargetId = lungeTargetId; }
                    else if (tickWithinMacro == LungeStrikeAttackTick) cmd.attack = true;
                    break;
                case MacroActionType.JumpStrike:
                    // 점프로 솟구쳐 얼어붙은 공중 슈터 고도까지 올라간 뒤, 매크로 정점 근처에서 좌클릭.
                    if (first) cmd.jump = true;
                    else if (tickWithinMacro == JumpStrikeAttackTick) cmd.attack = true;
                    break;
                case MacroActionType.AerialPursuit:
                    cmd.move = new Vector2(0f, 1f);
                    if (tickWithinMacro == 0 || tickWithinMacro == 7) cmd.jump = true;
                    if (tickWithinMacro == 11)
                    {
                        cmd.lunge = true;
                        cmd.lungeTargetId = lungeTargetId;
                    }
                    break;
                case MacroActionType.TerrainLeap:
                    cmd.move = new Vector2(0f, 1f);
                    if (tickWithinMacro == 0 || tickWithinMacro == 7) cmd.jump = true;
                    break;
                case MacroActionType.AerialAscent:
                    // TerrainLeap과 같은 입력 패턴(전진 + 1단/2단 점프)이지만 목적이 다르다 —
                    // 도주가 아니라 "사거리 밖 공중 적에게 닿는 고도까지 오르기"다. 타입을 나눠야
                    // 점수·디버그에서 둘을 구분할 수 있고, 이후 등반 전용으로 따로 튜닝할 수 있다.
                    cmd.move = new Vector2(0f, 1f);
                    if (tickWithinMacro == 0 || tickWithinMacro == 7) cmd.jump = true;
                    break;
            }
            return cmd;
        }
    }
}
