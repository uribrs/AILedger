#!/bin/sh
# Deploy the chain's skills. Claude remains the default runtime; Codex is an
# additive target selected with --runtime codex.
#
# The scripts need no deployment: this repo IS their runtime home
# (~/Dev/AILedger), so build-readme.py, validate-closeout.py and
# closeout-hook.sh are already where the hook and the skills expect them.
#
# Usage:
#   ./install.sh                         deploy to Claude
#   ./install.sh --check                 check Claude deployment
#   ./install.sh --runtime codex         deploy to Codex
#   ./install.sh --runtime codex --check check Codex deployment

set -e
SRC="$(cd "$(dirname "$0")" && pwd)/skills"
RUNTIME="claude"
CHECK=""

while [ "$#" -gt 0 ]; do
    case "$1" in
        --check)
            CHECK=1
            shift
            ;;
        --runtime)
            [ "$#" -ge 2 ] || { echo "--runtime requires claude or codex" >&2; exit 2; }
            RUNTIME="$2"
            shift 2
            ;;
        *)
            echo "unknown argument: $1" >&2
            exit 2
            ;;
    esac
done

case "$RUNTIME" in
    claude)
        DEST="${CLAUDE_SKILLS_DIR:-$HOME/.claude/skills}"
        ;;
    codex)
        DEST="${CODEX_SKILLS_DIR:-${CODEX_HOME:-$HOME/.codex}/skills}"
        ;;
    *)
        echo "unsupported runtime: $RUNTIME (expected claude or codex)" >&2
        exit 2
        ;;
esac

mkdir -p "$DEST"
drift=0

for dir in "$SRC"/*/; do
    name=$(basename "$dir")
    if [ -n "$CHECK" ]; then
        if diff -rq -x '.DS_Store' "$dir" "$DEST/$name" >/dev/null 2>&1; then
            printf 'ok      %s\n' "$name"
        else
            printf 'DRIFT   %s\n' "$name"
            drift=1
        fi
    else
        # Copy only the chain's own skills. Never wipe $DEST — the operator
        # keeps unrelated skills there.
        rm -rf "$DEST/$name"
        cp -R "$dir" "$DEST/$name"
        printf 'deployed %s\n' "$name"
    fi
done

if [ -n "$CHECK" ]; then
    if [ "$drift" -eq 0 ]; then
        echo "clean: $RUNTIME skills match this repo"
    else
        echo "drift: run ./install.sh --runtime $RUNTIME"
    fi
    exit "$drift"
fi

echo
echo "Skills deployed for $RUNTIME. Scripts already live here: $(dirname "$SRC")"
if [ "$RUNTIME" = "claude" ]; then
    echo "Arm the close-out gate once, by hand, in ~/.claude/settings.json — see README-INSTALL.md."
else
    echo "Codex uses the coordinator's explicit close-out validation — see README-INSTALL.md."
fi
