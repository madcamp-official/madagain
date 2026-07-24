namespace MindHexer.Shared.Protocol
{
    /// <summary>
    /// 두 앱이 공유하는 네트워크 상수. 값을 바꾸면 양쪽이 자동으로 같은 값을 사용한다.
    /// SPEC 2 / NETWORK_PROTOCOL.md 참조.
    /// </summary>
    public static class NetworkConstants
    {
        /// <summary>InputPacket 매직 넘버 'MHX1' (리틀 엔디언). 타 앱/오염 패킷 폐기용.</summary>
        public const uint InputPacketMagic = 0x3158484D; // 'M''H''X''1'

        /// <summary>DiscoveryBeacon 매직 넘버 'MHXB'.</summary>
        public const uint DiscoveryMagic = 0x4258484D; // 'M''H''X''B'

        /// <summary>S10e → S24+ UDP InputPacket 수신 포트.</summary>
        public const int UdpInputPort = 45710;

        /// <summary>S24+ → 서브넷 Discovery 브로드캐스트 포트.</summary>
        public const int UdpDiscoveryPort = 45711;

        /// <summary>S24+ 내장 WebSocket 서버 리슨 포트.</summary>
        public const int WebSocketPort = 45712;

        /// <summary>WebSocket 이벤트 채널 경로.</summary>
        public const string WebSocketPath = "/mhx";

        /// <summary>고정 길이 InputPacket 바이트 수. (NETWORK_PROTOCOL.md 와이어 포맷)</summary>
        public const int InputPacketSize = 60;

        /// <summary>UDP 미수신 경고 임계값(초). SPEC 5.1.</summary>
        public const float UdpTimeoutSeconds = 1.0f;

        /// <summary>RTT 목표(ms). SPEC 5.4.</summary>
        public const int TargetRttMs = 50;

        /// <summary>프로토콜 버전 — 양쪽 불일치 시 페어링 거부 판단에 사용.</summary>
        public const byte ProtocolVersion = 1;
    }
}
