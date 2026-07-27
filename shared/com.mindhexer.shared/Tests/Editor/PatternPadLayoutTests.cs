using NUnit.Framework;
using MindHexer.Shared.Input;

namespace MindHexer.Shared.Tests
{
    /// <summary>플로팅 2x2 패드 적응 크기(FitSpacing) EditMode 테스트. (가로 화면, 좌상단 앵커)</summary>
    public sealed class PatternPadLayoutTests
    {
        // 가로 예: 2340x1080
        private const float W = 2340f;
        private const float Max = 302f;    // ≈ 0.28*1080
        private const float Margin = 22f;  // ≈ 0.02*1080

        [Test]
        public void TopCenter_StartsAtMax()
        {
            // 상단 프레임 근처 + 가로 여유 → 방이 충분 → 상한(Max)에 도달.
            float s = PatternPadLayout.FitSpacing(pressX: 1170f, pressY: 1060f, screenW: W, maxSpacing: Max, edgeMargin: Margin);
            Assert.AreEqual(Max, s, 1e-3f, "상단·가로중앙 시작 → 최대");
        }

        [Test]
        public void NearBottom_ShrinksToDownRoom()
        {
            float pressY = 120f;
            float s = PatternPadLayout.FitSpacing(1170f, pressY, W, Max, Margin);
            Assert.AreEqual(pressY - Margin, s, 1e-3f, "아래 프레임에 가까우면 아래 여백만큼");
            Assert.Less(s, Max);
        }

        [Test]
        public void NearRight_ShrinksToRightRoom()
        {
            float pressX = 2300f;
            float s = PatternPadLayout.FitSpacing(pressX, 1000f, W, Max, Margin);
            Assert.AreEqual(W - pressX - Margin, s, 1e-3f, "오른쪽 프레임에 가까우면 오른 여백만큼");
        }

        [Test]
        public void NeverExceedsAvailableRoom()
        {
            // 여러 시작점에서 간격이 항상 min(아래여백, 오른여백) 이하(또는 floor)임을 확인 → 화면 밖 금지.
            foreach (var (x, y) in new[] { (1200f, 900f), (1800f, 400f), (2100f, 700f), (1300f, 200f) })
            {
                float s = PatternPadLayout.FitSpacing(x, y, W, Max, Margin);
                float room = System.Math.Min(y - Margin, W - x - Margin);
                Assert.LessOrEqual(s, System.Math.Max(room, 8f) + 1e-3f, $"({x},{y})에서 방을 넘지 않음");
            }
        }

        [Test]
        public void ExtremeCorner_FloorsToTiny()
        {
            float s = PatternPadLayout.FitSpacing(2335f, 10f, W, Max, Margin);
            Assert.AreEqual(8f, s, 1e-3f, "극단 모서리 → floor(8px)");
        }
    }
}
