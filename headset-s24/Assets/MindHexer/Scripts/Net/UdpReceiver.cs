using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using MindHexer.Shared.Protocol;

namespace MindHexer.Headset.Net
{
    /// <summary>
    /// S10e로부터 UDP <see cref="InputPacket"/>을 수신한다. (SPEC 3.3 / 5)
    /// 백그라운드 스레드에서 수신·검증하고, 최신 패킷을 원자적으로 노출한다.
    /// 게임 로직(InputBridge)은 메인 스레드에서 <see cref="TryGetLatest"/>로 읽는다.
    ///
    /// TODO(담당자 A/B, 1일차): UDP Ping-Pong 최소 검증 — 최우선 마감(SPEC 6).
    /// </summary>
    public sealed class UdpReceiver : MonoBehaviour
    {
        private UdpClient _udp;
        private Thread _thread;
        private volatile bool _running;

        private readonly SequenceValidator _validator = new SequenceValidator();
        private readonly byte[] _recvBuf = new byte[512];

        private InputPacket _latest;
        private bool _hasLatest;
        private readonly object _latestLock = new object();

        /// <summary>마지막 유효 패킷 수신 시각(Time.realtimeSinceStartup). 타임아웃 판정용(SPEC 5.1).</summary>
        public float LastPacketTime { get; private set; }

        public bool IsTimedOut =>
            Time.realtimeSinceStartup - LastPacketTime > NetworkConstants.UdpTimeoutSeconds;

        private void OnEnable()
        {
            _udp = new UdpClient(NetworkConstants.UdpInputPort);
            _running = true;
            _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "MHX-UdpReceiver" };
            _thread.Start();
        }

        private void OnDisable()
        {
            _running = false;
            try { _udp?.Close(); } catch { /* ignore */ }
            _thread?.Join(200);
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
                        continue; // 매직/길이 불일치 폐기
                    if (!_validator.Accept(packet.Sequence))
                        continue; // 역전/중복 폐기 (SPEC 5.2)

                    lock (_latestLock)
                    {
                        _latest = packet;
                        _hasLatest = true;
                    }
                    // LastPacketTime은 Unity API라 메인 스레드에서 갱신.
                    MainThreadDispatcher.Instance?.Enqueue(() =>
                        LastPacketTime = Time.realtimeSinceStartup);
                }
                catch (SocketException) when (!_running) { break; } // 종료 중 Close
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        /// <summary>가장 최근 유효 패킷을 읽는다(메인 스레드). 없으면 false.</summary>
        public bool TryGetLatest(out InputPacket packet)
        {
            lock (_latestLock)
            {
                packet = _latest;
                return _hasLatest;
            }
        }

        /// <summary>재페어링 시 시퀀스 상태 초기화.</summary>
        public void ResetSequence() => _validator.Reset();
    }
}
