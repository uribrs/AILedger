# Lessons — 2026-09-06_0615_close-claim-supersession-and-challenge-consequence (2026-09-06)

## L-9c279838 — A6 — one field on one record reaches eight files
belief:  Adding a field to an existing record in this kernel is contained to the command handler, the reducer and the state model.
counter: Adding SupersededByClaimId reached eight files. The two the earlier lesson omitted are the two that needed follow-up in both review rounds: TaskTransitionValidator, which re-validates every event at replay and silently no-ops if a new rule is written in only one copy, and the operator surface — CLI dispatch, the per-command option allow-list, help text and the Markdown projection, each a separate edit site with no compile-time link.
source:  review/verifier-2.md A6 row; git diff 310061f showing eight changed files for the field (verifier, 2026-09-06)
verify:  grep -c 'SupersededByClaimId' ~/Dev/Uri/localprojects/AILedger/src/AILedger.Core/Domain/TaskTransitionValidator.cs
do not:  do not size a change in this kernel by the aggregate it touches; count the exhaustive switches and the operator surface first

## L-f71030fb — A7 — a completed work item is still mutated by later claim rejection
belief:  A record in a terminal status is inert, so nothing later mutates it.
counter: AddDependencyInvalidations filters work items only to 'not Stale', so a Completed work item whose claim is later rejected is moved to Stale. Two independent review rounds hit this from different directions — once through the new refinement re-point, which had no status filter at all and rewrote a completed item's dependency list, and once through the pre-existing invalidation path, which still does.
source:  review/verifier-2.md A7 row, reproduced by probe; CommandHandler.AddDependencyInvalidations (verifier, 2026-09-06)
verify:  grep -n 'WorkItemStatus.Stale' ~/Dev/Uri/localprojects/AILedger/src/AILedger.Core/Application/CommandHandler.cs
do not:  do not assume a terminal status protects a record; read every filter that selects dependents, including the ones you are not changing

## L-2e173204 — A2 — an existing invalidation event cannot carry a new cause
belief:  A supported challenge's consequence can be expressed entirely through existing events, with no new event type.
counter: It holds for the claim and work branches but not for decisions. ValidateDecisionInvalidated requires the decision to name a rejected or superseded dependency claim, so DecisionInvalidated cannot express 'invalidated by challenge' — the cause is structurally part of the event's contract. A new DecisionOverturned(DecisionId, ChallengeId) was required.
source:  review/verifier-2.md A2 row; TaskTransitionValidator.ValidateDecisionInvalidated; research/internal-recon.md Landmine 5 (verifier, 2026-09-06)
verify:  grep -n 'DecisionOverturned' ~/Dev/Uri/localprojects/AILedger/src/AILedger.Core/Contracts/Events.cs
do not:  do not plan to reuse an invalidation event for a new cause without reading what its validator requires the cause to be
