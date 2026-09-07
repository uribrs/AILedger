# Prompt Contract — Batch-Scoped Storage Subfolders

Role:
You are a senior .NET engineer working in the Cymulate integration-adapters repo, implementing a shared egress mechanism.

Goal:
Ship an opt-in, collector-agnostic mechanism in Shared/DataPipeline/Egress that uploads each published page under a deterministic subfolder `{StorageUrl}/batch_{page:D6}/` and surfaces that subfolder in the page's progress event via `progressContext.Metadata["storageUrl"]`, so upstream can parse raw data per batch while collection proceeds.

Context:
- StorageUrl arrives in the RUN envelope, is lifted into `PlatformEvent.Metadata["storageUrl"]` (`AdapterBusEntrypointRunner.ResolveRunMetadata`, `AdapterPlatformEventFactory`), and lands in `AdapterProgressContext.Metadata`.
- `NdjsonBatchSession.ApplyStorageUrlPrefix` (~426) and `NdjsonUtf8BatchSession` (~417) read `Metadata["storageUrl"]` live at every flush and prepend it to the page-relative target path. `CollectorNdjsonPublisher` is the collector-facing entry point.
- Progress events are host-published (ISB, pass-through) from live progress-context metadata after `AdvancePage`/`OnCheckpoint`.
- `AdapterCheckpoint` carries no metadata; page number is restored on resume via `RestoreProgress`.
- `AdapterFailureDecisionExecutor` snapshots checkpoints via `AdvancePage(0, 0)` — its published events must carry the bare base URL.
- Flows are sequential per run: publish page → advance → next page.
- Design decisions are settled (see `decisions.md`); do not re-open them.

Constraints:
- All items in `constraints.md` apply verbatim. Highlights:
  - Deterministic `batch_{page:D6}` naming; never random.
  - Preserve pristine base; derive every path from base; no stacking.
  - Ordering invariant (set → publish → advance → next) locked by a test.
  - Dud pages: no upload, no subfolder, bare base URL in the event.
  - DONE/failure/partial events carry the bare base URL.
  - Checkpoint format unchanged; YamlCollector untouched; default (non-opted-in) behavior byte-identical to today.
  - Mechanism + opt-in surface only; no collector wiring.
- Verify assumption A5 (`assumptions.md`) by search before writing code; surface if violated.
- Follow repo conventions (net8.0 pin, central packages, xUnit/Moq/FluentAssertions, mirror neighboring code style, README boundary rules for Shared subsystems).

Success Criteria:
- A shared scope helper in `Shared/.../DataPipeline/Egress/` that any collector can call per page: derives `{base}/batch_{page:D6}`, sets `Metadata["storageUrl"]`, preserves the base, supports restore-to-base; documented ordering invariant.
- Base restoration is guaranteed before DONE/failure/partial-success publication, including the `AdapterFailureDecisionExecutor` snapshot path, for opted-in runs.
- Dud-page behavior implemented: 0-record page publishes nothing and the event carries the bare base URL.
- Unit tests (green) covering: derive-from-base/no-stacking; deterministic naming incl. D6 padding; dud page; base restoration on DONE/failure paths; ordering invariant (next subfolder not applied before prior advance); non-opted-in behavior unchanged.
- `dotnet build Cymulate.Integration.Adapters.sln` succeeds; existing test suite passes (respect the hung-test-run guidance: if the harness hangs, stop and rely on verifiers).
- `ai/skills/` egress/collector docs updated if documented behavior changed.

Execution Rules:
- Do not assume missing data; check the code first, then surface.
- Respect constraints strictly; scope creep is a defect.
- Fix review-surfaced bugs directly during execution; ask only at real forks.

Output Format:
- Code changes in Shared (+ tests in the appropriate UnitTests project).
- `execution_notes.md` appended with what changed, why, and test evidence.
- `state.json` steps updated as they complete.

Stop Conditions:
- Goal achieved with success criteria met.
- A5 violated (another writer of `Metadata["storageUrl"]` mid-run).
- Implementing the ordering invariant would require host/SDK changes.
- A settled decision proves technically impossible as specified.
