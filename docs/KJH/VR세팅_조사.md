# MINDHEXER — VR 세팅 조사 (Cardboard + ARCore)

> KJH 개인 문서. 상태: **조사 결과**(웹 리서치, 출처 명시). VR 세팅 세션의 실행 근거.
> 결론 수치·API는 **실기 검증 전까지 출발점**. 불확실 항목은 명시함.

---

## 0. 최우선 결론 (먼저 읽기)

1. **Unity 버전 = Unity 6로 확정 권장.**
   - Cardboard XR Plugin은 **v1.31.0부터 Unity 2022.3 지원을 제거**하고 Unity 6.2를 추가. 2022.3를 쓰려면 **v1.30.0에 고정**해야 하고 이후 버그 수정(Vulkan 등)을 못 받음.
   - **게임 프로젝트(MINDHEXER)는 이미 Unity 6.2** → 최신 플러그인 v1.34.0을 그대로 쓸 수 있음. **Unity 6 경로가 지원·수정 양면에서 유리.**
   - 남는 과제 = **SYB 통신 프로젝트(2022.3) ↔ 게임(6.2) 버전 정합**(공유 패킷은 순수 C#이라 대체로 호환, 통합 시 협의).

2. ⚠️ **S24+ 검은 아티팩트 = 구글 공식 known issue** (플러그인 v1.33.0 릴리스 노트: *"Black artifacts might appear in VR mode on Samsung S25 and S24 devices."*). **아직 공식 수정·우회책 없음.** 우리 HMD가 정확히 S24+라 **최우선 실기 검증 리스크.**

3. **앱 2개로 분리한 설계가 정답.** ARCore(트래킹) + Cardboard(스테레오)를 **한 앱에서 동시 구동하면 카메라·XR 세션이 충돌**(다수 재현 보고). HMD 앱=Cardboard만, 컨트롤러 앱=ARCore만 → 충돌 회피.

4. **대형폰 검은 여백은 정상.** 목표는 화면을 꽉 채우는 게 아니라 좌우 뷰포트를 **렌즈 간격(IPD ~63mm)에 정렬**하는 것.

---

## 1. 하드웨어 스펙

### Galaxy S24+ (HMD, 스테레오 렌더)
| 항목 | 값 |
|---|---|
| 화면 | 6.7" QHD+ **1440×3120**, ~513 ppi(**20.2 px/mm**), 19.5:9, 120Hz LTPO |
| 물리 화면(계산값⚠) | 세로 ~**71.3 × 154.5 mm** / 가로(HMD) 154.5 × 71.3 mm / **반쪽(한 눈) 폭 ~77 mm** |
| 본체 | 158.5×75.9×7.7mm, 196g. **3.5mm 없음**, USB-C 3.2 |
| 칩셋 | SD 8 Gen 3(미국) / **Exynos 2400**(국제) |
| OS | Android 14 출시(7세대 보장). 현재 버전 기기별 상이(불확실) |
| ARCore | **공식 지원 + Depth API** |

> 물리 mm는 삼성 미공표 → PPI 역산 **계산값**. 정밀 IPD/뷰포트 정렬엔 실측 권장. IPD 63mm ≈ 1273px.

### Galaxy S10e (컨트롤러, 터치+자이로+ARCore 6DoF)
| 항목 | 값 |
|---|---|
| 화면 | 5.8" FHD+ 1080×2280, ~438ppi. 물리 ~62.7×132.3mm |
| 센서 | **가속도계+자이로**(6DoF 가능), 기압계 등 |
| 칩셋 | Exynos 9820 / SD855. **3.5mm 있음** |
| OS | 최종 **Android 12** (One UI 4.1) |
| ARCore | **공식 지원**(Exynos/Qualcomm 둘 다) + Depth API |

---

## 2. Cardboard 광학·프로파일·멀티폰

### 렌즈가 화면을 확대하는 건 정상
- 폰이 눈 5~7cm 앞이라 맨눈 초점 불가 → **볼록 렌즈가 빛을 펴서 초점 맞게 + 확대**해 시야를 채움. 확대 없으면 코앞 작은 창.

### 두 파라미터 분리 (멀티폰 대응의 근간)
- **(A) 폰 화면(크기·PPI) = SDK/OS가 자동 처리.** Android는 OS 디스플레이 메트릭에서 읽음(단일 소스 파일 미확인 — 불확실). 정확한 렌더의 두 입력 = "뷰어 프로파일 + 정확한 PPI".
- **(B) 뷰어 프로파일(QR) = 렌즈 기하만.** `screen-to-lens 거리`, `inter-lens 거리(≈IPD, 예 63.9mm)`, `tray-to-lens 수직정렬`, `왜곡계수 k1/k2`, `FOV`. **폰 화면 정보는 안 담김.**
- → **뷰어 프로파일 1개로 여러 폰 대응이 설계 의도.** 폰이 바뀌어도 같은 뷰어면 같은 QR 재사용. 폰별 조정은 SDK가 그 폰 PPI를 모를 때(iOS 신규기기) 정도.

### 대형폰(6.7") 대응
- 좌우 뷰포트 중심 간격은 **렌즈 간격(IPD ~63mm)으로 고정** — 눈 사이 거리를 못 바꿈. 화면이 넓으면 **가장자리 검은 여백이 정상.** 폰을 뷰어 **중앙 정렬**.

### 커스텀 프로파일 / 멀미
- 비규격 렌즈면 **WWGC 생성기**(wwgc.firebaseapp.com)로 프로파일 제작(렌즈 보며 인터랙티브 캘리브레이션 → device URI → QR). 프로파일 URI = `google.com/cardboard/cfg?p=<base64 protobuf>`.
- **프로파일 ↔ 실제 렌즈 불일치 = 이중상·멀미**(주로 IPD 불일치). 배럴 왜곡 사전보정(k1/k2)이 렌즈와 맞아야 "선이 직선".

---

## 3. Unity Cardboard XR Plugin — 세팅·빌드

### repo·버전
- **`googlevr/cardboard-xr-plugin`**(UPM 배포) / `googlevr/cardboard`(SDK·이슈트래커). 최신 **v1.34.0**(2026-06), 활발히 유지.
- **v1.30.0 = 2022.3 지원 마지막** / **v1.31.0+ = Unity 6.2 추가·2022.3 제거·GLES2 제거**.

### 설치
- UPM git URL: `https://github.com/googlevr/cardboard-xr-plugin.git`(버전 고정 `#v1.34.0`). Unity 6000.0.23f1+.
- **XR Plug-in Management** 설치 → Android 탭 **Cardboard XR Plugin** 체크.
- **HelloCardboard 샘플**(Package Manager → Samples)로 즉시 검증.

### device params API (`using Google.XR.Cardboard;`)
- `Api.HasDeviceParams()` / `Api.ScanDeviceParams()`(QR 스캔) / `Api.SaveDeviceParams(uri)`(커스텀 주입).
- ⚠️ `SaveDeviceParams`는 **리다이렉트 1회 있는 URL에서만 동작**하는 버그(#323). 런타임 VR on/off = XR Management `StartSubsystems/StopSubsystems`(HelloCardboard `VrModeController.cs`).

### 빌드 세팅 (Android)
- Orientation: **Landscape Left/Right 고정**, Optimized Frame Pacing off.
- Graphics API: **OpenGLES3 또는 Vulkan**(GLES2 제거됨). **min API 26**, target 35.
- **IL2CPP + ARM64**, Internet=**Require**, Input System(New), Application Entry Point=**Activity**.
- gradle: appcompat 1.6.1 / play-services-vision 20.1.3 / protobuf-javalite 3.19.4 / material 1.12.0, `useAndroidX=true`·`enableJetifier=true`. **카메라 권한**(QR).

### 검은 화면 이슈 (S24+ 리스크 대응)
- **S24/S25 검은 아티팩트 = 공식 known issue**(수정 없음) → 실기 확인 필수.
- 우회 후보: **OpenGLES3 우선**(→ 문제 시 Vulkan 교차), URP에서 **Depth Texture off + MSAA 조합 회피**(#518), 플러그인 **1.29.0 ↔ 최신 교차**(1.31.0 회귀 사례). 2022.3+특정 패치에서 헤드트래킹 정지 사례(#506)도 있어 6 경로가 안전.

---

## 4. ARCore 컨트롤러 + 공존

### 2앱 분리 = 정답 (공존 충돌 확인됨)
- 한 앱에서 ARCore+Cardboard 동시 = 스테레오 실패/카메라 black(googlevr/cardboard #256, Unity Discussions). built-in "ARCore Supported" ↔ ARCore XR Plugin 빌드 충돌(#474).
- MINDHEXER는 앱이 나뉘어 각자 XR 세션 하나만 → **충돌 없음.**

### 컨트롤러 앱 (S10e, 6DoF만)
- **AR Foundation + ARCore XR Plugin** 만으로 충분(ARCore Extensions=Cloud Anchor/Geospatial용, 불필요).
- `XR Origin` 하위 카메라의 **`TrackedPoseDriver`** 가 포즈를 Transform에 반영 → 매 프레임 world pos/rot 읽어 전송.
- ⚠️ 포즈는 **세션 시작점 기준 상대 좌표** → HMD와 **좌표계 정합(캘리브레이션)** 별도 필요(설계안 §4.2 좌표계 분리).
- 한계(참고): head-motion 지연, 저조도·무특징·강광 저하, **발열 급상승**, 트래킹 소실 시 점프/드리프트. 선례: `ARCoreInsideOutTrackingGearVr`.

---

## 5. 다기종·성능 대응

- **화면 크기/종횡비는 SDK가 흡수**(뷰포트 수동계산 불필요). 렌즈/IPD는 **뷰어 프로파일**로 흡수.
- **성능 티어 2단**: Low(기본, URP Render Scale 0.8~0.9) / High(최신폰 1.0, MSAA·그림자 조심 상향). 기기 스펙/초기 프레임 측정으로 분기.
- **동적 해상도**: 발열 시 render scale 자동 하향 → 프레임 유지(멀미 방지 핵심).
- **스테레오 = 2뷰 렌더(GPU 2배)** → 최적화 부족 시 프레임 하락·발열. 드로우콜/오버드로 최소화, 목표 fps 초기 확정(실측).
- ⚠️ 런타임 **IPD 슬라이더 API** 제공 여부 불확실 — 프로파일 단위 관리가 표준, 커스텀 IPD UI는 자체 구현 가능성.

---

## 6. 실기 검증 순서 (체크리스트)

1. [ ] **빈 Unity 6 프로젝트** → Android 빌드 설정(Landscape·GLES3·API26·IL2CPP·ARM64) 통과 확인.
2. [ ] Cardboard XR Plugin(UPM git) + XR Management(Android) + **HelloCardboard 샘플** 빌드.
3. [ ] **S24+ 실기**: 스테레오 좌우 분할·왜곡 보정·헤드트래킹 육안 확인. **검은 아티팩트 발생 여부**(known issue) 확인 → 발생 시 §3 우회 후보 교차.
4. [ ] **device profile**: 실제 뷰어 QR 스캔(없으면 WWGC로 생성). 이중상=IPD/왜곡 재조정. 검은 여백은 정상.
5. [ ] 별도로 **컨트롤러 최소 앱**: ARCore 6DoF 포즈 로그 → 좌표계 정합 검증.
6. [ ] 그 다음 게임 + 네트워크 연동(HMD↔컨트롤러 각각 독립 검증 후 합침).

---

## 6.5 뷰어 하드웨어 — 카드보드지 말고 플라스틱 대안

카드보드지와 **같은 원리(패시브 렌즈 + 폰 홀더, 전자장치 없음)인데 플라스틱**으로 만든 뷰어가 많다. 폰을 넣어 스테레오로 보는 방식 그대로라 **Cardboard SDK와 호환**된다. (우리 카드보드지 문제 — 대형폰 정렬·내구·고정 — 를 직접 개선.)

**MINDHEXER 요구조건**
- ① **Google Cardboard 호환**(우리가 Cardboard XR Plugin 사용) — 패시브 렌즈 + QR 프로파일 지원.
- ② **S24+(6.7") 수용**(대형폰 지원 ~6.5–6.8" 명시).
- ③ **IPD·초점 조절** — 대형폰 정렬·이중상 완화에 직접 도움.
- ④ **머리 밴드**(S24+ 196g 필수). ⑤ **통풍**(밀폐 플라스틱 = 발열↑).

**제품군(대표 — 현재 판매·정확 모델은 확인 필요)**
- **BoboVR Z4/Z5/Z6** — 플라스틱, IPD·초점 조절, 밴드, 일부 헤드폰. 튼튼한 인기 옵션.
- **DESTEK V5** — 대형폰(~6.8") 지원, 넓은 FOV, 밴드.
- **VR Shinecon / VR Box** — 저가 플라스틱, IPD·초점 조절(QR 프로파일은 자체 측정+WWGC 생성 필요할 수 있음).
- **Merge VR** — 폼/고무, 내구성(낙하 견딤), 다기종.

**주의**
- **조절식 렌즈 = QR 프로파일이 "현재 조절 상태"와 맞아야** 정렬·왜곡이 맞음 → 조절 후 WWGC로 프로파일 생성 또는 제조사 QR. (고정형이 프로파일링은 단순.)
- **피할 것**: Gear VR·Daydream류(전용 커넥터·자체 센서·전용 SW) — 우리 Cardboard 방식과 다름. **패시브 렌즈 뷰어만.**
- 오디오/전원 **케이블 인출구** 확인(청각 단서 + 발열).

---

## 7. 미확정 / 실기로 확정할 것
- S24+ 검은 아티팩트 실제 발생 여부·우회책 (공식 수정 없음)
- S24+ 물리 화면 mm(계산값) 정밀도, device profile 실측 정합
- 성능 목표 fps·발열 한계(기기별 실측)
- 런타임 IPD 조정 UI 구현 방식(SDK API 유무 불확실)
- Unity 6 게임 ↔ SYB 2022.3 통신 버전 정합 방법
- 6DoF 두 폰 좌표계 정합(캘리브레이션) 구체 절차

---

## Sources (대표)
- ARCore devices: https://developers.google.com/ar/devices
- S24+ GSMArena: https://www.gsmarena.com/samsung_galaxy_s24+-12772.php
- Cardboard viewer profile 물리 파라미터: https://support.google.com/cardboard/manufacturers/answer/6324808
- WWGC 생성기: https://github.com/googlevr/wwgc
- Cardboard XR Plugin releases(버전·S24/S25 이슈): https://github.com/googlevr/cardboard/releases
- Unity Quickstart(설치·빌드): https://developers.google.com/cardboard/develop/unity/quickstart
- 공존 충돌: https://github.com/googlevr/cardboard/issues/256 · https://github.com/Unity-Technologies/arfoundation-samples/issues/474
- AR Foundation Device tracking: https://docs.unity3d.com/Packages/com.unity.xr.arfoundation@5.1/manual/features/device-tracking.html
- 검은화면 #518: https://github.com/googlevr/cardboard/issues/518

> 관련: [기초_설계안.md](design/기초_설계안.md), [이식_환경.md](design/이식_환경.md)
