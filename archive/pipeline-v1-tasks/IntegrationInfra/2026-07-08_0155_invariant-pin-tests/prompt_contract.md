# Prompt Contract

Role:
You are a senior .NET engineer writing behavior-pinning tests for an infrastructure library ahead of an API reshape.

Goal:
On a branch off `dev` in /Users/user/Dev/IntegrationInfra: add the SDK checkpoint-key pin test (core deliverable), audit the four existing suites against the contract's invariant list and fill only verified gaps, and deepen the runner invariant tests' assertions where shallow. Full suite green.

Context:
- Scope details + audit checklists: task.md. Grounded coverage facts: assumptions.md. Rulings: decisions.md.
- Key paths: src/IntegrationInfra/FaultGovernance/ (executor, budget, CheckpointAdapter), tests/IntegrationInfra.{FaultGovernance,Kernel,Conducting,Emission}.Tests.
- Existing doubles: Emission.Tests/EmissionTestDoubles.cs + RecordingPublisher.cs; Conducting.Tests/AdapterBusEntrypointRunnerInvariantTests.cs contains context/definition doubles — inspect before inventing.

Constraints:
- All items in constraints.md verbatim; highlights: tests-only, no duplicates (audit first), branch off dev, no reshape prep, no version bump, style mirrors neighbors, full suite green.

Success Criteria:
- New FaultGovernance test file pins: `_checkpoint.kind` = "StateSnapshot", `_checkpoint.reason` starts "deferred-recovery:", `itemsInBatch`/`findingsInBatch` = 0 after a deferred-recovery decision executes; the `_resilience.recovery.*` fields survive an AdapterCheckpoint→GetData→SeedFromPersistedState round trip; SeedFromPersistedState copies only `_resilience.*` keys; Clear vs ClearEpisode semantics pinned.
- Each of (b)(e)(f) has a recorded audit verdict in execution_notes.md (gap tests added, or no-op with evidence: existing test names per contract point).
- (c)/(d) runner tests assert: exactly one CompletionRequest, Success:true, partialCompletion metadata present, no failure completion; cancellation → zero publications + CancelledResult retryable. Deepened in place if currently shallow.
- `dotnet build IntegrationInfra.slnx` 0 errors; full `dotnet test` green including new tests.
- execution_notes.md + state.json updated; global archive mirrored.

Execution Rules:
- Audit before writing; verify every assumed API shape against the source.
- Fix genuine product bugs surfaced by tests directly; record in decisions.md.
- Respect constraints strictly; the statics reshape is out of scope.

Output Format:
- Test code on the branch; execution_notes.md per-step log with audit verdicts; updated state.json.
- Final summary: branch, files added/changed, audit verdicts, test counts before/after, any product bug found.

Stop Conditions:
- Goal achieved and verified.
- SDK types cannot be constructed/driven from tests at all (assumption broken) — stop, surface options.
- A gap test reveals a product bug whose fix would exceed a bounded direct repair — stop, surface.
