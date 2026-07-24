# 네트워크 프로토콜

## 채널 요약

| 채널 | 전송 | 방향 | 빈도 | 페이로드 |
| --- | --- | --- | --- | --- |
| Input 스트림 | **UDP** | S10e → S24+ | 초당 수십~수백 | `InputPacket` (터치/자이로/가속도) |
| Discovery | **UDP 브로드캐스트** | S24+ → 서브넷 | 앱 기동 시 주기적 | `DiscoveryBeacon` (서버 IP/포트) |
| 확정 이벤트 | **WebSocket** | 양방향 | 저빈도 | `EventMessage` (JSON) |

포트 등 상수는 `shared/com.mindhexer.shared/Runtime/Protocol/NetworkConstants.cs` 참조.

## UDP `InputPacket` 와이어 포맷 (리틀 엔디언, 고정 길이) — v2 / 6DoF

**프로토콜 버전 2**부터 컨트롤러가 3DoF(회전)에서 **6DoF(위치+회전) 포즈**로 확장 → `Position` 필드 추가, 패킷 크기 60 → **72바이트**. 값은 `NetworkConstants.InputPacketSize` / `ProtocolVersion`.

| 오프셋 | 필드 | 타입 | 바이트 |
| --- | --- | --- | --- |
| 0 | Magic (`MHX1`) | uint32 | 4 |
| 4 | Sequence | uint32 | 4 |
| 8 | Timestamp (ms, 송신측 기준) | int64 | 8 |
| 16 | TouchId | int32 | 4 |
| 20 | TouchPhase (0=None,1=Down,2=Move,3=Up) | uint8 | 1 |
| 21 | (padding) | — | 3 |
| 24 | NormX, NormY (터치 정규화) | float×2 | 8 |
| 32 | Position (x,y,z, meter) | float×3 | 12 |
| 44 | Rotation (x,y,z,w, 쿼터니언) | float×4 | 16 |
| 60 | Accel (x,y,z) | float×3 | 12 |
| — | **총** | | **72** |

- **Magic**: 잘못된/타 앱 패킷 방어. 불일치 시 폐기.
- **Sequence**: 단조 증가. 수신측은 지금까지 본 최대 시퀀스보다 작거나 같으면 폐기(역전/중복 방지).
- **Timestamp**: 지터 계산·보간 기준. 두 폰 시계가 안 맞을 수 있으므로 **절대시각 비교 금지**, 상대 간격만 사용.
- **Position**: 컨트롤러 로컬 원점 기준 6DoF 위치. 산출원(ARCore VIO 등)은 컨트롤러 앱 책임. 트래커 미가동 시 0(3DoF 폴백).
- **Rotation**: 6DoF 디바이스 자세. 헤드트래킹이 아니라 **컨트롤러 조준/동적 해킹 입력**용(SPEC 5.5).

> **버전 불일치**: v1(60B) ↔ v2(72B)는 길이가 달라 상호 역직렬화가 실패(폐기)한다. 페어링 시 `ProtocolVersion`을 교환해 조기 거부할 것.

## 보간 규칙 (수신측, SPEC 2.1)

- 터치 정규화 좌표 / 6DoF 위치: 프레임 간 **Lerp** (`Vector2.Lerp` / `Vector3.Lerp`).
- 6DoF 회전 쿼터니언: 프레임 간 **Slerp**.
- 목표: 패킷 유실로 인한 끊김 완화. RTT 목표 50ms 이내.

## RTT 측정 (UDP Ping/Pong)

입력 스트림과 분리된 경량 채널(`NetworkConstants.UdpRttPort`).

- S10e(`RttProbe`) → S24+(`RttResponder`)로 `RttPacket{magic, nonce, originTimestamp}` 송신.
- S24+는 받은 바이트를 **그대로 에코**. 시계 동기화 불필요(originTimestamp가 S10e 자신의 것).
- S10e가 에코 수신 시 `RTT = now - originTimestamp` 계산. 이동평균으로 SPEC 5.4(≤50ms) 판정.

`RttPacket` 와이어(리틀 엔디언, 16바이트): Magic(`MHXP`) uint32 | Nonce uint32 | OriginTimestamp int64.

## WebSocket `EventMessage` (JSON)

```json
{ "type": "PairRequest | PairAck | PatternResult | BatteryWarning | Disconnect", "payload": { ... } }
```

- `PairRequest` / `PairAck`: 페어링 수립/해제.
- `PatternResult`: `{ "success": bool, "patternId": int }` — 해킹 패턴 판정 결과(S24+ → S10e).
- `BatteryWarning`: `{ "level": float }`.

## 연결 수립 순서 (SPEC 2.3)

1. S24+ : WebSocket 서버 기동 + `DiscoveryBeacon` UDP 브로드캐스트 시작.
2. S10e : 비콘 수신 → 서버 IP 획득 → WebSocket 연결 → 성공 시 UDP `InputPacket` 스트리밍 시작.
3. **폴백**: 비콘 미수신 시 S10e에서 **IP 직접 입력 UI**로 연결.
4. 공유 Wi-Fi 없으면 S24+ **모바일 핫스팟**에 S10e 연결(코드 변경 없음, 같은 서브넷 전제).

## 예외 처리 (SPEC 5)

- UDP 1초 이상 미수신 → S24+ UI 경고 + WebSocket 재연결 시도.
- 시퀀스 역전 패킷 폐기.
- 재연결 로직은 지수 백오프 권장.
