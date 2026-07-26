# ADR-0002: PrecogPrototype 잔재 제거 (precog purge)

- 날짜: 2026-07-26
- 상태: 진행 중
- 관련: [이식_환경.md](../design/이식_환경.md) §2(버릴 것)·§4(그린 베이스라인), [기초_설계안.md](../design/기초_설계안.md)

## 목적

이식 전략이 "(B) 그린 베이스라인 + 점진 제거"였다(이식_환경 §4). PrecogPrototype `_Project`를
통째로 복사해 놓고 대체되는 순서대로 걷어내기로 했는데, 잔재가 **모든 씬에 자동부팅**되어
(RuntimeInitializeOnLoadMethod ~45개) 게임플레이 씬을 오염시킨다(예: TitleActor가 HackSandbox
렌더를 깨뜨림). 이 문서는 **무엇을 지웠고 어떻게 복구하는지**를 기록한다.

## 복구 방법 (핵심)

**모든 삭제는 git에 보존되어 있다. 언제든 되살릴 수 있다.**

- **purge 직전 baseline 커밋 = `4ac48c4`** ("checkpoint: 진행 중인 VR HUD 작업 보존").
- 개별 파일 복구:
  ```bash
  git checkout 4ac48c4 -- "<파일경로>"
  ```
- 폴더 통째 복구:
  ```bash
  git checkout 4ac48c4 -- "MINDHEXER/Assets/_Project/Prediction/"
  ```
- purge 전체 되돌리기: baseline 이후 purge 커밋들을 `git revert` 하거나, 로컬이면
  `git reset --hard 4ac48c4` (단, 이후 다른 작업이 섞였으면 revert 권장).
- **서드파티 에셋 팩**은 git에 없을 수 있음(대형이라 gitignore) → 아래 "재설치 출처"로 재임포트.

## 제거 대상 (이식_환경 §2 "버릴 것 4가지")

### 1. 예측·결정론 (Prediction)
- `_Project/Prediction/` 통째 (21파일: BeamSearch·WorldBufferPool·ActionGenerator·ThreatEvaluator·PredictionPlanner 등)
- `Sim/Core/`: `WorldHash.cs` · `DetRng.cs` · `Snapshot.cs`
- `Bridge/GraphPathfinder.cs`
- `View/Prediction/` (FollowMode·PredictionController·PredictionAudio·RhythmModes·MagnetRun·SlowAim·ClickChain·DrumRhythm·Freerun·PredictedRoute·RealRoutePreview·RoutePreviewStub·PredictionConfig)
- `Tests/EditMode/` 예측·결정론 테스트 (BeamSearch·Determinism·GraphPathfinder·Prediction·ScoreProfile·StateDeduplicator·ThreatEvaluator·CandidateReplayer·ActionGenerator·AerialTargeting)
- **KEEP(지우지 않음)**: `View/Prediction/FreezeFx.cs`, `View/Prediction/RadialInvertFeature.cs`, `Shaders/RadialInvert.shader`, `Prefabs/Resources/Fx/RadialInvert.mat` → 발광·후처리로 재활용(§1①)

### 2. 웨이브 슈터
- `View/Spawn/`: `WaveRunner.cs` · `ArenaWaves.cs` · `MapSpawnConfig.cs`
- `Editor/ArenaWavesEditor.cs`
- **KEEP**: `ArenaRoom`·`ArenaGate`·`FanSpawn` 계열(§1⑥)

### 3. 카타나 전투
- 에디터: `GhostSwordTool` · `GripSetupTool` · `HandIKToggleTool`
- 스크립트: `View/Combat/`의 `GripPreset`·`HandIK`·`KatanaClipper`·`SlashFollow`·`SlashFxDriver`·`SwordView`, `View/Debug/SlashFxPanel`
- 셰이더: `Shaders/KatanaClip.shader`
- 프리팹: `Prefabs/Resources/KatanaViewmodel`·`Weapons/SwordWeapon`·`VFX/Slash_*`(Basic·Double·Explosion·Mesh·Multi·Shiny)
- **KEEP**: `View/Combat/SwordSlash.cs`+`Shaders/SwordSlash.shader`(→해킹빔, §1①), `Editor/SlashTextureBaker.cs`(텍스처 베이커, §1②)
- 🔶 `Sim/Combat/` 판정(CombatResolve·CombatHit·CombatMath·PlayerCombat·EnemyCombatState·CombatConfig·CombatHash·PlayerCombatState): §1③ "HP·피해·사망"과 얽혀 파일별 판단 (Phase 6)

### 4. 서드파티 에셋 팩
- `Assets/Remesh Games/` (실제=Knife PRO Sci-Fi FX, 619M) — 상용 전투 FX(§2④)
- `Assets/TallCity/` (Precog 도시 환경, 1.4G)
- 🔶 `Assets/Magic Pig Games (Infinity PBR)/` (Knife FX, 27M) — §4 "유지" ↔ §2④ 상충, 참조 확인 후 결정
- Synty·Meshy·Matthew Guz → 흔적 없음(이미 없음)

### 5. 자동부팅 게이팅 (삭제 아님, 유지+가드)
삭제 후 생존하는 자동부팅 시스템(CombatAudio·CombatCamera·ScreenFx·Dismemberment·DeathScreen·
ViewmodelCamera·TitleScreen·CutsceneManager·HudCanvas·BgmPlayer 등 ~20개)에 공통 게이트
(`ShouldBoot()` = 게임/타이틀 씬 화이트리스트)를 걸어 샌드박스·빈 씬에 안 뜨게 한다.
**타이틀·컷신은 유지**되며, "제 씬에서만" 부팅.

## 재설치 출처 (서드파티 재임포트용)

| 팩 | 출처 |
|---|---|
| Surveillance Camera (CCTV, **유지**) | AK Studio Art — Unity Asset Store |
| Smart Turret Template (터렛, **유지**) | Unity Asset Store |
| Robot1F Workshop (경비병, **유지**) | Unity Asset Store |
| Remesh Games / Knife PRO FX (삭제) | Unity Asset Store (재검색 필요) |
| TallCity (삭제) | Unity Asset Store (재검색 필요) |
| Magic Pig Games / Infinity PBR (미정) | Unity Asset Store |

> 정확한 스토어 링크는 미기록. 재설치 시 이름으로 검색. (에셋은 계정 라이브러리에 남아있음.)

## ⚠️ 실행 중 발견 — 코어는 "삭제"가 아니라 "대체" 대상 (2026-07-26)

Phase 1(카타나 자산) 후, 코드 삭제를 시작하며 확인한 구조적 사실:

- **"버릴 것" 코드는 독립 4덩어리가 아니라 하나의 얽힌 클러스터**다:
  카타나 스크립트 ↔ Pose 전투(PoseCombatDriver·PosePlayer·PoseData) ↔ PredictionController ↔ 웨이브 ↔ 디버그 패널이 서로 참조.
- **결정타 — `Main.cs`(부트스트랩)가 Prediction 아키텍처 그 자체다:**
  - `prediction = new PredictionController()` (191행)
  - `Snapshot.Clone(in world)` 전역 사용 (117·125·149·280·449행)
  - `prediction.state`가 Update/FixedUpdate 루프 전체를 구동 (333~459행)
  - `GraphPathfinder` 맵 베이크(226~256), `WaveRunner`(159), `CombatConfig`(181)
  - → Prediction은 Main.cs의 "기능"이 아니라 **루프 골격**. 지우려면 Main.cs 코어를 재작성해야 함.
- **추가 제약**: `Main.cs`에는 진행 중인 **C(VR HUD) 작업**이 들어있어, 재작성 시 그 작업이 위험.

### 결론 (수정된 방침)
`이식_환경 §4`의 "대체되는 순서대로 제거"가 정확히 옳았다. **코어(Prediction·Combat·Pose)는
MINDHEXER 자체 게임 루프가 Main.cs를 대체한 "뒤에" 삭제 가능**하다. 지금 강제 삭제하면
그린 베이스라인이 깨진다. → **코어 삭제는 MINDHEXER 부트스트랩 구축 이후로 미룬다.**

지금 안전하게 가능한 것:
- ✅ 카타나 **자산**(무기·Slash 프리팹·셰이더) — Phase 1 완료(코드 무참조).
- ⬜ 카타나 **에디터 툴**(GhostSwordTool·GripSetupTool·HandIKToggleTool), 예측 **테스트** — 리프(leaf)라 안전하나 저가치.
- ⬜ **자동부팅 게이팅** — 씬 하이라이트리스트 필요(어느 게임/타이틀 씬만 부팅). Main.cs 부팅 게이팅은 VR 씬 화이트리스트를 사용자와 확정해야 안전.
- ⬜ **서드파티 팩 물리 삭제**(Remesh·TallCity) — git엔 이미 없음(gitignore). 디스크 삭제는 base.unity 참조를 깨므로 보류.

## 실행 로그 (Phase별 커밋)

- `4ac48c4` — baseline (purge 직전, C 작업 보존 체크포인트)
- `b0d28a0` — [purge 1/9] 카타나 무기·Slash 자산 삭제 (프리팹·셰이더·메시 24파일)
- **방침 전환** — 코어 삭제 대신 **MINDHEXER 부트스트랩(GameBoot)부터 구축**(사용자 결정).
- `a640a4a` — **GameBoot 신설** + Precog Main·TitleScreen 부팅 게이트.
  - GameBoot = 우리 게임 시동(예측 의존 없음). 씬에 [GameBoot] 하나로 카메라+해킹 구성.
  - Main.cs·TitleScreen.cs에 `if (FindFirstObjectByType<GameBoot>()) return;` 가드.
  - 효과: MINDHEXER 씬(HackSandbox)에서 [Main]·[TitleScreen]·[TitleActor](RT카메라)
    안 뜸 → 카메라 1개, 렌더 에러 0, Precog 폴루터 제거.
  - 남은 무해 자동부팅(BgmPlayer·CombatAudio·ScreenFx 등 로직-only): 유휴 상태, 추후 게이트/삭제.

### 다음(코어 삭제로 가는 길)
GameBoot이 아직 하는 일은 카메라+해킹뿐. Precog Main의 나머지 역할(플레이어 이동·적·HUD 등)을
MINDHEXER 방식으로 GameBoot에 채워 넣어 Main을 완전 대체하면, 그때 Prediction/Combat 코어를
삭제할 수 있다. 그 전까지는 삭제 금지(그린 베이스라인).
