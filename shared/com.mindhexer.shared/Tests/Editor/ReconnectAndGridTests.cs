using NUnit.Framework;
using UnityEngine;
using MindHexer.Shared.Net;
using MindHexer.Shared.Protocol;

namespace MindHexer.Shared.Tests
{
    /// <summary>재연결 백오프 + 3x3 그리드 매핑 EditMode 테스트.</summary>
    public sealed class ReconnectAndGridTests
    {
        [Test]
        public void ReconnectPolicy_ExponentialThenCapped()
        {
            var p = new ReconnectPolicy(500, 8000, 2);
            Assert.AreEqual(500, p.NextDelayMs());
            Assert.AreEqual(1000, p.NextDelayMs());
            Assert.AreEqual(2000, p.NextDelayMs());
            Assert.AreEqual(4000, p.NextDelayMs());
            Assert.AreEqual(8000, p.NextDelayMs());
            Assert.AreEqual(8000, p.NextDelayMs(), "상한 유지");
            Assert.AreEqual(6, p.Attempt);
            p.Reset();
            Assert.AreEqual(0, p.Attempt);
            Assert.AreEqual(500, p.NextDelayMs(), "Reset 후 처음부터");
        }

        [Test]
        public void HackGridMath_MapsAndClamps()
        {
            Assert.AreEqual(0, HackGridMath.ToCellIndex(new Vector2(0.1f, 0.1f)), "좌하단");
            Assert.AreEqual(1, HackGridMath.ToCellIndex(new Vector2(0.5f, 0.1f)), "중하단");
            Assert.AreEqual(4, HackGridMath.ToCellIndex(new Vector2(0.5f, 0.5f)), "중앙");
            Assert.AreEqual(8, HackGridMath.ToCellIndex(new Vector2(0.9f, 0.9f)), "우상단");
            Assert.AreEqual(8, HackGridMath.ToCellIndex(new Vector2(1.0f, 1.0f)), "(1,1) 클램프");
            Assert.AreEqual(0, HackGridMath.ToCellIndex(new Vector2(-0.5f, -0.5f)), "음수 클램프");
        }

        [Test]
        public void HackGridMath_CellCenterRoundTrips()
        {
            for (int i = 0; i < HackGridMath.CellCount; i++)
                Assert.AreEqual(i, HackGridMath.ToCellIndex(HackGridMath.CellCenter(i)), $"cell {i}");
        }

        [Test]
        public void HackGridMath_PadMapsInsideAndRejectsOutside()
        {
            float cx = HackGridMath.PadX + HackGridMath.PadW * 0.5f; // 패드 중앙
            float cy = HackGridMath.PadY + HackGridMath.PadH * 0.5f;
            Assert.IsTrue(HackGridMath.TryToCellIndex(cx, cy, out int mid));
            Assert.AreEqual(4, mid, "패드 중앙 → 셀 4");

            // 패드 좌하단/우상단 모서리
            Assert.IsTrue(HackGridMath.TryToCellIndex(HackGridMath.PadX + 0.001f, HackGridMath.PadY + 0.001f, out int bl));
            Assert.AreEqual(0, bl, "좌하단 → 0");
            Assert.IsTrue(HackGridMath.TryToCellIndex(HackGridMath.PadX + HackGridMath.PadW - 0.001f, HackGridMath.PadY + HackGridMath.PadH - 0.001f, out int tr));
            Assert.AreEqual(8, tr, "우상단 → 8");

            // 패드 밖(왼쪽 조이스틱 영역 / 상단)은 거부
            Assert.IsFalse(HackGridMath.TryToCellIndex(0.1f, 0.1f, out _), "좌하단 밖");
            Assert.IsFalse(HackGridMath.TryToCellIndex(0.75f, 0.9f, out _), "상단 밖");
        }
    }
}
