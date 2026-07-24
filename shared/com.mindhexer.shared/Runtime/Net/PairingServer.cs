using System;
using System.Collections.Generic;
using MindHexer.Shared.Events;
using MindHexer.Shared.Protocol;

namespace MindHexer.Shared.Net
{
    /// <summary>
    /// S24+ 측 페어링 서버 로직(Unity 비의존). (SPEC 2.3)
    /// WebSocket 서버 어댑터가 클라이언트 연결마다 <see cref="Register"/>로 채널을 넘기면,
    /// PairRequest의 프로토콜 버전을 검증해 PairAck 또는 PairReject로 응답하고 페어링 상태를 추적한다.
    /// 라이브러리 없는 <see cref="IEventChannel"/>만 의존 → in-memory 채널로 검증 가능.
    /// </summary>
    public sealed class PairingServer
    {
        private readonly byte _protocolVersion;
        private readonly HashSet<string> _paired = new HashSet<string>();
        private readonly Dictionary<string, IEventChannel> _channels = new Dictionary<string, IEventChannel>();
        private readonly object _lock = new object();

        /// <summary>클라이언트 페어링 성공 시 발생. 인자는 clientId.</summary>
        public event Action<string> ClientPaired;

        /// <summary>페어링 완료된 클라이언트에서 온 일반 이벤트. (clientId, 메시지)</summary>
        public event Action<string, EventMessage> EventReceived;

        public PairingServer(byte protocolVersion) => _protocolVersion = protocolVersion;

        /// <summary>현재 페어링된 클라이언트 수.</summary>
        public int PairedCount { get { lock (_lock) return _paired.Count; } }

        public bool IsPaired(string clientId) { lock (_lock) return _paired.Contains(clientId); }

        /// <summary>새 클라이언트 연결 등록. 어댑터가 연결 수립 시 호출.</summary>
        public void Register(string clientId, IEventChannel channel)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            lock (_lock) { _channels[clientId] = channel; }
            channel.Received += json => OnReceived(clientId, channel, json);
            channel.Closed += () => Unregister(clientId);
        }

        /// <summary>클라이언트 연결 해제. 어댑터가 채널 닫힘 시 호출.</summary>
        public void Unregister(string clientId)
        {
            lock (_lock) { _paired.Remove(clientId); _channels.Remove(clientId); }
        }

        private void OnReceived(string clientId, IEventChannel channel, string json)
        {
            if (!EventMessage.TryDecode(json, out var msg)) return;

            switch (msg.Type)
            {
                case EventType.PairRequest:
                    byte clientVer = msg.GetByte(EventMessage.KeyProtocolVersion);
                    if (clientVer == _protocolVersion)
                    {
                        lock (_lock) { _paired.Add(clientId); }
                        channel.Send(EventMessage.PairAck(_protocolVersion).Encode());
                        ClientPaired?.Invoke(clientId);
                    }
                    else
                    {
                        channel.Send(EventMessage.PairReject(
                            $"protocol mismatch: server v{_protocolVersion}, client v{clientVer}").Encode());
                    }
                    break;

                default:
                    if (IsPaired(clientId))
                        EventReceived?.Invoke(clientId, msg);
                    break;
            }
        }

        /// <summary>페어링된 특정 클라이언트로 이벤트 송신.</summary>
        public bool SendTo(string clientId, EventMessage message)
        {
            IEventChannel ch;
            lock (_lock) { if (!_channels.TryGetValue(clientId, out ch)) return false; }
            ch.Send(message.Encode());
            return true;
        }

        /// <summary>페어링된 모든 클라이언트로 브로드캐스트.</summary>
        public void Broadcast(EventMessage message)
        {
            List<IEventChannel> targets = new List<IEventChannel>();
            lock (_lock)
            {
                foreach (var id in _paired)
                    if (_channels.TryGetValue(id, out var ch)) targets.Add(ch);
            }
            string json = message.Encode();
            foreach (var ch in targets) ch.Send(json);
        }
    }
}
