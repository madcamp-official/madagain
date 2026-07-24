using UnityEngine;
using MindHexer.Shared.Net;
using MindHexer.Controller.Input;

namespace MindHexer.Controller.Net
{
    /// <summary>
    /// S10e 연결 수립 전체 흐름 조립. (SPEC 2.3)
    ///   디스커버리 발견 → (UDP/RTT 대상 설정 + WebSocket 접속) → PairAck → 스트리밍/RTT 시작
    /// 브로드캐스트가 막힌 환경 대비 <see cref="ConnectManually"/>(IP 직접 입력) 폴백 제공(SPEC 2.3-3).
    ///
    /// 스트리밍(TouchGyroCapture)과 RTT 프로브는 페어링 전까지 비활성 → 페어링 성공 시 활성화.
    /// </summary>
    public sealed class PairingFlow : MonoBehaviour
    {
        [SerializeField] private DiscoveryListenerBehaviour _discovery;
        [SerializeField] private WsClient _ws;
        [SerializeField] private UdpSender _udpSender;
        [SerializeField] private RttProbeBehaviour _rttProbe;
        [SerializeField] private TouchGyroCapture _capture;

        public bool IsPaired => _ws != null && _ws.State == PairingState.Paired;

        private void OnEnable()
        {
            // 페어링 전에는 입력 스트리밍/RTT 비활성.
            SetStreaming(false);

            if (_discovery != null) _discovery.ServerDiscovered += OnServerDiscovered;
            if (_ws != null)
            {
                _ws.Paired += OnPaired;
                _ws.Rejected += OnRejected;
            }
        }

        private void OnDisable()
        {
            if (_discovery != null) _discovery.ServerDiscovered -= OnServerDiscovered;
            if (_ws != null)
            {
                _ws.Paired -= OnPaired;
                _ws.Rejected -= OnRejected;
            }
        }

        private void OnServerDiscovered(DiscoveredServer server)
        {
            Debug.Log($"[Flow] server discovered {server.Ip}:{server.WebSocketPort} v{server.ProtocolVersion}");
            ConnectTo(server.Ip, server.WebSocketPort);
        }

        /// <summary>IP 직접 입력 폴백(SPEC 2.3-3). WS 포트는 기본값 사용.</summary>
        public void ConnectManually(string ip)
        {
            ConnectTo(ip, MindHexer.Shared.Protocol.NetworkConstants.WebSocketPort);
        }

        private void ConnectTo(string ip, int wsPort)
        {
            // UDP/RTT 대상을 먼저 맞춘 뒤 WebSocket 접속(페어링). 스트리밍은 PairAck 후 켠다.
            if (_udpSender != null) _udpSender.SetTarget(ip);
            if (_rttProbe != null) _rttProbe.SetTarget(ip);
            if (_ws != null) _ws.Connect(ip, wsPort);
        }

        private void OnPaired()
        {
            Debug.Log("[Flow] paired → streaming on");
            SetStreaming(true);
        }

        private void OnRejected(string reason)
        {
            Debug.LogWarning($"[Flow] pairing rejected: {reason}");
            SetStreaming(false);
        }

        private void SetStreaming(bool on)
        {
            if (_capture != null) _capture.enabled = on;
            if (_rttProbe != null) _rttProbe.enabled = on;
        }
    }
}
