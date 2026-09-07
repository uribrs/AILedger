# Code Review — Emission publisher reshape to instance emitter

## Scope & Calibration

- **Change type:** shared library / framework-level code (public egress API consumed by every collector).
- **Risk level:** High — data-plane egress, multipart/persistence boundary, public API, options lifecycle, potential concurrent reuse.
- **Reviewed:** `git diff c6a30c8...HEAD` (excluding `ai/`). Focus: the new public `NdjsonBatchEmitter`, the sessions' new `MultipartUploadOptions` parameter, options lifecycle, disposal/exception paths, and the updated test call sites.
- **Build:** `dotnet build IntegrationInfra.slnx` — succeeds, 0 errors (warnings are pre-existing/unrelated).

## Summary Assessment

Solid, safe reshape. The static service-locating publisher pair (`ResultsBatchPublisher` + `AdapterNdjsonPublisher`) is collapsed into one sealed instance type whose dependencies are all visible at construction. The engine behavior (normalization, byte batching, multipart, commit-once semantics, size-unbounded atomic objects, telemetry) is carried over verbatim — I compared the moved bodies line-for-line against the deleted statics and found no behavioral drift beyond the intended construction-time resolution.

The type is effectively immutable and therefore safe to construct once and reuse across concurrent publishes (see below). No blockers, no majors. Findings are two behavior/adoption traps worth a doc caveat and a test, plus minor observations.

## Thread-safety / reuse (explicitly assessed — clean)

A shared emitter instance is safe for concurrent publishes:

- All six emitter fields are `readonly`; the four options types (`ThrottlingOptions`, `BufferingOptions`, `MemoryPressureOptions`, `MultipartUploadOptions`) are `sealed record`s with `init`-only members — immutable after construction.
- All per-publish mutable state lives in the `NdjsonBatchSession` / `NdjsonUtf8BatchSession` created *inside* each `Publish*` call, and `BuildNdjsonOptions` allocates a fresh `NdjsonOptions` per call.
- The emitter holds no disposable state, so it correctly does not implement `IDisposable`; sessions are `await using`.

This is the right shape for the reshape's stated "build one, reuse" intent.

## Findings

### 1. Options bind to the construction-time provider, but the sink still binds to per-call `context.Services` — silent divergence if reused across scopes
**Severity: Minor (behavior change; possible operational trap)**

- **Problem:** Previously the statics resolved `ThrottlingOptions`/`BufferingOptions`/`MemoryPressureOptions`/`MultipartUploadOptions` from `context.Services` on *every* publish. Now they are resolved once from the `IServiceProvider` passed to `Create` (or supplied explicitly). The actual sink — `IAdapterDataPublisher` — is still resolved per-call from `context.Services` inside the session (`NdjsonBatchSession.cs:454`, `NdjsonUtf8BatchSession.cs:445`). So configuration and the sink now come from two independently-chosen providers. If an emitter built from provider A is later used with a `context` whose `Services` is provider B (e.g. a per-tenant/per-run scoped provider that registers different `ThrottlingOptions`), the run silently uses A's throttling/buffering thresholds against B's sink.
- **Impact:** Low likelihood in the expected usage (collectors almost certainly `Create(context.Services)` and publish with the same `context`, exactly as the tests do, and each host invocation is a fresh process). But it is a genuine semantic shift from the old per-call resolution, and nothing in the API prevents the mismatch. Under scoped/per-tenant option overrides it would produce wrong-but-not-failing behavior — the worst kind to diagnose.
- **Recommended fix (local, doc-level):** Add one line to the class `<remarks>` and `README.Publishing.md` stating the invariant: *publishes should use the same `IServiceProvider` the emitter was created from; options are frozen at construction and are not re-read from the per-call context.* No code change needed.
- **Refactor vs patch:** Patch (doc). Do not add runtime provider-equality checks — not worth the complexity.

### 2. The reshape's "resolve once, reuse" benefit is neither exemplified nor exercised; the only in-repo call sites (tests) `Create(...)` per publish
**Severity: Minor (adoption / maintainability)**

- **Problem:** Every test call site does `NdjsonBatchEmitter.Create(ctx.Services).PublishAsync(...)` — constructing a fresh emitter (and thus re-resolving + re-validating all options) on every publish. There is no production consumer in this repo (collectors live in the sibling `cymulate-integration-adapters` repo), so the tests are the de-facto reference for adapter authors. If authors copy that pattern per page, the reshape moves the resolution cost without removing it — the stated win (resolve/validate once, reuse across a run's many page publishes) never materializes, and it reintroduces exactly the per-call resolution the reshape set out to kill.
- **Impact:** No correctness impact. Missed benefit + a copy-paste template that teaches the wasteful pattern.
- **Recommended fix:** Add at least one test that constructs a single emitter and publishes multiple pages through it (this also directly covers the reuse/thread-safety contract that is currently only reasoned about, not tested). Optionally note in `README.Publishing.md` that `Create` is a per-run/per-process construction, not a per-publish call.
- **Refactor vs patch:** Local test addition.

### 3. Mixed static + instance surface on one public type
**Severity: Observation (no action)**

`BuildMandatoryTargetPath` and both `AsAsyncEnumerable` overloads are `public static` on an instance type; `NormalizeJsonPath` is `internal static`. This is intentional per the DESIGN.md static taxonomy (pure-function statics stay static) and is documented. It reads slightly oddly on an "instance emitter," but it is coherent and I would not change it. Noted only for completeness.

### 4. Validation now fails at construction rather than at first publish
**Severity: Observation (improvement)**

Throttling/buffering minimum-part-size validation moved into the constructor, so misconfiguration surfaces at `Create`/construction (fail-fast) instead of at the first publish. This is a behavior change in *timing* only and is a net improvement; flagging so it is a conscious call. The sessions retain their own `MaxBufferedRecords/Bytes > 0` guards (defense in depth) — fine.

## Disposal / exception paths (assessed — correct)

- Both publish methods wrap the session in `await using`; on any exception (including the defensive `CommitIncomplete` throw and JSON-validation `DataPipelineException`s) the session is disposed, which aborts an uncommitted multipart upload. Success (`PublishResult.Ok`) is produced only after `FinalizeAsync` and only when `!CommitIncomplete` — the commit-once / no-checkpoint-past-a-phantom-object invariant is preserved.
- `catch (Exception ex) when (ex is not OperationCanceledException)` marks egress-failure telemetry and rethrows; cancellation propagates without being mislabeled. Correct, and unchanged from the prior statics.
- `ConfigureAwait(false)` is applied consistently on the moved awaits. Cancellation token is threaded through the `await foreach` and checked per record.

## Docs

`ARCHITECTURE.md`, `DESIGN.md`, both Emission READMEs, and the `ThrottlingAdapterExecutionContext` cref are updated consistently with the rename; no dangling references to the deleted types remain anywhere in `src`/`tests`.
