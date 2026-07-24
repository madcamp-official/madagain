# Plugins (headset-s24)

UPM으로 배포되지 않는 네이티브/DLL 의존성을 여기에 배치한다.

- **WebSocketSharp** — `websocket-sharp.dll`을 이 폴더에 넣으면 `WebSocketServerHost.cs`가 컴파일된다.
  - 출처: NuGet `WebSocketSharp` 또는 GitHub 빌드 산출물.
  - 배치 후 Unity가 자동으로 참조(플랫폼 설정에서 Android 포함 확인).

자세한 절차: [../../../docs/SETUP.md](../../../docs/SETUP.md) 3절.
