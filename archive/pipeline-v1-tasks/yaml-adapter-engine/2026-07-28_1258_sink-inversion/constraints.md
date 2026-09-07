# Constraints

## Hard

- Zero `Cymulate.*` package references (CLAUDE.md rule 0). Egress, orchestration,
  checkpointing and resilience belong to the host; none may be reinvented here.
- `/Users/user/Dev/cymulate-integration-adapters` is **read-only**. No file created,
  edited or deleted. Any host-side change is recorded, not made.
- No host call site may break. The 4-argument sink-less `ExecuteOperationAsync` stays
  on `IIntegrationEngine` — the adapter's connection test uses it.
- Test count must not fall below 697, zero failures, zero newly skipped.
- **Determinism is a gate, not a nicety.** Verify with ≥12 runs and trx capture. The
  previous flake surfaced at roughly 2 failures per 25 runs; a single green run is not
  evidence.

## Structural (CLAUDE.md + ARCHITECTURE.md)

- One top-level type per file, named after the type.
- Methods under 100 lines; classes under 400–500. SRP outranks both.
- More than 3 parameters gets a record. `CancellationToken` excluded; constructor DI
  governed by SRP instead.
- `Contracts/` splits into `Enums/`, `Interfaces/`, `Models/`, `Exceptions/` — only the
  buckets a concept needs. `Logic/` is flat unless a nameable sub-concept lives there.
- Namespace is the concept, not the folder: new `Sinks/Logic/` types are
  `…Engine.Sinks`.
- Dependency rule is enforced at the `Logic` layer and audited **by type reference**,
  never by `using` directive.
- Default to `internal`. A new type is public only if a consumer outside the assembly
  needs it — `InMemorySink` probably does (standalone/UI callers), `CountingSink`
  probably does not.

## Behavioural

- `InMemorySink` must reproduce the old sink-less path exactly: records land in
  `OperationResult.Records`, `TotalRecords` matches.
- `CountingSink` must not alter what reaches the wrapped sink — it observes and
  delegates, nothing more.
- Sink lifecycle ordering change (`InitializeAsync` now fires before a stage runs) must
  be pinned by a test, not left implicit.
