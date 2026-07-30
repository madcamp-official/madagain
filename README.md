# madagain — MINDHEXER

> 몰입캠프 26s-w4-c3-02 팀 프로젝트

**MINDHEXER** 는 전용 VR 기기 없이 **스마트폰 2대**로 즐기는 **2폰 P2P VR 침투 해킹 퍼즐** 게임입니다.
한 대(**Galaxy S24+**)는 **카드보드 간이 헤드셋**으로 착용해 VR 화면을 보고, 다른 한 대(**Galaxy S10e**)는
손에 쥐는 **원격 컨트롤러**(조이스틱 이동 + 스와이프 해킹 패드)가 됩니다. 두 폰은 로컬 Wi‑Fi(또는 핫스팟)로
직접 통신합니다.

전체 기술 명세: [docs/SYB/SPEC.md](docs/SYB/SPEC.md) · 게임 설계 정본: [docs/KJH/design/기초_설계안.md](docs/KJH/design/기초_설계안.md)

<<<<<<< Updated upstream
전체 기술 명세는 [docs/SPEC.md](docs/SPEC.md) 참고.
=======
---

## 개발 목적

- **저비용 VR의 가능성 실험** — Quest 같은 전용 HMD 없이, 흔한 안드로이드 폰 2대 + 카드보드로
  "머리에 쓰는 화면 + 손에 든 컨트롤러" 구성을 만들어 VR 경험을 구현한다.
- **폰↔폰 저지연 입력 스트리밍** — 컨트롤러(S10e)의 터치·6DoF 포즈·조이스틱을 헤드셋(S24+)으로
  실시간 전송한다. 연속 상태값은 **UDP**, 반드시 도달해야 하는 확정 이벤트는 **WebSocket**으로 이원화.
- **그 위에 얹는 게임** — 도시에 잠입해 **해킹 퍼즐**로 기믹(유압프레스·터렛·CCTV)을 조종하며 나아가는
  침투 퍼즐. (초기 구상의 은신·드론은 제외하고 퍼즐·조종에 집중)

## 게임 개요

- **조작** — S10e 화면을 가로로 쥐고, 왼쪽 = 플로팅 조이스틱(이동), 오른쪽 = 스와이프 해킹 패드
  (안드로이드 잠금패턴식 점 잇기). 6DoF 포즈로 오브젝트를 직접 조종.
- **해킹** — 점 패턴을 그려 대상 기믹을 활성화/무력화. 판정은 헤드셋 측 `HackGrid`가 담당.
- **레벨/기믹** — `MINDHEXER/Assets/TallCity` 등 도시 레벨, 터렛·CCTV·경비병·유압프레스 등의 기믹.

자세한 조작·해킹 문법·기믹 설계는 [docs/KJH/](docs/KJH/) (게임 설계 담당 공간)를 참고.

## 참여 인원

| 이름 | 역할 | GitHub |
| --- | --- | --- |
| 김재훈 | 게임 로직·설계·연출 (헤드셋/게임플레이) | |
| 서영빈 | 통신·프로토콜·컨트롤러·퍼즐 구상 | |

> 역할은 크게 **게임(담당 A, [docs/KJH/](docs/KJH/))** 과 **통신(담당 B, [docs/SYB/](docs/SYB/))** 으로 나뉩니다.

---

## 시스템 구성

```
[Galaxy S10e : 컨트롤러]  ==(로컬 Wi-Fi / 모바일 핫스팟)==>  [Galaxy S24+ : 카드보드 VR 본체]
  · 조이스틱/터치/6DoF 포즈 스트리밍 (UDP)                    · UDP 수신 → 게임 입력
  · 스와이프 해킹 패턴 전송 (WebSocket)                        · WebSocket 서버 내장(외부 DLL 없음)
  · 페어링 요청 (WebSocket)                                    · 카드보드 스테레오 렌더링 + 헤드트래킹
```

- **서버 없는 P2P** — S24+가 WebSocket 서버를 겸한다. 별도 인프라 불필요.
- **공유 프로토콜** — 패킷 구조체/직렬화/시퀀스 검증을 `shared/com.mindhexer.shared` 한 곳에만 정의하고
  양쪽 Unity 프로젝트가 임베드한다 → 필드 불일치 원천 차단.

아키텍처·와이어 포맷·페어링 절차: [docs/SYB/ARCHITECTURE.md](docs/SYB/ARCHITECTURE.md) · [docs/SYB/NETWORK_PROTOCOL.md](docs/SYB/NETWORK_PROTOCOL.md)
>>>>>>> Stashed changes

## 레포 구성

```
madagain/
<<<<<<< Updated upstream
├── docs/                         # 명세 · 아키텍처 · 프로토콜 · 셋업 · 테스트 체크리스트
=======
├── MINDHEXER/            # 🎮 S24+ 카드보드 VR 게임 본체 (Unity 6000.5.4f1)
│                         #    Cardboard XR 렌더링, 레벨(TallCity), 적/기믹, 해킹, 오브젝트 조종
├── controller-s10e/      # 📱 S10e 원격 컨트롤러 앱 (조이스틱 + 스와이프 해킹 패드 + 센서 스트리밍)
├── headset-s24/          #    S24+ 네트워킹 초기 스캐폴드/프로토타입 (게임 본체는 MINDHEXER)
>>>>>>> Stashed changes
├── shared/
│   └── com.mindhexer.shared/   # 두 앱이 임베드하는 공유 UPM 패키지
│                               # (InputPacket, 직렬화, 시퀀스 검증, UDP/WebSocket/RTT/디스커버리, 페어링)
├── docs/
│   ├── SYB/              # 통신·명세·아키텍처·셋업·테스트 (담당 B)
│   ├── KJH/              # 게임 설계·이식 환경·결정 기록·세션 로그 (담당 A)
│   └── shared/           # 둘이 합의한 정본(SSOT)만 승격
└── tools/                # 보조·검증 도구 (pc-receiver 콘솔 수신기, pos_sniffer.py, 동기화 스크립트)
```

> **공유 코드 전략**: `shared/com.mindhexer.shared`는 Unity 로컬 임베드 패키지다. 각 Unity 프로젝트의
> `Packages/manifest.json`에서 `file:` 경로로 참조하며, 패킷 구조체가 한 곳에서만 정의돼 양쪽에서 동일하게 (역)직렬화된다.

---

<<<<<<< Updated upstream
1. **Unity 2022.3 LTS**(Android Build Support 모듈 포함) 설치. 두 프로젝트 모두 동일 버전 권장.
2. Cardboard XR Plugin, WebSocketSharp(DLL) 등 **UPM 밖에서 받아야 하는 의존성**은
   [docs/SETUP.md](docs/SETUP.md)의 안내를 따를 것.
3. 두 폰을 같은 로컬망(또는 S24+ 핫스팟)에 두고 빌드/실행. 연결 절차는 [docs/NETWORK_PROTOCOL.md](docs/NETWORK_PROTOCOL.md) 참고.

## 개발 규약

- `main`은 항상 빌드 가능 상태 유지. 기능 작업은 `feat/*`, 버그는 `fix/*`.
- 패킷 구조체·프로토콜 상수를 바꾸면 **반드시 `shared` 패키지에서** 바꾼다(양쪽 자동 반영).
- 2인/7일 작업 분배는 [docs/SPEC.md](docs/SPEC.md) 6절 참고. **1일차 UDP Ping-Pong 검증이 최우선 마감 항목.**
=======
## 배포 방법

두 개의 안드로이드 앱을 각 폰에 빌드해 설치한다. (엔진: **Unity 6000.5.4f1**, Android Build Support 포함)

### 1) 헤드셋 앱 (S24+)
- `MINDHEXER/` 프로젝트를 Unity 6000.5.4f1로 열어 **Android로 빌드**해 S24+에 설치.
- 수동 의존성(Google Cardboard XR Plugin 등)은 [docs/SYB/SETUP.md](docs/SYB/SETUP.md) 참고.
- 실행하면 WebSocket 서버 + UDP 수신 + 디스커버리가 기동한다(외부 WebSocket DLL 불필요 — 내장 `TcpWebSocketServer`).

### 2) 컨트롤러 앱 (S10e)
- `controller-s10e/` 프로젝트를 열어 **Android로 빌드**해 S10e에 설치. (USB 디버깅으로 설치, 유심 불필요)
- `Player → Active Input Handling = Both` 설정 확인.

### 3) 두 폰 연결
1. 두 폰을 **같은 로컬 Wi‑Fi**에 두거나, PC/폰의 **모바일 핫스팟**에 함께 붙인다.
   (캠퍼스·게스트 Wi‑Fi는 단말 격리로 막힐 수 있으니 핫스팟 권장)
2. 컨트롤러가 디스커버리로 헤드셋을 자동 발견 → 페어링. 실패 시 컨트롤러 HUD에서 **IP 직접 입력**.
3. 페어링되면 조이스틱/6DoF가 **UDP**로, 해킹 패턴이 **WebSocket**으로 흐른다.

연결 절차·포트·방화벽 상세: [docs/SYB/NETWORK_PROTOCOL.md](docs/SYB/NETWORK_PROTOCOL.md) · 테스트 체크리스트: [docs/SYB/TEST_CHECKLIST.md](docs/SYB/TEST_CHECKLIST.md)

### 실기기 전 PC 검증 (선택)
실제 폰↔폰 연결 전에, PC가 S24+ 역할을 대신해 컨트롤러 스트림을 수신·측정할 수 있다
(`tools/pc-receiver` 콘솔 수신기, `tools/pos_sniffer.py` 등). 개요는 [tools/README.md](tools/README.md) 참고.

---

## 시연 자료

_(작성 예정 — 데모 영상, 스크린샷, 실기기 플레이 GIF, 발표 슬라이드 링크 등을 여기에 추가)_

---

## 문서

| 위치 | 내용 |
| --- | --- |
| [docs/SYB/](docs/SYB/) | [SPEC](docs/SYB/SPEC.md) · [ARCHITECTURE](docs/SYB/ARCHITECTURE.md) · [NETWORK_PROTOCOL](docs/SYB/NETWORK_PROTOCOL.md) · [SETUP](docs/SYB/SETUP.md) · [TEST_CHECKLIST](docs/SYB/TEST_CHECKLIST.md) |
| [docs/KJH/](docs/KJH/) | 게임 기초 설계, 이식 환경, 결정 기록(decisions/), 세션 로그 |
| [docs/shared/](docs/shared/) | 둘이 합의한 정본(SSOT)만 승격되는 팀 합의 문서 |

## 개발 규약

- 기능 작업은 `feat/*`, 버그는 `fix/*` 브랜치. (현재 활성 예: `feat/level-SYB`)
- **패킷 구조체·프로토콜 상수를 바꾸면 반드시 `shared/com.mindhexer.shared`에서** 바꾼다(양쪽 자동 반영).
- 개인 문서는 `docs/<이름>/`에 자유롭게, **둘이 합의한 것만** [docs/shared/](docs/shared/)로 승격한다.
>>>>>>> Stashed changes
