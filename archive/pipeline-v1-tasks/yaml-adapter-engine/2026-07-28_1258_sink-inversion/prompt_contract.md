# Prompt Contract — Sink Inversion

## Role

You are a senior .NET engineer performing a targeted architectural inversion on a
library whose only safety net is its test suite.

## Goal

Make `IExecutionSink` the single, non-optional record path through the engine, so that
`ExecuteOperationCoreAsync` has one behaviour instead of two. Where records go becomes
the caller's decision, expressed by the sink it supplies — never a branch inside the
engine.

## Context

Repo `/Users/user/Dev/yaml-adapter-engine`, branch `feat/sink-inversion`, baseline
`3103a30` at **697 passed / 0 failed / 0 skipped**.

**Read first, treat as authoritative, do not re-derive:** `CLAUDE.md`,
`ARCHITECTURE.md`, and the host-boundary section of
`ai/active/2026-07-27_1749_yaml-engine-extraction/progress_log.md`. Then
`constraints.md`, `assumptions.md`, `decisions.md` and `defect_register.md` in this task
directory.

**The one fact that shapes the whole task** (`assumptions.md` A2): not every stage has a
downstream consumer. `WorkflowRunner.RunAsync` routes only merge targets and stages that
are topic'd *and* have no `ForEach` *and* no `Poll` to the sink-routed path. Control,
poll, fan-out and `MergeInto` lookup stages go through `RunStageInstanceAsync` and have
no consumer by design — their records feed `{{stages.*}}` scope or an in-process join.
So the inversion has two answers, and both are legitimate.

`topic` comes from `stage.Topic` (YAML config), so a sink can always be resolved before
a stage executes. That is what makes this possible at all.

## Constraints

Full list in `constraints.md`. The ones most easily violated:

* The monorepo is **read-only**. Host-side findings are recorded in
  `defect_register.md`, never fixed.
* The 4-argument sink-less `ExecuteOperationAsync` **stays** on `IIntegrationEngine` —
  the adapter's connection test uses it. It becomes sugar over an `InMemorySink`.
* Zero `Cymulate.*` references. No egress, buffering-policy or storage logic in the
  engine — those are the host's.
* `CountingSink` observes and delegates. It must not alter what reaches the wrapped
  sink.
* Determinism is a gate: ≥12 runs with trx capture, zero failures. A single green run is
  not evidence.
* Do not start the clock inversion, the definition-source inversion, or the page-loop
  decomposition.

## Execution Steps

1. **S1 `InMemorySink`** — `Sinks/Logic/InMemorySink.cs`. Accumulates records for callers
   with no downstream. Must reproduce the old sink-less behaviour exactly.
2. **S2 `CountingSink`** — `Sinks/Logic/CountingSink.cs`. Decorator that observes records
   in flight and delegates. **Resolve A4 here before proceeding:** confirm it can expose
   everything `PublishStageRecordsAsync` needs for `ApplyCounts` and `CollectFromRecords`
   (per-record nodes only when `NeedsRecordNodes(stage)`). If some spec cannot be
   satisfied from the decorator's vantage point, **stop and report** — do not reach back
   into `OperationResult.Records`.
3. **S3 Overload becomes sugar** — the sink-less overload supplies an `InMemorySink`
   internally and returns its records in `OperationResult.Records`.
4. **S4 + S5 together — ONE commit. Proven unsliceable.**

   The engine populates `OperationResult.Records` **only when no sink is supplied**. So the
   moment a stage receives a sink, `Records` comes back empty — and `WorkflowRunner` reads
   `Records` in three places:

   ```
   WorkflowRunner.cs:493   recordCount = opResult.Records.Count
   WorkflowRunner.cs:691   SourceRecordsToNodesAsync  → foreach (op.Records)
   WorkflowRunner.cs:715   EnumerateUtf8              → foreach (opResult.Records)
   ```

   Supplying sinks without simultaneously moving those three reads was attempted and
   produced **58 test failures**; it was reverted. Do not retry it as a "safe slice".

   So S4 is: route every stage class to the right sink **and** move those three reads to the
   sink, in one edit. Routing — read `WorkflowRunner.RunAsync`, do not infer:
   - **sink-routed** (merge targets, and topic'd stages with no `ForEach` and no `Poll`) →
     the `SinkProvider` sink wrapped in `CountingSink` with
     `captureNodes: NeedsRecordNodes(stage)`
   - **control / poll / fan-out / `MergeInto` lookup** → `InMemorySink`; no downstream
     consumer by design

   S5 lands in the same commit: remove the `IExecutionSink? sink = null` default, make the
   sink non-nullable internally, delete the `if (sink is not null) … else …` fork and the
   in-memory accumulation from `ExecuteOperationCoreAsync`.
6. **S6 Split `PublishStageRecordsAsync`** — separate publication from counting and
   collecting. Nine parameters today; bring it inside rule 5.
7. **S7 Pin the new behaviour** — tests for: sink lifecycle ordering
   (`InitializeAsync` before the stage), `TotalRecords` sourced from
   `TotalPublishedRecords` including the zero-counter trap (D2), and `InMemorySink`
   equivalence with the old sink-less path.
8. **S8 Prove it** — ≥12 runs, trx capture, zero failures.

Update `state.json` step statuses and append to `execution_notes.md` as you go. Record
findings in `defect_register.md` when you find them, not afterwards.

## Per-Step Review Gate (operator directive, 2026-07-28)

**Every step below gets a verifier pass and a code-reviewer pass before the next step
starts.** Not only the task as a whole. The engine was conceptualized by a human and
written by a machine, so it carries structural defects a diff does not reveal — per-step
independent review is the mechanism that catches them while they are still cheap.

- Findings that matter: **fix immediately, do not consult.**
- Findings that are not world-shattering: record in `progress_log.md` and surface in the
  final code review.
- Each step (or indivisible group of steps) is **committed**, then work proceeds. No
  approval gate. The operator reviews in git after a push.
- Code-reviewer prompts carry **minimal context only** — never the contract, the plan,
  prior review output, or the user request.

Precedent: the review of S1–S2 found four Majors in code written minutes earlier, every
one of which would have been cemented once S3–S5 depended on it.

## Success Criteria

* `ExecuteOperationCoreAsync` contains no `sink is null` branch and no in-memory record
  accumulation.
* Every `WorkflowRunner` call into the engine passes a sink; the
  `IExecutionSink? sink = null` default is gone.
* `Sinks/Logic/` exists with `InMemorySink` and `CountingSink`, one type per file.
* The sink-less `ExecuteOperationAsync` overload still exists and still returns records
  in `OperationResult.Records`.
* `PublishStageRecordsAsync` no longer publishes, counts and collects in one method, and
  is within rule 5.
* Tests: ≥697, zero failures, zero newly skipped, **and deterministic across ≥12 runs**.
* New tests exist for sink lifecycle ordering and for the `TotalPublishedRecords`
  sourcing trap.
* No `Cymulate.*` reference; nothing under `cymulate-integration-adapters` modified
  (`git status --porcelain` there is empty).
* Every namespace under `src/` is still `…Engine.<Concept>` with no `.Contracts` or
  `.Logic` segment.
* `defect_register.md` records anything found, including what was deliberately not fixed.

## Execution Rules

* Do not assume missing data. Read the routing in `WorkflowRunner.RunAsync` rather than
  inferring which stages are sink-routed.
* Respect constraints strictly.
* Run the suite between steps, not only at the end.
* Surface defects; do not absorb them into a refactor.
* Report honestly: if determinism cannot be demonstrated, say so with the numbers.

## Output Format

* Code in `src/` and `tests/`.
* `execution_notes.md` — per-step outcome, deviations, anything surprising.
* `defect_register.md` — findings, with status.
* `state.json` — step statuses and `verification` updated.
* Final report: baseline vs final test counts, determinism evidence, commit SHAs, and
  any assumption that moved to VALIDATED or REJECTED.

## Stop Conditions

* `CountingSink` cannot supply what the runner's counting or collecting needs (A4).
* Test count falls, a test fails, or determinism cannot be shown across 12 runs.
* A change would require touching the monorepo.
* A host call site would break.
* Removing the fork would require reintroducing filesystem or storage logic in the
  engine.
