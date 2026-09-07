# Task — Capability Drift Inventory

Compare the pre-extraction engine against `dev` at `dd25c0d` and record, capability by capability, what
survived, what improved, what weakened and what was lost.

**Deliverable:** `capability_diff_table.md` in this folder, plus a summary readable in under a minute.
The table must be **two-directional** — a list of only losses, or only wins, fails this task.

## Why now

Four refactor waves have landed: phase-1/2 extraction, sink-inversion, rule-compliance, SRP
decomposition. Each verified itself against its immediate predecessor. None compared against the
original. Locally-correct steps compound into global drift, and there is already one confirmed
instance — D18, where removing the NDJSON spill orphaned the streaming fast path and took `tenable.io`
chunk collection from O(1) to O(chunk), reported as a clean win and caught late.

This runs before the public-surface narrowing because that narrowing destroys the comparison surface.

## Baseline

| | |
|---|---|
| original | 65 `.cs` files, 11,364 lines, pre-concept layout |
| equivalent git ref | `5e5ca52` — 64 of 65 files byte-identical, and buildable in a worktree |
| current | 183 `.cs` files on `dev` at `dd25c0d`, 735 tests |
| corpus | 279 real definitions; 5 designated most-trusted |

## Seven dimensions, each two-directional

1. Config surface — every YAML key, honoured or silently dropped
2. Guards and defensive checks
3. Error paths and exact user-facing message text
4. Failure behaviour — retry/defer/externalise, cursor recovery, checkpoint field names
5. Performance characteristics
6. Host contract — what the adapter actually reads
7. Improvements — capabilities added, defects fixed, cycles removed, coverage gained

## What this task is not

- **Not a repair.** Findings are recorded and recommended, not fixed. Fixes are a separate task.
- **Not task 3.** The public-surface narrowing and the `InternalsVisibleTo` decision are out of scope,
  though findings that bear on them should be noted.
- **Not a re-derivation of the baseline.** The measurements above are established; use them.
