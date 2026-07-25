using NUnit.Framework;
using MindHexer.Shared.Protocol;
using MindHexer.Shared.Events;

namespace MindHexer.Shared.Tests
{
    /// <summary>이벤트 코덱(의존성 없는 플랫 JSON) EditMode 테스트.</summary>
    public sealed class EventCodecTests
    {
        [Test]
        public void PatternResult_RoundTrip()
        {
            var m = EventMessage.PatternResult(true, 7);
            Assert.IsTrue(EventMessage.TryDecode(m.Encode(), out var d));
            Assert.AreEqual(EventType.PatternResult, d.Type);
            Assert.IsTrue(d.GetBool(EventMessage.KeySuccess));
            Assert.AreEqual(7, d.GetInt(EventMessage.KeyPatternId));
        }

        [Test]
        public void PairRequest_RoundTrip()
        {
            var m = EventMessage.PairRequest(NetworkConstants.ProtocolVersion, "S10e");
            Assert.IsTrue(EventMessage.TryDecode(m.Encode(), out var d));
            Assert.AreEqual(NetworkConstants.ProtocolVersion, d.GetByte(EventMessage.KeyProtocolVersion));
            Assert.AreEqual("S10e", d.GetString(EventMessage.KeyDeviceName));
        }

        [Test]
        public void BatteryWarning_PreservesFloat()
        {
            var m = EventMessage.BatteryWarning(0.42f);
            Assert.IsTrue(EventMessage.TryDecode(m.Encode(), out var d));
            Assert.AreEqual(0.42f, d.GetFloat(EventMessage.KeyLevel), 1e-6f);
        }

        [Test]
        public void Escaping_RoundTrips()
        {
            var m = EventMessage.PairRequest(2, "he said \"hi\"\n\\path");
            Assert.IsTrue(EventMessage.TryDecode(m.Encode(), out var d));
            Assert.AreEqual("he said \"hi\"\n\\path", d.GetString(EventMessage.KeyDeviceName));
        }

        [Test]
        public void PatternSubmit_RoundTripsNodeSequence()
        {
            var m = EventMessage.PatternSubmit(new[] { 0, 1, 3, 2 });
            Assert.IsTrue(EventMessage.TryDecode(m.Encode(), out var d));
            Assert.AreEqual(EventType.PatternSubmit, d.Type);
            CollectionAssert.AreEqual(new[] { 0, 1, 3, 2 }, d.GetIntArray(EventMessage.KeyNodes));
        }

        [Test]
        public void RejectsMalformedAndTypeless()
        {
            Assert.IsFalse(EventMessage.TryDecode("{not json", out _));
            Assert.IsFalse(EventMessage.TryDecode("{\"foo\":\"bar\"}", out _));
            Assert.IsFalse(EventMessage.TryDecode("", out _));
        }
    }
}
