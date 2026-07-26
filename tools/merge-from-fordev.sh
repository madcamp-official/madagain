#!/usr/bin/env bash
# merge-from-fordev.sh — MINDHEXER-forDev(미러) → MINDHEXER(정본) 병합 ("합치기").
#
# forDev의 Assets/ProjectSettings/Packages(manifest)를 MINDHEXER로 복사한다.
# ⚠️ 삭제는 반영하지 않는다(파일 추가·수정만). forDev에서 지운 파일은 MINDHEXER에서 git으로 직접 지워라.
# ⚠️ 이 스크립트는 커밋하지 않는다. 복사 후 반드시 git으로 검토(=병합 안전장치)하고 직접 커밋하라.
#
# 안전 규율(docs/KJH/decisions/0003):
#  - 두 인스턴스는 서로 다른 파일을 만진다(같은 파일 동시 편집 금지).
#  - 병합 전에 MINDHEXER 쪽 병렬 작업은 먼저 커밋해 둔다 → git diff가 forDev 변경만 깨끗이 보여줌.
set -euo pipefail
cd "$(dirname "$0")/.."   # repo root
SRC="MINDHEXER-forDev"
DST="MINDHEXER"

[ -d "$SRC/Assets" ] || { echo "❌ $SRC/Assets 없음. 먼저 sync-to-fordev로 forDev를 채우세요." >&2; exit 1; }

cp -R "$SRC/Assets/."          "$DST/Assets/"
cp -R "$SRC/ProjectSettings/." "$DST/ProjectSettings/"
[ -f "$SRC/Packages/manifest.json" ]      && cp -f "$SRC/Packages/manifest.json"      "$DST/Packages/manifest.json"
[ -f "$SRC/Packages/packages-lock.json" ] && cp -f "$SRC/Packages/packages-lock.json" "$DST/Packages/packages-lock.json"

echo "✅ $SRC → $DST 복사 완료 (추가·수정만, 삭제 미반영)."
echo ""
echo "다음을 반드시 하세요 (병합 안전장치):"
echo "  cd $DST && git add -A && git status"
echo "  → diff에 forDev가 바꾼 것만 보여야 정상."
echo "  → MINDHEXER 병렬 작업이 되돌려진 게 보이면, 그 파일만 'git checkout -- <경로>'로 복원 후 커밋."
