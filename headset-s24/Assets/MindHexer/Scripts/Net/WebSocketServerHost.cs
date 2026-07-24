using System;
using UnityEngine;
using WebSocketSharp;
using WebSocketSharp.Server;
using MindHexer.Shared.Protocol;
using MindHexer.Shared.Events;
using MindHexer.Shared.Net;

namespace MindHexer.Headset.Net
{
    /// <summary>
    /// S24+ 내장 WebSocket 서버(확정 이벤트 채널). WebSocketSharp 어댑터. (SPEC 2.2 / 2.3)
    /// 순수 로직은 <see cref="PairingServer"/>가 담당하고, 여기서는 라이브러리 바인딩 + 스레드 마샬링만 한다.
    ///
    /// WebSocketSharp 콜백은 별도 스레드 → <see cref="MainThreadDispatcher"/>로 메인 스레드에 넘겨
    /// PairingServer/게임 로직이 항상 메인 스레드에서 돌게 한다.
    ///
    /// 전제: Assets/Plugins/websocket-sharp.dll (docs/SETUP.md 3절). 씬에 MainThreadDispatcher 필요.
    /// </summary>
    public sealed class WebSocketServerHost : MonoBehaviour
    {
        private WebSocketServer _server;
        private PairingServer _pairing;

        /// <summary>현재 페어링된 클라이언트 수(HUD 표시용).</summary>
        public int PairedCount => _pairing?.PairedCount ?? 0;

        /// <summary>클라이언트가 확정 이벤트를 보내왔을 때(메인 스레드). (clientId, msg)</summary>
        public event Action<string, EventMessage> ClientEventReceived;

        /// <summary>클라이언트 페어링 성공 시(메인 스레드).</summary>
        public event Action<string> ClientPaired;

        private void OnEnable()
        {
            _pairing = new PairingServer(NetworkConstants.ProtocolVersion);
            _pairing.ClientPaired += id => ClientPaired?.Invoke(id);
            _pairing.EventReceived += (id, msg) => ClientEventReceived?.Invoke(id, msg);

            _server = new WebSocketServer(System.Net.IPAddress.Any, NetworkConstants.WebSocketPort);
            // 연결마다 SessionChannel 인스턴스가 생성되며, 초기화 시 이 호스트를 주입한다.
            _server.AddWebSocketService<SessionChannel>(
                NetworkConstants.WebSocketPath, s => s.Bind(this));
            _server.Start();
            Debug.Log($"[WS] server up on :{NetworkConstants.WebSocketPort}{NetworkConstants.WebSocketPath}");
        }

        private void OnDisable()
        {
            try { _server?.Stop(); } catch (Exception e) { Debug.LogException(e); }
            _server = null;
            _pairing = null;
        }

        /// <summary>페어링된 특정 클라이언트로 이벤트 송신.</summary>
        public bool SendTo(string clientId, EventMessage message) => _pairing?.SendTo(clientId, message) ?? false;

        /// <summary>페어링된 모든 클라이언트로 브로드캐스트(예: PatternResult).</summary>
        public void Broadcast(EventMessage message) => _pairing?.Broadcast(message);

        // 세션 등록/해제/수신을 메인 스레드로 넘긴다.
        private void DispatchRegister(string id, IEventChannel ch) => Dispatch(() => _pairing?.Register(id, ch));
        private void DispatchUnregister(string id) => Dispatch(() => _pairing?.Unregister(id));

        private static void Dispatch(Action a)
        {
            var d = MainThreadDispatcher.Instance;
            if (d != null) d.Enqueue(a);
            else a(); // 디스패처 없으면(테스트 등) 직접 실행
        }

        /// <summary>
        /// WebSocketSharp 세션 하나 = 하나의 <see cref="IEventChannel"/>.
        /// 라이브러리가 인스턴스를 만들고 콜백을 background 스레드에서 호출한다.
        /// </summary>
        public sealed class SessionChannel : WebSocketBehavior, IEventChannel
        {
            private WebSocketServerHost _host;

            public event Action<string> Received;
            public event Action Closed;

            internal void Bind(WebSocketServerHost host) => _host = host;

            // IEventChannel.Send → WebSocketBehavior.Send(string) (protected, 같은 클래스라 호출 가능)
            void IEventChannel.Send(string json) => Send(json);

            protected override void OnOpen()
            {
                // Register가 Received/Closed를 구독하므로, 등록도 메인 스레드에서.
                _host?.DispatchRegister(ID, this);
            }

            protected override void OnMessage(MessageEventArgs e)
            {
                if (!e.IsText) return;
                string data = e.Data;
                WebSocketServerHost.Dispatch(() => Received?.Invoke(data));
            }

            protected override void OnClose(CloseEventArgs e)
            {
                _host?.DispatchUnregister(ID);
                WebSocketServerHost.Dispatch(() => Closed?.Invoke());
            }
        }
    }
}
