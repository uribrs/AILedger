# Verifier Report — cybi-batch-scoped-upload (verifier-2, post-repair)

Date: 2026-07-19. Scope: re-verification of the repairs for verifier-1 findings 1 and 2 only (finding 3 recorded as server-half contract note — confirmed present in execution_notes.md, no agent-side change, none required). Verified by reading the repaired code, independently recomputing the new UUID vector, and running the tests myself.

## Verdict

**PASS**

Both repairs are correct and are pinned by tests that would fail if the repair regressed. One residual limitation is inherited from the frozen diff surface, not from the repair (see Finding 1); it is acceptable and now consistent-state rather than lossy.

## Success Criteria Coverage (repaired areas)

### Repair 1 — completion-close commit ordering (CybiBatchUploader.cs) — MET
Verified by reading `Source/Infrastructure/Cymulate.Agent.Infrastructure.Common/Services/CybiBatchUploader.cs`:

- **Ordering**: `TryAppendFinalBatchCloseAsync` (lines 625-656) now only *builds* the close (sequenceId, itemCount, metadata with `instanceBatchId`) under the scope gate and returns `bool`; it no longer resets counters. `SendProgressUpdateAsync` commits via `CommitFinalBatchCloseAsync` (line 528) strictly **after** `PublishAsync` returns without throwing (line 518-524), inside the `try`. Commit (lines 658-683) resets counters and advances `CurrentBatchNumber` under the gate.
- **Failed-send path**: an exception from the POST skips the commit → counters intact → `CurrentBatchNumber` unchanged → a retried update rebuilds the same segment and the **same replay-stable `instanceBatchId`**, with a fresh (gapped) `sequenceId` — exactly as specified. Proven by `Armed_FinalCloseSendFails_FolderStaysOpenAndRetryReAnnouncesSameBatchId`: first send throws (FakeItEasy `.Throws(...).Once()` overrides the capture rule for exactly one call, so payload indexing in the test is correct — `[1]` is the retry), `failedSend == false`, retry carries `Batch1InstanceId`.
- **Double-call idempotency**: after a successful close+commit, `FilesInBatch == 0` makes both `TryAppend` (line 638) and `Commit` (line 669) no-ops; the same test's third call asserts the payload carries no `metadata`. The pre-existing `Armed_CompletionProgressUpdate_ClosesFinalPartialBatchExactlyOnce` still covers the plain double-call case.
- **Thread safety preserved**: append and commit each take the gate; no state is mutated outside it (the only mutation in append is the sequenceId increment, under the gate, intentionally allowed to gap on failure).

### Repair 2 — identity-aliasing test gap (CybiBatchScopedUploadTests.cs) — MET
- `Armed_ActionIdDiffersFromInstanceOid_UuidDerivesFromInstanceOidAndBothKeysScope` (lines 183-208) arms with `ActionId = "executor-action-1"` ≠ `InstanceOid = "000000000000000000000001"`, uploads once via each dictionary key with `MaxFilesPerBatch = 1`, and asserts:
  - upload via **instance-oid key** → `batch_000001`, `instanceBatchId = 02426957-2705-576a-bde8-8439617d1a82` (matches my verifier-1 Python recomputation of `000000000000000000000001/batch_000001`);
  - upload via **executor-action-id key** → `batch_000002` (proves the two keys share one run-global counter/state) with `instanceBatchId = 42b6f975-86cb-5e25-93b4-dcab84ae4472`.
- **Independent recomputation**: `python3 uuid.uuid5(UUID('b584b489-7c3d-4caf-97eb-49d7ff6d78fb'), '000000000000000000000001/batch_000002')` → `42b6f975-86cb-5e25-93b4-dcab84ae4472`. Matches the pinned constant. Because the upload key in that assertion is the *action id* while the UUID base is the *instance oid*, a wrong-base bug (verifier-1 finding 2a) now fails the test, as do dual-key registration loss (2c) and counter split-brain.

### Test runs — MET
- `dotnet test --filter FullyQualifiedName~CybiBatch` → **36/36 passed** (was 31; +2 new, +3 previously matched by the narrower `CybiBatchScope` filter — the `~CybiBatch` filter also catches the storage tests; count as expected).
- Full `Cymulate.Agent.Infrastructure.Common.Tests` project → **207 passed / 0 failed / 21 skipped** (was 205; +2 new tests, no regressions).

## Findings

1. **[Low — residual, inherited from frozen diff surface] A failed final-close send is consistent but still unrecovered in practice.**
   The repair makes the state correct (folder stays open, retry re-announces), but the only caller — `CybiIntegrationAction.SendBatchUploadCompletionAsync` (`Source/Application/.../CybiIntegrationAction.cs:245`) — still ignores the return value and never retries, and that file is in `Application.Actions`, which the constraints freeze. So today a transient failure on the last progress update still ends with an unannounced final folder and a success `stop`. If this matters operationally, the in-surface fix is a bounded retry *inside* `SendProgressUpdateAsync` (Infrastructure.Common) — deliberately not added here to avoid unrequested scope expansion. Carry as a follow-up candidate alongside the A2 server-half work.

2. **[Info — theoretical only] Append→commit window is not atomic with respect to concurrent scoped uploads.**
   If a scoped upload landed between the close-append and the commit, its file would join a folder that the in-flight update is announcing (and the commit's `FilesInBatch == 0` guard would then not fire because the count is non-zero — it would reset counters including the interloper's). In production this cannot happen: the single `SendProgressUpdateAsync` call site runs after all collector flows complete. Recording only so a future caller pattern (mid-run progress updates) knows this invariant is load-bearing.

## Summary of Evidence

- Code read: `CybiBatchUploader.cs:465-683` (repaired ordering).
- Tests read: `CybiBatchScopedUploadTests.cs:183-237` (two new tests; FakeItEasy rule-ordering checked — payload capture indexing is correct).
- `python3 uuid.uuid5` → new pinned vector independently confirmed.
- Filtered tests 36/36, full project 207/0/21 — both run by me, post-repair.
- execution_notes.md "repairs after verifier-1" section is consistent with the actual diff, including the finding-3 server-half contract note (batchedAssets/batchedFindings must be mapped from `batch_file` events only).
