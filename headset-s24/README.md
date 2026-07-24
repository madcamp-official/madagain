# headset-s24 — Galaxy S24+ 카드보드 VR 본체

카드보드 스테레오 렌더링 + 게임 로직 + 네트워크 서버(UDP 수신 / WebSocket 서버)를 담당하는 Unity 프로젝트.

## 담당 (SPEC 6, 담당자 A)

- Cardboard XR 스테레오 렌더 · 헤드트래킹(자체 자이로)
- UDP `InputPacket` 수신 → 시퀀스 검증 → Lerp/Slerp 보간 → 게임 입력
- WebSocket **서버**(WebSocketSharp) 내장 + 확정 이벤트 송수신
- 3x3 해킹 그리드/패턴 판정, 적 AI(드론/터렛/CCTV, Unity Behavior), 레벨(ProBuilder), 암살 기믹

## 폴더

```
Assets/MindHexer/
├── Scripts/
│   ├── Net/       UdpReceiver, WebSocketServerHost, MainThreadDispatcher
│   ├── Input/     InputBridge (패킷 → 게임 입력)
│   └── Gameplay/  HackGrid (3x3 패턴 판정) 등
├── Scenes/        Main.unity (TODO)
├── Prefabs/
└── Plugins/       WebSocketSharp.dll 배치 (docs/SETUP.md)
```

## 열기 전에

- Unity 2022.3 LTS (`ProjectSettings/ProjectVersion.txt` 참고).
- 수동 의존성(Cardboard, WebSocketSharp, DOTween): [../docs/SETUP.md](../docs/SETUP.md) 3절.
- 공유 프로토콜은 `com.mindhexer.shared` 패키지에서 온다 — 여기서 재정의하지 말 것.
