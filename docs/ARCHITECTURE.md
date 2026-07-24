# 아키텍처 개요

두 개의 Unity Android 앱 + 하나의 공유 코드 패키지로 구성된 P2P 시스템.

```
┌──────────────────────────┐         ┌──────────────────────────────┐
│  controller-s10e         │         │  headset-s24                 │
│  (Galaxy S10e)           │         │  (Galaxy S24+, 카드보드)       │
│                          │         │                              │
│  Input System (터치)      │  UDP →  │  UdpReceiver                 │
│  Input.gyro (자이로)      │ 좌표/센서 │   → SequenceValidator        │
│  UdpSender               │         │   → Lerp/Slerp 보간          │
│  WsClient(NativeWebSocket)│ ← WS →  │   → 게임 입력 이벤트           │
│                          │ 확정이벤트│  WsServer(WebSocketSharp)    │
│                          │         │  Cardboard XR (스테레오/HMD)  │
└──────────────────────────┘         └──────────────────────────────┘
             │                                      │
             └──────────── shared/com.mindhexer.shared ─┘
                 InputPacket / PacketSerializer /
                 SequenceValidator / NetworkConstants /
                 EventMessage
```

## 레이어

| 레이어 | 위치 | 책임 |
| --- | --- | --- |
| **Shared Protocol** | `shared/com.mindhexer.shared` | 패킷 구조체 정의, 바이트 (역)직렬화, 시퀀스 검증, 상수(포트/매직/버전) |
| **Transport (Controller)** | `controller-s10e/Assets/MindHexer/Scripts/Net` | UDP 송신, WebSocket 클라이언트 |
| **Transport (Headset)** | `headset-s24/Assets/MindHexer/Scripts/Net` | UDP 수신, WebSocket 서버, 메인스레드 디스패처 |
| **Input Bridge (Headset)** | `headset-s24/Assets/MindHexer/Scripts/Input` | 수신 패킷 → 보간 → 게임 입력 이벤트 |
| **Gameplay (Headset)** | `headset-s24/Assets/MindHexer/Scripts/Gameplay` | 3x3 해킹 그리드, 패턴 판정, 적 AI, 암살 기믹 |

## 핵심 원칙

1. **단일 진실 원천**: 패킷 구조체는 `shared`에만 정의. 양쪽이 동일 코드로 (역)직렬화 → 필드 불일치 원천 차단.
2. **역할 분리**: 헤드트래킹 = S24+ 자체 센서. S10e 자이로 = 해킹 보조 연출 전용. (SPEC 3.3 / 5.5)
3. **프로토콜 이원화**: 최신값 = UDP(유실 허용), 확정 이벤트 = WebSocket(도달 보장). (SPEC 2)
4. **스레드 경계**: WebSocketSharp/소켓 수신은 별도 스레드 → `MainThreadDispatcher` 큐로 Unity 메인 스레드에 전달.

관련 문서: [NETWORK_PROTOCOL.md](NETWORK_PROTOCOL.md), [SPEC.md](SPEC.md)
