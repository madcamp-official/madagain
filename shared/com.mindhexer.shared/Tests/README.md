# Tests — com.mindhexer.shared

공유 프로토콜/전송 코어의 Unity 테스트.

| 위치 | 종류 | 내용 |
| --- | --- | --- |
| `Editor/ProtocolTests.cs` | **EditMode** (NUnit) | 직렬화 6DoF 라운드트립, 매직/길이 폐기, 시퀀스 역전·중복·wrap-around, Discovery 비콘, RttPacket |
| `Editor/EventCodecTests.cs` | **EditMode** | 이벤트 플랫 JSON 코덱 라운드트립·이스케이프·거부 |
| `Editor/PairingTests.cs` | **EditMode** | 페어링 핸드셰이크(in-memory 채널): 성공/버전불일치 거부/양방향 이벤트 |
| `Runtime/UdpLoopbackTests.cs` | **PlayMode** (`[UnityTest]`) | 실제 127.0.0.1: UDP 단조 수용·역전/중복 폐기, RTT Ping/Pong, 디스커버리 비콘 |

## 실행 방법

두 앱 프로젝트(`headset-s24`, `controller-s10e`)의 `Packages/manifest.json`에
`"testables": ["com.mindhexer.shared"]` 가 이미 추가돼 있어, 프로젝트를 열면
**Window → General → Test Runner** 에 위 테스트가 나타난다.

- EditMode 탭 → Run All: 결정론적 프로토콜 로직.
- PlayMode 탭 → Run All: 루프백 소켓 테스트(로컬에서 UDP 포트 4772x 바인딩).

> 배경: 동일 검증을 Unity 없이 순수 .NET 콘솔 하니스로도 돌린다(개발 중 `scratchpad/udpverify`).
> 여기 테스트는 그 하니스를 Unity Test Runner로 정식 이식한 것이라, 실기기 세팅 전에도
> 에디터/CI에서 회귀를 잡을 수 있다.
