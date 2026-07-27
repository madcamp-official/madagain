using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.View
{
    /// <summary>
    /// PC 입력 → HexInput 스냅샷. 표현 계층 전용. 컨텍스트별로 다른 물리 입력을 읽는다.
    /// 기존 InputReader(이동/시점)와 병행 — 이 리더는 해킹/조종/빙의 채널만 담당한다.
    /// VR 이식 시 이 클래스만 UDP 수신기로 교체하면 된다. (기초_설계안 §2.5)
    ///
    /// 해킹 키 = <b>Space</b>. 홀드=해킹 / 단발 탭=조종 해제. 점프는 자동이라 Space와 안 겹친다.
    /// 탭/홀드 판정과 조준 연속성은 HackDriver가 소유한다 — 여기선 raw 상태·엣지만 낸다.
    ///
    /// 주의(임시방편): 더블클릭 플릭을 여기서 손코딩으로 판정한다.
    /// 추후 HexControls.inputactions(Tap/Hold/MultiTap interaction)로 대체하면 더 견고하다.
    /// </summary>
    public class HexInputReader
    {
        public ControlContext Context = ControlContext.Player;

        // 튜닝값 (§2.5 실측 대상)
        public float DoubleClickWindow = 0.25f;   // 더블클릭 = 플릭 판정 창

        float lastLeftClick = -1f, lastRightClick = -1f;

        /// <summary>이번 프레임 HexInput을 만든다.</summary>
        public HexInput Poll()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;

            HexInput cmd = HexInput.Empty;
            cmd.context = Context;
            if (kb == null) return cmd;

            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;

            // 해킹 = Space. 홀드/탭 구분은 HackDriver가 한다.
            cmd.hackHeld = kb.spaceKey.isPressed;
            cmd.hackPressed = kb.spaceKey.wasPressedThisFrame;
            cmd.hackReleased = kb.spaceKey.wasReleasedThisFrame;

            // Q 복귀(빙의)
            cmd.returnToBody = kb.qKey.wasPressedThisFrame;

            if (mouse == null) return cmd;

            switch (Context)
            {
                case ControlContext.Hacking:
                    cmd.strokeDir = mouse.delta.ReadValue();
                    break;

                case ControlContext.Player:
                {
                    // 외부 조종(§6.5 1회 장악) — 장악한 대상을 바라보는 동안 HackDriver가 이 값을 쓴다.
                    bool l = mouse.leftButton.isPressed, r = mouse.rightButton.isPressed;
                    if (shift) { if (l) cmd.axisV += 1f; if (r) cmd.axisV -= 1f; }
                    else       { if (l) cmd.axisH -= 1f; if (r) cmd.axisH += 1f; }
                    cmd.flick = DetectFlick(mouse, shift);
                    break;
                }

                case ControlContext.ViewEntry:
                    cmd.primary = mouse.leftButton.wasPressedThisFrame;
                    cmd.primaryHeld = mouse.leftButton.isPressed;
                    break;
            }
            return cmd;
        }

        FlickDir DetectFlick(Mouse mouse, bool shift)
        {
            float t = Time.unscaledTime;

            if (mouse.leftButton.wasPressedThisFrame)
            {
                bool dbl = t - lastLeftClick <= DoubleClickWindow;
                lastLeftClick = t;
                if (dbl) return shift ? FlickDir.Up : FlickDir.Left;
            }
            if (mouse.rightButton.wasPressedThisFrame)
            {
                bool dbl = t - lastRightClick <= DoubleClickWindow;
                lastRightClick = t;
                if (dbl) return shift ? FlickDir.Down : FlickDir.Right;
            }

            return FlickDir.None;
        }
    }
}
