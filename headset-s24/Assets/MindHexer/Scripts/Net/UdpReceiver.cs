using UnityEngine;
using MindHexer.Shared.Protocol;
using MindHexer.Shared.Net;

namespace MindHexer.Headset.Net
{
    /// <summary>
    /// S10e로부터 UDP <see cref="InputPacket"/>을 수신하는 MonoBehaviour 어댑터. (SPEC 3.3 / 5)
    /// 실제 소켓·스레드·시퀀스 검증 로직은 Unity 비의존 코어 <see cref="InputStreamReceiver"/>에 있고,
    /// 여기서는 생명주기(OnEnable/OnDisable)만 Unity에 연결한다.
    /// 게임 로직(InputBridge)은 메인 스레드에서 <see cref="TryGetLatest"/>로 최신 패킷을 읽는다.
    /// </summary>
    public sealed class UdpReceiver : MonoBehaviour
    {
        private InputStreamReceiver _core;

        private static readonly long TimeoutMs =
            (long)(NetworkConstants.UdpTimeoutSeconds * 1000f);

        /// <summary>수용/폐기 통계(HUD 표시·디버깅용).</summary>
        public long AcceptedCount => _core?.AcceptedCount ?? 0;
        public long DiscardedCount => _core?.DiscardedCount ?? 0;

        /// <summary>UDP 미수신 타임아웃(SPEC 5.1). 미연결 상태도 true.</summary>
        public bool IsTimedOut => _core == null || _core.IsTimedOut(TimeoutMs);

        private void OnEnable()
        {
            _core = new InputStreamReceiver(NetworkConstants.UdpInputPort);
            _core.Start();
        }

        private void OnDisable()
        {
            _core?.Dispose();
            _core = null;
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
