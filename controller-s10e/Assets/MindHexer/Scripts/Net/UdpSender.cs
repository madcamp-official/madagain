using UnityEngine;
using MindHexer.Shared.Protocol;
using MindHexer.Shared.Net;

namespace MindHexer.Controller.Net
{
    /// <summary>
    /// InputPacket을 S24+로 UDP 스트리밍하는 MonoBehaviour 어댑터. (SPEC 4)
    /// 실제 소켓·시퀀스 부여 로직은 Unity 비의존 코어 <see cref="InputStreamSender"/>에 있다.
    /// </summary>
    public sealed class UdpSender : MonoBehaviour
    {
        [Tooltip("S24+ IP. Discovery 비콘 수신 또는 직접 입력으로 설정(SPEC 2.3).")]
        public string TargetIp = "192.168.0.2";

        private InputStreamSender _core;

        /// <summary>지금까지 송신한 패킷 수.</summary>
        public uint SentCount => _core?.SentCount ?? 0;

        private void OnEnable()
        {
            _core = new InputStreamSender();
            _core.Connect(TargetIp, NetworkConstants.UdpInputPort);
        }

        private void OnDisable()
        {
            _core?.Dispose();
            _core = null;
        }

        /// <summary>대상 IP 변경(페어링 확정 시 호출).</summary>
        public void SetTarget(string ip)
        {
            TargetIp = ip;
            _core?.SetTarget(ip);
        }

        /// <summary>한 프레임의 6DoF 입력 상태를 전송. 시퀀스는 코어가 부여.</summary>
        public void Send(TouchPhaseCode phase, int touchId, Vector2 normalized,
                         Vector3 position, Quaternion rotation, Vector3 accel, long timestampMs)
        {
            _core?.Send(phase, touchId, normalized, position, rotation, accel, timestampMs);
        }
    }
}
