# Orchestration Plan

## Complexity Decision
- Path: decompose
- Rationale: Not a scoring outcome — the execution shape is an operator-mandated hard constraint in
  `constraints.md`. The seam is implemented by the main agent and gated on green before any fan-out;
  then one worker per work item under strict file-touch partitioning. Left to the rubric this would
  likely have scored `direct` (three small, well-specified carries), but the mandate governs.

## Research Decisions
- None needed. Every carry has a verbatim source file in the read-only reference tree and the analysis
  in `PARITY_AUDIT.md` / `CARRY_PLAN.md` was grounded against live code. A1–A8 are already VALIDATED.
- A9 (is `ContentHashHex` reachable from `Emission.Tests`?) is OPEN but is internal-codebase
  uncertainty, not external-system behavior — it is resolved by W3 during execution, not by research.

## Seam (main agent, before fan-out)
Files owned exclusively by the main agent; no worker may touch them:
- `src/IntegrationInfra/Envelopes/Common/BatchScopedStorage.cs` — restore `BuildBatchInstanceId` + frozen namespace; set `instanceBatchId` from it
- `src/IntegrationInfra/Emission/Ndjson/NdjsonContentHasher.cs` — new, verbatim carry
- `src/IntegrationInfra/Emission/Ndjson/NdjsonBatchSession.cs` — 4 hasher lines
- `src/IntegrationInfra/Emission/Ndjson/NdjsonUtf8BatchSession.cs` — 4 hasher lines
- `src/IntegrationInfra/Emission/NdjsonBatchEmitter.cs` — `batchScopedStorage` flag on ctor + `Create`, the fold in the two generic page methods, the two `Hash={Hash}` log lines, and the concurrency doc-comment reconciliation

**Seam gate:** `dotnet build IntegrationInfra.slnx` clean + full suite run. The ONLY tolerated failures
are `tests/IntegrationInfra.Emission.Tests/BatchScopedStorageTests.cs:49` and `:145`. Anything else is a
STOP condition.

## Worker Plan
- W1 — scope: instanceBatchId + batch-scoping verification and Emission docs.
  inputs: aligned seam; Shared's `BatchScopedStorageTests.cs` as pin-test source.
  owns: `tests/IntegrationInfra.Emission.Tests/BatchScopedStorageTests.cs`, `src/IntegrationInfra/Emission/README.md`, `src/IntegrationInfra/Emission/README.Publishing.md`.
  output: the two pinned assertions flipped to UUIDv5, three carried pin tests, one test proving
  one-boolean opt-in (scoped path + dud-page RestoreBase), docs reflecting the new surface.
  dependencies: seam.
- W2 — scope: SessionAuthRetry carry.
  inputs: `Shared/Session/SessionAuthRetry.cs` + `SessionAuthRetryTests.cs` (read-only reference).
  owns: `src/IntegrationInfra/Conversation/SessionAuthRetry.cs` (new), a new test file in
  `tests/IntegrationInfra.Conversation.Tests/`, `src/IntegrationInfra/Conversation/README.md`.
  output: verbatim static carry + 4 passing tests + README entry.
  dependencies: none (fully disjoint from seam and from W1/W3) — may run concurrently with W1/W3.
- W3 — scope: content-hash observability tests only; no `src/` edits.
  inputs: aligned seam.
  owns: `tests/IntegrationInfra.Emission.Tests/` hash test file (new sibling preferred over editing
  `AtomicStreamedObjectsTests.cs` if cleaner — decided by W3, reported either way).
  output: test proving single-upload and multipart publication of identical content yield the same
  digest; test demonstrating a repeated digest across pages as the stuck-collector signal; resolution of
  assumption A9 (report, do not unilaterally add an accessor).
  dependencies: seam.

Partitioning check: no file appears in two workers' ownership, and no worker owns a seam file. W1 and W3
both write into `Emission.Tests` but to different files — W1 owns `BatchScopedStorageTests.cs`, W3 owns a
new hash test file and must not edit `BatchScopedStorageTests.cs`.

## Synthesis Approach
Main thread re-runs `dotnet build` + the full suite after all workers return, reconciles any
cross-worker fallout (both W1 and W3 add tests to the same project, so a duplicate helper or a
namespace collision is the realistic failure), and confirms every success criterion has a concrete
proof before verification.

## Verification Obligations
- Cross-check against every `prompt_contract.md` Success Criterion.
- `BuildBatchInstanceId` output matches Shared's golden vector byte for byte — the load-bearing upstream
  contract; verify by value, not by code inspection.
- Frozen namespace GUID unchanged: `b584b489-7c3d-4caf-97eb-49d7ff6d78fb`.
- Batch-level `PublishAsync`/`PublishUtf8Async` remain unscoped.
- The ~24 pre-existing emitter call sites still compile unchanged (proves the `= false` defaults held).
- `ContentHashHex` present on both sessions and in both publish-completed log lines.
- `PublishResult` NOT modified (SDK scope fence held).
- `SessionAuthRetry` still a static, replays exactly once, never loops.
- Both accepted limits documented on the ctor flag; emitter's concurrency doc comment reconciled.
- No csproj change, no version bump, no commit, no push.
