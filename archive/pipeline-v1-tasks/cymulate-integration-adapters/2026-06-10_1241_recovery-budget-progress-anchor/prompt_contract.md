# Prompt Contract

## Role
You are a senior .NET engineer working in a resilience/recovery subsystem for resumable, paginated data collectors.

## Goal
Change the Shared deferred-recovery retry budget so it bounds **consecutive no-progress** recoveries (anchored to a durable progress coordinate) instead of **total lifetime** recoveries, and add a generous whole-collection backstop. Global behavior change; must be safe for every collector.

## Context
- Bug: `_resilience.recovery.attemptCount` (`AdapterRecoveryBudget.cs`) is a single shared lifetime counter in checkpoint `AdapterState`. Not partitioned by failure type; reset only on full-scan success (`AdapterBusStrategyFlowExecutor.cs:48`, `CollectorResumeStrategyExecutor.cs:38`); each healthy page re-persists the stale count. Gate at `AdapterFailureDecisionExecutor.cs:120`, increment at `:198-212`, defer snapshot via `AdvancePage(0,0)` at `~:290`.
- Budgeted recoveries are `RecoverAndRetry`/`RequestDeferredRecovery` with `UseRecoveryBudget=true` (the default, `AdapterFailureDecision.cs:28`). Unbudgeted waits (`false`: planned yields, server-suggested delays) clear the budget and must stay unchanged.
- Resume reloads the count via `CollectorResumeSetup.cs:70` (`AdapterRecoveryBudget.Load(...).AttemptCount`).

## Constraints
See `constraints.md`. Key: net8.0 (no `-f net9.0`); Shared-only, no per-collector flow-loop reach-in; deterministic culture-invariant hash; capture counters before `AdvancePage(0,0)`; preserve success-only `Clear` callsites and extend `Clear` to wipe new keys; unbudgeted waits stay budget-neutral.

## Algorithm (on each BUDGETED deferred recovery)
1. Load `prevCount`, `prevCoordinate`, `totalDeferralCount`, `firstBudgetedDeferAtUtc`.
2. Compute `currentCoordinate` = deterministic hash of `AdapterState` entries whose key does NOT start with `_resilience.`, folded with `ProcessedItems` + `ProcessedFindings` + `CurrentPage`. Capture the three counters BEFORE the defer's `AdvancePage(0,0)`.
3. `progressed = prevCoordinate is null || currentCoordinate != prevCoordinate`.
4. `newCount = progressed ? 1 : prevCount + 1`.
5. Backstop gate (hard stop regardless of progress): fall back if `totalDeferralCount + 1 > 50` OR `now - firstBudgetedDeferAtUtc > 24h`.
6. Stuck gate: fall back (existing `FallbackDecision` → `PublishFailure`) if `newCount > MaxRetries`.
7. Honor path: persist `newCount`, `currentCoordinate`, `totalDeferralCount + 1`, and `firstBudgetedDeferAtUtc` (set once if unset). Schedule the wait. Backoff delay must index off `newCount` (reset-aware).
8. Migration: missing `prevCoordinate` ⇒ `progressed = true` ⇒ `newCount = 1`.

## Success Criteria
- Defer → page/cursor progress → defer again does NOT accumulate toward the stuck gate (count resets to 1).
- Only consecutive no-progress defers accumulate; the 4th (MaxRetries=3) falls back to `PublishFailure`.
- A defer whose only page change is the artificial `AdvancePage(0,0)` bump does NOT register as progress.
- Backstop independently hard-stops at 50 total budgeted deferrals or 24h since first budgeted defer.
- Unbudgeted waits remain budget-neutral and still clear.
- Old checkpoints upgrade gracefully (missing coordinate ⇒ progress ⇒ count 1).
- Solution builds on net8.0; Falcon resilience test project + ≥1 non-Falcon collector test project pass.

## Required Tests
- coordinate change resets count to 1, incl. empty-page `CurrentPage`-only advance (no item/finding/`AdapterState` change).
- coordinate unchanged increments; 4th consecutive no-progress defer falls back to `PublishFailure`.
- `CurrentPage` captured before `AdvancePage(0,0)` — regression test locking capture ordering (artificial bump ≠ progress).
- backstop trips independently on total-count (>50) and age (>24h).
- unbudgeted wait still clears and does not consume/inflate the budget.
- migration: missing coordinate on load ⇒ treated as progress ⇒ count 1.

## Execution Rules
- Do not assume missing data; confirm the OPEN assumptions in `assumptions.md` against the code before relying on them.
- Respect constraints strictly. Diagnose before changing; if a fix causes more failures than it resolves, revert and report.
- Match existing C# style (small methods, mirror neighbors). No speculative abstractions.

## Output Format
- Code edits to the Shared resilience files + new/updated tests.
- `execution_notes.md` updated with what changed, key seams, and any OPEN assumption resolutions.

## Stop Conditions
- Goal achieved and tests pass.
- A constraint cannot be honored (e.g. no clean seam to capture counters pre-`AdvancePage`) — stop and surface.
- An OPEN assumption resolves contrary to the design (e.g. `CurrentPage` IS in `AdapterState`) — stop and surface before proceeding.
