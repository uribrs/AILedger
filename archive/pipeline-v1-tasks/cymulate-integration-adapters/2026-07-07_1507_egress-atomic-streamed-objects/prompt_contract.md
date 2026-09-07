# Prompt Contract

Role:
You are a senior .NET 8 engineer specializing in streaming data pipelines, S3 multipart semantics, and behavior-preserving refactors of shared infrastructure used by many consumers.

Goal:
Make "one publish call = one atomic object, streamed, size-unbounded" the universal behavior of `Shared/DataPipeline/Egress` on branch `fix/oversized-record-guard`: delete object-splitting and the fail-fast single-record rule, add multipart escalation with Abort-on-failure/cancel/dispose, add size observability, and perform the full dead-code cleanup this enables — with a test suite strong enough to protect every collector.

Context:
- Egress root: `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/DataPipeline/Egress/` (sessions `Ndjson/NdjsonBatchSession.cs`, `Ndjson/NdjsonUtf8BatchSession.cs`, `ResultsBatchPublisher.cs`, `CollectorNdjsonPublisher.cs`, `BatchScopedStorage.cs`, `Ndjson/NdjsonContentHasher.cs`, `ThrottlingOptions.cs`; README documents the four-tier heap defense and Stable Output Rules).
- Multipart surface: `IAdapterDataPublisher` (SDK) — Initiate/UploadPart/Complete/Abort; implementation verified adequate (assumptions A1/A2).
- Collector call sites on dev that may carry pre-splitting servitude: TenableIo flows (byte-budget re-pagination), Falcon, Qualys, InsightVmCloud, Cortex, DefenderVm and any other `CollectorNdjsonPublisher`/session consumers — inventory during execution (A5).
- Contract artifacts in this directory (`task.md`, `constraints.md`, `assumptions.md`, `decisions.md`) are binding.

Constraints:
- All items in `constraints.md`. Non-negotiables: universality (no flag); byte-identical small-call behavior; abort on failure/cancel/dispose; checkpoint after Complete only; four-tier memory semantics unchanged; cleanup judgment rule; test exclusions (ISBLoadTestCollector, DummyCollector never run); no ISB/feature-branch/other-repo changes.

Success Criteria:
- Solution builds, 0 errors.
- Test coverage, all green via `dotnet vstest` on built dlls: (1) small-object single-PUT byte-identical regression (path, naming, content); (2) multipart escalation at threshold, ≥5MiB parts, atomic Complete with correct part assembly; (3) byte-for-byte object equality single-PUT vs multipart for identical input; (4) NdjsonContentHasher hash-identical invariant preserved, extended with size; (5) mid-stream failure → Abort exactly once, no checkpoint, clean retry; (6) cancellation → Abort; dispose-without-complete → Abort; (7) checkpoint hook exactly once per call, only after Complete; (8) per-record soft-threshold warning fires only past threshold; (9) giant-record (>50MiB) publish succeeds end-to-end (old fail-fast case); (10) BatchScopedStorage interplay — scoped path + atomic object + single announcement after Complete.
- Cleanup executed and inventoried: every deleted path listed in execution_notes with a one-line justification; ambiguous sites kept and documented.
- Affected collector test suites green via vstest (exclusions honored; known-hang suites via vstest only).
- Docs synced: Egress README (incl. Stable Output Rules), session docs, ai/skills content describing 50MiB object behavior; ops handoff (S3 abort lifecycle rule) recorded in execution_notes.
- Collector csproj version bumps only where collector code was touched.

Execution Rules:
- Do not assume missing data; enumerate call sites and consumers from code (A5/A6/A7) before deleting anything.
- Respect constraints strictly; pinned decisions are not re-litigated.
- Fix review-surfaced bugs directly; reserve questions for genuine forks.
- Behaviour over shape: when weighing what carries over, runtime behavior under realistic failure decides, not code aesthetics.

Output Format:
- Code changes on branch `fix/oversized-record-guard` (no commit unless instructed).
- `execution_notes.md` appended: per-step changes, cleanup inventory with justifications, test names + verbatim vstest results, residual risks, ops handoffs.
- `state.json` step statuses updated.

Stop Conditions:
- Goal achieved (all success criteria met).
- A consumer is found that genuinely depends on multi-object-per-call semantics (A6 violated) — stop and surface before proceeding.
- A pinned constraint cannot be satisfied without violating another — stop and surface.
