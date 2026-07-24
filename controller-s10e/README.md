# controller-s10e — Galaxy S10e 원격 컨트롤러

터치/자이로/가속도를 캡처해 S24+로 스트리밍하는 경량 Unity 프로젝트. 그래픽 리소스 최소.

## 담당 (SPEC 6, 담당자 B)

- Input System으로 멀티터치/스와이프 캡처, `Input.gyro`로 자이로/가속도 획득
- UDP `InputPacket` **송신**
- WebSocket **클라이언트**(NativeWebSocket)로 S24+ 서버에 접속, 확정 이벤트 수신
- Discovery 비콘 수신 → 자동 페어링, 실패 시 IP 직접 입력 폴백

## 폴더

```
Assets/MindHexer/
├── Scripts/
│   ├── ControllerBootstrap.cs   런타임 자동 조립(빈 씬에서도 부팅)
│   ├── Net/    UdpSender, WsClient(NativeWebSocket), RttProbeBehaviour,
│   │           DiscoveryListenerBehaviour, PairingFlow(재연결 백오프 포함)
│   ├── Input/  TouchGyroCapture (6DoF 포즈 + 터치 → InputPacket)
│   └── UI/     ControllerHud (IMGUI: 3x3 그리드 오버레이 + 연결상태 + IP 직접입력)
├── Scenes/     비어 있어도 됨 — Bootstrap이 런타임에 컨트롤러를 조립
└── Prefabs/
```

## 씬 구성

`ControllerBootstrap`이 `[RuntimeInitializeOnLoadMethod]`로 부팅 시 필요한 컴포넌트를
하나의 GameObject에 자동 조립한다. **씬을 손으로 만들 필요 없음** — 빈 씬을 빌드에 넣고 실행하면
HUD와 연결 흐름이 바로 동작한다. (프로덕션에서는 uGUI 씬으로 교체 가능)

연결: 디스커버리 비콘 자동 발견 → 페어링 → 스트리밍/RTT 시작. 브로드캐스트가 막히면
HUD의 **IP 직접 입력 → 연결**(폴백). 연결이 끊기면 지수 백오프로 자동 재연결(SPEC 5.1).

## 열기 전에

- Unity 2022.3 LTS (`ProjectSettings/ProjectVersion.txt` 참고).
- NativeWebSocket은 UPM git URL로 자동 복원(`Packages/manifest.json`).
- 공유 프로토콜은 `com.mindhexer.shared` 패키지에서 온다 — 여기서 재정의하지 말 것.
