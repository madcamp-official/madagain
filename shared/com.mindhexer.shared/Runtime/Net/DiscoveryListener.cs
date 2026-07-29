using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MindHexer.Shared.Protocol;

namespace MindHexer.Shared.Net
{
    /// <summary>발견된 서버 정보.</summary>
    public struct DiscoveredServer
    {
        public string Ip;
        public int WebSocketPort;
        public byte ProtocolVersion;
        public bool IsValid => !string.IsNullOrEmpty(Ip);
    }

    /// <summary>
    /// S10e 측 디스커버리 리스너(Unity 비의존). (SPEC 2.3-2)
    /// 디스커버리 포트를 바인딩해 <see cref="DiscoveryBeacon"/>을 수신·파싱하고, 최신 발견 서버를 노출한다.
    /// 첫 발견 시 <see cref="ServerDiscovered"/> 이벤트가 (수신 스레드에서) 발생 → 앱은 WebSocket 연결을 시작한다.
    ///
    /// 브로드캐스트가 막힌 환경 대비, 앱은 IP 직접 입력 폴백을 별도로 제공해야 한다(SPEC 2.3-3).
    /// </summary>
    public sealed class DiscoveryListener : IDisposable
    {
        private readonly int _port;
        private UdpClient _udp;
        private Thread _thread;
        private volatile bool _running;

        private readonly object _lock = new object();
        private DiscoveredServer _latest;
        private bool _hasServer;

        /// <summary>첫 서버 발견 시 발생. 이후 동일 서버 재수신에는 발생하지 않는다.</summary>
        public event Action<DiscoveredServer> ServerDiscovered;

        public bool HasServer => _hasServer;
        public bool IsRunning => _running;

        public DiscoveryListener(int port = NetworkConstants.UdpDiscoveryPort) => _port = port;

        public void Start()
        {
            if (_running) return;
            _udp = new UdpClient();
            // 여러 리스너/재바인딩 대비 주소 재사용 허용.
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, _port));
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "MHX-DiscoveryRx" };
            _thread.Start();
        }

        private void Loop()
        {
            var remote = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    byte[] data = _udp.Receive(ref remote);
                    if (!DiscoveryBeacon.TryParse(data, data.Length, out string ip, out int wsPort, out byte ver))
                        continue;

                    var server = new DiscoveredServer { Ip = ip, WebSocketPort = wsPort, ProtocolVersion = ver };
                    bool isNew;
                    lock (_lock)
                    {
                        isNew = !_hasServer || _latest.Ip != ip || _latest.WebSocketPort != wsPort;
                        _latest = server;
                        _hasServer = true;
                    }
                    if (isNew) ServerDiscovered?.Invoke(server);
                }
                catch (SocketException) when (!_running) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception) { /* 개별 오류 무시하고 계속 */ }
            }
        }

        /// <summary>가장 최근 발견 서버. 없으면 IsValid=false.</summary>
        public bool TryGetServer(out DiscoveredServer server)
        {
            lock (_lock)
            {
                server = _latest;
                return _hasServer;
            }
        }

        public void Stop()
        {
            _running = false;
            try { _udp?.Close(); } catch { /* ignore */ }
            _thread?.Join(300);
            _thread = null;
            _udp = null;
        }

        public void Dispose() => Stop();
    }
}
