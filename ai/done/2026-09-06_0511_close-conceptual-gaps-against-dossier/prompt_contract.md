# Prompt Contract

## Role

You are a .NET domain engineer working on an event-sourced governance kernel.

## Goal

Close four conceptual gaps between AILedger 2.0 v0.1 and design dossier v2.1, so the kernel supports extended pre-scope brainstorming and stops conflating a provider process exit with completed work.

## Context

`/Users/user/Dev/Uri/localprojects/AILedger`, branch `conceptual-gaps-v2.1`, baseRef `87ca8b1`, clean tree, 122 tests passing, .NET 8.

Kernel shape: commands validated in `src/AILedger.Core/Application/CommandHandler.cs`, events applied in `src/AILedger.Core/Domain/TaskReducer.cs`, state on `src/AILedger.Core/Contracts/TaskState.cs`, context in `src/AILedger.Core/Application/ContextAssembler.cs`, CLI in `src/AILedger.Cli/CliApplication.cs`, projections in `src/AILedger.Storage/MarkdownTaskProjectionWriter.cs`.

The operator's working method is extended brainstorming — investigate, discuss, shape — before any agent is dispatched. Gaps 1 and 2 exist to make that phase durable.

## Constraints

See `constraints.md`. All of them bind.

## Success Criteria

**Gap 1 — typed escalation**

- A new `Escalation` state object with id, kind, question, status, provenance, and an optional work-item link.
- Exactly two kinds: `BusinessDecision` and `TrueUnknown`.
- The kernel refuses a `BusinessDecision` that does not carry at least two distinct options and a recommendation naming one of them.
- The kernel refuses a `TrueUnknown` that does not reference at least one existing evidence record standing as proof of the failed attempt.
- Only an actor holding operator authority may resolve an escalation; resolution records the answer and the resolving actor.
- Tests prove both refusals and both successful paths.

**Gap 2 — rejected alternatives**

- A new `Alternative` state object with id, statement, rejection rationale, provenance, and an optional link to the decision that replaced it.
- Recording an alternative requires a non-empty rejection rationale.
- Assembled context includes recorded alternatives so a later actor sees what was already discarded.
- A test proves an alternative recorded before any decision exists is still surfaced in a later context build.

**Gap 3 — work lifecycle**

- `work complete` and `work block` commands exist end to end: command, event, reducer, CLI, help text.
- Both require `ManageWork`.
- `TaskReducer` no longer sets a work item to `Completed` when a run completes successfully; it sets `Paused`.
- Completing a work item is refused while it has an active or pending run, or an open escalation linked to it.
- Blocking records a reason and may cite an escalation.
- Tests prove: a successful provider run leaves the work item `Paused`; explicit completion succeeds; completion is refused with an active run; completion is refused with an open linked escalation.

**Gap 4 — first-class constraints**

- A new `Constraint` state object with id, statement, source, scope, status, and provenance.
- `constraint add` and `constraint supersede` commands exist end to end.
- `ContextAssembler` emits active constraints from task state and the hardcoded `operator-authority` constraint sentence in `CognitiveArtifactLoader` is removed.
- A superseded constraint is excluded from assembled context.
- Tests prove active constraints appear in context and superseded ones do not.

**Gap 3a — unblock (operator amendment, 2026-09-06)**

Added after the verifier found that `work block` is a one-way door: a `Blocked` item refuses both
`work complete` and `run start`, and no command returns it to a workable status.

- A `work unblock` command exists end to end: command, event, reducer, replay validator, CLI, help.
- It requires `ManageWork`, the same gate as complete and block.
- It returns a `Blocked` item to `Paused` and clears its block reason. `Stale` is not unblockable: it is only ever reached through invalidation, so it always implies a rejected dependency.
- It is refused while any dependency claim is currently `Rejected` or `Superseded`, with a message
  saying a replacement work item is the repair path. Causal invalidation must not be undoable by
  simply unblocking: a rejected claim is terminal, so work resting on one stays invalid.
- Tests prove: a manually blocked item unblocks and can then run and complete; an item blocked by
  claim invalidation is refused.

**Whole task**

- All new objects survive a write/replay round trip through `events.jsonl`.
- Escalations, alternatives, and constraints appear in the Markdown projections.
- `dotnet build` produces zero warnings and `dotnet test AILedger.sln` passes.
- `docs/architecture.md` and `docs/operator-guide.md` describe the new commands and the changed run-completion semantics.

## Execution Rules

- Freeze the shared contract types (`Identifiers`, `GovernanceModels`, `Commands`, `Events`, `TaskState`) in one pass before any parallel implementation.
- Do not assume missing data; respect constraints strictly.
- Record any deviation from this contract in `decisions.md` and `execution_notes.md`.
- When a review finding falls outside the success criteria above, write it to `execution_notes.md` under `## Backlog` and do not fix it.

## Output Format

Working code in `src/`, tests in `tests/AILedger.Tests/`, updated `docs/`, and an `execution_notes.md` recording what each worker did, the tests run, and any backlog items.

## Stop Conditions

- Stop if removing run auto-completion breaks a production path rather than only test assertions (A2 refuted).
- Stop if existing `events.jsonl` files cannot replay into the extended state without a schema-version bump (A3 refuted).
- Stop if a success criterion cannot be met without violating a constraint.
- Stop if a third verifier or code-reviewer round would be required.
