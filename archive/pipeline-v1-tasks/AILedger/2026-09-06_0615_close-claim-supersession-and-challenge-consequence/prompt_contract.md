# Prompt Contract

## Role

You are a .NET domain engineer working on an event-sourced governance kernel.

## Goal

Make claim supersession evidenced and recoverable, and make a supported challenge cause a real state
change, so causal governance holds in both places it currently does not.

## Context

`/Users/user/Dev/Uri/localprojects/AILedger`, branch `conceptual-gaps-v2.1`, baseRef `310061f`, clean
tree, 140 tests passing, .NET 8.

Rules are expressed twice in this kernel: `src/AILedger.Core/Application/CommandHandler.cs` validates
at command time, and `src/AILedger.Core/Domain/TaskTransitionValidator.cs` re-validates every event at
replay time. `TaskReducer.Apply` calls the validator before applying, and its `default:` arm throws —
an unregistered event type makes a task permanently unreadable.

## Constraints

See `constraints.md`. All of them bind.

## Success Criteria

**Gap A — evidenced, recoverable supersession**

- `Claim` carries the id of the claim that superseded it.
- Resolving a claim to `Superseded` requires naming that replacement claim.
- The replacement must exist, must not be the claim itself, and must be current — not `Rejected` and
  not itself `Superseded`.
- Resolving to `Validated` or `Rejected` must reject a supplied replacement id.
- Supersession has two outcomes, and the kernel decides which from state rather than from a
  declaration by the actor performing it:
  - **correction** — the default. Dependent decisions and work items invalidate exactly as today.
  - **refinement** — no cascade; dependent decisions and work items are re-pointed at the
    replacement claim.
- `refinement` is taken only when **both** hold: the replacement claim is already `Validated`, and no
  evidence on record refutes the original claim. Either failing means `correction`.
- The resulting outcome is recorded on the emitted event so replay does not have to re-derive it, and
  so a later reader can see which path was taken.
- Both rule copies agree. Tests prove each refusal, both outcomes, the boundary between them, and
  that the replacement and the chosen outcome survive a real replay.

**Gap B — a supported challenge causes a state change**

- Disposing a challenge `Supported` emits a consequence event determined by its target type:
  - target `decision` — the decision is invalidated, if it is currently `Proposed` or `Accepted`.
  - target `work` — the work item is blocked, with a reason naming the challenge.
  - target `claim` — the claim is rejected, using the challenge's own evidence, which in turn
    invalidates that claim's dependents through the existing path.
- A challenge targeting a claim cannot be supported unless its evidence refutes that claim, matching
  the rule already governing claim rejection.
- A challenge cannot be supported when its target is already in a terminal state that makes the
  consequence meaningless; the refusal names why.
- Disposing `Rejected` or `Withdrawn` still emits only `ChallengeDisposed`.
- Both rule copies agree. Tests prove all three target consequences, the evidence-direction refusal,
  and that a supported claim challenge cascades to that claim's dependent work.

**Hygiene**

- `AgentRunStatus.Pending` and `WorkItemStatus.Ready` are removed, and every guard that read them is
  simplified rather than left with a dead branch.

**Whole task**

- Every new or changed event replays through `FileGovernedTaskService` from a fresh service instance.
- Consequence events appear in the Markdown projections where the affected record is rendered.
- `dotnet build` produces zero warnings and `dotnet test AILedger.sln` passes.
- `docs/architecture.md` and `docs/operator-guide.md` describe both behaviours.

## Execution Rules

- Deliver every attention-item artifact named in `orchestration_plan.md`; they are not the verifier's
  to discover.
- Change both rule copies together, in the same edit, for every rule touched.
- Do not assume missing data; respect constraints strictly.
- Record any deviation in `decisions.md` and `execution_notes.md`.
- A finding outside the success criteria goes to `execution_notes.md` under `## Backlog`, not into the
  code.

## Output Format

Working code in `src/`, tests in `tests/AILedger.Tests/`, updated `docs/`, and an
`execution_notes.md` recording what changed, the tests run, and any backlog items.

## Stop Conditions

- Stop if a supported challenge's consequence cannot be expressed without a new claim status (A2 refuted).
- Stop if re-pointing a dependent decision or work item at a replacement claim cannot be expressed as
  an event the replay validator accepts.
- Stop if removing either enum member breaks a persisted log or a production path rather than only
  simplifying guards (A4 refuted).
- Stop if a success criterion cannot be met without violating a constraint.
- Stop if a third verifier or code-reviewer round would be required.
