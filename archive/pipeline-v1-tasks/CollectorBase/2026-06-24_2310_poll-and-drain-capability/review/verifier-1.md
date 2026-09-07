# Verifier Report — poll-and-drain capability

Verdict: **PASS (with one PENDING and minor caveats)**. Build clean, 51/51 tests green, all four
primitives implemented as specified, interleave proof is genuine, resume round-trips the processed
set, no conformance/async regressions. Live Tenable run is honestly recorded as PENDING and the
in-process interleave proof is an acceptable substitute. No must-fix correctness bugs found.

## Success Criteria

### SC1 — Build clean — PASS
`dotnet build CollectorBase.slnx` → 0 errors, 12 warnings (pre-existing NU1507 package-source +
nullable noise; not introduced here).

### SC2 — All tests pass + 5 new behavior tests — PASS
`dotnet test` → **Passed: 51, Failed: 0, Skipped: 0**. +8 over the 43 baseline.
- (a) interleave: `PollAndDrain_InterleavesDownloads_WhileStillProcessing` (Tests:1272). **Genuine, not
  tautological.** Mock (Tests:382-387) returns `PROCESSING + [d1]` on poll #1, `FINISHED + [d1,d2]` on
  poll #2. Runner drains new items each cycle BEFORE checking `finished` (Runner:814-847, exit gate at
  856). Request order is start, status#1, chunk(d1), status#2, chunk(d2) → `firstChunk(2) < lastStatus(3)`.
  If the impl waited for FINISHED, d1 would download after status#2 and the assertion would fail. Real proof.
- (b) tolerance: `PollAndDrain_ToleratesItemFailures_UnderRatio` (1/3 skipped → Success) +
  `_TooManyItemFailures_Aborts` (2/3 → TOO_MANY_ITEM_FAILURES) + retry budget
  `_PerItemRetryBudget_RetriesTransientThenSucceeds` (503→200, asserts 2 GETs). All real.
- (c) `PollAndDrain_TerminalFailState_Fails` (ERROR→POLL_FAILED) + `_StatusGone_404_SignalsFreshRestart`
  (404→EXPORT_GONE).
- (d) `BestEffort_Hydrate_EmitsBaseRecords_WhenEnrichmentFails`.
- (e) `PollAndDrain_ResumeMidDrain_SkipsProcessed_NoRedownload` — restores `__drained_1=[d1]`, asserts
  d1 not re-downloaded, d2 downloaded, /pad/start not re-requested, records==2 (d2+d3, so no skip).

### SC3 — tenable-io.yaml findings uses poll_and_drain — PARTIAL (live run PENDING)
Both `findings` and `assets` converted to `poll_and_drain` (tenable-io.yaml:33, 70) with
`max_item_retries: 3`, `max_failure_ratio: 0.5`, fail_states `[ERROR, CANCELLED]`. The **live** Tenable
run is NOT done (creds burned/rotated; honestly recorded in execution_notes.md:60-63). The deterministic
in-process interleave test is an acceptable and arguably stronger substitute; gap is honestly recorded.

### SC4 — No conformance/remediation regression — PASS
Adapter wiring intact: `ProcessAsync → AdapterBusEntrypointRunner.RunAsync` (Adapter:203),
`ResumeAsync → CollectorResumeRunner` (Adapter:243-244), `IResumableAdapter` (Adapter:36). Per-target
page counter (Runner:467), checkpoint-before-AdvancePage (Runner:480-483). No
`.Result/.Wait()/Thread.Sleep/.GetAwaiter().GetResult()` introduced (only `c.Result` property access on
a result record at Runner:993). Both for_each tolerance tests still pass after the refactor onto
`RunToleratedItemAsync`; for_each passes its `idx` as the item index so mid-item resume re-runs the
item — observable behavior unchanged.

### SC5 — Resume restores processed set + handles + poll anchor; idempotent — PASS
Processed set persisted as reserved CaptureLists key `__drained_<stepIndex>` (Runner:757, 830, 842,
866); CaptureLists round-trip through WriteCheckpoint (936) and the resume seed (Runner:269). Captures
(export_uuid) restored (268); poll anchor `PollStartedUtc` restored and passed only when `i==stepIndex`
(252, 314). strategy="none" written for the step (831/845/867) and `StepStrategy(poll_and_drain)=>"none"`
(619), so the resume strategy-match guard (248) accepts re-entry — **does NOT break the guard.** Items
(including tolerated-skips) are added to `processed` before the exit check, so a permanently-bad chunk
can't loop forever; maxWait also bounds it (765).

## Findings (ranked)

1. **LOW — `FlakyStreamPublisher` is dead code.** The helper (Tests:86-104) and the
   `failFirstPublishes` ctor arg (Tests:107,116) are never invoked with a non-zero value — no test
   wires it in. The per-item retry budget IS genuinely tested, but via the 503 HTTP path
   (`PadRetryProfile`/`chunk_flaky`), not via a publish failure. execution_notes.md:65-67 already
   explains the publisher throws rather than returning `Success=false`, so the publish-retry branch
   (Runner:471-472) is defensive/unreachable in practice. Net: an unused test helper; criterion still met.

2. **LOW/MEDIUM — "404→restart-fresh" is classification, not an automatic restart.** EXPORT_GONE returns
   a non-transient `FailureResult` (Runner:789-791). There is no in-engine mechanism that forces the
   *next* dispatch to start fresh; that relies on `restart_if_stale_seconds`, which tenable-io.yaml does
   NOT set. So a stale export only restarts fresh if the host treats a hard FAILED as a fresh
   re-dispatch (no checkpoint resume) — realistic, but the "reuse restart_if_stale semantics" wording in
   decisions.md is not literally wired. The test only asserts the error code, not actual fresh-restart.
   Acceptable given host redispatch semantics; worth a one-line note in the YAML if auto-restart is desired.

3. **LOW — reserved-key collision risk.** `__drained_<stepIndex>` lives in the same `CaptureLists`
   namespace as user `capture_list`/`accumulate_list` keys and `over` lookups. A profile that declared a
   capture_list literally named `__drained_1` would collide. Documented as reserved (Runner:754-757);
   no validation rejects a user key with that prefix. Negligible in practice.

## Cross-checks that PASSED skeptical review
- Interleave is real (drain precedes the `finished` gate; proof order verified).
- TransientFailure propagates (budget-exhausted → Stop → terminal; resume re-runs the item, processed
  set persisted before return at 830-833) while only permanent `Failure` is skipped (720-721).
- max_failure_ratio gate uses strict `>` consistently in both for_each (583) and poll_and_drain (850);
  1/3 tolerated, 2/3 aborts in tests.
- Inner fetch's own checkpoint (strategy = its pagination) is overwritten by the poll-level "none"
  checkpoint after each item, so the resume guard always sees "none". No strategy-mismatch break.
- async-all-the-way; checkpoint-before-AdvancePage preserved.

## Must-fix
None. Recommended (non-blocking): wire `FlakyStreamPublisher` into a test or delete it; add a YAML note
(or `restart_if_stale_seconds`) clarifying the EXPORT_GONE restart path; optionally reject user
capture keys with the `__drained_` prefix at load.
