# com.mindhexer.shared

S24+ 헤드셋과 S10e 컨트롤러가 **공유하는** 프로토콜 코드. 단일 진실 원천(single source of truth).

두 Unity 프로젝트가 `Packages/manifest.json`에서 `file:` 경로로 이 폴더를 임베드한다:

```json
"com.mindhexer.shared": "file:../../shared/com.mindhexer.shared"
```

## 내용 (`Runtime/`)

| 파일 | 역할 |
| --- | --- |
| `Protocol/NetworkConstants.cs` | 포트, 매직 넘버, 패킷 크기, 타임아웃/RTT 목표, 프로토콜 버전 |
| `Protocol/InputPacket.cs` | S10e→S24+ 입력 상태 구조체 (SPEC 4.2) |
| `Protocol/PacketSerializer.cs` | InputPacket ↔ 60바이트 리틀엔디언 (역)직렬화 + 매직 검증 |
| `Protocol/SequenceValidator.cs` | 시퀀스 역전/중복 패킷 폐기, wrap-around 처리 (SPEC 5.2) |
| `Protocol/DiscoveryBeacon.cs` | S24+ IP 브로드캐스트 비콘 빌드/파싱 (SPEC 2.3) |
| `Events/EventMessage.cs` | WebSocket 확정 이벤트 + payload 타입 (SPEC 2.2) |

## 원칙

- **패킷 필드를 바꾸면 반드시 여기서** 바꾼다 → 양쪽 자동 반영, 필드 불일치 원천 차단.
- `PacketSerializer`는 플랫폼 엔디언에 의존하지 않도록 명시적 리틀엔디언으로 구현됨.
- 자이로 필드는 **해킹 보조 연출용**. 헤드트래킹은 S24+ 자체 센서 전담(SPEC 5.5).

> 이 asmdef는 `allowUnsafeCode: true` — float 비트캐스트에 사용.
