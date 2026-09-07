# Constraints

## Output contract (user-specified, hard)

- Uploaded payload must be **byte-identical** to pre-change behavior.
- The enriched asset must land in the batch exactly as today — no exceptions.
- The change may alter **only who holds references**; never what is built,
  enqueued, or uploaded.
- The release runs **after** `AddAsync` has taken the row, so it cannot
  introduce a skip. Do not move it earlier.

## Mechanism (user-specified, hard)

- Do **not** use `Remove`/`RemoveRange` — that shifts indices under the
  `i += cBatchSize` loop. Null the slots.
- `iAssets.Count` must stay constant for the index arithmetic to hold.
- `iAssets.Skip(i)` must never dereference already-nulled earlier slots.
- Do not restructure into streaming pages in this change (considered and
  deferred — see `decisions.md` D1).

## Out of scope — deferred, not omissions

- 404 retry policy (382 pairs retried 3× on a non-retryable status).
- Silent-drop paths: `processSingleAsset`'s blanket catch (`:357-360`) and the
  missing-ID skip (`:368-371`). Neither fired in the customer run; the 1,528
  `[Error]` lines are fully accounted for by API-call errors plus failed
  asset-scoped fetches.
- Per-vulnerability payload deduplication (the ~1MB row is largely global vuln
  details and solution text duplicated across assets).
- GC configuration / Server GC.
- The `O(assets × vulns)` API fan-out: ~53h projected against a 48h
  `executionTimeout`. Separate redesign.

## Repo conventions (authoritative: `ReadMEs/coding-standards.md`)

- **Explicit types over `var` everywhere.**
- New parameters: `camelCase`, **no `i*` prefix** (`i*` in this file is legacy —
  do not propagate it into new code, do not mass-rename existing).
- Comments only where they carry information the code cannot: non-obvious
  invariants, subtle ordering, why one option over another. One or two sentences
  max. The ownership-transfer invariant qualifies; a comment restating
  `iAssets[k] = null` does not.
- No stale `TODO`/`HACK` markers.
- `<Nullable>enable</Nullable>` is set in `InsightVmCollector.csproj`.

## Environment

- Branch `fix/insightvm-collector-heap-retention` already exists. No Jira
  ticket (user's choice), so no `CA-xxxxx` prefix on commits.
- `ai/` is untracked and **not** in `.gitignore`. Task artifacts must not be
  swept into commits — stage explicit paths, never `git add .` / `git add -A`.
- `InsightVmCollector.csproj` has `InternalsVisibleTo InsightVmCollector.Tests`
  — internal seams are testable, private ones are not.
