# Constraints

## Location

- Work only in `/Users/user/Dev/cymulate-integration-adapters-falcon-concurrency`, branch `falcon-concurrency-and-server-side-retries-with-backoff`.
- Never write to `/Users/user/Dev/cymulate-integration-adapters` (holds uncommitted TenableIo work).

## Serial publish — non-negotiable

- Publish, checkpoint write, and `AdvancePage` happen on exactly one consumer, in frozen-key-list order.
- Nothing awaitable and nothing cancellable may sit between a successful publish and its checkpoint write.
- Two publishes must never overlap on one `AdapterProgressContext`.
- Checkpoint format stays v4. No `findingsFormatVersion` bump, no completed-set, no new checkpoint keys for ordering.
- `DeleteHostPageAsync` for a page stays strictly after the checkpoint that retires that page's last batch.

## Concurrency

- Degree comes from `FalconCollectorConfiguration`, clamped in `FalconCollectorConfigurationBuilder.ExtractFields` like the other scalars. Not a private const.
- Default must be conservative and justified in `decisions.md`; Tenable's 32/4/4 are reference points, not defaults to copy.
- A degree of 1 must reproduce today's behaviour exactly, and must be reachable by configuration.
- The buffer between producers and the consumer must be bounded. Unbounded is a memory-limit breach in the making.

## Vendor cursor discipline

- The Spotlight scroll inside one batch is a cursor chain (`after` token, `updatedFloor`, `seenFindingIds`) and stays strictly sequential. Do not parallelise inside `EmitBatchRecordsAsync`.
- The `after` token expires at ~120s. A producer must not hold a live cursor while blocked on backpressure; if it can, the design must say why that is safe or how it is avoided.

## Preserve

- `FalconResumeRunner`, `FalconCheckpoint*`, the generation/manifest model, and `FalconResumeReach` semantics.
- `RecordCooperativeYield` behaviour on `OperationCanceledException`.
- `FalconFlowExceptionClassifier` outcomes and the Falcon resilience strategy (stays on `CreateTransient()`).
- Existing log lines and their fields where they are the operational receipt (batch published line, stats).

## Do not

- Change `AidBatchSize` away from its current default.
- Add rate-limit header reading in this task.
- Modify `Cymulate.IntegrationInfra` or `Cymulate.Http.Package` unless a written justification shows the goal is unreachable without it.
- Touch the assets flow.

## Validation

- `dotnet build` clean, 0 warnings, for the FalconCollector project and its test project.
- `Cymulate.Integration.Adapters.Collectors.FalconCollector.Test` passes in full.
- A 401/403 on `Cymulate.*` restore is a CodeArtifact auth issue, not a code failure.

## Added mid-flight from recon's FalconDocs pass (durable layer, cited)

- **Never relate the two number kinds.** The frozen-list address (`LastCompletedStagedPage` /
  `LastCompletedBatchIndex`) and `progressContext.CurrentPage` are different kinds of number and must never be
  compared, clamped, or assigned to each other. Source: `FalconDocs/CollectorDocs/06-resume-live-state-authority.md:154-175`
  — a named hard rule: "if you're about to write `Math.Max`, `<`, or `=` between them, stop." This rules out any
  buffer or ordering design that gates/reorders concurrent producers via `CurrentPage`.
- **AdapterState is the single live authority on collector state, on every leg.** Source: same doc, :8. The
  consumer's checkpoint write reads live state as the consumer advanced it — never a producer's snapshot.
- **Assert the publish, not the status.** Any new test asserts the actual published count/objects, never
  `result.Success`. Source: `06-resume-live-state-authority.md`, reinforced by `05-testing.md`. Reason this is
  sharp here: T9 (`03-current-concerns.md`) — a resumed leg's partial-success gate is pre-satisfied before any
  work runs, so a non-retryable failure before the first publish reports false success. Concurrency does not
  cause T9, but N producers in flight with an undrained consumer makes its triggering shape ordinary.
- **Behavioural-equivalence discipline** (`05-testing.md`): 0 warnings, whole suite green, unchanged public
  surface, preserved log lines, unchanged checkpoint round-trip.

### Known doc/code drift — informational, NOT in scope to fix
The CollectorDocs predate the current code; per their own README the code wins. Recorded so no one "fixes" the
code to match a stale doc: (1) docs describe checkpoint v3 with a separately-tracked OutputPage ordinal — code
is v4 and passes `CurrentPage` itself to `BeginPage`, which appears to moot hazard T2; (2) `03-current-concerns.md:226`
says `BatchScopedStorage` defaults false — `FalconCollectorConfiguration.cs:160` defaults it true; (3) docs say
`AidBatchSize` was lowered to 10 — code has since dropped it to 4 (comment dated 2026-08-16).
