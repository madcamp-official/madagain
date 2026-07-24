using System;
using System.Text;
using UnityEngine;
using NativeWebSocket;
using MindHexer.Shared.Protocol;
using MindHexer.Shared.Events;
using MindHexer.Shared.Net;

namespace MindHexer.Controller.Net
{
    /// <summary>
    /// S10e WebSocket 클라이언트(확정 이벤트 채널). NativeWebSocket 어댑터 + <see cref="IEventChannel"/>. (SPEC 2.2/2.3)
    /// 순수 페어링 로직은 <see cref="PairingClient"/>가 담당. 연결이 열리면 PairRequest를 보내고
    /// PairAck 수신 시 <see cref="Paired"/>가 발생한다(앱은 여기서 UDP 스트리밍/RTT 시작).
    ///
    /// NativeWebSocket은 비-WebGL에서 Update()의 DispatchMessageQueue()로 콜백을 메인 스레드에 올린다.
    /// 전제: NativeWebSocket UPM 패키지(controller-s10e/Packages/manifest.json).
    /// </summary>
    public sealed class WsClient : MonoBehaviour, IEventChannel
    {
        private WebSocket _ws;
        private PairingClient _pairing;
        private string _deviceName = "S10e";

        public event Action<string> Received;
        public event Action Closed;

        /// <summary>페어링 성공 시(메인 스레드). 앱은 여기서 스트리밍/RTT를 켠다.</summary>
        public event Action Paired;

        /// <summary>페어링 거부 시. 인자는 사유.</summary>
        public event Action<string> Rejected;

        /// <summary>페어링 후 서버(S24+)에서 온 이벤트(PatternResult 등).</summary>
        public event Action<EventMessage> ServerEventReceived;

        public PairingState State => _pairing?.State ?? PairingState.Idle;

        void IEventChannel.Send(string json)
        {
            if (_ws != null && _ws.State == WebSocketState.Open)
                _ws.SendText(json);
        }

        /// <summary>S24+ WebSocket 서버에 접속. 발견된 서버 IP/포트로 호출.</summary>
        public async void Connect(string serverIp, int wsPort)
        {
            await CloseIfOpen();

            string url = $"ws://{serverIp}:{wsPort}{NetworkConstants.WebSocketPath}";
            _ws = new WebSocket(url);

            _pairing = new PairingClient(this, NetworkConstants.ProtocolVersion, _deviceName);
            _pairing.Paired += () => Paired?.Invoke();
            _pairing.Rejected += r => Rejected?.Invoke(r);
            _pairing.EventReceived += m => ServerEventReceived?.Invoke(m);

            _ws.OnOpen += () =>
            {
                Debug.Log($"[WS] connected → {url}");
                _pairing.BeginPairing(); // PairRequest 송신
            };
            _ws.OnMessage += bytes =>
            {
                string json = Encoding.UTF8.GetString(bytes);
                Received?.Invoke(json); // PairingClient가 구독 중
            };
            _ws.OnError += err => Debug.LogWarning($"[WS] error: {err}");
            _ws.OnClose += _ => Closed?.Invoke();

            await _ws.Connect();
        }

        /// <summary>발견된 서버(DiscoveredServer)로 접속하는 편의 오버로드.</summary>
        public void Connect(DiscoveredServer server) => Connect(server.Ip, server.WebSocketPort);

        /// <summary>확정 이벤트를 서버로 송신(페어링 이후).</summary>
        public void SendEvent(EventMessage message) => _pairing?.SendEvent(message);

        private void Update()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            _ws?.DispatchMessageQueue();
#endif
        }

        private async System.Threading.Tasks.Task CloseIfOpen()
        {
            if (_ws == null) return;
            try { await _ws.Close(); } catch (Exception e) { Debug.LogException(e); }
            _ws = null;
        }

        private async void OnDisable()
        {
            await CloseIfOpen();
            _pairing = null;
        }
    }
}
