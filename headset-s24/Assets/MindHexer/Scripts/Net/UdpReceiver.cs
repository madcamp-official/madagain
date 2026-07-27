using System.Collections.Generic;
using UnityEngine;
using MindHexer.Shared.Protocol;
using MindHexer.Shared.Net;

namespace MindHexer.Headset.Net
{
    /// <summary>
    /// S10e로부터 UDP <see cref="InputPacket"/>을 수신하는 MonoBehaviour 어댑터. (SPEC 3.3 / 5)
    /// 실제 소켓·스레드·시퀀스 검증 로직은 Unity 비의존 코어 <see cref="InputStreamReceiver"/>에 있고,
    /// 여기서는 생명주기 + **수신 스레드 → 메인 스레드 큐 전달**을 담당한다.
    ///  - <see cref="Drain"/>: 지난 프레임 이후 도착한 모든 패킷을 메인 스레드로 넘긴다(지터 버퍼 입력).
    ///  - <see cref="TryGetLatest"/>: 최신 1개만 필요할 때(레거시).
    /// </summary>
    public sealed class UdpReceiver : MonoBehaviour
    {
        private InputStreamReceiver _core;

        private static readonly long TimeoutMs =
            (long)(NetworkConstants.UdpTimeoutSeconds * 1000f);

        // 수신 스레드가 채우고 메인 스레드가 Drain으로 비운다.
        private readonly Queue<InputPacket> _incoming = new Queue<InputPacket>();
        private readonly object _lock = new object();

        /// <summary>수용/폐기 통계(HUD 표시·디버깅용).</summary>
        public long AcceptedCount => _core?.AcceptedCount ?? 0;
        public long DiscardedCount => _core?.DiscardedCount ?? 0;

        /// <summary>UDP 미수신 타임아웃(SPEC 5.1). 미연결 상태도 true.</summary>
        public bool IsTimedOut => _core == null || _core.IsTimedOut(TimeoutMs);

        private void OnEnable()
        {
            _core = new InputStreamReceiver(NetworkConstants.UdpInputPort);
            _core.PacketReceived += OnPacket; // 수신 스레드에서 호출됨
            _core.Start();
        }

        private void OnDisable()
        {
            if (_core != null) _core.PacketReceived -= OnPacket;
            _core?.Dispose();
            _core = null;
            lock (_lock) { _incoming.Clear(); }
        }

        // 수신 스레드 → 큐 적재(스레드 안전).
        private void OnPacket(InputPacket p)
        {
            lock (_lock) { _incoming.Enqueue(p); }
        }

        /// <summary>지난 프레임 이후 도착한 모든 패킷을 순서대로 넘긴다(메인 스레드). 반환: 넘긴 개수.</summary>
        public int Drain(System.Action<InputPacket> consume)
        {
            if (consume == null) return 0;
            int count = 0;
            lock (_lock)
            {
                while (_incoming.Count > 0)
                {
                    consume(_incoming.Dequeue());
                    count++;
                }
            }
            return count;
        }

        /// <summary>가장 최근 유효 패킷을 읽는다(메인 스레드). 없으면 false.</summary>
        public bool TryGetLatest(out InputPacket packet)
        {
            if (_core == null) { packet = default; return false; }
            return _core.TryGetLatest(out packet);
        }

        /// <summary>재페어링 시 시퀀스 상태 초기화.</summary>
        public void ResetSequence() => _core?.ResetSequence();
    }
}
