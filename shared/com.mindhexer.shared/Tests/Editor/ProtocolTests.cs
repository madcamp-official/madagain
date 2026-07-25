using System.Text;
using NUnit.Framework;
using UnityEngine;
using MindHexer.Shared.Protocol;

namespace MindHexer.Shared.Tests
{
    /// <summary>
    /// 결정론적 프로토콜 로직 EditMode 테스트. (scratchpad 검증 하니스의 정식 이식)
    /// 직렬화/시퀀스/비콘/RTT패킷은 스레드·소켓 없이 검증 가능하므로 EditMode.
    /// 실제 UDP 송수신은 PlayMode(UdpLoopbackTests) 참고.
    /// </summary>
    public sealed class ProtocolTests
    {
        private static InputPacket Sample(uint seq) => new InputPacket
        {
            Sequence = seq,
            TimestampMs = 1234567890123L,
            TouchId = 7,
            Phase = TouchPhaseCode.Move,
            NormalizedPos = new Vector2(0.25f, 0.75f),
            Position = new Vector3(0.11f, -0.22f, 1.33f),
            Rotation = new Quaternion(0.1f, -0.2f, 0.3f, 0.9f),
            Acceleration = new Vector3(-1.5f, 0.03f, 9.81f),
            MoveAxis = new Vector2(0.66f, -0.42f)
        };

        [Test]
        public void InputPacket_RoundTrip_PreservesAllFields_6DoF()
        {
            var p = Sample(42);
            byte[] bytes = PacketSerializer.Serialize(in p);

            Assert.AreEqual(NetworkConstants.InputPacketSize, bytes.Length, "고정 길이(80B)");
            Assert.AreEqual(80, NetworkConstants.InputPacketSize, "v3 크기");

            Assert.IsTrue(PacketSerializer.TryDeserialize(bytes, bytes.Length, out var q));
            Assert.AreEqual(p.Sequence, q.Sequence);
            Assert.AreEqual(p.TimestampMs, q.TimestampMs);
            Assert.AreEqual(p.TouchId, q.TouchId);
            Assert.AreEqual(p.Phase, q.Phase);
            Assert.AreEqual(p.NormalizedPos.x, q.NormalizedPos.x);
            Assert.AreEqual(p.NormalizedPos.y, q.NormalizedPos.y);
            Assert.AreEqual(p.Position.x, q.Position.x);
            Assert.AreEqual(p.Position.y, q.Position.y);
            Assert.AreEqual(p.Position.z, q.Position.z);
            Assert.AreEqual(p.Rotation.x, q.Rotation.x);
            Assert.AreEqual(p.Rotation.y, q.Rotation.y);
            Assert.AreEqual(p.Rotation.z, q.Rotation.z);
            Assert.AreEqual(p.Rotation.w, q.Rotation.w);
            Assert.AreEqual(p.Acceleration.x, q.Acceleration.x);
            Assert.AreEqual(p.Acceleration.y, q.Acceleration.y);
            Assert.AreEqual(p.Acceleration.z, q.Acceleration.z);
            Assert.AreEqual(p.MoveAxis.x, q.MoveAxis.x);
            Assert.AreEqual(p.MoveAxis.y, q.MoveAxis.y);
        }

        [Test]
        public void InputPacket_RejectsBadMagicAndShortAndNull()
        {
            var p = Sample(1);
            byte[] bytes = PacketSerializer.Serialize(in p);

            byte[] badMagic = (byte[])bytes.Clone();
            badMagic[0] ^= 0xFF;
            Assert.IsFalse(PacketSerializer.TryDeserialize(badMagic, badMagic.Length, out _), "매직 불일치 폐기");
            Assert.IsFalse(PacketSerializer.TryDeserialize(bytes, NetworkConstants.InputPacketSize - 1, out _), "길이 부족 폐기");
            Assert.IsFalse(PacketSerializer.TryDeserialize(null, 0, out _), "null 폐기");
        }

        [Test]
        public void SequenceValidator_DiscardsReorderedAndDuplicates()
        {
            var v = new SequenceValidator();
            Assert.IsTrue(v.Accept(0), "첫 패킷");
            Assert.IsTrue(v.Accept(1));
            Assert.IsFalse(v.Accept(1), "중복");
            Assert.IsFalse(v.Accept(0), "역전");
            Assert.IsTrue(v.Accept(5), "점프");
            Assert.IsFalse(v.Accept(4), "역전");
        }

        [Test]
        public void SequenceValidator_HandlesWrapAround()
        {
            var v = new SequenceValidator();
            Assert.IsTrue(v.Accept(uint.MaxValue));
            Assert.IsTrue(v.Accept(0), "wrap 전진 수용");
            Assert.IsFalse(v.Accept(uint.MaxValue), "wrap 후 과거 폐기");
        }

        [Test]
        public void DiscoveryBeacon_RoundTrip_And_RejectsJunk()
        {
            byte[] beacon = DiscoveryBeacon.Build("192.168.43.1", NetworkConstants.WebSocketPort);
            Assert.IsTrue(DiscoveryBeacon.TryParse(beacon, beacon.Length, out string ip, out int port, out byte ver));
            Assert.AreEqual("192.168.43.1", ip);
            Assert.AreEqual(NetworkConstants.WebSocketPort, port);
            Assert.AreEqual(NetworkConstants.ProtocolVersion, ver);

            byte[] junk = Encoding.UTF8.GetBytes("HELLO|world");
            Assert.IsFalse(DiscoveryBeacon.TryParse(junk, junk.Length, out _, out _, out _));
        }

        [Test]
        public void RttPacket_RoundTrip_And_RejectsBadMagic()
        {
            var rp = new RttPacket { Nonce = 99, OriginTimestamp = 42424242L };
            byte[] rb = RttPacket.Serialize(in rp);
            Assert.AreEqual(NetworkConstants.RttPacketSize, rb.Length);
            Assert.IsTrue(RttPacket.TryDeserialize(rb, rb.Length, out var rq));
            Assert.AreEqual(99u, rq.Nonce);
            Assert.AreEqual(42424242L, rq.OriginTimestamp);

            byte[] bad = (byte[])rb.Clone();
            bad[0] ^= 0xFF;
            Assert.IsFalse(RttPacket.TryDeserialize(bad, bad.Length, out _));
        }
    }
}
