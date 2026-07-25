using NUnit.Framework;
using MindHexer.Shared.Input;

namespace MindHexer.Shared.Tests
{
    /// <summary>안드로이드 잠금패턴식 스와이프 패턴 코어 EditMode 테스트.</summary>
    public sealed class SwipePatternTests
    {
        [Test]
        public void StraightMove_AutoIncludesMidpoint()
        {
            var p = new SwipePattern();
            p.Begin();
            Assert.IsTrue(p.AddCell(0));
            Assert.IsTrue(p.AddCell(2)); // 0→2 사이 1 자동 포함
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, p.Snapshot());

            Assert.IsTrue(p.AddCell(8)); // 2→8 사이 5 자동 포함
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 5, 8 }, p.Snapshot());
        }

        [Test]
        public void DiagonalAdjacent_HasNoMidpoint()
        {
            var p = new SwipePattern();
            p.Begin();
            p.AddCell(0);
            p.AddCell(4); // 0→4 대각 인접: 중간 셀 없음
            CollectionAssert.AreEqual(new[] { 0, 4 }, p.Snapshot());
        }

        [Test]
        public void RevisitingCell_IsIgnored()
        {
            var p = new SwipePattern();
            p.Begin();
            p.AddCell(0);
            p.AddCell(4);
            Assert.IsFalse(p.AddCell(0), "이미 방문한 셀 재추가 불가");
            Assert.IsFalse(p.AddCell(4));
            CollectionAssert.AreEqual(new[] { 0, 4 }, p.Snapshot());
        }

        [Test]
        public void MidpointSkipped_WhenAlreadyVisited()
        {
            var p = new SwipePattern();
            p.Begin();
            p.AddCell(4); // 중앙 먼저 방문
            p.AddCell(0);
            p.AddCell(2); // 0→2 중간은 1 (4 아님) → 정상 포함
            CollectionAssert.AreEqual(new[] { 4, 0, 1, 2 }, p.Snapshot());

            // 6→8 은 중간 7. 8→6 방향으로 가도 7. 4-경유 케이스: 2→6 중간은 4(이미 방문) → 건너뜀.
            Assert.IsTrue(p.AddCell(6)); // last=2,new=6: 2=(0,2),6=(2,0) mid=(1,1)=4 방문됨 → 4 건너뛰고 6만
            CollectionAssert.AreEqual(new[] { 4, 0, 1, 2, 6 }, p.Snapshot());
        }

        [Test]
        public void Matches_ChecksOrder()
        {
            var p = new SwipePattern();
            p.Begin();
            p.AddCell(0); p.AddCell(2); p.AddCell(8); // → 0,1,2,5,8
            Assert.IsTrue(p.Matches(new[] { 0, 1, 2, 5, 8 }));
            Assert.IsFalse(p.Matches(new[] { 0, 1, 2, 8, 5 }));
            Assert.IsFalse(p.Matches(new[] { 0, 1, 2 }));
        }

        [Test]
        public void Begin_Resets()
        {
            var p = new SwipePattern();
            p.Begin(); p.AddCell(3); p.AddCell(5);
            p.Begin();
            Assert.AreEqual(0, p.Count);
            Assert.IsFalse(p.Contains(3));
        }
    }
}
