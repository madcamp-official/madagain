using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MindHexer.Shared.Protocol;

namespace MindHexer.Shared.Net
{
    /// <summary>
    /// S24+ 측 InputPacket UDP 수신의 **Unity 비의존 코어**. (SPEC 3.3 / 5)
    /// 백그라운드 스레드에서 수신·매직검증·시퀀스검증을 하고, 최신 유효 패킷을 원자적으로 보관한다.
    /// MonoBehaviour(headset-s24의 UdpReceiver)가 이 클래스를 감싸 Unity 메인 스레드로 노출한다.
    ///
    /// 시간은 Stopwatch(모노토닉, 순수 .NET)를 쓰므로 Unity Time 의존이 없다 →
    /// 콘솔 하니스로 단독 검증 가능.
    /// </summary>
    public sealed class InputStreamReceiver : IDisposable
    {
        private readonly int _port;
        private UdpClient _udp;
        private Thread _thread;
        private volatile bool _running;

        private readonly SequenceValidator _validator = new SequenceValidator();

        private InputPacket _latest;
        private volatile bool _hasLatest;
        private readonly object _latestLock = new object();

        // 통계(검증/디버깅용). 수신 스레드에서만 증가시키므로 Interlocked까지는 불필요하나
        // 외부에서 읽을 수 있게 volatile 처리.
        private long _acceptedCount;
        private long _discardedCount;
        private long _lastAcceptTicks; // Stopwatch.GetTimestamp() 기준(모노토닉). Unity .NET에 TickCount64가 없어 Stopwatch 사용.

        /// <summary>수용된(유효·최신) 패킷 수.</summary>
        public long AcceptedCount => Interlocked.Read(ref _acceptedCount);

        /// <summary>폐기된(매직 불일치/역전/중복) 패킷 수.</summary>
        public long DiscardedCount => Interlocked.Read(ref _discardedCount);

        /// <summary>수신 스레드가 돌고 있는지.</summary>
        public bool IsRunning => _running;

        /// <summary>유효 패킷을 한 번이라도 받았는지.</summary>
        public bool HasReceived => _hasLatest;

        /// <summary>
        /// 유효 패킷을 수용할 때마다 발생(옵션). **수신 스레드에서 호출**되므로,
        /// Unity에서 이 이벤트로 UI/게임 오브젝트를 만지려면 MainThreadDispatcher를 경유할 것.
        /// (Unity 어댑터 UdpReceiver는 이 이벤트를 쓰지 않고 TryGetLatest 폴링을 유지한다.)
        /// PC 측정 도구처럼 스레드 제약이 없는 곳에서 패킷별 로깅/통계에 쓰기 좋다.
        /// </summary>
        public event System.Action<InputPacket> PacketReceived;

        public InputStreamReceiver(int port) => _port = port;

        public InputStreamReceiver() : this(NetworkConstants.UdpInputPort) { }

        /// <summary>수신 시작. UDP 포트를 바인딩하고 백그라운드 수신 루프를 띄운다.</summary>
        public void Start()
        {
            if (_running) return;
            _udp = new UdpClient(_port);
            _running = true;
            _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "MHX-InputRx" };
            _thread.Start();
        }

        /// <summary>수신 종료. 스레드 join.</summary>
        public void Stop()
        {
            _running = false;
            try { _udp?.Close(); } catch { /* ignore */ }
            _thread?.Join(300);
            _thread = null;
            _udp = null;
        }

        private void ReceiveLoop()
        {
            var remote = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    byte[] data = _udp.Receive(ref remote);
                    if (!PacketSerializer.TryDeserialize(data, data.Length, out var packet))
                    {
                        Interlocked.Increment(ref _discardedCount); // 매직/길이 불일치
                        continue;
                    }
                    if (!_validator.Accept(packet.Sequence))
                    {
                        Interlocked.Increment(ref _discardedCount); // 역전/중복 (SPEC 5.2)
                        continue;
                    }

                    lock (_latestLock)
                    {
                        _latest = packet;
                        _hasLatest = true;
                    }
                    Interlocked.Increment(ref _acceptedCount);
                    Interlocked.Exchange(ref _lastAcceptTicks, Stopwatch.GetTimestamp());
                    PacketReceived?.Invoke(packet); // 옵션: 수신 스레드에서 호출
                }
                catch (SocketException) when (!_running) { break; } // 종료 중 Close
                catch (ObjectDisposedException) { break; }
                catch (Exception)
                {
                    // 개별 패킷 오류는 무시하고 계속 수신. 상세 로깅은 Unity 어댑터가 담당.
                }
            }
        }

        /// <summary>가장 최근 유효 패킷을 읽는다. 없으면 false.</summary>
        public bool TryGetLatest(out InputPacket packet)
        {
            lock (_latestLock)
            {
                packet = _latest;
                return _hasLatest;
            }
        }

        /// <summary>
        /// 마지막 유효 패킷 이후 timeoutMs 이상 경과했는지(연결 끊김 감지, SPEC 5.1).
        /// 아직 아무것도 못 받았으면 true(미연결)로 간주.
        /// </summary>
        public bool IsTimedOut(long timeoutMs)
        {
            if (!_hasLatest) return true;
            long last = Interlocked.Read(ref _lastAcceptTicks);
            long elapsedMs = (Stopwatch.GetTimestamp() - last) * 1000L / Stopwatch.Frequency;
            return elapsedMs > timeoutMs;
        }

        /// <summary>재페어링 시 시퀀스/통계 상태 초기화.</summary>
        public void ResetSequence() => _validator.Reset();

        public void Dispose() => Stop();
    }
}
