using System.Net.Sockets;
using UnityEngine;
using MindHexer.Shared.Protocol;

namespace MindHexer.Controller.Net
{
    /// <summary>
    /// InputPacket을 S24+로 UDP 스트리밍한다. (SPEC 4 / 6, 담당자 B)
    /// 시퀀스 번호를 단조 증가시키며, 버퍼를 재사용해 GC 부담을 줄인다.
    ///
    /// TODO(담당자 B, 1일차): UDP Ping-Pong 최소 검증 — 최우선 마감(SPEC 6).
    /// </summary>
    public sealed class UdpSender : MonoBehaviour
    {
        [Tooltip("S24+ IP. Discovery 비콘 수신 또는 직접 입력으로 설정(SPEC 2.3).")]
        public string TargetIp = "192.168.0.2";

        private UdpClient _udp;
        private readonly byte[] _buffer = new byte[NetworkConstants.InputPacketSize];
        private uint _sequence;

        private void OnEnable()
        {
            _udp = new UdpClient();
            _udp.Connect(TargetIp, NetworkConstants.UdpInputPort);
        }

        private void OnDisable()
        {
            try { _udp?.Close(); } catch { /* ignore */ }
            _udp = null;
        }

        /// <summary>대상 IP 변경(페어링 확정 시 호출).</summary>
        public void SetTarget(string ip)
        {
            TargetIp = ip;
            _udp?.Connect(TargetIp, NetworkConstants.UdpInputPort);
        }

        /// <summary>한 프레임의 입력 상태를 전송. 시퀀스/타임스탬프는 자동 부여.</summary>
        public void Send(TouchPhaseCode phase, int touchId, Vector2 normalized,
                         Quaternion gyro, Vector3 accel, long timestampMs)
        {
            if (_udp == null) return;

            var packet = new InputPacket
            {
                Sequence = _sequence++,
                TimestampMs = timestampMs,
                TouchId = touchId,
                Phase = phase,
                NormalizedPos = normalized,
                GyroRotation = gyro,
                Acceleration = accel
            };

            PacketSerializer.Serialize(in packet, _buffer);
            _udp.Send(_buffer, _buffer.Length);
        }
    }
}
