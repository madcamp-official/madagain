using System;
using NUnit.Framework;
using MindHexer.Shared.Protocol;
using MindHexer.Shared.Events;
using MindHexer.Shared.Net;

namespace MindHexer.Shared.Tests
{
    /// <summary>
    /// 페어링 핸드셰이크 EditMode 테스트. in-memory 채널로 PairingClient↔PairingServer를 붙여
    /// WebSocket 라이브러리 없이 로직 전체를 검증한다.
    /// </summary>
    public sealed class PairingTests
    {
        // 동기 전달 in-memory 양방향 채널.
        private sealed class FakeChannel : IEventChannel
        {
            private FakeChannel _peer;
            public event Action<string> Received;
            public event Action Closed;
            public void Send(string json) => _peer?.Received?.Invoke(json);
            public void Close() => Closed?.Invoke();
            public static (FakeChannel a, FakeChannel b) Pair()
            {
                var a = new FakeChannel(); var b = new FakeChannel();
                a._peer = b; b._peer = a; return (a, b);
            }
        }

        [Test]
        public void Handshake_SucceedsOnMatchingVersion()
        {
            var (clientCh, serverCh) = FakeChannel.Pair();
            var server = new PairingServer(NetworkConstants.ProtocolVersion);
            bool serverPaired = false; string pairedId = null;
            server.ClientPaired += id => { serverPaired = true; pairedId = id; };
            server.Register("c1", serverCh);

            var client = new PairingClient(clientCh, NetworkConstants.ProtocolVersion, "S10e");
            bool clientPaired = false;
            client.Paired += () => clientPaired = true;
            client.BeginPairing();

            Assert.AreEqual(PairingState.Paired, client.State);
            Assert.IsTrue(clientPaired);
            Assert.IsTrue(serverPaired);
            Assert.AreEqual("c1", pairedId);
            Assert.AreEqual(1, server.PairedCount);
        }

        [Test]
        public void Handshake_RejectsOnVersionMismatch()
        {
            var (clientCh, serverCh) = FakeChannel.Pair();
            var server = new PairingServer(NetworkConstants.ProtocolVersion);
            server.Register("c1", serverCh);

            var client = new PairingClient(clientCh, (byte)(NetworkConstants.ProtocolVersion + 1), "old");
            string reason = null;
            client.Rejected += r => reason = r;
            client.BeginPairing();

            Assert.AreEqual(PairingState.Rejected, client.State);
            Assert.IsNotNull(reason);
            Assert.AreEqual(0, server.PairedCount);
        }

        [Test]
        public void EventsFlowBothWays_AfterPairing()
        {
            var (clientCh, serverCh) = FakeChannel.Pair();
            var server = new PairingServer(NetworkConstants.ProtocolVersion);
            server.Register("c1", serverCh);
            var client = new PairingClient(clientCh, NetworkConstants.ProtocolVersion, "S10e");
            client.BeginPairing();

            // 서버 → 클라
            EventMessage clientGot = null;
            client.EventReceived += m => clientGot = m;
            server.SendTo("c1", EventMessage.PatternResult(true, 3));
            Assert.IsNotNull(clientGot);
            Assert.AreEqual(EventType.PatternResult, clientGot.Type);
            Assert.AreEqual(3, clientGot.GetInt(EventMessage.KeyPatternId));

            // 클라 → 서버
            EventMessage serverGot = null;
            server.EventReceived += (id, m) => serverGot = m;
            client.SendEvent(EventMessage.BatteryWarning(0.15f));
            Assert.IsNotNull(serverGot);
            Assert.AreEqual(EventType.BatteryWarning, serverGot.Type);
        }
    }
}
