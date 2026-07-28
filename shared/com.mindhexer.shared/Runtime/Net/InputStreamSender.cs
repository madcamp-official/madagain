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

        /// <summary>
        /// 이 실행(프로세스)의 식별자. 앱을 재시작하면 타임스탬프가 0으로 돌아가는데,
        /// 수신 측은 이 값이 바뀌는 것으로 그 사실을 알고 지연 추정을 리셋한다.
        /// </summary>
        public uint SessionId { get; } =
            unchecked((uint)System.Guid.NewGuid().GetHashCode());

        /// <summary>지금까지 송신한 패킷 수(= 다음 시퀀스 값). v3부터 <b>프레임당 1</b>.</summary>
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
        /// 한 <b>프레임</b>의 입력 상태를 전송. 호출자는 포즈·터치·화면 기하를 채우고,
        /// <see cref="InputPacket.SessionId"/>·<see cref="InputPacket.Sequence"/>는 여기서 채운다.
        /// Connect 전에 호출하면 false.
        ///
        /// <para>⚠️ <b>프레임당 한 번만</b> 부를 것. v2는 터치 하나당 한 번씩 불렀는데, 그러면
        /// 시퀀스가 터치 수만큼 올라가고 수신 측이 최고 시퀀스만 수용하므로 같은 프레임의 다른
        /// 손가락이 조용히 버려졌다. 터치는 <see cref="InputPacket.SetTouch"/>로 배열에 담는다.</para>
        /// </summary>
        public bool Send(InputPacket packet)
        {
            if (_udp == null) return false;

            packet.SessionId = SessionId;
            packet.Sequence = _sequence;

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
