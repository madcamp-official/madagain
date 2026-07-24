# madagain — MINDHEXER

몰입캠프 26s-w4-c3-02 프로젝트 repository.

카드보드 간이 VR 헤드셋(**Galaxy S24+**) + 원격 컨트롤러(**Galaxy S10e**) 2폰 P2P 구성의 VR 해킹/암살 게임 **MINDHEXER**.

- **서버 없음**: 두 폰이 로컬 Wi-Fi 또는 S24+ 모바일 핫스팟으로 직결(P2P). S24+가 WebSocket 서버 역할을 겸함.
- **통신 이원화**: 연속 상태값은 **UDP**, 반드시 도달해야 하는 확정 이벤트는 **WebSocket**.
- 두 앱 모두 **Unity Android** 빌드로 통일 → 패킷 구조체/프로토콜 코드를 공유 패키지로 공유.

전체 기술 명세는 [docs/SYB/SPEC.md](docs/SYB/SPEC.md) 참고.

## 레포 구성

```
madagain/
├── docs/                         # 개인 작업 공간(SYB/·KJH/) + 팀 합의 문서(shared/)
│   ├── SYB/                      # 통신·명세·아키텍처·셋업·테스트 (담당 B)
│   ├── KJH/                      # 게임 설계·이식 환경·결정 기록 (담당 A)
│   └── shared/                   # 셋이 합의한 것만 승격
├── shared/
│   └── com.mindhexer.shared/     # 두 Unity 프로젝트가 임베드하는 공유 UPM 패키지
│                                 # (패킷 구조체, 직렬화, 시퀀스 검증, 프로토콜 상수)
├── headset-s24/                  # Galaxy S24+ : 카드보드 VR 본체 (Unity 프로젝트)
├── controller-s10e/              # Galaxy S10e : 원격 컨트롤러 (Unity 프로젝트)
└── tools/                        # 링크 셋업 등 보조 스크립트
```

> **공유 코드 전략**: `shared/com.mindhexer.shared`는 Unity 로컬 임베드 패키지입니다.
> 두 프로젝트의 `Packages/manifest.json`에서 `file:` 경로로 참조하며, 패킷 구조체가
> 한 곳에서만 정의되어 S24+/S10e 양쪽에서 동일하게 (역)직렬화됩니다.

## 시작하기

1. **Unity 2022.3 LTS**(Android Build Support 모듈 포함) 설치. 두 프로젝트 모두 동일 버전 권장.
2. Cardboard XR Plugin, WebSocketSharp(DLL) 등 **UPM 밖에서 받아야 하는 의존성**은
   [docs/SYB/SETUP.md](docs/SYB/SETUP.md)의 안내를 따를 것.
3. 두 폰을 같은 로컬망(또는 S24+ 핫스팟)에 두고 빌드/실행. 연결 절차는 [docs/SYB/NETWORK_PROTOCOL.md](docs/SYB/NETWORK_PROTOCOL.md) 참고.

## 개발 규약

- `main`은 항상 빌드 가능 상태 유지. 기능 작업은 `feat/*`, 버그는 `fix/*`.
- 패킷 구조체·프로토콜 상수를 바꾸면 **반드시 `shared` 패키지에서** 바꾼다(양쪽 자동 반영).
- 2인/7일 작업 분배는 [docs/SYB/SPEC.md](docs/SYB/SPEC.md) 6절 참고. **1일차 UDP Ping-Pong 검증이 최우선 마감 항목.**
