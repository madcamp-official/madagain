#!/usr/bin/env bash
# sync-to-fordev.sh — MINDHEXER(정본, HEAD) → MINDHEXER-forDev(미러) 동기화 ("받아오기").
#
# forDev의 Assets/ProjectSettings/Packages(manifest)를 MINDHEXER의 "커밋된(HEAD)" 상태로 덮어쓴다.
# git archive를 쓰므로 추적 파일만 나온다 → Library/Temp/.csproj 등은 자동 제외.
# Library/Temp/UserSettings 등 forDev 자체 생성물은 건드리지 않는다(각자 재생성).
#
# ★ gitignore된 대형 팩(TallCity 1.4G·Remesh 619M 등)은 archive에 담기지 않는다. 예전 판은
#   Assets를 통째로 지운 뒤 archive만 풀어서, 그 팩들을 미러에서 영구 소실시켰다
#   (2026-07-30 실제 사고: forDev TallCity 1.4G → 642K, 스테이지 씬 전부 깨짐).
#   지금은 지우기 전에 옮겨 두고 되돌려 놓는다. 보호 목록은 .gitignore에서 매번 다시
#   계산한다 — 하드코딩하면 팩이 추가될 때 같은 사고가 반복된다.
#
# ⚠️ forDev에 아직 MINDHEXER로 병합 안 한 작업이 있으면, 먼저 merge-from-fordev.sh 를 돌려라.
#    이 스크립트는 forDev의 Assets/ProjectSettings를 지우고 다시 채운다(=forDev 작업 유실).
# ⚠️ forDev Unity 에디터는 닫아두고 실행 권장(파일 잠금·재임포트 충돌 방지).
#
# (docs/KJH/decisions/0003-two-copy-mcp-workflow.md)
set -euo pipefail
cd "$(dirname "$0")/.."   # repo root
SRC="MINDHEXER"
DST="MINDHEXER-forDev"

# forDev Unity가 열려 있으면 경고
if [ -f "$DST/Temp/UnityLockfile" ]; then
  echo "⚠️  $DST Unity 에디터가 열려 있는 것 같습니다(UnityLockfile 존재). 닫고 다시 실행하세요." >&2
  exit 1
fi

# MINDHEXER 미커밋 변경 경고 (동기화는 HEAD 기준)
if ! git diff --quiet -- "$SRC" || ! git diff --cached --quiet -- "$SRC"; then
  echo "⚠️  경고: $SRC에 커밋 안 된 변경이 있습니다. 이 동기화는 HEAD(커밋된 상태)만 반영합니다."
fi

mkdir -p "$DST/Packages"

# ── 보호 목록: 지울 영역 안에 있는 gitignore된 경로 (= archive가 복원해 주지 못하는 것) ──
PROTECT=()
while IFS= read -r p; do
  [ -n "$p" ] && PROTECT+=("${p%/}")   # --directory가 붙여 주는 끝 슬래시 제거
done < <(git ls-files --others --ignored --exclude-standard --directory \
           "$SRC/Assets" "$SRC/ProjectSettings" "$SRC/Packages" | sed "s|^$SRC/||")

# 같은 볼륨에 둬야 mv가 즉시 끝난다(다른 볼륨이면 2GB 복사가 된다).
HOLD="$DST/.sync-hold"
rm -rf "$HOLD"
for p in "${PROTECT[@]}"; do
  [ -e "$DST/$p" ] || continue
  mkdir -p "$HOLD/$(dirname "$p")"
  mv "$DST/$p" "$HOLD/$p"
done

# 동기화 대상만 비우고(생성물·Library는 보존) HEAD에서 다시 추출
rm -rf "$DST/Assets" "$DST/ProjectSettings"
rm -f  "$DST/Packages/manifest.json" "$DST/Packages/packages-lock.json"
git archive "HEAD:$SRC" | tar -x -C "$DST"

# 보호해 둔 것을 되돌린다. 미러에 아예 없던 것은 정본에서 채운다(자기 치유).
for p in "${PROTECT[@]}"; do
  mkdir -p "$DST/$(dirname "$p")"
  if [ -e "$HOLD/$p" ]; then
    mv "$HOLD/$p" "$DST/$p"
  elif [ -e "$SRC/$p" ]; then
    cp -R "$SRC/$p" "$DST/$p"
    echo "   ↻ 미러에 없어 정본에서 복원: $p"
  fi
done
rm -rf "$HOLD"

# ── LFS 실체 복원 ──
# git archive는 smudge 필터를 돌리지 않는다 → LFS 파일이 135바이트 "포인터 스텁"으로 나온다.
# .gitattributes가 png/jpg/tga/psd/fbx/obj/blend/wav/mp3/dll 등을 LFS로 잡고 있으므로,
# 그대로 두면 미러의 텍스처·메시·DLL이 전부 실체 없는 텍스트가 된다
# (2026-07-30 실제 사고: forDev LFS 478개 전부 스텁 → 씬 전부 깨짐).
# 정본 작업트리에는 이미 smudge된 실체가 있으니 그걸 덮어쓴다.
# 주의: 정본에 미커밋 변경이 있는 LFS 파일은 그 작업트리 버전이 들어간다(위 경고 참조).
LFS_N=0
while IFS= read -r f; do
  rel="${f#"$SRC"/}"
  [ -f "$SRC/$rel" ] || continue
  mkdir -p "$DST/$(dirname "$rel")"
  cp -f "$SRC/$rel" "$DST/$rel"
  LFS_N=$((LFS_N+1))
done < <(git lfs ls-files -n 2>/dev/null | grep "^$SRC/" || true)

echo "✅ $SRC → $DST 동기화 완료 (HEAD 추적 파일 + gitignore 팩 ${#PROTECT[@]}건 + LFS 실체 ${LFS_N}개)."
echo "   forDev Unity를 열면(포커스) 재임포트됩니다."
