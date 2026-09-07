# Verifier Report — cybi-batch-scoped-upload (verifier-1)

Date: 2026-07-19. Verified by reading code and running commands (build, tests, independent UUID recomputation, diff audit). Working tree on branch `colelctors-patch_id-upload`, uncommitted.

## Verdict

**PASS WITH GAPS**

The mechanism is a faithful mirror of the adapters chain adapted to the agent's POST-body-only control surface, the frozen contracts are untouched, the dormant path is byte-identical by test, and the UUID port is an exact algorithmic match with independently recomputed pinned vectors. The gaps are: (1) a commit-before-send asymmetry on the completion-time folder close, (2) test identity aliasing (ActionId == InstanceOid in every armed test) that blinds the suite to a class of wrong-id bugs in exactly the code path production uses, and (3) the execution notes' full-solution-build claim is not reproducible in this environment (environmental, not caused by this work).

## Success Criteria Coverage

### 1. Build clean; new tests green — MET WITH CAVEAT
- `dotnet build AgentService.sln` (plain Debug, the only solution config that exists — DebugMac is not a solution configuration) **fails in this environment** at `Cymulate.SmtpClient` with MSB3644 (.NET Framework 4.8.1 targeting pack absent on macOS). Verified **pre-existing**: same failure on pristine HEAD via stash; the project is untouched by this change. All 50 warnings, 1 error — the one error is that project.
- Touched projects verified directly: `Cymulate.Agent.Executor.csproj` builds with 0 errors (transitively builds Infrastructure.Common + DI).
- `dotnet test Tests/Infrastructure/Cymulate.Agent.Infrastructure.Common.Tests` run by me: **205 passed / 0 failed / 21 skipped**. Filtered run of the new tests: **31/31 passed** (`FullyQualifiedName~CybiBatchScope`).
- Executor tests re-run by me: 6 failed / 66 passed — failure signatures are `Cryptographer.Decrypt` / IntegrationActionManager + Bas2 fixtures, unrelated to this diff, matching the stated pre-existing count.
- Caveat: execution_notes.md claims "`dotnet build AgentService.sln` — Build succeeded, 0 errors", which I could not reproduce (SmtpClient env failure). Not attributable to this work, but the note overstates.

### 2. Dormant-path byte-identity proven by test — MET
- `CybiBatchScopedUploadTests.Dormant_SaveUploadAndDelete_PayloadIsByteIdenticalToLegacyFormat` and `Armed_OtherActionId_RemainsDormant` compare the **entire captured HTTP payload string** against `BuildExpectedLegacyPayload`, which reconstructs the wire format independently (raw string concatenation + `JsonConvert.ToString`), not via the production builder. Full-string equality catches an added field, a reordered field, or an escaping change. Test data includes a quote, newline, tab, and multi-byte emoji, exercising the streaming escaper.
- Cross-checked structurally: the new `BuildBatchRequestHead` is an append-for-append identical refactor of HEAD's `BuildBatchRequestPrefix` (verified against `git show HEAD:...CybiBatchUploader.cs`).
- Minor gap: the dormant `UploadBatchAsync` (temp-file) variant is not byte-identity tested; it shares `UploadBatchFileAsync` with `scopeContext: null` so coverage is transitive. Dormant `SendProgressUpdateAsync` is checked for key absence only (its construction is diff-verified unchanged).

### 3. UUID vectors independently recomputed; port matches adapters — MET
- Recomputed with `python3 uuid.uuid5(UUID('b584b489-7c3d-4caf-97eb-49d7ff6d78fb'), name)`:
  - `6a54eecdab590c9b8dc13af3/batch_000001` → `d3070c71-5d51-55dc-a36c-a07fdef35ae1` ✓
  - `6a54eecdab590c9b8dc13af3/batch_000002` → `81d8649f-8b31-513f-978c-2003bbddb149` ✓
  - `000000000000000000000001/batch_000001` → `02426957-2705-576a-bde8-8439617d1a82` ✓
  All three match the pinned constants in `CybiBatchScopedStorageTests.cs`.
- Line-by-line comparison of `CybiBatchScopedStorage.BuildBatchInstanceId(string)` against adapters `BatchScopedStorage.BuildBatchInstanceId` (`/Users/user/Dev/cymulate-integration-adapters/.../BatchScopedStorage.cs:127-146`): identical algorithm — `byte[16 + UTF8 count]`, `TryWriteBytes(bigEndian: true)`, UTF8 name at offset 16, `SHA1.HashData` into a 20-byte stackalloc span, `hash[6] = (hash[6] & 0x0F) | 0x50`, `hash[8] = (hash[8] & 0x3F) | 0x80`, `new Guid(hash[..16], bigEndian: true).ToString()` (lowercase hyphenated). Namespace frozen and identical. Name shape (`{24-hex oid}/batch_N` vs adapters' `{URL}/batch_N`) cannot collide with adapter-minted ids.

### 4. Diff surface audit — MET
`git status --short` shows exactly:
- M `Source/Infrastructure/Cymulate.Agent.Infrastructure.Common/Services/CybiBatchUploader.cs`
- M `Source/Infrastructure/Cymulate.Agent.Infrastructure.DI/Extensions/ServiceCollectionExtensions.cs`
- M `Source/Presentation/Cymulate.Agent.Executor/Actions/CyBI/CybiActionManager.cs`
- ?? `CybiBatchScopedStorage.cs`, `ICybiBatchScopeController.cs` (Infrastructure.Common)
- ?? three test files under `Tests/Infrastructure/Cymulate.Agent.Infrastructure.Common.Tests/Services/`
- ?? `ai/` (task files)

`ICybiBatchUploader` interface file untouched. Zero collector-project, `Application.Actions`, or `Application.Common` changes. The uploader implements the new `ICybiBatchScopeController` alongside the frozen interface; DI registers one singleton behind both.

### 5. Contract constraints spot-checks — MET
- **Announce-once-per-folder**: `BuildScopeUploadContext` sets `includeBatchInstanceId` only when `closesBatch`; `CommitScopedUpload` resets counters and advances `CurrentBatchNumber` on close, so a fresh folder never re-announces. Completion path (`TryAppendFinalBatchCloseAsync`) announces the open partial folder once and is idempotent thereafter (`FilesInBatch == 0` guard). Both tested: `Armed_ClosingUpload_AnnouncesBatchInstanceIdOnceAndAdvancesFolder`, `Armed_CompletionProgressUpdate_ClosesFinalPartialBatchExactlyOnce` (double-call proves once-only), `Armed_FailureProgressUpdate_DoesNotAnnounceOpenBatch`.
- **Metadata field names**: compared against `CollectorProgressMetadata` in `/Users/user/Dev/cymulate-integrations/.../connector-manager.service.ts:23-35` — `instanceOid`, `instanceId`, `clientID`, `integrationSettingId`, `integrationSettingFlowId`, `clientIntegrationFlowId`, `clientIntegrationId`, `storageUrl`, `instanceBatchId` all match exactly (consumer reads `metadata.instanceBatchId || payload.instanceBatchId`, `metadata.storageUrl`, `clientID || clientId`). Consumer-shape assertions exist in `CybiBatchScopeOptionsTests`.
- **sequenceId monotonic**: per-run `++scope.NextSequenceId` under the scope's `SemaphoreSlim`; failures burn a sequence number (gap), which satisfies the consumer's `$lt` guard.
- **Local batch subdirectory**: `Directory.CreateDirectory(batchFolder)` before write; proven on disk by `Armed_FailedUpload_KeepsScopedFileAndDoesNotAdvanceCounters`.
- **stop/collection_complete unchanged**: `SendCompletionAsync` has zero diff.
- **Thread safety**: all scope mutation (`NextSequenceId`, folder counters, close decision) happens under the per-scope `SemaphoreSlim`; both dictionary keys (`ActionId`, `InstanceOid`) map to the same `BatchScopeState` so there is no split-brain; dormant `TryGetScope` reads a `ConcurrentDictionary`. Scoped uploads are serialized per run (acknowledged concurrency trade-off in execution notes; uploads were already effectively bounded by network).
- **Arming wiring verified end-to-end**: collectors pass the integration instance `_id` as the `actionId` argument (verified in ServiceNowCmdb, CortexXdr, Taegis, EntraId, Falcon, InsightVm call sites), and the single `SendProgressUpdateAsync` call site (`CybiIntegrationAction.SendBatchUploadCompletionAsync`, called exactly once per run with `rInstanceId`) is why `ArmBatchScope` registers the scope under both the action id and the instance oid — both keys are load-bearing and present. `TryArmBatchScope` is gated to `eActions.CybiRunIntegration`, catches everything, and can only fall back to dormant. `ICybiBatchScopeController` resolves via `ActivatorUtilities.CreateInstance` from the same provider that runs `AddAgentServices` → `AddCybiBatchUploader`.

### 6. Consistency + edge cases — MOSTLY MET (see Findings 1-3)

### 7. Coding standards — MET WITH ONE NIT
New code: `_camelCase` private fields, PascalCase public consts, explicit types (no `var`), braces everywhere, no legacy `m/r/s/i` prefixes in new members, camelCase parameters (`batchScopeController`, not `iBatchScopeController`), comments explain non-obvious invariants only. `dotnet format --verify-no-changes` on touched files reports only ENDOFLINE errors — **environmental**: HEAD blobs are LF, `.editorconfig` says `end_of_line = crlf`, `core.autocrlf=input`; every file in the repo fails this check on this machine. Nit: ctor parameter placement (Finding 5).

## Request Coverage

The original ask — mirror the adapters' batch-scoped storage + `instanceBatchId` mechanism into the AgentService CYBI batch upload path, precisely, given the agent controls only the POST body — is satisfied:
- Deterministic `batch_{N:D6}` folders mirrored on disk and in the wire `fileName` (path prefix), the agent-side equivalent of adapters' S3 subfolder scoping.
- `instanceBatchId` = exact adapters UUIDv5 algorithm, frozen namespace, replay-stable name base (`instanceOid/batch_N`).
- ISB-shape `metadata` + monotonic `sequenceId` + `itemCount`, matching the merged consumer's `CollectorProgressMetadata`.
- Announce-once-per-folder honoring the consumer's no-guard Glue trigger (A5).
- Dormant by default, byte-identical; server-driven opt-in placeholder key `batchScopedUpload` (A1) and provisional thresholds (A6) documented.
- CyAgentServer passthrough (A2) correctly out of scope but the agent side is built to final shape.

## Findings

1. **[Medium] Completion-time folder close commits state before the send succeeds.**
   `Source/Infrastructure/Cymulate.Agent.Infrastructure.Common/Services/CybiBatchUploader.cs:641-644` — `TryAppendFinalBatchCloseAsync` resets `FilesInBatch`/`RowsInBatch` and advances `CurrentBatchNumber` when *building* the payload; if the subsequent POST in `SendProgressUpdateAsync` throws, the final folder is permanently unannounced (a retry hits the `FilesInBatch == 0` guard). The per-file close path has the opposite (correct) ordering: `CommitScopedUpload` runs only after upload success. Compounding it, the caller (`CybiIntegrationAction.SendBatchUploadCompletionAsync`, `Source/Application/.../CybiIntegrationAction.cs:245`) ignores the return value and still sends the success `stop`, so a transient network blip on the last progress update silently loses the final partial batch's parse. Practical impact is bounded (the DONE flow may still process the run), but the asymmetry is real and unacknowledged in execution notes.

2. **[Medium — test gap] Every armed test uses `ActionId == InstanceOid`, masking wrong-id bugs on the production path.**
   `Tests/.../CybiBatchScopedUploadTests.cs:8-9` (`InstanceOid = ActionId`). Consequences: (a) if the UUID name base mistakenly used the action id instead of the instance oid, or (b) `storageUrl` used the wrong id, or (c) the dual-key registration in `ArmBatchScope` (`CybiBatchUploader.cs:47-48`) silently lost one key, all tests would still pass. Production collectors key uploads by the **instance oid** (verified call sites) and the completion update uses `rInstanceId`, so the `InstanceOid` dictionary key — the one actually exercised in production — has no dedicated test. One test arming with distinct ids and uploading via the instance-oid key would close this.

3. **[Info — cross-repo note for the A2 server half] `itemCount` has two semantics.**
   Per-file uploads (including folder-closing ones) carry that file's row count; the completion-time close carries the whole open folder's accumulated rows (`scope.RowsInBatch`). Those same rows were already reported per-file. The CyAgentServer mapping (decisions.md: `batchedAssets`/`batchedFindings` derived from `assets_`/`findings_` fileName prefix) must not map the completion event's `itemCount` into `batched*` or the final folder's rows double-count in the consumer's `$inc`; the completion event's lack of a `fileName`/`stage` is what protects it today. Document this in the server-half task.

4. **[Low] Failed folder-closing upload can double-trigger Glue if the "failed" POST actually landed.**
   `CybiBatchUploader.cs:218-226` — on failure the close isn't committed and is re-announced with the same `instanceBatchId` on retry. If the first POST timed out client-side but was processed server-side, the consumer triggers Glue twice on the folder (it triggers on every event carrying the id, per A5). Mitigated by the replay-stable id (batch doc upserts converge) and by today's collector behavior (they abort on upload failure). Consistent with the residual-risks note.

5. **[Nit — standards] Ctor parameter inserted mid-list.**
   `Source/Presentation/Cymulate.Agent.Executor/Actions/CyBI/CybiActionManager.cs:39` — `batchScopeController` was added before `iHttpRequestTimeout`/`iActionDisconnectionTimeout` rather than at the end of the real parameter list (ReadMEs/coding-standards.md, Parameter Ordering). Harmless at runtime (`ActivatorUtilities` matches by type) and arguably keeps DI-resolved services grouped, but it's a literal deviation.

6. **[Info — environment] Execution-notes build claim not reproducible; format check fails repo-wide.**
   Full-solution build fails at untouched `Cymulate.SmtpClient` (MSB3644, macOS, pre-existing on HEAD — verified by stash). `dotnet format --verify-no-changes` fails with ENDOFLINE on every file in this environment (LF blobs vs `crlf` editorconfig). Neither is caused by this work; both mean the two "clean" claims in execution_notes.md hold only modulo environment.

7. **[Low — inherited behavior] Cross-run `sequenceId` reset depends on consumer-side `batchingStats` reset.**
   The agent's sequence restarts at 1 each run; the consumer filters on persisted `batchingStats.lastSequenceId $lt sequenceId`. If the consumer does not reset `batchingStats` at run start, a shorter re-run's updates are all rejected. This is identical to the adapters chain's shape (same consumer), so not a defect of this work — noting for completeness.

## Untested Edge Cases

- Armed run where `ActionId != InstanceOid` (see Finding 2) — the actual production configuration.
- Uploads keyed by the instance-oid dictionary entry (the key production collectors hit).
- Failure of the completion progress POST after the final close was appended (Finding 1) — no test pins the current (lossy) behavior.
- Dormant `UploadBatchAsync` temp-file path byte-identity (covered only transitively via the shared builder).
- Concurrent scoped uploads (the gate serializes them; tests are sequential — acceptable since the gate makes interleaving impossible by construction).
- `instance._id` arriving as a non-string JSON token (e.g. `{$oid:...}`) — `ToString()` would produce a JSON blob as the oid; same pattern as the existing `enrichCredentialsForBatchUpload`, so consistent precedent, but neither is guarded.
- `maxFilesPerBatch`/`maxBatchContentBytes` sent as non-integer JSON (e.g. string `"3"`) — `Value<int>()` converts or throws inside `TryParse`'s… actually outside its `try` (only `JObject.Parse` is guarded); a type-mismatch here would throw from `TryReadOptIn` and be swallowed by `TryArmBatchScope`'s catch in the executor, falling back to dormant. Safe, but reliant on the outer catch.

## Commands Run (evidence)

- `dotnet build AgentService.sln` → 1 error (SmtpClient MSB3644, pre-existing on HEAD via stash-verify)
- `dotnet build .../Cymulate.Agent.Executor.csproj` → 0 errors
- `dotnet test .../Cymulate.Agent.Infrastructure.Common.Tests.csproj` → 205 passed / 0 failed
- `dotnet test --filter FullyQualifiedName~CybiBatchScope` → 31/31 passed
- `dotnet test .../Cymulate.Agent.Executor.Tests.csproj` → 6 failed / 66 passed (crypto/fixture failures, matching pre-existing claim)
- `python3 uuid.uuid5` recomputation → all 3 pinned vectors match
- `git status --short`, `git diff` → diff surface exactly as contracted
- `dotnet format --verify-no-changes --include <touched>` → ENDOFLINE only (environmental)
