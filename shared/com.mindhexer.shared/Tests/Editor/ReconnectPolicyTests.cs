using NUnit.Framework;
using MindHexer.Shared.Net;

namespace MindHexer.Shared.Tests
{
    /// <summary>재연결 지수 백오프 EditMode 테스트.</summary>
    public sealed class ReconnectPolicyTests
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
    }
}
