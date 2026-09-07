#!/bin/sh
# Build a distributable chain bundle: workflow-coordinator-chain-<version>-<date>.zip
#
# Zip layout is flat, matching every prior release: the scripts, RULES.md,
# CHANGELOG.md and README-INSTALL.md at the root, one directory per skill.
# A recipient unzips it and follows README-INSTALL.md.
#
# Usage:
#   ./build-bundle.sh 1.5.0                 -> ~/Dev/Uri/Skills/<today>/
#   ./build-bundle.sh 1.5.0 /some/other/dir
#
# The default output keeps the handout habit: bundles live under
# ~/Dev/Uri/Skills/<date>/ alongside the earlier releases.

set -e
VERSION="$1"
[ -n "$VERSION" ] || { echo "usage: $0 <version> [outdir]" >&2; exit 2; }

REPO="$(cd "$(dirname "$0")" && pwd)"
DATE="$(date +%Y-%m-%d)"
OUT="${2:-$HOME/Dev/Uri/Skills/$DATE}"
NAME="workflow-coordinator-chain-$VERSION-$DATE.zip"

grep -qE "^## \\[?$VERSION\\]?" "$REPO/CHANGELOG.md" 2>/dev/null \
  || echo "warning: CHANGELOG.md has no '## $VERSION' section" >&2

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

cp "$REPO/build-readme.py" "$REPO/validate-closeout.py" "$REPO/closeout-hook.sh" "$STAGE/"
[ -f "$REPO/mint-lesson.py" ] && cp "$REPO/mint-lesson.py" "$STAGE/"
cp "$REPO/RULES.md" "$REPO/CHANGELOG.md" "$REPO/README-INSTALL.md" "$STAGE/"
cp -R "$REPO/skills/"* "$STAGE/"
find "$STAGE" -name .DS_Store -delete

mkdir -p "$OUT"
( cd "$STAGE" && zip -qr "$OUT/$NAME" . )
echo "$OUT/$NAME"
unzip -l "$OUT/$NAME" | tail -n 3
