using NUnit.Framework;
using MindHexer.Shared.Input;

namespace MindHexer.Shared.Tests
{
    /// <summary>플로팅 2x2 패드 배치(Fit): 최소 크기 보장 + 화면 이탈 방지 EditMode 테스트.</summary>
    public sealed class PatternPadFitTests
    {
        // 가로 예: 2340x1080
        private const float W = 2340f, H = 1080f;
        private const float Max = 405f;    // 0.375*1080
        private const float Min = 140.4f;  // 0.13*1080
        private const float Margin = 21.6f;
        private const float Eps = 1e-2f;

        [Test]
        public void AmpleRoom_UsesMax_AnchorUnchanged()
        {
            var f = PatternPadLayout.Fit(1170f, 1060f, W, H, Max, Min, Margin);
            Assert.AreEqual(Max, f.Spacing, Eps, "여유 충분 → 최대");
            Assert.AreEqual(1170f, f.AnchorX, Eps, "앵커=시작점");
            Assert.AreEqual(1060f, f.AnchorY, Eps);
        }

        [Test]
        public void ModerateRoom_ShrinksButKeepsAnchor()
        {
            // 아래로 방이 min보다는 크지만 max보다 작음 → 방에 맞춰 축소, 앵커는 유지.
            float pressY = 300f;
            var f = PatternPadLayout.Fit(1170f, pressY, W, H, Max, Min, Margin);
            Assert.AreEqual(pressY - Margin, f.Spacing, Eps, "아래 여백만큼 축소");
            Assert.Greater(f.Spacing, Min);
            Assert.AreEqual(1170f, f.AnchorX, Eps);
            Assert.AreEqual(pressY, f.AnchorY, Eps);
        }

        [Test]
        public void TightCorner_ClampsToMin_AndStaysOnScreen()
        {
            // 우하단 극단 시작: 방 < min → 최소 크기로 고정하고 앵커를 안쪽으로 보정.
            var f = PatternPadLayout.Fit(2320f, 60f, W, H, Max, Min, Margin);
            Assert.AreEqual(Min, f.Spacing, Eps, "최소 크기 보장");

            // 네 노드가 모두 [margin, screen-margin] 안 → 화면 밖으로 안 나감.
            float x0 = f.AnchorX, y0 = f.AnchorY;
            float x1 = x0 + f.Spacing;  // 오른쪽 끝
            float y2 = y0 - f.Spacing;  // 아래 끝
            Assert.GreaterOrEqual(x0, Margin - Eps, "좌측 프레임 안");
            Assert.LessOrEqual(x1, W - Margin + Eps, "우측 프레임 안");
            Assert.GreaterOrEqual(y2, Margin - Eps, "아래 프레임 안");
            Assert.LessOrEqual(y0, H - Margin + Eps, "위 프레임 안");
        }

        [Test]
        public void MinGreaterThanMax_IsClampedToMax()
        {
            var f = PatternPadLayout.Fit(1170f, 1000f, W, H, maxSpacing: 100f, minSpacing: 200f, edgeMargin: 20f);
            Assert.AreEqual(100f, f.Spacing, Eps, "min은 max로 클램프되어 그 이상 커지지 않음");
        }
    }
}
