using System;
using MindHexer.Shared.Events;
using MindHexer.Shared.Protocol;

namespace MindHexer.Shared.Net
{
    public enum PairingState
    {
        Idle,        // 채널 미연결
        Pairing,     // PairRequest 보냄, PairAck 대기
        Paired,      // 페어링 성공 → UDP 스트리밍/RTT 시작 가능
        Rejected,    // 버전 불일치 등으로 거부됨
        Closed       // 채널 닫힘
    }

    /// <summary>
    /// S10e 측 페어링 상태 머신(Unity 비의존). (SPEC 2.3)
    /// 채널이 열리면 PairRequest(프로토콜 버전 포함) 송신 → PairAck 수신 시 Paired.
    /// 버전 불일치로 PairReject를 받으면 Rejected. 라이브러리 없는 <see cref="IEventChannel"/>만 의존하므로
    /// in-memory 채널로 서버와 붙여 핸드셰이크 전체를 테스트할 수 있다.
    ///
    /// Paired 진입 시 <see cref="Paired"/> 이벤트가 발생 → 앱은 여기서 UDP 스트리밍/RTT 프로브를 시작한다.
    /// 확정 이벤트(PatternResult/BatteryWarning)는 <see cref="EventReceived"/>로 전달.
    /// </summary>
    public sealed class PairingClient
    {
        private readonly IEventChannel _channel;
        private readonly byte _protocolVersion;
        private readonly string _deviceName;

        public PairingState State { get; private set; } = PairingState.Idle;

        /// <summary>페어링 성공 시 발생.</summary>
        public event Action Paired;

        /// <summary>거부 시 발생. 인자는 사유.</summary>
        public event Action<string> Rejected;

        /// <summary>페어링 완료 후 도착하는 일반 이벤트(PatternResult 등).</summary>
        public event Action<EventMessage> EventReceived;

        public PairingClient(IEventChannel channel, byte protocolVersion, string deviceName)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _protocolVersion = protocolVersion;
            _deviceName = deviceName ?? "controller";
            _channel.Received += OnReceived;
            _channel.Closed += OnClosed;
        }

        /// <summary>채널이 열린 직후 호출. PairRequest를 보내고 PairAck를 기다린다.</summary>
        public void BeginPairing()
        {
            if (State != PairingState.Idle && State != PairingState.Closed) return;
            State = PairingState.Pairing;
            _channel.Send(EventMessage.PairRequest(_protocolVersion, _deviceName).Encode());
        }

        private void OnReceived(string json)
        {
            if (!EventMessage.TryDecode(json, out var msg)) return;

            switch (msg.Type)
            {
                case EventType.PairAck:
                    if (State == PairingState.Pairing)
                    {
                        State = PairingState.Paired;
                        Paired?.Invoke();
                    }
                    break;

                case EventType.PairReject:
                    State = PairingState.Rejected;
                    Rejected?.Invoke(msg.GetString(EventMessage.KeyReason, "rejected"));
                    break;

                default:
                    // 페어링 완료 후 일반 이벤트만 상위로 전달.
                    if (State == PairingState.Paired)
                        EventReceived?.Invoke(msg);
                    break;
            }
        }

        private void OnClosed() => State = PairingState.Closed;

        /// <summary>확정 이벤트를 서버로 송신(페어링 이후).</summary>
        public void SendEvent(EventMessage message) => _channel.Send(message.Encode());
    }
}
