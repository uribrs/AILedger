# Constraints

## Branch / process

- Branch off `origin/dev` (`ad2699a`); never commit to `dev`/`master` directly.
- Do NOT commit or push. Push authorization is per-change and must be requested explicitly for THIS change.
- No version bump in any csproj. Versions live in `Directory.Build.props` defaults only; bumping is the operator's call — suggest, never set.
- The adapters repo is a READ-ONLY reference source: `/Users/user/Dev/cymulate-integration-adapters/.claude/worktrees/dev-work/src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared`. Modify nothing there.
- `ai/` is gitignored; mirror the task dir to `~/codex-state/tasks/IntegrationInfra/2026-07-30_1108_shared-parity-carry/`.
- Build with `dotnet build IntegrationInfra.slnx`. If `dotnet test` hangs, stop and rely on verifier agents.
- Do not name any identifier "Legacy".

## Execution shape (operator-mandated, hard)

- The main agent implements the SEAM ITSELF. No subagent touches a seam file.
- Seam = `Envelopes/Common/BatchScopedStorage.cs`, `Emission/Ndjson/NdjsonContentHasher.cs` (new), `Emission/Ndjson/NdjsonBatchSession.cs`, `Emission/Ndjson/NdjsonUtf8BatchSession.cs`, `Emission/NdjsonBatchEmitter.cs`.
- The seam is deliberately wider than the single collided file: the emitter's `Hash={Hash}` log lines cannot compile before the hasher and the sessions' `ContentHashHex` exist.
- Seam must build clean and run the full suite BEFORE any fan-out. The only tolerated failures at that gate are the two known pinned assertions (below), which are W1's to fix.
- Then exactly one subagent per work item, with file-touch partitioning: no two workers touch the same file.

## Behavior constraints

- `BatchIdNamespace` = `b584b489-7c3d-4caf-97eb-49d7ff6d78fb`, frozen forever. Never regenerate, never parameterize.
- `BuildBatchInstanceId` must be byte-for-byte identical in output to Shared's: RFC 4122 v5 (name-based, SHA-1), big-endian namespace bytes, `hash[6] = (hash[6] & 0x0F) | 0x50`, `hash[8] = (hash[8] & 0x3F) | 0x80`, lowercase hyphenated.
- Batch scoping applies in the two generic page methods (`PublishUtf8PageAsync`, `PublishPageAsync`) ONLY. All four named page methods delegate to those two (verified) — do not duplicate the logic into them.
- Batch-level `PublishAsync`/`PublishUtf8Async` stay UNSCOPED (explicit `targetPath`, no page number).
- Scoping order is load-bearing: `BeginPage(progressContext, pageNumber)` before the publish; `RestoreBase(progressContext)` when `result.RecordCount == 0`.
- `bool batchScopedStorage = false` on both the ctor and `Create(IServiceProvider)` — the `= false` default is required so the ~24 existing emitter test call sites compile unchanged.
- Content-hash semantics: flushed payloads append in UPLOAD ORDER, so a single-upload publish and a multipart publish of identical content yield the SAME digest. The digest describes the final logical object, never an individual multipart part, and is comparable to `sha256sum` of the stored object.
- `SessionAuthRetry` stays a static — it is a sanctioned explicit-args/pure-function static under DESIGN.md's static taxonomy. Do NOT reshape to an instance.
- `SessionAuthRetry` replays EXACTLY ONCE and never loops; returns a second 401/403 unchanged.
- `ct` flows into both sends AND the refresh delegate.
- Returned `HttpResponseMessage` is caller-owned and must NOT be disposed inside the helper; both attempt requests must be `using`-disposed.

## Scope fences

- OUT: adding `ContentHashHex` to `PublishResult`. That type lives in the SDK (`src/Cymulate.Integration.Sdk/Contracts/PublishRequests.cs:145`); surfacing it belongs to `2026-07-08_1720_sdk-physical-absorption`.
- OUT: the four places where Infra is already ahead of Shared (converter recursion fix, cancellation-proof failure publication, host-callback isolation, log redaction). Those are Shared-side backports, not this task.
- OUT: any change to consumer collectors in the adapters repo.
- OUT: csproj changes. `Cymulate.Http.Package.Session` is already a direct `PackageReference`; `IHttpSession` is the only new import.

## Known conflict — handle, do not discover

- `tests/IntegrationInfra.Emission.Tests/BatchScopedStorageTests.cs:49` and `:145` assert `instanceBatchId == "batch_000001"` / `"batch_000002"`. Both MUST flip to the UUIDv5 expectation — they currently pin the regression.
- `:139` checks key presence only and survives unchanged. Every other `batch_` assertion in that file is about the storage path and is unaffected.
