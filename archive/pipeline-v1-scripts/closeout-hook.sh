#!/bin/sh
# Stop-hook wrapper for validate-closeout.py.
#
# Why a script instead of an inline settings.json command: the inline form needed nested
# shell-in-JSON escaping on one ~230-character line, which does not survive a copy-paste that
# wraps — a wrapped line becomes a literal newline inside a JSON string, and an invalid
# settings.json silently disables EVERY setting in the file, not just the hook.
#
# Contract: silent with exit 0 when close-out is clean; on failure, emit
# {"decision":"block","reason":"<validator output>"} so the reason is fed back.
#
# --recent 1 scopes the gate to tasks touched in the last day. Without it an old backlog of
# unclosed tasks blocks every session, and a gate that always fails gets switched off.

LEDGER_HOME="${AILEDGER_HOME:-$HOME/Dev/AILedger}"
VALIDATOR="$LEDGER_HOME/validate-closeout.py"

# Missing validator must not wedge the session — say nothing, let the turn end.
[ -f "$VALIDATOR" ] || exit 0

out=$(python3 "$VALIDATOR" --recent 1 --quiet 2>&1) && exit 0

printf '%s' "$out" | python3 -c 'import json,sys; print(json.dumps({"decision": "block", "reason": sys.stdin.read()}))'
