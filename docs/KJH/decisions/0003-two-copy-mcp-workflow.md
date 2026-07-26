# ADR-0003: MINDHEXER / MINDHEXER-forDev 2-카피 병렬 작업 (MCP 분업)

- 날짜: 2026-07-26
- 상태: 확정 (동기화 방식 세부는 확인 필요)

## 결정

`MINDHEXER/`와 `MINDHEXER-forDev/`는 **같은 프로젝트의 작업 카피 2개**다. 서로 다른 트랙(비주얼/게임플레이)이
**아니다** — 둘 다 동일한 MINDHEXER 프로젝트를 이어서 작업한다.

## 왜 폴더를 2개로 나눴나

- Unity는 **한 폴더(프로젝트)를 동시에 두 번 열 수 없다.**
- 폴더를 2개 두면 **Unity 인스턴스 2개 → MCP 인스턴스 2개**가 되어, 에이전트가 **병렬로 분업**할 수 있다.
- MCP 타깃 지정: 각 도구 호출에 `unity_instance` 파라미터로 어느 에디터인지 지정(`Name@hash` / 포트 등).

## 작업 흐름 (fork ↔ merge)

1. **분업**: forDev에서 일부 작업, MINDHEXER에서 다른 작업 (동시).
2. **병합**: forDev에서 한 작업을 **MINDHEXER에 합친다.**
3. **재동기화**: forDev가 다시 **MINDHEXER의 최신을 받아온다.**
4. 위를 반복. (git branch/merge와 같은 개념 — 두 카피를 오가며 합침)

## 핵심 원칙

- **두 카피는 궁극적으로 같은 상태로 수렴**해야 한다. 한쪽에만 있는 코드는 임시일 뿐, 병합으로 합쳐진다.
- 따라서 한쪽에서 내린 구조 결정(예: precog 엔진 제거)은 **다른 쪽에도 반영**되어야 한다.

## 현재 상태 (2026-07-26)

- **`MINDHEXER/`**: precog 엔진 **제거 완료(clean)** + 해킹 스캐폴딩(Hacking/·Input/·GameBoot·HackSandbox) + 셰이더 자산. git 추적됨(브랜치 `docs/kjh-initial`).
- **`MINDHEXER-forDev/`**: 빈 clean Unity 6.2 프로젝트. MCP 패키지 추가됨. **우리 코드 아직 없음(seed·동기화 필요).** git 미추적(`?? MINDHEXER-forDev/`).
- → 다음: 두 카피 동기화(MINDHEXER의 현재 코드를 forDev로) 후 병렬 작업 시작.

## 동기화(병합) 방식 — 확정: (가) 같은 repo + 파일 복사

- **정본 = `MINDHEXER/`(git 추적). 미러 = `MINDHEXER-forDev/`(gitignore).** 두 카피를 다 추적하면 파일이
  2배로 저장돼 diff가 뒤엉키므로, git 집은 MINDHEXER 하나. forDev는 스크립트로 동기화하는 미러.
- **동기화 단위**: `Assets/` + `Packages/manifest.json`(+lock) + `ProjectSettings/`. **.meta 포함**(GUID 유지).
  **제외**: `Library/` `Temp/` `Logs/` `obj/` `.utmp/` `UserSettings/` `*.csproj/sln/slnx`, 대형팩(TallCity·Remesh).
- **스크립트**(`tools/`):
  - `sync-to-fordev.sh` — MINDHEXER HEAD → forDev 미러(`git archive`, 추적 파일만 → 제외 자동). "받아오기".
  - `merge-from-fordev.sh` — forDev → MINDHEXER 복사(추가·수정만, 삭제 미반영) → `git add -A && git status`로 검토 후 커밋. "합치기".
- **충돌 방지 규율**: 두 인스턴스는 서로 다른 파일을 만진다 / 병합 전 MINDHEXER 병렬 작업은 먼저 커밋 / 병합은
  **git diff 검토가 안전장치**(파일복사라 자동 3-way 병합은 없음 — 같은 파일 동시 편집 금지가 전제).
- **주의**: 각 스크립트는 대상 Unity 에디터를 **닫고** 실행(파일 잠금·재임포트 충돌 방지). sync-to-fordev는
  forDev `UnityLockfile` 있으면 자동 중단.

## 관련
- [0002-precog-purge.md](0002-precog-purge.md) — MINDHEXER precog 제거(두 카피 모두 clean 유지의 근거)
- 옛 핸드오프의 "MINDHEXER=비주얼 / forDev=게임플레이" 트랙 분리 서술은 **오해였음**(이 문서가 정정).
