using System;
using UnityEngine;
using MindHexer.Shared.Net;

namespace MindHexer.Controller.Net
{
    /// <summary>
    /// S10e 디스커버리 리스너 어댑터. 비콘을 수신해 서버 발견을 알린다(SPEC 2.3-2). 코어는 <see cref="DiscoveryListener"/>.
    /// 수신 스레드 콜백을 <see cref="ServerDiscovered"/>(메인 스레드)로 넘긴다.
    /// </summary>
    public sealed class DiscoveryListenerBehaviour : MonoBehaviour
    {
        private DiscoveryListener _core;
        private volatile bool _pending;
        private DiscoveredServer _pendingServer;

        /// <summary>서버 발견 시(메인 스레드).</summary>
        public event Action<DiscoveredServer> ServerDiscovered;

        public bool HasServer => _core?.HasServer ?? false;

        private void OnEnable()
        {
            _core = new DiscoveryListener();
            _core.ServerDiscovered += OnCoreDiscovered; // 수신 스레드
            _core.Start();
        }

        private void OnDisable()
        {
            _core?.Dispose();
            _core = null;
        }

        // 수신 스레드 → 플래그만 세우고 Update(메인 스레드)에서 이벤트 발생.
        private void OnCoreDiscovered(DiscoveredServer s)
        {
            _pendingServer = s;
            _pending = true;
        }

        private void Update()
        {
            if (!_pending) return;
            _pending = false;
            ServerDiscovered?.Invoke(_pendingServer);
        }

        /// <summary>최신 발견 서버 조회.</summary>
        public bool TryGetServer(out DiscoveredServer server)
        {
            if (_core != null) return _core.TryGetServer(out server);
            server = default;
            return false;
        }
    }
}
