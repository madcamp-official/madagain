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
        private static InputPacket Sample(uint seq)
        {
            var p = new InputPacket
            {
                SessionId = 0xABCD1234u,
                Sequence = seq,
                TimestampMs = 1234567890123L,
                Tracking = TrackingStateCode.Tracking6Dof,
                Position = new Vector3(0.11f, -0.22f, 1.33f),
                Rotation = new Quaternion(0.1f, -0.2f, 0.3f, 0.9f),
                Acceleration = new Vector3(-1.5f, 0.03f, 9.81f),
                ScreenWidth = 2280,
                ScreenHeight = 1080,
                Dpi = 438.5f,
                SafeArea = new Rect(0f, 24f, 2280f, 1032f),
                TouchCount = 2,
            };
            p.SetTouch(0, new TouchSample
            {
                Id = 7,
                Phase = TouchPhaseCode.Move,
                Normalized = new Vector2(0.25f, 0.75f)
            });
            p.SetTouch(1, new TouchSample
            {
                Id = 9,
                Phase = TouchPhaseCode.Down,
                Normalized = new Vector2(0.80f, 0.10f)
            });
            return p;
        }

        [Test]
        public void InputPacket_RoundTrip_PreservesAllFields_V3()
        {
            var p = Sample(42);
            byte[] bytes = PacketSerializer.Serialize(in p);

            Assert.AreEqual(NetworkConstants.InputPacketSize, bytes.Length, "고정 길이");
            Assert.AreEqual(128, NetworkConstants.InputPacketSize, "v3 크기");

            Assert.IsTrue(PacketSerializer.TryDeserialize(bytes, bytes.Length, out var q));
            Assert.AreEqual(p.SessionId, q.SessionId);
            Assert.AreEqual(p.Sequence, q.Sequence);
            Assert.AreEqual(p.TimestampMs, q.TimestampMs);
            Assert.AreEqual(p.Tracking, q.Tracking);

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

            Assert.AreEqual(p.ScreenWidth, q.ScreenWidth, "화면 폭");
            Assert.AreEqual(p.ScreenHeight, q.ScreenHeight, "화면 높이");
            Assert.AreEqual(p.Dpi, q.Dpi, "DPI");
            Assert.AreEqual(p.SafeArea, q.SafeArea, "safe area");

            Assert.AreEqual(p.TouchCount, q.TouchCount, "터치 수");
            for (int i = 0; i < NetworkConstants.MaxTouches; i++)
            {
                TouchSample a = p.GetTouch(i), b = q.GetTouch(i);
                Assert.AreEqual(a.Id, b.Id, $"touch{i}.Id");
                Assert.AreEqual(a.Phase, b.Phase, $"touch{i}.Phase");
                Assert.AreEqual(a.Normalized.x, b.Normalized.x, $"touch{i}.x");
                Assert.AreEqual(a.Normalized.y, b.Normalized.y, $"touch{i}.y");
            }
        }

        /// <summary>
        /// 두 손가락이 <b>같은 패킷</b>에 실린다. v2는 터치당 패킷을 보내 한 프레임의 다른
        /// 손가락이 시퀀스 검증에 걸려 버려졌다 — 그 회귀를 막는 테스트다.
        /// </summary>
        [Test]
        public void InputPacket_CarriesBothTouches_InOneFrame()
        {
            var p = Sample(1);
            byte[] bytes = PacketSerializer.Serialize(in p);
            Assert.IsTrue(PacketSerializer.TryDeserialize(bytes, bytes.Length, out var q));

            Assert.AreEqual(2, q.TouchCount);
            Assert.AreEqual(7, q.GetTouch(0).Id);
            Assert.AreEqual(9, q.GetTouch(1).Id);
            Assert.AreNotEqual(q.GetTouch(0).Id, q.GetTouch(1).Id, "서로 다른 손가락이 함께 도착");
        }

        /// <summary>길이 필드 덕분에 뒤에 미지의 바이트가 붙어도 해석된다(향후 필드 추가 대비).</summary>
        [Test]
        public void InputPacket_ToleratesTrailingUnknownBytes()
        {
            var p = Sample(3);
            byte[] bytes = PacketSerializer.Serialize(in p);

            var padded = new byte[bytes.Length + 32];
            System.Array.Copy(bytes, padded, bytes.Length);
            for (int i = bytes.Length; i < padded.Length; i++) padded[i] = 0xEE;

            Assert.IsTrue(PacketSerializer.TryDeserialize(padded, padded.Length, out var q));
            Assert.AreEqual(p.Sequence, q.Sequence);
            Assert.AreEqual(p.TouchCount, q.TouchCount);
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
