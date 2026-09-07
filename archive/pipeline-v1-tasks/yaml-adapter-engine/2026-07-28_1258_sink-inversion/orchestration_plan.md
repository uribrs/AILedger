# Orchestration Plan

## Complexity Decision

- **Path: direct**
- Rationale: Four interlocking code changes in two files plus two new types. Separability is
  low and coupling high — S3/S4/S5 must land together, because the sink cannot become
  non-nullable until every caller supplies one, and until it does the fork cannot be deleted.
  Dependency order S1→S8 is strictly sequential with a hard gate at S2. Worker clarity is
  low: "write CountingSink" in isolation produces nothing that compiles into value.

## Research Decisions

None needed. The two OPEN assumptions are both about this codebase, not an external system.
A4 (can `CountingSink` expose what `ApplyCounts`/`CollectFromRecords` need) resolves by
reading `WorkflowRunner`; A5 (do test doubles become redundant) resolves by reading the test
project. Neither is a vendor, protocol or library-internals question.

## Worker Plan

Not applicable — direct path.

## Synthesis Approach

Not applicable — direct path.

## Verification Obligations

Beyond the contract's Success Criteria, the verifier must confirm:

1. **The two-sink distinction survived.** A2 is the fact the design rests on. Execution must
   not have collapsed `CountingSink` and `InMemorySink` into one type, nor routed
   control/poll/fan-out/merge-source stages through a publishing sink. Check which sink each
   stage class receives.
2. **A4 was resolved, not bypassed.** If `CountingSink` could not supply the counting inputs,
   the contract required stopping. Confirm the runner does not reach back into
   `OperationResult.Records` for counts as a workaround.
3. **Determinism is evidenced, not asserted.** `execution_notes.md` must show ≥12 runs with
   trx capture. Re-run independently; a single green run is not evidence, and the previous
   flake sat at ~2 failures per 25 runs.
4. **The fork is genuinely gone.** No `sink is null` branch and no in-memory record
   accumulation left in `ExecuteOperationCoreAsync`.
5. **No host call site broke.** The 4-argument sink-less overload still exists on
   `IIntegrationEngine` and still returns records in `OperationResult.Records`.
6. **Monorepo untouched** — `git -C /Users/user/Dev/cymulate-integration-adapters status
   --porcelain` empty.
7. **Rules held.** One type per file; new `Sinks/Logic/` types in namespace `…Engine.Sinks`
   with no `.Logic` segment; `PublishStageRecordsAsync` within rule 5; nothing over 100
   lines/500 lines newly introduced.
8. **D2 is pinned by a test**, not just recorded — a sink overriding `PublishStreamAsync`
   without bumping its counter must be caught.

## Out-of-Scope Guard

No clock/`TimeProvider` inversion, no definition-source inversion, no page-loop
decomposition. If execution finds those tempting, that is a signal to stop and report, not
to widen the branch.
