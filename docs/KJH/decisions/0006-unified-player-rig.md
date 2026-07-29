# ADR-0006 — 통합 플레이어 리그 (몸과 시점의 분리)

상태: 구현 완료, 실기 검증 대기(이동·충돌 미확인)
날짜: 2026-07-29
관련: ADR-0005(VR 컨트롤러 입력 매핑)

---

## 문제

PC 경로는 **카메라 하나에 몸·시점·연출을 전부** 얹은 구조였다. `FirstPersonPlayer`가 카메라
GameObject에 붙어 CharacterController·중력·마우스 시점·MotionFeel 롤을 동시에 소유했다.

VR은 이 구조와 근본적으로 맞지 않는다 — **시점을 머리가 소유**하기 때문이다. 그래서 별도로
`SetupVrRig()`를 두고 `[XR Rig]` + `FreeLookController`로 갈랐는데, 그 경로에는 **플레이어 몸이
아예 없었다**. 실기에서 드러난 증상:

| 증상 | 원인 |
|---|---|
| 벽·바닥을 통과 | CharacterController·중력이 없음. 리그 트랜스폼 좌표를 직접 더하고 있었다 |
| 컨트롤러 이동이 전혀 안 먹음 | `ControllerDriver`가 `FirstPersonPlayer`를 못 찾으면 `Update`를 통째로 return |
| 땅이 꺼져 보임 | `TrackedPoseDriver`가 `RotationAndPosition`이라 Cardboard(3DoF)의 거의-0 위치가 카메라 로컬을 덮어써 눈높이 1.6m가 지워짐 |
| 스플래시는 회전하는데 게임은 안 돎 | 신형 XR SDK는 머리 포즈를 카메라에 **자동 적용하지 않는다**. 트래킹은 살아 있고 받는 쪽이 없었다 |

눈높이만 고치는 안(RotationOnly로 바꾸기 / 중간 노드 추가)을 냈다가 기각됐다 — **몸이 없다는
근본 문제를 안 건드리는 대증요법**이었기 때문이다.

---

## 결정

PC·VR이 **같은 리그**를 쓴다. 차이는 카메라에 붙는 시점 드라이버 하나뿐.

```
[PlayerBody]   ← CharacterController + FirstPersonPlayer(이동·중력·밀림감지·EdgeStop)
   │             + AutoTraversal + MantleRig
   │             ★ 회전하지 않는다. 위치만 소유.
   └ Main Camera ← 시점 회전의 유일한 소유자
                    PC: MouseLook (신규 — FirstPersonPlayer에서 적출)
                    VR: TrackedPoseDriver (RotationOnly)
                    + MotionFeel(연출), HackDriver(조준=카메라 정면)
```

### 원칙 셋

1. **몸은 회전하지 않는다.** CC 캡슐은 어차피 항상 수직이라 잃는 것이 없다.
2. **시점은 카메라만 소유한다.** PC는 마우스가, VR은 머리가 채운다.
3. **이동 방향은 시점을 읽기만 한다.** 조이스틱 앞 = 지금 보는 방향. 결합이 아니라 참조다 —
   시점이 몸을 돌리거나 이동이 시점을 돌리는 일은 없다.

### 몸 원점 = 눈높이

기존 이동·등반 코드 전부가 "트랜스폼 위치 = 눈"을 전제하므로 그 규약을 유지했다
(발 위치는 CC의 `center = -0.7`이 만든다). 카메라 로컬은 0이고, VR에서 TPD가 `RotationOnly`라
위치를 안 건드려 눈높이가 꺼지지 않는다.

### "보는 쪽이 앞" — 검토했다 기각한 대안

한때 "옆을 보고 앞으로 밀면 옆으로 가는 게 맞나"라는 의문에서 **컨트롤러 yaw를 몸 정면으로
삼는 안**(controller-relative locomotion)을 냈다. 실제 VR 게임에도 있는 방식이고, S10e 자세를
이미 받고 있어 추가 데이터도 필요 없었다.

기각 — **대부분의 VR 게임 기본값이 head-relative**이고, PC의 원래 동작과도 같다. 두 방식을
"시점과 이동의 결합/분리"로 대립시킨 것은 오해였다. 고개를 돌린 채 밀면 그쪽으로 가는 것이
곧 head-relative locomotion이다.

---

## 바뀐 파일

| 파일 | 변경 |
|---|---|
| `FirstPersonPlayer` | 시점 코드 전부 적출(`_yaw`/`_pitch`/`lookSens`/`ExternalLook`/회전 대입). `view` 필드 추가 — 이동 기준을 카메라 수평 forward에서 읽는다. `FlatForward`도 시점 기준으로 |
| `MouseLook` | **신규.** 적출한 마우스 시점 + `MotionFeel.CurrentRoll` 합성. VR엔 안 붙인다(TPD와 싸운다) |
| `GameBoot` | `SetupPcRig`/`SetupVrRig` → `SetupRig` 통합. 구 `[XR Rig]`+`FreeLookController` 경로 삭제 |
| `ControllerDriver` | 리그 직접이동 패치 삭제(급조였다). 자이로 시점은 `MouseLook.ExternalLook`으로 |
| `AutoTraversal`·`MoveTuningPanel` | `MotionFeel`을 `GetComponentInChildren`으로 (연출이 자식 카메라로 이동) |
| `VrTuning` | `eyeBase` 추가 — 몸이 이미 눈높이를 갖고 있으므로 카메라 로컬엔 차이만 반영 |

## 함께 고친 것

**`Application.targetFrameRate`** — Unity는 Android에서 이 값을 지정하지 않으면 배터리 절약을
위해 **30fps로 묶는다**. 실기에서 `avg=33.4ms`, `spike`가 `avg`와 **소수점까지 동일**한 것이
증거였다(성능 부족이면 편차가 생긴다). `GameBoot`에서 60으로 명시하니 59.9fps가 됐다.
VR에서 30fps는 멀미를 유발하므로 필수다.

---

## 남은 것

- **실기 검증 안 됨** — 이동·충돌·중력·EdgeStop이 헤드셋에서 실제로 도는지 확인 필요
- `ViewEntryController:221`이 마우스+커서잠금 의존 → 안드로이드에서 터렛·CCTV 조준 불가
- VR에서 `MotionFeel` 롤을 켤지 — 머리가 소유한 회전 위에 롤을 얹으면 멀미 위험. 위치 연출만
  남기고 롤은 끄는 것을 기본으로 제안하나 미결
- `PatternUI`가 양안 렌더에서 보이는지 미확인
