using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MindHexer.Shared.Protocol;

namespace MindHexer.Shared.Net
{
    /// <summary>
    /// S24+ 측 RTT 응답기(Unity 비의존 코어). RttPort로 들어온 Ping을 **바이트 그대로 에코**한다. (SPEC 5.4)
    /// 유효한 <see cref="RttPacket"/> 매직인 경우에만 응답 → 오염 패킷 무시.
    /// </summary>
    public sealed class RttResponder : IDisposable
    {
        private readonly int _port;
        private UdpClient _udp;
        private Thread _thread;
        private volatile bool _running;
        private long _echoedCount;

        public long EchoedCount => Interlocked.Read(ref _echoedCount);
        public bool IsRunning => _running;

        public RttResponder(int port) => _port = port;
        public RttResponder() : this(NetworkConstants.UdpRttPort) { }

        public void Start()
        {
            if (_running) return;
            _udp = new UdpClient(_port);
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "MHX-RttResponder" };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            try { _udp?.Close(); } catch { /* ignore */ }
            _thread?.Join(300);
            _thread = null;
            _udp = null;
        }

        private void Loop()
        {
            var remote = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    byte[] data = _udp.Receive(ref remote);
                    if (!RttPacket.TryDeserialize(data, data.Length, out _))
                        continue; // 매직 불일치 → 무시
                    _udp.Send(data, data.Length, remote); // 그대로 에코
                    Interlocked.Increment(ref _echoedCount);
                }
                catch (SocketException) when (!_running) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception) { /* 개별 오류 무시하고 계속 */ }
            }
        }

        public void Dispose() => Stop();
    }
}
