using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MindHexer.Shared.Protocol;

namespace MindHexer.Shared.Net
{
    /// <summary>
    /// S24+ 측 디스커버리 브로드캐스터(Unity 비의존). (SPEC 2.3-1)
    /// 자신의 서버 IP/WebSocket 포트를 담은 <see cref="DiscoveryBeacon"/>을 주기적으로 UDP 브로드캐스트한다.
    /// 기본 대상은 제한 브로드캐스트(255.255.255.255)라 동일 서브넷/핫스팟의 S10e가 수신 가능.
    ///
    /// 백그라운드 스레드에서 주기 송신하며, <see cref="BroadcastOnce"/>로 단발 송신도 가능(테스트/폴백용).
    /// </summary>
    public sealed class DiscoveryBroadcaster : IDisposable
    {
        private readonly string _serverIp;
        private readonly int _wsPort;
        private readonly int _discoveryPort;
        private readonly IPAddress _target;
        private readonly int _intervalMs;

        private UdpClient _udp;
        private Thread _thread;
        private volatile bool _running;
        private long _sentCount;

        public long SentCount => Interlocked.Read(ref _sentCount);
        public bool IsRunning => _running;

        /// <param name="serverIp">비콘에 실을 자신의 IP(S10e가 WebSocket 접속할 주소).</param>
        /// <param name="wsPort">WebSocket 서버 포트.</param>
        /// <param name="discoveryPort">브로드캐스트 대상 포트.</param>
        /// <param name="targetAddress">브로드캐스트 주소. null이면 255.255.255.255. 테스트 시 127.0.0.1 지정 가능.</param>
        /// <param name="intervalMs">주기 송신 간격(ms).</param>
        public DiscoveryBroadcaster(string serverIp, int wsPort,
            int discoveryPort = NetworkConstants.UdpDiscoveryPort,
            IPAddress targetAddress = null, int intervalMs = 1000)
        {
            _serverIp = serverIp;
            _wsPort = wsPort;
            _discoveryPort = discoveryPort;
            _target = targetAddress ?? IPAddress.Broadcast;
            _intervalMs = intervalMs;
        }

        private void EnsureSocket()
        {
            if (_udp != null) return;
            _udp = new UdpClient();
            _udp.EnableBroadcast = true;
        }

        /// <summary>비콘 1회 송신.</summary>
        public void BroadcastOnce()
        {
            EnsureSocket();
            byte[] payload = DiscoveryBeacon.Build(_serverIp, _wsPort);
            _udp.Send(payload, payload.Length, new IPEndPoint(_target, _discoveryPort));
            Interlocked.Increment(ref _sentCount);
        }

        /// <summary>주기 브로드캐스트 시작.</summary>
        public void Start()
        {
            if (_running) return;
            EnsureSocket();
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "MHX-DiscoveryTx" };
            _thread.Start();
        }

        private void Loop()
        {
            while (_running)
            {
                try { BroadcastOnce(); }
                catch (Exception) { /* 인터페이스 다운 등 일시 오류 무시 */ }
                for (int slept = 0; slept < _intervalMs && _running; slept += 50)
                    Thread.Sleep(50);
            }
        }

        public void Stop()
        {
            _running = false;
            _thread?.Join(300);
            _thread = null;
            try { _udp?.Close(); } catch { /* ignore */ }
            _udp = null;
        }

        public void Dispose() => Stop();
    }
}
