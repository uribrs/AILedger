# Verifier Report — Batch-Scoped Storage Subfolders

Date: 2026-07-06
Verifier: independent (read-only; no product code modified)

## Verdict: PASS-WITH-GAPS

The mechanism is correct, minimal, and well-tested for everything reachable from in-repo publishers.
One substantive gap: the resume normal-success completion is an ISB host-side fallback DONE with no
in-repo restoration hook, and the execution notes misstate that it is covered. It is latent (no
collector is opted in) but must be closed before the first opt-in.

## Success Criteria (prompt_contract.md)

| # | Criterion | Status | Evidence |
|---|-----------|--------|----------|
| 1 | Shared scope helper in Egress: derive `{base}/batch_{page:D6}`, set `Metadata["storageUrl"]`, preserve base, restore-to-base, documented ordering invariant | SATISFIED | `Shared/.../DataPipeline/Egress/BatchScopedStorage.cs` — `BeginPage` (:51) derives from the preserved base via `ResolveBaseUrl` (:90, preserved-key-first → no stacking), `RestoreBase` (:76), `BuildBatchSegment` (:88, D6), ordering contract in the XML doc (:22–:31). `page >= 1` guard (:54) mirrors `CollectorOutputDefaults.BuildPageTargetPath`. |
| 2 | Base restoration before DONE/failure/partial publication incl. `AdapterFailureDecisionExecutor` snapshot path | PARTIAL | All five in-repo publication choke points hooked: success DONE `AdapterBusEntrypointRunner.cs:117`; ERROR + failure DONE `AdapterFlowFailureHandling.cs:57` (top of `PublishErrorAndFailureCompletionAsync`, the single funnel for all 13 failure call sites incl. resume failure/setup/unhandled); partial DONE `AdapterBusPartialSuccessPublisher.cs:36`; resume partial DONE `CollectorResumePartialSuccessPublisher.cs:35`; deferred-wait snapshot `AdapterFailureDecisionExecutor.cs:344` (before `OnCheckpoint` at :350 — correct order). **Gap: resume normal-success DONE is not adapter-published** — see Gap 1. |
| 3 | Dud page: 0-record page publishes nothing, event carries bare base | SATISFIED | Caller-side pattern documented in the helper doc (:26–:27) and README; 0-record no-upload is pre-existing egress behavior (`NdjsonBatchSession.FlushAsync` :205 early-returns when `_records == 0`); `RestoreBase_AfterDudPage_...` test locks base-at-event-time + later derivation. No S3 folder materializes without an object. |
| 4 | Unit tests green covering the six named cases | SATISFIED | `BatchScopedStorageTests.cs` — derive/no-stacking (:52), D6 padding (:135), determinism incl. same-page-after-resume (:64), dud page (:109), missing-URL no-op (:87), never-scoped no-op = default unchanged (:123), ordering lifecycle (:142), plus two egress-level tests proving live pick-up by the real NDJSON session with an `s3://` base (:164, :186). Independently re-run by this verifier: **12/12 passed** (`dotnet test --no-build --filter FullyQualifiedName~BatchScopedStorage`). New executor test read (not run; prior evidence 19/19): it genuinely discriminates — its fallback publish delegate is a local stub without `RestoreBase`, so the assert at metadata==base only passes via the snapshot-restore path. Ordering-invariant caveat: see Gap 2. |
| 5 | Build succeeds; existing suite passes | PARTIAL (accepted) | Prior evidence: solution build 0 errors; targeted suites green. Full suite killed by operator per the repo's hung-test guidance; CI backstop. Consistent with the contract's escape hatch. |
| 6 | ai/skills docs updated | SATISFIED | `Shared/.../Egress/README.md` (BatchScopedStorage entry, ordering + determinism rationale) and `ai/skills/collector-flow-patterns/SKILL.md` (opt-in hard rule). Both consistent with code. |

## Verification Obligations (orchestration_plan.md)

- **A5/A6 out of OPEN**: YES — both marked VALIDATED with dates in `assumptions.md`. A5 independently
  re-verified by grep: the only writers of `Metadata["storageUrl"]` are ingress
  (`AdapterPlatformEventFactory.cs:82`, `SetIfMissing`) and the new helper; both NDJSON sessions only
  read (`NdjsonBatchSession.cs:429`, `NdjsonUtf8BatchSession.cs:423`). A6 re-verified:
  `CollectorOutputDefaults.cs:29` uses `{page:D6}`; helper mirrors it. (A4 remains OPEN by design —
  the plan required only A5/A6 to move.)
- **Non-opted-in byte-identical**: CONFIRMED — every hook is a `RestoreBase` call; `RestoreBase`
  keys off `baseStorageUrl`, which only `BeginPage` writes; a collector that never calls `BeginPage`
  sees zero metadata mutation and zero new keys (locked by `RestoreBase_WhenNeverScoped_...`). No
  changes to `NdjsonBatchSession`/`NdjsonUtf8BatchSession` (not in the diff).
- **Ordering-invariant test exists and would fail if broken**: PARTIAL — see Gap 2.
- **Executor snapshot path covered**: YES — `AdapterFailureDecisionExecutor.cs:344` restores before
  `OnCheckpoint`; new DummyCollector test covers it end-to-end through `ExecuteAsync`.
- **Build + tests**: as criterion 5.

## Gaps

1. **[MEDIUM] Resume normal-success DONE has no restoration hook, and execution_notes misstates coverage.**
   Location: `Orchestration/Collectors/Recovery/CollectorResumeStrategyExecutor.cs:39` /
   `CollectorResumeLegacyExecutor.cs:38` (`BuildSuccessResult` — returns without publishing a
   `CompletionRequest`). On a successful resume, DONE is a host-side ISB *fallback* (documented in-repo:
   `Tools/.../IsbCompletionSimulation/IsbCompletionSimulationRunner.cs:226-229`, "resume success: ISB
   fallback publishes final success done"). The SDK shares the metadata dictionary by reference
   (`AdapterProgressContext.FromPlatformEvent` assigns `Metadata = platformEvent.Metadata` — verified by
   decompilation of Cymulate.Integration.Sdk 3.2.0), so when an opted-in resumed flow finishes, the live
   dict still holds the *last page's batch path* (the documented usage never restores after the final
   page). If the ISB fallback DONE echoes live metadata (as it does for progress events per A1), it
   would carry a batch subfolder — violating the "DONE carries bare base" constraint. Whether the
   fallback reads live metadata vs. the stored RUN-envelope value cannot be settled in-repo; the design
   provides no guarantee either way. `execution_notes.md` ("Resume normal-success completion ... flows
   through the same shared publishers hooked above") is inaccurate — the hooked resume publishers cover
   only partial-success and failure. Cheap fix, symmetric with the fresh-run hook: `RestoreBase` in both
   resume executors' success paths (and/or before `AdapterRecoveryBudget.Clear` at
   `CollectorResumeStrategyExecutor.cs:38`). Latent today (no collector opted in); must land with or
   before the first opt-in.

2. **[LOW] Ordering-invariant test is a usage simulation, not an enforcement lock.**
   Location: `BatchScopedStorageTests.cs:142` (`PageLifecycle_HoldsEachPagesPathUntilItsAdvance`).
   The test drives the correct call sequence itself and asserts each page's announced path; it fails if
   *helper behavior* changes (e.g., eager next-path computation, auto-restore on advance), but nothing
   fails if a future *flow* calls `BeginPage(N+1)` before page N's `AdvancePage` — the invariant is a
   caller contract with no mechanical guard. Acceptable within scope (enforcing it would require
   host/flow changes, a contract stop condition), but the plan's phrasing "fails if the invariant is
   broken" is only true for the helper side. The doc comment + SKILL.md rule are the real guard.

3. **[LOW] `RestoreBase`/`BeginPage` dereference `progressContext.Metadata` without a null guard.**
   Location: `BatchScopedStorage.cs:80` (and :65). Neighboring egress code treats null metadata as
   possible (`NdjsonBatchSession.ApplyStorageUrlPrefix` :428 checks `Metadata == null`). If an event
   ever deserializes with `Metadata = null` (property has a public setter), `RestoreBase` now NREs at
   the *top* of `PublishErrorAndFailureCompletionAsync` — a new crash on the shared failure path for all
   collectors, opted-in or not. Probability is low (SDK initializes the dict non-null), but the failure
   path is the worst place to add an unguarded dereference. One-line guard aligns it with the session's
   defensiveness.

4. **[INFO] state.json bookkeeping stale.** `status: "contract_ready"`, `currentPhase:
   "contract_design"`, `requiredFiles["execution_notes.md"]: "pending"`, `lastUpdated:
   2026-07-06T13:50` all predate the recorded 14:20 execution completion. Steps S1–S6 and skillsRun are
   updated; the top-level fields were not. Contract's output format required state.json step updates —
   steps are done; the header fields are cosmetic drift.

5. **[INFO] Trailing-slash base is restored trimmed.** `ResolveBaseUrl` preserves `TrimEnd('/')` of the
   ingress value, so for an opted-in run whose RUN storageUrl had a trailing slash, DONE metadata
   differs from the ingress value by one character. Harmless — `NormalizeStorageUrlToKeyPrefix` trims
   both forms identically — and arguably an improvement; noted for byte-level expectations only
   (the byte-identical constraint applies to non-opted-in runs, which are unaffected).

## Edge-case Notes

- **Multipart stability**: one page's parts cannot split across folders. The multipart target path is
  bound once at `InitiateMultipartAsync` on the first part flush (`NdjsonBatchSession.cs:255`); later
  flushes upload parts to the same session — the per-flush `ApplyStorageUrlPrefix` recomputation (:212)
  only affects the single-upload path and logging. Under the documented usage, metadata is stable for
  the whole publish anyway (no mutation between `BeginPage` and publish completion).
- **Missing storageUrl**: `BeginPage` returns null and writes no keys; uploads stay relative —
  unchanged behavior, locked by test (:87).
- **s3:// scheme / NormalizeStorageUrlToKeyPrefix interplay**: the batch segment is appended after the
  full URL; normalization strips scheme+bucket and yields `key/batch_N/...` — locked by the
  egress-level test asserting the exact final key with an `s3://` base (:164).
- **Stale batch path redelivered on resume**: `ResolveBaseUrl` prefers the preserved `baseStorageUrl`
  key, so a resume event carrying a batch path as `storageUrl` *plus* the preserved base derives
  correctly (stack-free). If a host redelivered a batch-path `storageUrl` *without* `baseStorageUrl`,
  `BeginPage` would stack — theoretical: per the resume design the redelivered event carries the stable
  RUN-envelope root.
- **ERROR with isRetryable=true**: covered — `RestoreBase` runs at the top of
  `PublishErrorAndFailureCompletionAsync` (:57), before `PublishErrorAsync`; the failure completion is
  then correctly skipped for retryable errors.
- **Cancellation**: nothing published (pre-existing design, `AdapterBusEntrypointRunner.cs:129-143`);
  the last checkpoint's metadata legitimately carried that page's batch path (progress events are meant
  to announce it).
- **Metadata-by-reference side effect**: `BeginPage` mutates the shared `PlatformEvent.Metadata` dict
  (SDK assigns by reference). This is the designed vehicle (A1) and also why Gap 1 matters; the
  additive `baseStorageUrl` key appearing in host-echoed events is acknowledged in execution_notes and
  is harmless.

## Consistency

- Docs vs code: consistent (README ordering matches XML doc matches SKILL.md).
- execution_notes vs diff: accurate except the resume-normal-success claim (Gap 1) and the
  full-suite section (self-consistent: killed, evidence substituted).
- state.json vs reality: steps accurate; header fields stale (Gap 4).
- YamlCollector: untouched (not in diff). Checkpoint format: untouched (no Recovery serialization
  changes in diff). No collector wired: confirmed — `BeginPage` has no callers outside tests.

## Certainty

High on everything verified in-repo (hooks, tests, A5/A6, byte-identical default). Moderate on Gap 1's
runtime impact — the ISB fallback DONE's metadata source is host code outside this repo; the gap is a
missing *guarantee*, and the plausible failure mode is stated, not observed.
