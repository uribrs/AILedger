# Assumptions

## A1 — Audit baseline still applies to origin/dev — VALIDATED
The parity audit ran against `absorb/sdk-source` @ `40f828a`. `origin/dev` @ `ad2699a` is the merge of
that branch (PR #4). `git diff --stat 40f828a ad2699a -- src/ tests/` is empty, so the tree is
content-identical and every audit finding holds verbatim. Local `dev` is 2 commits behind `origin/dev`
— branch off the remote, not the local ref.

## A2 — The segment-string instanceBatchId was drift, not a decision — VALIDATED
Checked every prior Infra task archive under `~/codex-state/tasks/IntegrationInfra/` for an
instanceBatchId/UUID decision: none exists. Shared's UUIDv5 change landed `7934f07` on 2026-07-08, one
day AFTER the `2026-07-07_1730_atomic-objects-and-batch-scoping-carry` task carried batch scoping. So
this is carry drift and there is no prior decision being reversed.

## A3 — All four named page methods delegate to the two generic ones — VALIDATED
Read `Emission/NdjsonBatchEmitter.cs:92-135`: `PublishFindingsUtf8PageAsync`,
`PublishAssetsUtf8PageAsync`, `PublishFindingsPageAsync`, `PublishAssetsPageAsync` are all
expression-bodied delegations to `PublishUtf8PageAsync` / `PublishPageAsync`. Folding scoping into the
two generic methods therefore covers every page-level entry point with no duplication.

## A4 — Adding the ctor param does not churn existing tests — VALIDATED
The ~24 emitter call sites all go through `NdjsonBatchEmitter.Create(ctx.Services)` (grep-confirmed
across `AtomicStreamedObjectsTests`, `BatchScopedStorageTests`, `NdjsonBatchEmitterReuseTests`). With
`bool batchScopedStorage = false` defaults on both ctor and `Create`, every one of them compiles and
behaves identically.

## A5 — `ContentHashHex` has exactly one consumer — VALIDATED
Grep across the adapters repo: the only reads are `ResultsBatchPublisher.cs:151` and `:290`, both the
`Hash={Hash}` field of the publish-completed log line. Nothing else consumes it, so restoring it
cannot change any other behavior.

## A6 — SessionAuthRetry needs no new dependency — VALIDATED
Its only import beyond BCL is `IHttpSession` from `Cymulate.Http.Package.Session.Contracts.Interfaces`,
already a direct `PackageReference` in `IntegrationInfra.csproj` (used by `Conversation`).

## A7 — The AdvancePage ordering rule cannot move into the emitter — VALIDATED (accepted limit)
Rule "BeginPage for page N+1 only after page N's AdvancePage" depends on `AdvancePage`, which is the
collector's call and sits outside the emitter. It stays consumer-side. Any sequential page loop
satisfies it, so no current consumer is at risk. Must be documented, not silently dropped.

## A8 — Batch scoping is not concurrency-safe on a shared progress context — VALIDATED (accepted limit)
`BeginPage`/`RestoreBase` mutate `progressContext.Metadata["storageUrl"]`. Concurrent page publishes
against the SAME progress context would race. Already true in Shared; the ordering contract implies
sequential page publishing. Must be documented on the ctor flag — do NOT claim the emitter is
concurrency-safe for this path (its existing doc comment claims safety for concurrent publishes and
that claim now needs the scoping caveat).

## A9 — Digest stability across single-upload vs multipart is testable in-process — VALIDATED
Resolved during execution by W3. `ContentHashHex` IS reachable from `Emission.Tests`
(`InternalsVisibleTo` at `IntegrationInfra.csproj:31`). W3 chose to observe the digest through a capturing
`ILogger` reading the structured `Hash` field rather than by constructing a session directly, on the
grounds that the log line is the ONLY operator-visible surface — a session-level test would still pass if
the emitter stopped logging the digest, i.e. it would not catch the regression that actually occurred. No
production change was needed. `NdjsonContentDigestTests` drives the real emitter, so both publication
modes come from the existing harness.
