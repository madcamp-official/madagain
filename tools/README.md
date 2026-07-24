# tools

보조 스크립트/메모 모음.

## 공유 패키지 참조 확인

두 Unity 프로젝트는 임베드 패키지 `com.mindhexer.shared`를 `file:` 상대경로로 참조한다.
경로가 깨지면(폴더 이동 등) 두 `Packages/manifest.json`의 아래 항목을 확인:

```json
"com.mindhexer.shared": "file:../../shared/com.mindhexer.shared"
```

## RTT 측정 메모 (SPEC 5.4)

- 구현: 입력 스트림과 분리된 UDP Ping/Pong 채널(`NetworkConstants.UdpRttPort`).
  - S10e `RttProbe` → S24+ `RttResponder`가 바이트 그대로 에코 → S10e가 왕복시간 계산.
  - OriginTimestamp에 S10e 자신의 Stopwatch 틱을 실으므로 두 폰 시계 동기화 불필요.
- 씬 배치: S24+에 `RttResponderBehaviour`, S10e에 `RttProbeBehaviour`.
- 목표 50ms 이내(`RttProbe.MeetsTarget`). 초과 시 핫스팟/채널 간섭/패킷 크기 점검.

> 필요 시 여기에 빌드/배포 자동화 스크립트(adb install 등)를 추가.
