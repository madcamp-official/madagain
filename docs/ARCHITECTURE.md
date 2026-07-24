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
| **Shared Protocol/Net** | `shared/com.mindhexer.shared` | 패킷 구조체·(역)직렬화·시퀀스 검증·상수, **Unity 비의존 전송 코어**(UDP 송수신, RTT Ping/Pong, 디스커버리, 페어링 상태머신, 이벤트 코덱) |
| **Transport (Controller)** | `controller-s10e/.../Net` | 코어 어댑터: `UdpSender`, `WsClient`(NativeWebSocket), `RttProbeBehaviour`, `DiscoveryListenerBehaviour`, `PairingFlow` |
| **Transport (Headset)** | `headset-s24/.../Net` | 코어 어댑터: `UdpReceiver`, `WebSocketServerHost`(WebSocketSharp), `RttResponderBehaviour`, `DiscoveryBroadcasterBehaviour`, `MainThreadDispatcher` |
| **Input Bridge (Headset)** | `headset-s24/.../Input` | 수신 6DoF 패킷 → 위치 Lerp/회전 Slerp 보간 → 게임 입력 |
| **Gameplay (Headset)** | `headset-s24/.../Gameplay` | 3x3 해킹 그리드, 패턴 판정, 적 AI, 암살 기믹 |

> **어댑터 패턴**: 소켓/스레드/핸드셰이크 등 **로직은 전부 shared의 순수 코어**에 있고, MonoBehaviour는 생명주기와 라이브러리 바인딩만 담당. WebSocket 라이브러리는 `IEventChannel` 뒤로 격리되어, 페어링·이벤트 로직은 라이브러리 없이 테스트된다.

## 핵심 원칙

1. **단일 진실 원천**: 패킷/이벤트/프로토콜 코드는 `shared`에만 정의. 양쪽이 동일 코드로 (역)직렬화 → 필드 불일치 원천 차단.
2. **역할 분리**: 헤드트래킹 = S24+ 자체 센서. S10e의 6DoF 포즈 = 컨트롤러 조준/해킹 입력 전용. (SPEC 3.3 / 5.5)
3. **프로토콜 이원화**: 최신값 = UDP(유실 허용), 확정 이벤트 = WebSocket(도달 보장). (SPEC 2)
4. **스레드 경계**: WebSocketSharp/소켓 수신은 별도 스레드 → `MainThreadDispatcher` 큐로 Unity 메인 스레드에 전달.
5. **검증 가능성**: 라이브러리 의존을 어댑터로 밀어내 순수 코어를 확보 → 콘솔 하니스/EditMode·PlayMode 테스트로 회귀 검증(현재 67 checks green).

관련 문서: [NETWORK_PROTOCOL.md](NETWORK_PROTOCOL.md), [SPEC.md](SPEC.md)
