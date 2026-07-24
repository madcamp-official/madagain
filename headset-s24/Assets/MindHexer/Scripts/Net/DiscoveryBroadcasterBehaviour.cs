using UnityEngine;
using MindHexer.Shared.Protocol;
using MindHexer.Shared.Net;

namespace MindHexer.Headset.Net
{
    /// <summary>
    /// S24+ 디스커버리 브로드캐스터 어댑터. 자신의 IP/WS 포트를 주기적으로 UDP 브로드캐스트한다(SPEC 2.3-1).
    /// 코어는 <see cref="DiscoveryBroadcaster"/>.
    /// </summary>
    public sealed class DiscoveryBroadcasterBehaviour : MonoBehaviour
    {
        [Tooltip("비콘에 실을 자신의 IP. 비우면 로컬 IP 자동 탐지 시도.")]
        public string ServerIpOverride = "";

        private DiscoveryBroadcaster _core;

        private void OnEnable()
        {
            string ip = string.IsNullOrEmpty(ServerIpOverride) ? LocalIPv4.Resolve() : ServerIpOverride;
            _core = new DiscoveryBroadcaster(ip, NetworkConstants.WebSocketPort);
            _core.Start();
            Debug.Log($"[Discovery] broadcasting ip={ip} wsPort={NetworkConstants.WebSocketPort}");
        }

        private void OnDisable()
        {
            _core?.Dispose();
            _core = null;
        }
    }
}
