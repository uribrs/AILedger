# Execution Notes

## 2026-07-06 — contract-driven-execution (direct path)

### What changed

New:
- `Shared/.../DataPipeline/Egress/BatchScopedStorage.cs` — public static helper:
  - `BeginPage(progressContext, pageNumber)` → preserves pristine base under
    `Metadata["baseStorageUrl"]` (first use), sets `Metadata["storageUrl"] = {base}/batch_{page:D6}`,
    returns the batch path. No-op (returns null) when the run has no storage URL.
    Always derives from the preserved base → no stacking. `page >= 1` guarded (matches
    `CollectorOutputDefaults`).
  - `RestoreBase(progressContext)` → restores the live key to the base; strict no-op when
    `BeginPage` never ran (non-opted-in collectors unaffected).
  - `BuildBatchSegment(page)` → `batch_{page:D6}`.
  - XML doc carries the opt-in usage contract and the ordering invariant
    (BeginPage → publish → [RestoreBase on dud] → AdvancePage → next BeginPage).

Restoration hooks (all one-line `RestoreBase` calls, no-op unless opted in):
- `Orchestration/AdapterBusEntrypointRunner.cs` — before success DONE (`CompletionRequest(true)`).
- `Orchestration/AdapterFlowFailureHandling.cs` — at `PublishErrorAndFailureCompletionAsync` start
  (covers ERROR + failure DONE).
- `Orchestration/Bus/Logic/AdapterBusPartialSuccessPublisher.cs` — before partial-success DONE.
- `Orchestration/Collectors/Recovery/CollectorResumePartialSuccessPublisher.cs` — before resume
  partial-success DONE.
- `Resilience/Logic/AdapterFailureDecisionExecutor.cs` — in `PersistDeferredWaitSnapshot`, before
  the `OnCheckpoint` snapshot (deferred-recovery wait events carry the run root).

Tests:
- `UnitTests/Shared/.../DataPipeline/Egress/BatchScopedStorageTests.cs` (12 tests): derive-from-base /
  no stacking, deterministic naming + same-page-after-resume determinism, D6 padding, trailing-slash
  normalization, missing-storageUrl no-op, page-guard, dud-page restore + later derivation,
  never-scoped no-op (default behavior unchanged), ordering-invariant page lifecycle, and two
  egress-level tests proving `ResultsBatchPublisher` uploads land under `batch_N` when scoped and at
  the run root after `RestoreBase` (locks the live-metadata-read behavior of the NDJSON session).
- `UnitTests/Collectors/...DummyCollector.Test/AdapterFailureDecisionExecutorTests.cs` — new test:
  deferred-wait snapshot restores the run-root storage URL when the failure hits mid-scoped-page.

Docs:
- `Shared/.../DataPipeline/Egress/README.md` — BatchScopedStorage entry (deterministic naming is
  load-bearing; ordering contract).
- `ai/skills/collector-flow-patterns/SKILL.md` — hard rule for opting in.

### Validation
- A5 VALIDATED: only ingress writes `Metadata["storageUrl"]`; sessions read-only.
- A6 VALIDATED: D6 + `>= 1` matches `CollectorOutputDefaults.BuildPageTargetPath`.
- `dotnet build Cymulate.Integration.Adapters.sln` — 0 errors (2 pre-existing YamlCollector warnings).
- New tests: 12/12 passed (Shared.Tests), executor tests 19/19 passed (incl. new one).
- Full suite: run at phase boundary (result recorded below when complete).

### Design notes / residual risks
- The base is preserved in a metadata key (`baseStorageUrl`) rather than in-memory — it therefore
  appears in host-echoed event metadata. Judged harmless (additive key; also lets upstream see the
  run root on scoped events). If upstream objects, switch to an in-memory carrier the collector owns.
- Dud-page handling is caller-side by design (`result.RecordCount == 0 → RestoreBase`) — the shared
  egress cannot know a page is empty before the stream is consumed; documented in the usage contract.
- ~~Resume normal-success completion flows through the same shared publishers~~ — WRONG, caught by
  verifier-1 (gap 1): the resume executors return success without publishing DONE; the host publishes
  a fallback DONE from live metadata. Repaired: `RestoreBase` added to the success legs of
  `CollectorResumeStrategyExecutor` and `CollectorResumeLegacyExecutor`.

## 2026-07-06 — verifier-1 repairs

- Gap 1 (MEDIUM): resume-success `RestoreBase` hooks added (both executors, see above).
- Gap 3 (LOW): `BatchScopedStorage.BeginPage`/`RestoreBase` now null-guard `Metadata` (matches the
  defensive pattern in `NdjsonBatchSession.ApplyStorageUrlPrefix`).
- Gap 2 (LOW): accepted — the ordering invariant is guarded by doc + SKILL.md rule + the lifecycle
  test under correct usage; a misuse-detection mechanism would need SDK support. Accepted risk.
- Gap 4 (INFO): state.json header fields refreshed.
- Rebuild: 0 warnings / 0 errors. Shared BatchScopedStorage tests 12/12.
- verifier-2 verdict: PASS (review/verifier-2.md).

## 2026-07-06 — code-reviewer-1 outcome (review/code-reviewer-1.md)

Verdict: sound, no blockers. Repairs applied:
- Finding 1 (major, host-contingent): `ResolveBaseUrl` now strips an exact trailing `batch_\d{6}`
  segment (`StripBatchSegment`) — a resume message that round-trips a scoped path can no longer nest
  batch folders. Two new tests pin strip + non-strip (`batch_00000X` survives). 14/14 green.
- Finding 3 (minor): metadata-key literals unified on `BatchScopedStorage.StorageUrlMetadataKey` in
  both NDJSON sessions, `AdapterBusEntrypointRunner`, and `AdapterPlatformEventFactory` (JSON wire
  field literal kept as-is — different contract).

Accepted risks (recorded, not repaired):
- Finding 2: run-root restoration relies on 7 explicit `RestoreBase` call sites, no structural choke
  point — a wrapper over completion/error publication would be new abstraction ahead of need; revisit
  at first collector opt-in.
- Finding 4: only the resilience-snapshot restore site is directly test-pinned; entrypoint-runner-level
  pinning needs heavy scaffolding — deferred to the opt-in pass's integration tests.
- Verifier gap 2: ordering-invariant misuse (early BeginPage) is guarded by docs/SKILL rule, not
  detectable by tests without SDK support.

Post-review rebuild: 0 warnings / 0 errors; BatchScopedStorage suite 14/14. (Strip + const changes do
not alter request coverage; verifier not re-run per orchestration rules.)

## 2026-07-06 — wiring pass: Qualys + InsightVM Cloud opt in

Opt-in is a CAPABILITY CONSTANT: `BatchScopedStorage { get; init; } = true` hardcoded on the
collector's *Configuration record — never a payload/backend knob (operator directive).

- `QualysCollectorConfiguration.BatchScopedStorage = true`; flag threads
  `QualysFindingsFlow` → `QualysFindingsBatchPublisher` (BeginPage before publish, RestoreBase on
  0-record publish, AdvancePage unchanged). Qualys has only the findings flow; empty batches are
  skipped pre-publish (`Hosts.Count == 0 → continue`), the RecordCount guard is belt-and-braces.
- `InsightVmCloudCollectorConfiguration.BatchScopedStorage = true`; flag threads
  `InsightVmCloudCollectorFlowRunner` → both flows → both page publishers (same pattern, both lanes).
  All construction funnels through the config-holding runners (ProcessAsync + ResumeAsync verified).
- Version bumps: both collectors → 5.0.0 (output layout is a consumer-facing contract change).
- Tests: `QualysBatchScopedStorageTests` (3: capability constant, batch-scoped target+announcement,
  flag-off flat layout), `InsightVmCloudBatchScopedStorageTests` (4: constant, both lanes scoped,
  flag-off flat). Existing InsightVmCloud ProcessAsync e2e tests updated to pin the new layout
  (batch_00000N/ in expected target paths) — they now guard the wiring at collector level.
- Evidence: build 0 warnings/0 errors; Qualys suite 25/25; InsightVmCloud suite 19/19.
- Local-runner check deferred: needs vendor credentials; collector-level e2e tests (mocked HTTP)
  cover the same path through ProcessAsync.

## 2026-07-06 — code-reviewer-2 outcome (review/code-reviewer-2.md)

Verdict: sound, no blockers. Contract adherence, bypass sweep, resume-flag persistence, and
scoping-window integrity all independently verified.

Repaired:
- Finding 1 (minor, verified real): Qualys double `AdvancePage` per page — publisher advanced BEFORE
  the checkpoint writer set state (stale first checkpoint), then the writer advanced again (double
  page increment + two progress events per batch path). Removed the publisher's advance; the
  checkpoint writer is the single owner (state-first ordering). One progress event per page now
  announces the batch path on a consistent checkpoint. Pre-existing at HEAD; became material with
  per-path parse triggering.
- Finding 2 (nits): both stale ordering comments corrected (Qualys deferred-snapshot reference;
  IVMC assets comment now documents the real advance/state ordering and why the one-page resume
  overlap is safe under deterministic keys).

Noted, not changed:
- IVMC checkpoint state saved after the in-publisher advance (checkpoint lags one page) —
  pre-existing; resume re-publishes at most one page onto the same deterministic keys. Documented
  in-code; a real fix belongs to an IVMC-scoped change, not this wiring.
- Defaulted `batchScopedStorage = false` ctor params are a silent-opt-out hazard for future call
  sites — accepted; the capability constant on *Configuration is the single source.

Post-repair evidence: build 0 warnings/0 errors; Qualys 25/25; InsightVmCloud 19/19.

### Full-suite result
- Full-suite run killed by operator mid-run (per repo guidance on long test runs in this harness).
- Evidence relied on instead: solution build 0 errors; Shared.Tests BatchScopedStorage 12/12;
  DummyCollector AdapterFailureDecisionExecutorTests 19/19 (includes the new snapshot-restore test).
- Residual: untouched test projects not re-run locally; CI remains the backstop.
