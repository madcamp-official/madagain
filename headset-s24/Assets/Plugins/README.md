# Plugins (headset-s24)

UPM으로 배포되지 않는 네이티브/DLL 의존성을 여기에 배치한다.

- **WebSocketSharp — 더 이상 불필요.** S24+ WebSocket 서버는 shared `TcpWebSocketServer`
  (TcpListener 기반 RFC6455, 외부 DLL 없음)로 구동한다. DLL을 넣을 필요 없다.

현재 이 프로젝트에서 수동 DLL 배치가 필요한 항목은 없다(Cardboard/DOTween은 패키지/Asset Store 임포트).
자세한 절차: [../../../docs/SETUP.md](../../../docs/SETUP.md) 3절.
