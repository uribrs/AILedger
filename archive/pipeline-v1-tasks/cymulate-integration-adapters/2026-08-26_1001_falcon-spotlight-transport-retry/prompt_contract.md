# Prompt Contract

## Role
You are a senior .NET engineer working on Cymulate's Falcon collector, inside a resumable,
checkpointed, long-haul streaming collection pipeline.

## Goal
Make Falcon `CollectFindings` Phase 2 survive a mid-stream transport failure: retry the aid-batch
fetch in-process, and on exhaustion defer through the resilience layer instead of publishing a
terminal failure. A run with an intact checkpoint must never die on a TLS EOF again.

## Context
- `FalconSpotlightBatchPump.FetchAsync` (`Flows/Findings/Correlated/FalconSpotlightBatchPump.cs:280`)
  runs one frozen aid batch's Spotlight scroll to completion and materialises its records. It is the
  retry seam: pure, upstream of publish, and it mints no page.
- `progressContext.CurrentPage` advances only on publish (`FalconFindingsFlow.cs:283`), on the far
  side of the pump's `yield return`. Objects are named from `CurrentPage`.
- Resume position is `(LastCompletedStagedPage, LastCompletedBatchIndex)` in the frozen generation —
  not a vendor cursor. An unpublished batch is redone on resume.
- Reference implementation: `Collectors/TenableIoCollector/Flows/Findings/Correlated/TenableIoChunkRetry.cs`
  and its two export loops (`TenableIoVulnPhase.cs`, `TenableIoAssetSpoolPhase.cs`,
  `TenableIoAssetsFlow.cs`). Copy the retry shape; do NOT copy the skip-and-tolerate exhaustion policy.
- Chain position matters: `RetryableTransportFailurePolicy` (Infra, position 4) will terminate the run
  if the exception escapes the flow, because Falcon passes no `transientTransportBackoff`. The whole
  point of this change is that the exception must not escape as a raw transport failure.

## Constraints
See `constraints.md` — all of it binds. The load-bearing ones:
- Adapters repo only. Infra and Http.Package are out of scope; `HttpTransportFailureClassifier` is
  consumed, never modified.
- A skipped aid batch must be impossible by construction — Falcon has no claim/sweep model.
- The backoff must sleep inside `FetchAsync` so no live `after` cursor (120s TTL) is held across it.
- Exhaustion defers unbudgeted; it does not publish a failure.
- No `.csproj` version bumps. No commit or push without explicit approval.
- Never run the full `FalconCollector.Test` suite; filter to change-relevant classes.

## Success Criteria
- A mid-stream transport failure on a Spotlight aid-batch fetch is retried in-process, with no page
  minted and no live cursor held across the backoff.
- Exhaustion produces an unbudgeted flat 5-minute `RequestDeferredRecovery` with
  `Reason = "falcon-transport-failure"` and `UseRecoveryBudget: false` — not a `PublishFailure`.
- A skipped aid batch is impossible by construction, and a test demonstrates the exhaustion path
  throws rather than returning a partial or empty batch.
- Unit tests cover: the retry predicate; the delay ladder; exhaustion-to-deferral end to end through
  `FalconResilienceStrategyFactory.Create()`; and that a retried fetch neither advances the page nor
  double-counts `_fetchedBatches`.
- The concurrency tradeoff (a backing-off batch holds its semaphore slot; degree 6 with k retrying
  yields 6-k) is stated in a code comment and in the docs.
- Docs updated in the same diff: `FalconDocs/CollectorDocs/01-collection-strategy.md` (amend the
  "Transport retries stay in DefensiveToolkit's Polly loop … neither is duplicated in the collector"
  line — correct for request-level faults, wrong for mid-stream ones),
  `FalconDocs/CollectorDocs/03-current-concerns.md`, and
  `ai/skills/collector-execution-and-recovery/SKILL.md` (plus `collector-flow-patterns` if it fits).
- Build clean; the filtered Falcon test classes green.

## Execution Rules
- Do not assume missing data. Resolve A5, A6, A7 against source or logs before relying on them; if
  one cannot be resolved, say so and state what the code does under either branch.
- Respect constraints strictly. Do not re-litigate the Infra-vs-Falcon decision.
- Match the surrounding code's idiom: small methods, helpers over long procedural blocks, comment
  density as in `FalconSpotlightBatchPump.cs` and `TenableIoChunkRetry.cs`.
- Fix review-surfaced bugs directly rather than asking; reserve questions for genuine forks.
- Suggest a `CollectorVersion` bump magnitude; do not apply one.

## Output Format
- Source changes under `Collectors/FalconCollector/`.
- New tests in `UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.FalconCollector.Test/`.
- Doc updates as listed.
- `execution_notes.md` recording what was done, what each open assumption resolved to, and the
  filtered test command plus its result.

## Stop Conditions
- A6 resolves against the design — the EOF does not originate in `FetchAsync`.
- A7 resolves against the design — deferral does mint a page over an unpublished batch.
- The change would require touching IntegrationInfra or Cymulate.Http.Package to work.
- A fix causes more test failures than it resolves.
