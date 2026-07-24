using UnityEngine;
using MindHexer.Shared.Protocol;
using MindHexer.Shared.Net;

namespace MindHexer.Headset.Net
{
    /// <summary>
    /// S24+ RTT 응답기 어댑터. RttPort로 온 Ping을 에코한다(SPEC 5.4). 코어는 <see cref="RttResponder"/>.
    /// </summary>
    public sealed class RttResponderBehaviour : MonoBehaviour
    {
        private RttResponder _core;

        public long EchoedCount => _core?.EchoedCount ?? 0;

        private void OnEnable()
        {
            _core = new RttResponder(NetworkConstants.UdpRttPort);
            _core.Start();
        }

        private void OnDisable()
        {
            _core?.Dispose();
            _core = null;
        }
    }
}
