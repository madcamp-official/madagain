# pc-receiver — PC에서 S10e 6DoF 수신 실측

실기기 S24+에 올리기 전에, **이 PC가 S24+ 역할을 대신**해서 S10e가 보내는 6DoF 데이터가
잘 도착하는지 실측하는 콘솔 도구. shared 패키지의 실제 프로토콜/전송 코어를 그대로 재사용하고,
외부 의존성 없는 **자체 WebSocket 서버(RFC 6455, TcpListener 기반)** 를 포함한다.

> 왜 WebSocket 서버까지 필요한가: 컨트롤러(S10e)는 **페어링(PairAck) 이후에만 UDP 스트리밍을 켠다**
> (`PairingFlow`). 따라서 UDP만 열어두면 데이터가 오지 않는다. 이 도구는 UDP 수신 + WebSocket 서버
> + 디스커버리 + RTT 응답을 모두 띄워 폰이 실제로 페어링·스트리밍하게 만든다.

## 실행

**자가검증(폰 없이, 이 PC 내부 루프)** — WebSocket 핸드셰이크/페어링/6DoF 수신을 한 번에 확인:

```bash
dotnet run -c Release -- --selftest
```

**라이브 수신(실제 S10e 대상)**:

```bash
dotnet run -c Release
```

실행하면 배너에 **이 PC의 IP와 포트, 폰 연결 방법**이 출력되고, 이후 초당 통계가 갱신된다:

```
pkts=843 rate=119.8/s loss=0 | pos=(0.11, -0.02, 1.33) rotQ=(0.01, 0.71, 0.0, 0.70) |q|=1.000 touch=Move@(0.42, 0.55)
```

- `rate` 초당 수신 패킷 수, `loss` 시퀀스 기준 근사 손실 수(0이 이상적).
- `pos` 6DoF 위치, `rotQ` 회전 쿼터니언(`|q|`≈1이면 정상 정규화), `touch` 터치 상태/정규화 좌표.

## 포트 (shared `NetworkConstants`)

| 용도 | 프로토콜/포트 | 방향 |
| --- | --- | --- |
| 6DoF InputPacket 스트림 | **UDP 45710** | S10e → PC |
| RTT Ping/Pong | **UDP 45713** | S10e ↔ PC |
| 디스커버리 비콘 | **UDP 45711** | PC → 서브넷(브로드캐스트) |
| 페어링/확정 이벤트 | **TCP 45712** (WebSocket `/mhx`) | S10e ↔ PC |

---

# S10e ↔ PC 연결 방법 (UDP/WebSocket)

## 1. 같은 로컬망에 두기 (둘 중 하나)

- **공유 Wi-Fi**: PC와 S10e를 같은 공유기/AP에 연결. (가장 간단)
- **폰 핫스팟**: S10e에서 모바일 핫스팟을 켜고 **PC를 그 핫스팟에 연결**. 이때 PC IP는 핫스팟 서브넷(보통 `192.168.x.x`)으로 바뀐다. 코드 변경 불필요.

> 카페/학교 등 **게스트 Wi-Fi는 단말 간 통신(client isolation)이 막혀** UDP/WS가 안 통할 수 있다.
> 이 경우 폰 핫스팟 방식을 쓸 것. (SPEC 5.3)

## 2. PC 방화벽 열기 (인바운드 허용)

Windows 방화벽이 인바운드 UDP/TCP를 막으면 데이터가 안 온다. **관리자 PowerShell**에서 1회:

```powershell
New-NetFirewallRule -DisplayName "MindHexer UDP" -Direction Inbound -Protocol UDP -LocalPort 45710,45711,45713 -Action Allow
New-NetFirewallRule -DisplayName "MindHexer WS"  -Direction Inbound -Protocol TCP -LocalPort 45712 -Action Allow
```

(테스트 후 제거: `Remove-NetFirewallRule -DisplayName "MindHexer UDP","MindHexer WS"`)

## 3. 폰에서 PC로 연결 (둘 중 하나)

라이브 실행 배너에 표시된 **PC IP**를 확인한 뒤:

- **자동(디스커버리)**: 이 도구가 UDP 브로드캐스트로 자신을 알린다. S10e 앱을 실행하면
  `DiscoveryListener`가 이를 받아 자동으로 WebSocket 접속 → 페어링. (같은 서브넷 전제)
- **수동(폴백)**: 브로드캐스트가 막힌 환경이면, S10e HUD의 **IP 입력란에 PC IP를 입력하고 [연결]**.
  (`PairingFlow.ConnectManually`)

## 4. 연결 후 흐름

1. S10e가 `ws://<PC IP>:45712/mhx`로 접속 → `PairRequest{protocolVersion=2}` 전송.
2. PC가 버전 확인 후 `PairAck` 응답 → **폰이 UDP 6DoF 스트리밍 시작**.
3. 콘솔에 `pkts`/`rate`/`pos`/`rotQ`/`touch`가 실시간 표시.
4. RTT는 폰의 `RttProbe`가 자동 측정(폰 HUD에 표시). 이 도구는 UDP 45713에서 에코만 한다.

## 문제 해결

| 증상 | 점검 |
| --- | --- |
| `paired=0`에서 안 넘어감 | 같은 서브넷인지, TCP 45712 방화벽, PC IP 정확한지 |
| 페어링은 되는데 `pkts=0` | UDP 45710 방화벽(인바운드), client isolation, 폰이 PC IP로 UDP 보내는지 |
| `loss`가 큼 | Wi-Fi 혼잡/거리. 핫스팟 근접 테스트, 채널 변경 |
| `|q|`가 1에서 크게 벗어남 | 폰 자이로/포즈 소스 확인(정규화 안 된 회전값) |
| 버전 불일치로 거부 | 폰/PC 모두 `ProtocolVersion=2`(6DoF)인지 |

## 참고

- 이 도구는 **측정/브링업 전용**이다. UnityEngine 타입은 `UnityShim.cs`가 최소 대체하며, 검증 대상
  로직(직렬화·페어링·수신)은 전부 shared 실코드다.
- 동일 검증은 CI/에디터에서도 돈다: shared `Tests/`(EditMode·PlayMode).
