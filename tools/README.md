# tools

보조 스크립트/메모 모음.

## 공유 패키지 참조 확인

두 Unity 프로젝트는 임베드 패키지 `com.mindhexer.shared`를 `file:` 상대경로로 참조한다.
경로가 깨지면(폴더 이동 등) 두 `Packages/manifest.json`의 아래 항목을 확인:

```json
"com.mindhexer.shared": "file:../../shared/com.mindhexer.shared"
```

## RTT 측정 메모 (SPEC 5.4)

- S10e에서 InputPacket 송신 시각(TimestampMs)을 S24+가 WebSocket으로 에코 → 왕복 시간 측정.
- 목표 50ms 이내. 초과 시 핫스팟/채널 간섭/패킷 크기 점검.

> 필요 시 여기에 빌드/배포 자동화 스크립트(adb install 등)를 추가.
