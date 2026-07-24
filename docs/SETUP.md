# 개발 환경 셋업

## 0. 공통

- **Unity 6000.5.4f1 (Unity 6)** — Android Build Support + OpenJDK + Android SDK/NDK 모듈 포함.
  두 프로젝트 모두 이 버전으로 연다(`ProjectSettings/ProjectVersion.txt`에 고정됨).
- 두 폰(S24+, S10e) USB 디버깅 활성화. (유심 없는 공기계도 USB 빌드/설치는 가능)

### 0.1 Active Input Handling = Both (필수)

컨트롤러가 레거시 입력(`Input.gyro`, `Input.touch`)을 쓰므로:
**Project Settings → Player → Other Settings → Active Input Handling = Both** 로 설정.
(Input System 패키지도 함께 쓰므로 "Both"가 맞다. "Input System Package (New)" 단독이면 레거시 Input이 런타임 예외.)

## 1. 공유 패키지 참조 확인

`headset-s24`와 `controller-s10e`의 `Packages/manifest.json`에 이미 아래 항목이 들어 있음:

```json
"com.mindhexer.shared": "file:../../shared/com.mindhexer.shared"
```

두 프로젝트를 열면 Unity가 자동으로 임베드 패키지로 인식한다. 별도 작업 불필요.

## 2. UPM으로 자동 설치되는 패키지 (Unity 6 기준)

`manifest.json`에 Unity 6 기준 버전으로 선언됨 → 프로젝트 열면 자동 복원:

- URP / Shader Graph / VFX Graph **17.x**, Input System **1.11.x**, XR Interaction Toolkit **3.x**,
  ProBuilder **6.x**, Unity Behavior **1.0.x**, Test Framework **1.4.x**.

> **버전 정렬**: manifest의 SRP 계열(URP/ShaderGraph/VFX) 값은 최소 기준선이며, 에디터에 종속되어
> 6000.5.4f1로 처음 열 때 Package Manager가 에디터 버전으로 자동 정렬한다.
> 혹시 특정 패키지 버전이 "not found"로 뜨면, **Window → Package Manager**에서 해당 패키지를
> 에디터 권장 버전으로 **Update** 한 번 하면 된다. (필요 시 `Packages/packages-lock.json` 삭제 후 재열기로 재해결)

## 3. 수동 설치가 필요한 의존성 ⚠️

UPM 레지스트리에 없거나 특수 배포라 **직접 받아야 함**:

| 패키지 | 대상 프로젝트 | 설치 방법 |
| --- | --- | --- |
| **Google Cardboard XR Plugin** | headset-s24 | GitHub `googlevr/cardboard` 릴리스의 Unity 패키지를 받아 `Package Manager → Add from tarball/disk`, 또는 매니페스트에 git URL 추가. 공식 지원 종료됐으나 오픈소스로 동작. |
| **WebSocketSharp** | headset-s24 | `websocket-sharp` DLL을 `headset-s24/Assets/Plugins/`에 배치. (NuGet `WebSocketSharp` 또는 GitHub 빌드) |
| **NativeWebSocket** | controller-s10e | 매니페스트에 git URL(`https://github.com/endel/NativeWebSocket.git#upm`) 추가. (이미 주석으로 표기됨) |
| **DOTween** | headset-s24 | Unity Asset Store에서 임포트 후 `Tools → Demigiant → DOTween Utility Panel`에서 Setup 실행. |

각 프로젝트 폴더의 `Assets/Plugins/README.md`, `Packages/manifest.json` 주석도 함께 볼 것.

## 4. 빌드/실행

1. `File → Build Settings → Android`로 플랫폼 전환.
2. 각 프로젝트를 해당 폰에 빌드/디플로이.
3. S24+ 먼저 실행(서버 기동) → S10e 실행 → 자동 페어링(폴백: IP 직접 입력).
4. 검증 절차는 [TEST_CHECKLIST.md](TEST_CHECKLIST.md).

## 5. 자주 막히는 지점

- **Cardboard 렌즈 왜곡**은 에디터 시뮬레이션으로 판단 불가 → 1~2일차에 실기기 착용 확인 필수(SPEC 6).
- **UDP가 안 통하면** 방화벽/AP 격리(client isolation) 의심. S24+ 핫스팟으로 우회 테스트.
- **WebSocketSharp 콜백**은 별도 스레드 → 반드시 `MainThreadDispatcher` 경유로 Unity API 호출.
