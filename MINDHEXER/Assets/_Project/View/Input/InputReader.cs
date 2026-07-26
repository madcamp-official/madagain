using UnityEngine;
using UnityEngine.InputSystem;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 키/마우스 → InputCmd. 표현 계층에만 존재.
    /// 순간입력(점프/대시/평타/런지)은 Update에서 버퍼링, FixedUpdate에서 소비.
    /// Yaw(좌우)는 시뮬레이션에, Pitch(상하)는 카메라 전용(sim 미사용).
    /// </summary>
    public class InputReader
    {
        public float Yaw   { get; set; }
        public float Pitch { get; set; }   // 카메라 전용. 런지 종료 시 CombatCamera가 인계(스냅백 방지)

        const float Sens = 0.08f;
        bool jumpBuf, dashBuf, attackBuf, lungeBuf;

        /// <summary>
        /// 시점(Yaw/Pitch)만 동결한다. 버튼 버퍼는 계속 받는다.
        ///
        /// 히트스톱·찌르기 락온처럼 "시선을 내가 소유하지 않는" 구간에서 켠다.
        /// 안 켜면 그동안의 마우스 이동이 <b>Yaw/Pitch에 계속 누적</b>됐다가 풀리는 순간
        /// 한꺼번에 드러나 시점이 뚝 튄다. 동결하면 풀린 그 시점의 시선에서 이어진다.
        /// (mouse.delta 자체는 프레임마다 리셋되므로 읽든 안 읽든 쌓이지 않는다.
        ///  문제는 그 값을 Yaw에 더하는 쪽이었다)
        /// 버튼까지 막으면 그 사이 누른 공격·점프가 씹히므로 버퍼는 살려 둔다.
        /// </summary>
        public bool FreezeLook { get; set; }

        public void PollFrame()
        {
            var kb = Keyboard.current; var mouse = Mouse.current;
            if (kb == null) return;
            if (mouse != null)
            {
                Vector2 d = mouse.delta.ReadValue();
                if (!FreezeLook)   // 동결 중엔 시선에 반영하지 않는다(버튼은 아래에서 계속 받음)
                {
                    Yaw += d.x * Sens;
                    Pitch = Mathf.Clamp(Pitch - d.y * Sens, -85f, 85f);
                }
                if (mouse.leftButton.wasPressedThisFrame)  attackBuf = true;   // 평타
                if (mouse.rightButton.wasPressedThisFrame) lungeBuf  = true;   // 타깃 런지
            }
            if (kb.spaceKey.wasPressedThisFrame)     jumpBuf = true;
            if (kb.leftShiftKey.wasPressedThisFrame) dashBuf = true;           // Shift+WASD 4방향 대시
        }

        public InputCmd Consume()
        {
            InputCmd cmd = InputCmd.Empty;
            cmd.yaw = Yaw;
            cmd.pitch = Pitch;   // 런지 조준 레이(상하)
            var kb = Keyboard.current;
            if (kb != null)
            {
                Vector2 m = Vector2.zero;
                if (kb.aKey.isPressed) m.x -= 1f;
                if (kb.dKey.isPressed) m.x += 1f;
                if (kb.wKey.isPressed) m.y += 1f;
                if (kb.sKey.isPressed) m.y -= 1f;
                cmd.move = m;
            }
            cmd.jump = jumpBuf;
            cmd.dash = dashBuf;
            cmd.dashDirection = ResolveDashDirection(cmd.move);   // 방향키 없으면 전방
            cmd.attack = attackBuf;
            cmd.lunge = lungeBuf;
            cmd.lungeTargetId = -1;   // 직접 조작은 자동 타깃(예측 재생만 확정 id 주입)
            jumpBuf = dashBuf = attackBuf = lungeBuf = false;
            return cmd;
        }

        static DashDirection ResolveDashDirection(Vector2 move)
        {
            if (move.sqrMagnitude < 1e-4f) return DashDirection.Forward;   // 무방향 = 전방
            if (Mathf.Abs(move.x) > Mathf.Abs(move.y))
                return move.x < 0f ? DashDirection.Left : DashDirection.Right;
            return move.y < 0f ? DashDirection.Backward : DashDirection.Forward;
        }

        public bool EscapePressed()
        {
            var kb = Keyboard.current;
            return kb != null && kb.escapeKey.wasPressedThisFrame;
        }
    }
}
