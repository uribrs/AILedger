# Execution Notes

## Execution (contract-driven, main thread)
- B2 VALIDATED: engine.ExecuteStageAsync already accepts a sink into the per-page loop; CanStreamIngest untouched (sink-less non-merge stages keep the fast path).
- B3 VALIDATED: cursor priming exists via reserved _resume_cursor/_resume_offset/_resume_page inputs; no engine-core change needed for resume.
- B5 handled: collect-mode on-list must share ONE source key; loader rejects mixed keys.
- New: Workflow/MergeEnrichment.cs (MergePlan, key extraction, embed+collect apply, BoundedKeyCache), Workflow/MergeEnrichmentSink.cs (lazy inner sink; per-page enrich→publish; TakeUncheckpointed for non-paginated targets; SetPaginationState → per-page checkpoint callback).
- WorkflowRunner: forced-fresh merge branch REMOVED; plansByTarget built per run (caches run-scoped); merge stages are attachment points (skip+advance); target stages run sink-ful with the decorator; mid-target resume primes reserved inputs; boundary checkpoint flushes uncheckpointed counts. Held-replay code fully removed (grep=0).
- WorkflowCheckpoint: +InTargetStage/TargetCursor/TargetOffset/TargetPage.
- MergeIntoConfig: On is string|list; +mode (embed|collect); page_size documented inert; TryParseJoin (shared loader/runner grammar).
- Loader: one-merge-per-target bound LIFTED; new rejects: merge stage with for_each/poll, target with for_each/poll, embed-with-list, mixed source keys, unknown mode.
- Schema: on string|array + mode enum (targeted diff).
- Tests: MergeIntoTests reshaped to streaming (all behavioral assertions preserved); new: paginated per-page publish, cross-page cache hit/eviction, per-page checkpoint content, boundary-resume (skip-completed w/ counters), MID-TARGET RESUME (page 1 never re-fetched/re-published, no loss/no dup), chaining (later merge joins on earlier merge's embedded field), collect (dedupe/order, keep-empty-array, drop), loader accept/reject matrix, schema-gate test with validating loader.
- Results: merge+workflow filter 41/41; FULL engine suite 556/556; engine+adapter build clean; 0 vendor names in changed engine code; schema JSON valid.
- Residual notes for verifier: page_size now inert (documented); resume granularity = target pagination (single-page targets restart, by design); duplicate-source-key last-wins log now debug-level in sink.

## Review cycle + close-out
- verifier-1: PASS, 3 LOW gaps → repaired inline (schema page_size doc now says inert; non-paginated restart-fresh test; merge-source-failure test proving the last checkpoint points at the failed page). 558/558 at that point.
- code-reviewer-1: ship-with-nits. Repaired: (1) Major — loader now REJECTS merge targets whose operation uses pagination.cursor_recovery or error_handling set_control (the enrichment sink forwards only pagination state; silent loss on resume otherwise) + tests; (2) stale one-merge-per-target comment fixed; (3) unmatched value validated (keep|drop) + test.
- Accepted observations (native-parity or degenerate-input): embed on a scalar-array anchor is a no-op; no negative caching; {{keys}} comma-join unescaped. Crash-window idempotency (page-number overwrite) verified by reviewer as sound; relies on sinks keying files by page number (true for ISB emitter) — noted, untested.
- Verifier not re-run for the repairs: additive fail-fast loader rules with direct tests; no verified success criterion's behavior changed (rationale recorded per protocol).
- FINAL: full engine suite 561/561; engine + adapter build clean; uncommitted on feature/yaml-engine-declarative-enrichment.
