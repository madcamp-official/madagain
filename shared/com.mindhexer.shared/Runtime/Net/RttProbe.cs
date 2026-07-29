using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MindHexer.Shared.Protocol;

namespace MindHexer.Shared.Net
{
    /// <summary>
    /// S10e 측 RTT 프로브(Unity 비의존 코어). RttPort로 Ping을 보내고 에코를 받아 왕복시간을 잰다. (SPEC 5.4)
    /// OriginTimestamp에 자신의 Stopwatch 틱을 실어 보내므로 두 폰 시계 동기화가 필요 없다.
    ///
    /// 사용: Connect() → 주기적으로 SendPing() 호출(예: 0.5초마다) → LastRttMs/AverageRttMs 조회.
    /// 스레드 안전(수신은 백그라운드, 통계는 Interlocked/lock 보호).
    /// </summary>
    public sealed class RttProbe : IDisposable
    {
        private UdpClient _udp;
        private Thread _thread;
        private volatile bool _running;
        private readonly Stopwatch _sw = Stopwatch.StartNew();

        private uint _nextNonce;
        private long _sentCount;
        private long _recvCount;

        private readonly object _statLock = new object();
        private double _lastRttMs = -1;
        private double _avgRttMs = -1;
        private const double AvgAlpha = 0.2; // 지수이동평균 계수

        public long SentCount => Interlocked.Read(ref _sentCount);
        public long ReceivedCount => Interlocked.Read(ref _recvCount);

        /// <summary>마지막 측정 RTT(ms). 아직 없으면 -1.</summary>
        public double LastRttMs { get { lock (_statLock) return _lastRttMs; } }

        /// <summary>지수이동평균 RTT(ms). 아직 없으면 -1.</summary>
        public double AverageRttMs { get { lock (_statLock) return _avgRttMs; } }

        /// <summary>SPEC 5.4 목표(≤50ms) 충족 여부. 측정값 없으면 false.</summary>
        public bool MeetsTarget
        {
            get { lock (_statLock) return _avgRttMs >= 0 && _avgRttMs <= NetworkConstants.TargetRttMs; }
        }

        public void Connect(string targetIp, int port)
        {
            if (string.IsNullOrEmpty(targetIp))
                throw new ArgumentException("targetIp is required", nameof(targetIp));
            _udp ??= new UdpClient();
            _udp.Connect(targetIp, port);

            if (!_running)
            {
                _running = true;
                _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "MHX-RttProbe" };
                _thread.Start();
            }
        }

        public void Connect(string targetIp) => Connect(targetIp, NetworkConstants.UdpRttPort);

        /// <summary>Ping 한 번 송신. Connect 전이면 false.</summary>
        public bool SendPing()
        {
            if (_udp == null) return false;
            var p = new RttPacket
            {
                Nonce = unchecked(_nextNonce++),
                OriginTimestamp = _sw.ElapsedTicks
            };
            byte[] bytes = RttPacket.Serialize(in p);
            _udp.Send(bytes, bytes.Length);
            Interlocked.Increment(ref _sentCount);
            return true;
        }

        private void ReceiveLoop()
        {
            var remote = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    byte[] data = _udp.Receive(ref remote);
                    if (!RttPacket.TryDeserialize(data, data.Length, out var echo))
                        continue;

                    long now = _sw.ElapsedTicks;
                    double ms = (now - echo.OriginTimestamp) * 1000.0 / Stopwatch.Frequency;
                    if (ms < 0) ms = 0; // 방어

                    Interlocked.Increment(ref _recvCount);
                    lock (_statLock)
                    {
                        _lastRttMs = ms;
                        _avgRttMs = _avgRttMs < 0 ? ms : _avgRttMs + AvgAlpha * (ms - _avgRttMs);
                    }
                }
                catch (SocketException) when (!_running) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception) { /* 무시하고 계속 */ }
            }
        }

        public void Dispose()
        {
            _running = false;
            try { _udp?.Close(); } catch { /* ignore */ }
            _thread?.Join(300);
            _thread = null;
            _udp = null;
        }
    }
}
