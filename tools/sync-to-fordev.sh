#!/usr/bin/env bash
# sync-to-fordev.sh — MINDHEXER(정본, HEAD) → MINDHEXER-forDev(미러) 동기화 ("받아오기").
#
# forDev의 Assets/ProjectSettings/Packages(manifest)를 MINDHEXER의 "커밋된(HEAD)" 상태로 덮어쓴다.
# git archive를 쓰므로 추적 파일만 나온다 → Library/Temp/대형팩/.csproj 등은 자동 제외.
# Library/Temp/UserSettings 등 forDev 자체 생성물은 건드리지 않는다(각자 재생성).
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
# 동기화 대상만 비우고(생성물·Library는 보존) HEAD에서 다시 추출
rm -rf "$DST/Assets" "$DST/ProjectSettings"
rm -f  "$DST/Packages/manifest.json" "$DST/Packages/packages-lock.json"
git archive "HEAD:$SRC" | tar -x -C "$DST"

echo "✅ $SRC → $DST 동기화 완료 (HEAD 기준, 추적 파일만)."
echo "   forDev Unity를 열면(포커스) 재임포트됩니다."
