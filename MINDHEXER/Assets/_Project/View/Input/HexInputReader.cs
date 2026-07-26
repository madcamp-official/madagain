using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.View
{
    /// <summary>
    /// PC 입력 → HexInput 스냅샷. 표현 계층 전용. 컨텍스트별로 다른 물리 입력을 읽는다.
    /// 기존 InputReader(이동/시점)와 병행 — 이 리더는 해킹/조종/빙의 채널만 담당한다.
    /// VR 이식 시 이 클래스만 UDP 수신기로 교체하면 된다. (기초_설계안 §2.5)
    ///
    /// 주의(임시방편): Space 탭/홀드, 더블클릭 플릭을 여기서 손코딩으로 판정한다.
    /// 추후 HexControls.inputactions(Tap/Hold/MultiTap interaction)로 대체하면 더 견고하다.
    /// </summary>
    public class HexInputReader
    {
        public ControlContext Context = ControlContext.Player;

        // 튜닝값 (§2.5 실측 대상)
        public float HoldThreshold     = 0.15f;   // Space 홀드 = 해킹 판정 임계
        public float DoubleClickWindow = 0.25f;   // 더블클릭 = 플릭 판정 창
        public float ScrollFlickSpeed  = 120f;    // 빠른 스크롤 = 깊이 플릭 임계

        float spaceDownTime = -1f;
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

            // Space: 탭=점프 / 홀드=해킹 (탭 판정은 ConsumeJumpTap 참조)
            if (kb.spaceKey.wasPressedThisFrame) spaceDownTime = Time.unscaledTime;
            bool spaceHeldLongEnough = kb.spaceKey.isPressed && spaceDownTime >= 0f
                && Time.unscaledTime - spaceDownTime >= HoldThreshold;
            cmd.hackHeld = spaceHeldLongEnough;

            // Q 복귀
            cmd.returnToBody = kb.qKey.wasPressedThisFrame;

            if (mouse == null) return cmd;

            switch (Context)
            {
                case ControlContext.Hacking:
                    cmd.strokeDir = mouse.delta.ReadValue();
                    break;

                case ControlContext.ExternalControl:
                {
                    bool l = mouse.leftButton.isPressed, r = mouse.rightButton.isPressed;
                    if (shift) { if (l) cmd.axisV += 1f; if (r) cmd.axisV -= 1f; }
                    else       { if (l) cmd.axisH -= 1f; if (r) cmd.axisH += 1f; }
                    cmd.axisDepth = mouse.scroll.ReadValue().y;
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

            float sy = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(sy) > ScrollFlickSpeed) return sy > 0f ? FlickDir.DepthFar : FlickDir.DepthNear;

            return FlickDir.None;
        }

        /// <summary>Space를 홀드 임계보다 짧게 눌렀다 뗐으면 점프(탭)로 소비. 이동 커맨드에 반영할 때 호출.</summary>
        public bool ConsumeJumpTap()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb.spaceKey.wasReleasedThisFrame) return false;

            bool wasTap = spaceDownTime >= 0f && Time.unscaledTime - spaceDownTime < HoldThreshold;
            spaceDownTime = -1f;
            return wasTap;
        }
    }
}
