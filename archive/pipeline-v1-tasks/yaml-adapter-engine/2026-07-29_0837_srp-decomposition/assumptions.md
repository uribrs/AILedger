# Assumptions

Every entry carries a status. OPEN assumptions must be resolved to VALIDATED or REJECTED with recorded
evidence before the step that depends on them completes.

---

## A1 — The four public overloads can keep their signatures while `ExecutionRequest` becomes the internal core — OPEN

`IIntegrationEngine` exposes four overloads funnelling into a 10-parameter private core, five of whose
parameters are null-defaulted feature switches. `ARCHITECTURE.md` names `ExecutionRequest` +
`ExecutionOptions` as the fix.

The host calls exactly three entry points: `ExecuteOperationAsync` 6p+ct
(`YamlOperationRunner.cs:386`), `ExecuteOperationAsync` 4p+ct (`:518`), `WorkflowRunner.RunAsync` 8p+ct
(`:245`). If the overloads survive as a thin facade over `ExecutionRequest`, the host is untouched.

**Resolve by:** grepping the adapters repo for all three call sites *before* writing S4, not after.
Note the complication: the adapters repo vendors its own engine copy, so a grep proves what the host
*would* need, not what it currently compiles against.

**If REJECTED** — the overloads cannot survive — S4 changes a host-visible signature. That is a
decision to record and surface, not a constraint to break silently.

## A2 — `ARCHITECTURE.md`'s 11-type Execution plan is still accurate against the current code — OPEN

The plan (`ARCHITECTURE.md`, "The Execution decomposition") names `OperationPlanFactory`,
`OperationPlan`, `OperationRun`, `PageLoop`, `PageRequestFactory`, `PageDispatcher`,
`PageRecordReader`, `HydrationExecutor`, `ControlStateMutator`, `RecordPublisher`,
`OperationResultFactory`, plus `PageOutcome`/`PageOutcomeKind`.

Two of its stated figures are already off: it describes the page loop as "741 lines" (measured 729) and
`OperationRun` as covering "today ~15 locals" (uncounted). The plan predates the sink inversion, which
changed the record path it describes.

**Resolve by:** reading `ExecuteOperationCoreAsync` end to end and mapping each of its comment-labelled
regions onto a named type before extracting anything. Where the plan no longer matches, deviate and
record why — the plan is a strong prior, not a specification.

## A3 — There are exactly 3 methods over 100 lines in `src/` — OPEN

`ARCHITECTURE.md`'s ledger says 3. Two are confirmed by measurement: `ExecuteOperationCoreAsync` (729)
and `RunAsync` (155). The third is **unconfirmed**.

My ad-hoc counter during contract design reported `ReadStringInput` at 130 lines and
`ReadStringListInput` at 127. Both are false — they are expression-bodied one-liners
(`IntegrationEngine.cs:1734-1738`), and the counter ran past them to a distant brace because their
declaration line carries no `{`. `ExecuteHydrateAsync`'s reported 120 is unverified for the same reason.

**This is the argument for S0 in miniature:** an unreliable measurement produced a number that looked
like a finding. **Resolve by:** the committed `rules_audit`, which is the authority — not this file,
not `ARCHITECTURE.md`, and not any figure produced during contract design.

## A4 — The seven steps are separable enough to commit independently — OPEN

S1–S3 all cut into the same 729-line method, so they are sequential rather than independent: S2's
`PageLoop` cannot be extracted before S1 gives it a state object to thread. S5 depends on S4 putting
`ExecuteStageAsync` on the interface. S6 is genuinely independent.

**Resolve by:** confirming after S1 that the remaining cuts are still viable in the stated order.
Task 1's predecessor claimed separability *in the plan before anything was run* and paid 58 test
failures for it (retrospective, "Untested separability").

**Known non-independence, recorded up front:** S1, S2 and S3 will each touch
`IntegrationEngine.cs`. Task 1's "no file in more than one commit" property does not hold here and
should not be promised.

## A5 — Task-1 shapes can be reshaped where a seam makes the better shape obvious — OPEN

D12 argues `PaginationContext` should be replaced by binding config on the paginator via
`PaginatorFactory` (called once per operation at `IntegrationEngine.cs:283`). D9 argues
`ClassificationContext` should be flat. Both were refused during task 1 on contract authority; both
reviewers had working evidence.

The page-loop helpers those arguments target — `BuildPageRequest`, `AdvancePagination`,
`TryPrefetchNextPageAsync` — are exactly what S1–S3 rewrite.

**Resolve by:** deciding at the seam, once, with the mutation gate as the guard. **If the decomposition
does not make the answer obvious, leave both shapes alone** — reshaping them for their own sake is the
rule-5-first error this task exists to correct, pointed in the opposite direction.

## A6 — The 710-test suite is adequate coverage for a decomposition of this size — REJECTED

Rejected before execution starts, on task 1's evidence. D1 and D5 each survived a behaviour-breaking
mutation with the whole suite green. D15 shows the merge parser's error wire can be cut with 710 green.
Two of the classifier's three core behaviours are pinned by exactly one test each (D2).

**Consequence, binding:** a green suite is not evidence for any step of this task. Every extracted seam
gets a filtered mutation. This is why the gate exists, not a formality.

## A7 — `YamlIntegrationLoader` is a rule 4/7 problem only, not rule 3 — VALIDATED

Measured 2026-07-29: 672 lines, **zero** methods over 100 lines. So S6 is a split-by-responsibility
step with no long-method component, which makes it the lowest-risk of the six src steps and a
reasonable place to fall back to if the Execution work overruns.

## A8 — The engine's second copy does not constrain this task — OPEN

`Platform.Integrations.Sdk` in `cymulate-magic-integration` is the same engine, hand-synced, with
ADR-0003 (*Proposed*) yet to pick a canonical home. Its own `CLAUDE.md` states that until ADR-0003
lands, "any engine change must be applied to both copies."

This task will make that reconciliation substantially harder — ~11 new types where there was one class.

**Resolve by:** recording the divergence in `execution_notes.md` as it accumulates. **Do not edit the
second copy.** Whether this task should wait for ADR-0003 is an operator decision, not an executor one;
surface it if the divergence looks like it forecloses the reconciliation rather than merely enlarging it.
