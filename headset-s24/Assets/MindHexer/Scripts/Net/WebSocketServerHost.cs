using System;
using UnityEngine;
using MindHexer.Shared.Protocol;
using MindHexer.Shared.Events;
using MindHexer.Shared.Net;
// UnityEngine.EventType(IMGUI)와의 모호성 제거: 이벤트 종류는 항상 shared의 것.
using EventType = MindHexer.Shared.Events.EventType;

namespace MindHexer.Headset.Net
{
    /// <summary>
    /// S24+ 내장 WebSocket 서버(확정 이벤트 채널). **외부 라이브러리 없이** shared <see cref="TcpWebSocketServer"/>로 구동.
    /// (기존 WebSocketSharp DLL 의존 제거 → S24+가 별도 플러그인 없이 서버를 띄운다.)
    /// 순수 로직은 <see cref="PairingServer"/>가 담당하고, 여기서는 라이브러리 바인딩 + 스레드 마샬링만 한다.
    ///
    /// 서버 콜백은 연결별 백그라운드 스레드에서 올라오므로, 앱을 향한 이벤트는 <see cref="MainThreadDispatcher"/>로
    /// 메인 스레드에 넘겨 게임 로직이 안전하게 받게 한다. 씬에 MainThreadDispatcher 필요.
    /// </summary>
    public sealed class WebSocketServerHost : MonoBehaviour
    {
        [SerializeField] private UdpReceiver _rx; // UDP 끊김 감지용(워치독)

        private TcpWebSocketServer _server;
        private PairingServer _pairing;
        private bool _droppedForTimeout;

        public int PairedCount => _pairing?.PairedCount ?? 0;

        /// <summary>UDP 스트림 끊김 경고(HUD 표시용). 한 번이라도 받은 뒤 1초+ 미수신이면 true.</summary>
        public bool LinkWarning { get; private set; }

        /// <summary>클라이언트 페어링 성공(메인 스레드).</summary>
        public event Action<string> ClientPaired;

        /// <summary>클라이언트가 보낸 확정 이벤트(메인 스레드). (clientId, msg)</summary>
        public event Action<string, EventMessage> ClientEventReceived;

        /// <summary>완성된 스와이프 패턴 수신(메인 스레드). 인자는 노드 시퀀스(0..3).</summary>
        public event Action<int[]> PatternSubmitted;

        private void Awake()
        {
            if (_rx == null) _rx = GetComponent<UdpReceiver>();
        }

        // 워치독: UDP 스트림이 끊기면(1초+ 미수신) 경고를 세우고, 페어링된 스테일 세션을 끊어
        // 컨트롤러의 WebSocket 재연결(재페어링)을 유도한다. (SPEC 5.1)
        private void Update()
        {
            bool timedOut = _rx != null && _rx.AcceptedCount > 0 && _rx.IsTimedOut;
            LinkWarning = timedOut;

            if (timedOut && PairedCount > 0 && !_droppedForTimeout)
            {
                Debug.LogWarning("[WS] UDP 스트림 1초+ 미수신 → 스테일 세션 드롭(컨트롤러 재연결 유도).");
                _pairing?.DropAll();
                _droppedForTimeout = true;
            }
            if (!timedOut) _droppedForTimeout = false;
        }

        private void OnEnable()
        {
            _pairing = new PairingServer(NetworkConstants.ProtocolVersion);
            _pairing.ClientPaired += id => Dispatch(() => ClientPaired?.Invoke(id));
            _pairing.EventReceived += OnClientEvent;

            _server = new TcpWebSocketServer(NetworkConstants.WebSocketPort);
            // 등록은 동기(백그라운드 스레드)로 — 콜백 구독을 먼저 걸어 초기 메시지 유실 방지.
            _server.ClientConnected += (id, ch) => _pairing?.Register(id, ch);
            _server.Start();
            Debug.Log($"[WS] server up on :{NetworkConstants.WebSocketPort}{NetworkConstants.WebSocketPath} (no external DLL)");
        }

        private void OnDisable()
        {
            try { _server?.Stop(); } catch (Exception e) { Debug.LogException(e); }
            _server = null;
            _pairing = null;
        }

        // PairingServer.EventReceived 는 WS 백그라운드 스레드에서 온다 → 메인 스레드로 마샬.
        private void OnClientEvent(string clientId, EventMessage msg)
        {
            Dispatch(() =>
            {
                ClientEventReceived?.Invoke(clientId, msg);
                if (msg.Type == EventType.PatternSubmit)
                    PatternSubmitted?.Invoke(msg.GetIntArray(EventMessage.KeyNodes));
            });
        }

        /// <summary>페어링된 특정 클라이언트로 이벤트 송신.</summary>
        public bool SendTo(string clientId, EventMessage message) => _pairing?.SendTo(clientId, message) ?? false;

        /// <summary>페어링된 모든 클라이언트로 브로드캐스트(예: PatternResult).</summary>
        public void Broadcast(EventMessage message) => _pairing?.Broadcast(message);

        private static void Dispatch(Action a)
        {
            var d = MainThreadDispatcher.Instance;
            if (d != null) d.Enqueue(a);
            else a(); // 디스패처 없으면 직접 실행(테스트 등)
        }
    }
}
