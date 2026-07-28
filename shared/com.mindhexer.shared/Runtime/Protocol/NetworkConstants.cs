namespace MindHexer.Shared.Protocol
{
    /// <summary>
    /// 두 앱이 공유하는 네트워크 상수. 값을 바꾸면 양쪽이 자동으로 같은 값을 사용한다.
    /// SPEC 2 / NETWORK_PROTOCOL.md 참조.
    /// </summary>
    public static class NetworkConstants
    {
        /// <summary>
        /// InputPacket 매직 넘버 'MHX2' (리틀 엔디언). 타 앱/오염 패킷 폐기용.
        /// v2('MHX1')와 값이 달라, 구버전 앱이 남아 있어도 서로의 패킷을 조용히 오해하지 않고 폐기한다.
        /// </summary>
        public const uint InputPacketMagic = 0x3258484D; // 'M''H''X''2'

        /// <summary>DiscoveryBeacon 매직 넘버 'MHXB'.</summary>
        public const uint DiscoveryMagic = 0x4258484D; // 'M''H''X''B'

        /// <summary>RttPacket(Ping/Pong) 매직 넘버 'MHXP'.</summary>
        public const uint RttMagic = 0x5058484D; // 'M''H''X''P'

        /// <summary>고정 길이 RttPacket 바이트 수.</summary>
        public const int RttPacketSize = 16;

        /// <summary>S10e → S24+ UDP InputPacket 수신 포트.</summary>
        public const int UdpInputPort = 45710;

        /// <summary>S24+ → 서브넷 Discovery 브로드캐스트 포트.</summary>
        public const int UdpDiscoveryPort = 45711;

        /// <summary>S24+ 내장 WebSocket 서버 리슨 포트.</summary>
        public const int WebSocketPort = 45712;

        /// <summary>RTT 측정용 UDP Ping/Pong 포트(입력 스트림과 분리). S24+가 리슨·에코.</summary>
        public const int UdpRttPort = 45713;

        /// <summary>WebSocket 이벤트 채널 경로.</summary>
        public const string WebSocketPath = "/mhx";

        /// <summary>
        /// 고정 길이 InputPacket 바이트 수. (NETWORK_PROTOCOL.md 와이어 포맷) v3: 128바이트.
        /// 패킷 안에 <b>길이 필드</b>가 있어, 나중에 필드가 늘어도 구버전 수신부는 아는 만큼만 읽고
        /// 뒷부분을 무시한다 → 한쪽만 고쳐도 안 깨진다(컨트롤러 재빌드를 줄이려는 장치).
        /// </summary>
        public const int InputPacketSize = 128;

        /// <summary>한 패킷에 실을 수 있는 터치 슬롯 수. 양손 엄지 기준 2개.</summary>
        public const int MaxTouches = 2;

        /// <summary>UDP 미수신 경고 임계값(초). SPEC 5.1.</summary>
        public const float UdpTimeoutSeconds = 1.0f;

        /// <summary>RTT 목표(ms). SPEC 5.4.</summary>
        public const int TargetRttMs = 50;

        /// <summary>프로토콜 버전 — 양쪽 불일치 시 페어링 거부 판단에 사용. v2: 6DoF(위치+회전) 포즈 도입.</summary>
        public const byte ProtocolVersion = 3;
    }
}
