# Prompt Contract

Role:
You are a senior .NET 8 engineer performing a high-fidelity cross-repo port of streaming-egress infrastructure, expert in S3 multipart semantics and behavior-preserving carries between renamed architectures.

Goal:
Carry two adapters-repo changesets — atomic size-unbounded egress objects (source commit `832754a`) and batch-scoped storage (source dev merges #265/#266) — into IntegrationInfra on branch `carry/atomic-objects-and-batch-scoping`, with 1:1 functionality, D7 replaced by the source mechanism, `BatchScopedStorage` homed in `Envelopes/Common`, ported tests in package house style, and a parity checklist proving no functionality was lost.

Context:
- Target: `/Users/user/Dev/Uri/localprojects/IntegrationInfra` (package `Cymulate.IntegrationInfra`, single assembly, concern-per-folder; Emission = old Egress, Conducting = old Orchestration, FaultGovernance = old Resilience+Recovery; D8 renames `Collector*`→`Adapter*` EXCEPT Conducting).
- Source: `/Users/user/Dev/cymulate-integration-adapters` (dev). Changeset B files: `Shared/.../DataPipeline/Egress/{Ndjson/NdjsonBatchSession.cs, Ndjson/NdjsonUtf8BatchSession.cs, Ndjson/NdjsonOptions.cs, ResultsBatchPublisher.cs, ThrottlingOptions.cs}` + `Shared/.../Glossary/CollectorGlobalDefaults.cs` + READMEs (see `git show 832754a`). Changeset A files: `Shared/.../DataPipeline/Egress/BatchScopedStorage.cs` + session 1-liners + 7 orchestration/resilience/recovery call sites (see `git diff 8948fba..7218abc` in the source repo). Tests to port: `UnitTests/Shared/.../DataPipeline/Egress/{AtomicStreamedObjectsTests.cs, BatchScopedStorageTests.cs}`.
- Migration context: the package's own `ai/active/2026-06-30_1328_emission-carry/` (D7 record), `2026-07-01_1249_conducting-carry/` (D-charter DAG), FaultGovernance carry docs (edge check), `2026-07-01_1454_review-fixnow/` (D-C1b).
- Contract artifacts in this directory (`task.md`, `constraints.md`, `assumptions.md`, `decisions.md`) are binding.

Constraints:
- All items in `constraints.md`. Non-negotiables: replace D7 (no `_multipartFinalized` remnants); twins symmetric; no size throws in the session path; BSS in Envelopes/Common; Conducting gains no Emission dependency; plain Assert tests without FluentAssertions; D4 reshape untouched; append-only history edits; version bump.

Success Criteria:
- Package builds 0 errors; ALL package test suites green including the two ported files.
- Ported test coverage intact through translation: byte-identity single-PUT vs multipart; abort exactly-once on mid-stream failure / cancellation / dispose-without-complete; never-abort-completed; FinalizeAsync empty-residual completion (the M1 case); CommitIncomplete gate; giant >50MiB record success; soft-threshold warning fires only past threshold; small-call byte-identical regression; BatchScopedStorage BeginPage/RestoreBase/tail-strip/idempotent-rescope semantics.
- Grep-clean: zero `_multipartFinalized`, zero `record-too-large`/`batch-boundary` remnants (code+tests+docs); literal `"storageUrl"` replaced by the constant at the sessions and all seven call sites (wire-model JSON property names stay literal — they are the wire contract, not metadata-key reads).
- DAG intact: no new Conducting→Emission using; FaultGovernance→Envelopes.Common edge verified permissible (or STOP).
- Emission README updated; D7-SUPERSEDED note appended to the emission-carry decisions file; `Version` = `1.0.0-preview.3`.
- execution_notes.md contains the 1:1-parity checklist: every source hunk of both changesets mapped to its target file/location, or an explicit justified N/A.

Execution Rules:
- Inventory before editing: resolve A8 (diff package sessions vs source pre-changeset baseline) and A6-FaultGovernance before any code change.
- Respect constraints strictly; pinned decisions are not re-litigated.
- Mirror source hunks precisely; where target naming differs (D8/D2), map names, not behavior.
- Fix review-surfaced defects directly; reserve questions for genuine forks.

Output Format:
- Code changes on branch `carry/atomic-objects-and-batch-scoping` (no commit unless instructed).
- `execution_notes.md`: inventory results, per-step changes, the parity checklist, test results verbatim, residual risks.
- `state.json` step statuses updated.

Stop Conditions:
- Goal achieved (all success criteria met).
- FaultGovernance docs forbid an Envelopes.Common dependency (A6) — stop and surface.
- The package sessions have drifted beyond D7 in a way that changes the port shape (A8) — stop and surface.
- A pinned constraint cannot be satisfied without violating another — stop and surface.
