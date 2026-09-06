- Superseding a claim requires naming the claim that replaces it, rather than requiring generic evidence. The replacement is a stronger justification than an evidence record, and it closes both halves of gap A with one field.
- Supersession is not one behaviour. A refinement sharpens a claim and its dependents stay valid; a correction narrows or contradicts it and they do not. Treating both as destructive makes the safety mechanism too expensive to use, and agents then avoid superseding at all — leaving contradictory claims live, which is worse.
- The kernel derives which of the two applies from state, never from a flag set by the actor resolving the claim. That actor is usually an agent — `ResolveClaim` is a `PlanningLead` default — so a declaration would let it grade its own homework on the branch that costs it less.
- The destructive path is the default and the cheap path carries the entry condition: `refinement` requires the replacement to be `Validated` and no refuting evidence on the original. This is the same shape as the escalation kinds, where the interrupting path must carry the payload that proves it was earned.
- A supported challenge reuses existing causal machinery rather than introducing a parallel one: a decision is invalidated, a work item is blocked, a claim is rejected using the challenge's own refuting evidence.
  - **Drifted during execution.** True for the claim and work branches, false for the decision branch. `ValidateDecisionInvalidated` requires a rejected or superseded dependency claim, so `DecisionInvalidated` cannot express "invalidated by challenge". A new `DecisionOverturned(DecisionId, ChallengeId)` event was added instead — the contingency already recorded below. The stop condition was scoped to a new claim *status*, which was not needed, so execution correctly continued.
- A challenge whose evidence does not refute its target claim cannot be supported. The evidence-direction rule that already governs claim rejection governs this path too, rather than opening a second unevidenced route to rejection.
- Remove `AgentRunStatus.Pending` and `WorkItemStatus.Ready` rather than finding a use for them. Neither is written by any path; both are read by guards that then carry a dead branch. No persisted event references either name.
- Proceeding on unverified: a supported challenge against a claim can always be expressed as a normal rejection, so no new claim status is needed. If wrong: emit a distinct `ChallengeUpheld` consequence event per target type instead.

## Deviations recorded after review

- **Two code-reviewer-1 Major findings were repaired although neither was a strict criterion
  violation.** `constraints.md` says such findings go to backlog. The evidence-bar finding had a real
  criterion link — `docs/operator-guide.md` in the same diff asserted the rule for all three targets,
  making the docs criterion false, and three tests encoded the hole. The repoint-filter finding did
  not: my stated justification was that correction and refinement "must select the same dependents",
  and verifier-2 showed that is exactly what the code does not do. The correction path still stales a
  `Completed` dependent; the refinement path leaves it alone.

  The code is right for a different reason than I gave: a terminal record's dependency list is the
  audit trail of what it was actually built on, and rewriting it destroys evidence and produces a
  state no valid `WorkItemAdded` could have created. Recorded as a deviation rather than defended,
  and the three comments that repeated the false symmetry claim are corrected.
- **The evidence bar was widened beyond the contract.** The criteria required the evidence-direction
  rule only for `claim` targets. It now requires at least one evidence record for every target, with
  direction still checked only for claims because the model has no `supports`/`refutes` relation for
  decisions or work items.
