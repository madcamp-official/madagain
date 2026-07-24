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
│   ├── Net/       UdpSender, WsClient
│   └── Input/     TouchGyroCapture (캡처 → InputPacket)
├── Scenes/        Main.unity — 3x3 터치 그리드 오버레이 + 연결 상태 UI (TODO)
└── Prefabs/
```

## 열기 전에

- Unity 2022.3 LTS (`ProjectSettings/ProjectVersion.txt` 참고).
- NativeWebSocket은 UPM git URL로 자동 복원(`Packages/manifest.json`).
- 공유 프로토콜은 `com.mindhexer.shared` 패키지에서 온다 — 여기서 재정의하지 말 것.
