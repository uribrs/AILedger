# Assumptions

- A1 OPEN — Requiring a superseding claim id on a `Superseded` resolution is a better gate than requiring evidence, because the replacement claim carries its own evidence obligations and the pointer is what makes the supersession recoverable from state.
- A9 OPEN — "Replacement is `Validated` and nothing refutes the original" is a sufficient state-checkable test for a refinement; an agent cannot reach the non-cascading path without producing directed evidence for the replacement.
- A10 OPEN — Re-pointing a dependent decision or work item at a replacement claim can be expressed as an event the replay validator accepts, without relaxing the existing rule that a dependency claim must be current.
- A2 OPEN — A supported challenge can express its consequence entirely through existing events (`ClaimResolved`, `DecisionInvalidated`, `WorkItemBlocked`) with no new consequence event type.
- A3 OPEN — Rejecting a claim as the consequence of a supported challenge should reuse the existing evidence-direction rule, so a challenge whose evidence does not refute the claim cannot be supported.
- A4 OPEN — Removing `AgentRunStatus.Pending` and `WorkItemStatus.Ready` is safe because neither is written by any code path and neither name appears in any persisted `events.jsonl`.
- A5 OPEN — Emitting consequence events from `DisposeChallenge` does not disturb the archive prerequisite that refuses to archive with an open challenge.

## Prior Art

Matched 11 rows for tags: ailedger-kernel, lifecycle-semantics, persisted-state. None superseded or
retracted. Triaged the newest 10; the three AILedger rows are from this repo's immediately preceding
task and are directly on point.

- A6 OPEN — Adding a new state aggregate or field to this kernel is contained to the handler, reducer and state. source: lessons.md#L-ebe1d09c (verifier, 2026-09-06)
- A7 OPEN — A record in a terminal status is inert, so nothing later mutates it. source: lessons.md#L-cd74b5ae (verifier, 2026-09-06)
- A8 OPEN — One command and one predicate can serve several different target outcomes by branching on a type or status column. source: lessons.md#L-55212e51 (verifier, 2026-09-06)
