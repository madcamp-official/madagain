using System;
using System.Net.Sockets;
using UnityEngine;
using MindHexer.Shared.Protocol;

namespace MindHexer.Shared.Net
{
    /// <summary>
    /// S10e → S24+ InputPacket UDP 송신의 **Unity 비의존 코어**. (SPEC 4)
    /// MonoBehaviour(controller-s10e의 UdpSender)가 이 클래스를 감싼다.
    /// 순수 .NET이라 에디터/실기기 없이 콘솔 하니스로 단독 검증 가능.
    ///
    /// 스레드 안전하지 않음 — 단일 송신 스레드(보통 Unity 메인 루프)에서 호출할 것.
    /// </summary>
    public sealed class InputStreamSender : IDisposable
    {
        private UdpClient _udp;
        private readonly byte[] _buffer = new byte[NetworkConstants.InputPacketSize];
        private uint _sequence;

        /// <summary>지금까지 송신한 패킷 수(= 다음 시퀀스 값).</summary>
        public uint SentCount => _sequence;

        /// <summary>대상 IP/포트로 연결. 재호출 시 대상 변경.</summary>
        public void Connect(string targetIp, int port)
        {
            if (string.IsNullOrEmpty(targetIp))
                throw new ArgumentException("targetIp is required", nameof(targetIp));

            _udp ??= new UdpClient();
            _udp.Connect(targetIp, port);
        }

        /// <summary>페어링 확정/재페어링 시 대상 변경(포트는 기본 InputPort).</summary>
        public void SetTarget(string targetIp) => Connect(targetIp, NetworkConstants.UdpInputPort);

        /// <summary>
        /// 한 프레임의 6DoF 입력 상태를 전송. 시퀀스는 자동 부여(단조 증가), 나머지는 인자로 받는다.
        /// Connect 전에 호출하면 false.
        /// </summary>
        public bool Send(TouchPhaseCode phase, int touchId, Vector2 normalized,
                         Vector3 position, Quaternion rotation, Vector3 accel, long timestampMs)
        {
            if (_udp == null) return false;

            var packet = new InputPacket
            {
                Sequence = _sequence,
                TimestampMs = timestampMs,
                TouchId = touchId,
                Phase = phase,
                NormalizedPos = normalized,
                Position = position,
                Rotation = rotation,
                Acceleration = accel
            };

            PacketSerializer.Serialize(in packet, _buffer);
            _udp.Send(_buffer, _buffer.Length);
            _sequence++;
            return true;
        }

        /// <summary>이미 구성된 InputPacket을 그대로 전송(시퀀스도 packet 값 사용). 테스트/재전송용.</summary>
        public bool SendRaw(in InputPacket packet)
        {
            if (_udp == null) return false;
            PacketSerializer.Serialize(in packet, _buffer);
            _udp.Send(_buffer, _buffer.Length);
            return true;
        }

        public void Dispose()
        {
            try { _udp?.Close(); } catch { /* ignore */ }
            _udp = null;
        }
    }
}
