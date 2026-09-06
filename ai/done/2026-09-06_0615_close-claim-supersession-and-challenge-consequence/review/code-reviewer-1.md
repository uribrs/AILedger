# Code Review — claim supersession and challenge consequence

Reviewed: `git diff 310061fefbe858dd79ee536d583fe22541aed27c` and the surrounding code needed to judge it.

## Calibration

- Change type: feature logic in a domain kernel with an event-sourced persistence boundary.
- Risk level: **High** for the parts touching replay and dependency state; Low for the CLI and docs.
- Stack: C# 12 / .NET 8, `System.Text.Json`, immutable records.
- Tests: `dotnet test AILedger.sln` — **153 passed, 0 failed, 0 skipped**. Run by me.

## Verdict

No blockers. The two-copy rule agreement holds everywhere I could reach it, the repoint cannot
corrupt a dependency list, and the new enumerations are deterministic. Two findings are material:
an evidence-free challenge can now destroy a decision, and the repoint rewrites dependency records
on terminal entities. Everything else is polish.

---

## Material findings

### 1. A challenge with no evidence can overturn a decision or block a work item — Major

**Problem.** `RaiseChallenge` and `ValidateChallengeRaised` both permit an empty `EvidenceIds`
(`src/AILedger.Core/Application/CommandHandler.cs`, `RaiseChallenge`; `TaskTransitionValidator.cs`,
`ValidateChallengeRaised` — both only call `EnsureUnique` / `EnsureReferencesExist`, neither requires
a non-empty list). Before this diff that was harmless, because a `Supported` disposition was inert.
This diff gives it teeth, and it applies the evidence bar to only one of the three target types:

- `claim` — `src/AILedger.Core/Application/CommandHandler.cs:425` requires
  `refuting.Length == challenge.EvidenceIds.Count && refuting.Length != 0`. Correctly strict.
- `decision` — no evidence requirement at all. Emits `DecisionOverturned`.
- `work` — no evidence requirement at all. Emits `WorkItemBlocked`.

**Impact.** `challenge raise --target-type decision --target D1 --reason "it does not hold"` with no
`--evidence`, followed by `challenge dispose --status supported`, moves an `Accepted` decision to
`Invalidated` on a bare assertion. The path that destroys the most state is the one with the lowest
evidentiary bar. `docs/operator-guide.md` in this same diff states the intended principle —
"supporting a challenge is not a second, unevidenced route to rejection" — and that sentence is now
true for claims and false for decisions and work items.

The new replay test encodes the hole as expected behaviour:
`tests/AILedger.Tests/Storage/NewCommandReplayTests.cs`, in
`R4_SupersessionAndChallengeConsequencesReplayThroughTheFileStore`:

```csharp
await Run(writer, taskId, new RaiseChallengeCommand(actor, null, "s14", new ChallengeId("CH1"), "decision", "D1", "It does not hold", []));
await Run(writer, taskId, new DisposeChallengeCommand(actor, null, "s15", new ChallengeId("CH1"), ChallengeStatus.Supported));
...
Assert.Equal(DecisionStatus.Invalidated, replayed.Decisions[new DecisionId("D1")].Status);
```

That is the only decision-overturn coverage in the suite, so the gap has no failing test to reveal it.

**Recommended fix.** Local patch. Require at least one evidence id before emitting a consequence, in
the `decision` and `work` arms of `AddChallengeConsequence`. Requiring it at
`RaiseChallenge`/`ValidateChallengeRaised` instead is the cleaner rule but is a wider behaviour
change — it would forbid raising a purely procedural challenge that is later withdrawn. Prefer the
consequence-side check; it matches where the `claim` arm already puts it. Then update the replay
test to supply evidence.

Not a refactor.

### 2. `ClaimDependenciesRepointed` rewrites dependency records on terminal entities — Major

**Problem.** `TaskReducer.RepointDependencies` (`src/AILedger.Core/Domain/TaskReducer.cs:185`)
selects dependents with no status filter:

```csharp
foreach (var decision in state.Decisions.Values.Where(item => item.DependsOnClaims.Contains(repointed.SupersededClaimId)))
```

The correction path it mirrors filters deliberately —
`CommandHandler.AddDependencyInvalidations` restricts to
`DecisionStatus.Proposed or Accepted` for decisions and `not WorkItemStatus.Stale` for work items.
The refinement path restricts nothing. So a `Completed` work item, a `Stale` work item, an
`Invalidated` decision and a `Superseded` decision all have their `DependsOnClaims` rewritten.

**Impact.** Two distinct costs.

- Audit loss. A `Completed` work item's record of what it was built on is overwritten in place. The
  fact survives in `events.jsonl`, but `state.json` and the Markdown projections — the artifacts an
  operator or an agent actually reads — no longer say it.
- Invariant break in the projection. `ValidateWorkItemAdded` rejects a `DependsOnClaims` naming a
  claim that does not yet exist. After a repoint, a completed work item can name a replacement claim
  that was added after the item finished. Replay produces a `WorkItems` entry that no valid
  `WorkItemAdded` could ever have produced.

I checked the safety axis and it holds: the repoint **cannot** resurrect causally invalidated work.
The only claim that could unblock a `Blocked` item is the one that invalidated it, and such a claim
is already `Rejected`/`Superseded`, which `EnsureClaimResolution` makes terminal — so it can never
later be refined. `Stale` and `Completed` are not unblockable at all
(`ValidateWorkItemUnblocked` requires `Blocked`). So this is a record-integrity finding, not a
state-machine break.

**Recommended fix.** Local patch, two `Where` clauses. Mirror the correction path's filters:
`Proposed or Accepted` for decisions, `not (Stale or Completed)` for work items. Living dependents
are the only ones the refinement claim ("this work is still sound") is about.

Not a refactor.

---

## Minor findings

### 3. `Claim.EvidenceIds` no longer carries direction

`TaskReducer.ResolveClaim` now unions the claim's existing evidence with the resolution's
(`src/AILedger.Core/Domain/TaskReducer.cs:88-95`). The erasure it fixes is real — a challenge
rejecting a previously-validated claim cites only refuting evidence and used to wipe the supporting
record. But `EnsureClaimResolution` permits `Validated → Rejected`, so after that transition the
claim carries supporting and refuting evidence in one undifferentiated list. The Markdown projection
renders it flat:

`- \`C1\` — **Rejected** — ... Evidence: E1, E2.` where `E1` supports and `E2` refutes.

I confirmed this cannot be rejected at replay: no validator reads `claim.EvidenceIds`, only
`resolved.EvidenceIds` from the event payload. So it is a legibility loss, not a correctness one.

**Fix.** Do not change the model. Render direction in
`src/AILedger.Storage/MarkdownTaskProjectionWriter.cs` by cross-referencing
`state.Evidence[id].Supports` / `.Refutes` against the claim id. Local patch, deferrable.

### 4. The work-item consequence is not bound to its challenge at replay

`ValidateDecisionOverturned` (`src/AILedger.Core/Domain/TaskTransitionValidator.cs:806`) does the
right thing — it re-checks that the challenge is `Supported` and that its `TargetId` matches the
decision. The work-item arm has no equivalent: `ValidateWorkItemBlocked` requires only `ManageWork`
and a non-`Completed` item. The link between the block and the challenge that caused it lives only
in the free-text `Reason` string, which nothing parses.

Low impact given the calibration (`events.jsonl` is written only by `CommandHandler`), but it is an
asymmetry between two arms of the same new switch, and the decision arm shows what the intended bar
is. If you want it closed, the shape is a distinct event carrying the `ChallengeId` — which is a
wider change than it is worth here. Deferrable; worth a note in the design record so the asymmetry
is a decision rather than an oversight.

### 5. `ValidateClaimAdded` does not reject a pre-set `SupersededByClaimId`

`ValidateClaimAdded` enforces "a new claim is clean" for two fields —
`claim.Status != ClaimStatus.Open || claim.EvidenceIds.Count != 0`. The new
`Claim.SupersededByClaimId` escaped that check. A `ClaimAdded` naming a replacement while `Open`
replays without complaint.

Consequence is contained (`ValidateClaimDependenciesRepointed` still requires
`superseded.Status == Superseded`), so nothing downstream acts on it. One clause on an existing
condition. Local patch, deferrable.

### 6. Two small pieces of unreachable or misleading code

- `src/AILedger.Core/Domain/TaskReducer.cs:99` — `SupersededByClaimId = resolved.SupersededByClaimId ?? current.SupersededByClaimId`.
  The right-hand branch is unreachable: `EnsureClaimResolution` forbids re-resolving a `Superseded`
  claim, and nothing else sets the field, so `current.SupersededByClaimId` is always null when a
  `ClaimResolved` arrives. Harmless as defence; worth a comment saying so, or drop the coalesce.
- `src/AILedger.Core/Application/CommandHandler.cs:427` — the message
  "can only be supported when every piece of its evidence refutes that claim" is also emitted for
  `refuting.Length == 0`, i.e. a challenge that had no evidence at all. The operator is told the
  wrong thing about why it failed. Split the two conditions and give the empty case its own message.
  (Fixing finding 1 makes this moot for `claim` targets too.)

---

## Observations — verified, no action

These are the things the review was specifically pointed at. All came out clean; recording them so
the checks are not repeated.

**Removing `AgentRunStatus.Pending` and `WorkItemStatus.Ready` cannot break an existing ledger.**
Two independent reasons. First, neither member was ever producible: at
`310061f`, `ValidateRunStarted` rejected any `RunStarted` whose `run.Status != AgentRunStatus.Active`,
and `ValidateWorkItemAdded` rejected any `WorkItemAdded` whose status was not `Proposed`. Every old
reference to both members was a read-side `is ... or ...` guard. No event could carry them. Second,
even if one had, `src/AILedger.Storage/LedgerJson.cs:18` configures
`new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false)` — enums persist
as names, so the ordinal shift from deleting a member is not a hazard. The simplification is safe.

**The refinement/correction derivation cannot disagree between the two copies, including across the
batch.** `CommandHandler.DeriveSupersession` reads pre-batch state.
`ValidateClaimResolved` re-derives against pre-`ClaimResolved` state — identical.
`ValidateClaimDependenciesRepointed` re-derives against **post**-`ClaimResolved` state, which is the
interesting case, but the two inputs to the derivation are `replacement.Status` and `state.Evidence`,
and `ClaimResolved` mutates neither. It touches only the superseded claim, and
self-supersession is refused in both copies (`replacementId == command.ClaimId` /
`replacementId == resolved.ClaimId`), so the superseded claim can never *be* the replacement.
The derived outcome is therefore stable across the batch. Carrying the outcome on the event and
re-deriving it at replay is the right call — a forged outcome is refused.

**`RepointDependencies` cannot produce duplicates or reorder a dependency list.**
`Replace` maps then `Distinct()`s. `ClaimId` is a `readonly record struct` with compiler-generated
value equality over an ordinal string, so `Distinct()` uses `EqualityComparer<ClaimId>.Default` and
preserves first-occurrence order. The collapsing case — a dependent listing both the original and the
replacement, `[A, B] → [B, B] → [B]` — is correct and order-stable.

**Unioning claim evidence cannot produce a state the validator rejects on replay.** I traced every
consumer of `Claim.EvidenceIds`: `EnsureUnique` and `EnsureReferencesExist` run against the *event's*
`resolved.EvidenceIds`, never the claim's accumulated list, and the direction check likewise reads
the event payload. `EnsureDependenciesAreCurrent` reads only `Claim.Status`. The only reader of the
accumulated list is `MarkdownTaskProjectionWriter`. Hence finding 3 is legibility, not replay.

**No challenge consequence can be emitted for a target it cannot legally apply to.** Each arm guards
its target's status before emitting, and each guard is at least as strict as the corresponding
validator:

| Arm | Handler guard | Validator | Agreement |
|---|---|---|---|
| `claim` | refuses `Rejected`/`Superseded` | `EnsureClaimResolution` refuses the same, plus `status == target` | handler ⊆ validator |
| `decision` | requires `Proposed`/`Accepted` | `ValidateDecisionOverturned` requires the same | exact |
| `work` | refuses `Blocked`/`Stale`/`Completed` | `ValidateWorkItemBlocked` refuses `Completed` | handler stricter |

The work-item row is a deliberate "already true" guard, stricter than the operator's own
`BlockWorkItemCommand`. A stricter handler is safe — every event the handler emits still passes replay.

**Capability escalation through the consequence is closed.** The `R1` note in the code is right and
the implementation matches it. `ClaimResolved` needs `ResolveClaim` at replay, and so does the
`DecisionInvalidated`/`WorkItemInvalidated` cascade it triggers — one `RequireConsequenceCapability`
call covers all three. `DecisionOverturned` needs `ResolveDecision`; `WorkItemBlocked` needs
`ManageWork`. All three demanded up front. Without this, a `DisposeChallenge`-only actor would write
an unreplayable ledger.

**Determinism of the new enumerations is fine.** `DeriveSupersession` uses `Any()`, which is
order-free. `RepointDependencies` iterates `Values` unordered, but `TaskReducer.Set` copy-constructs
a `Dictionary` and assigns an existing key, which does not move it in enumeration order — so the
accumulated dictionary preserves the pre-image ordering regardless of loop order, and downstream
projections are stable. The loop also reads `state.Decisions` while writing into a separate
accumulator, so there is no mutation-during-enumeration hazard.

**The replay test is honest.** `FileGovernedTaskService.GetStateAsync` calls `ReplayAsync` and only
then rewrites `state.json` and the projections, so reading back through a fresh `Service(root.Path)`
genuinely exercises the validator copy, as the test's comment claims.

**The validator does not enforce "a supported challenge carries a consequence".**
`ValidateChallengeDisposed` is unchanged. A `ChallengeDisposed(Supported)` with its consequence event
removed replays clean. The invariant lives only in `CommandHandler`. That matches how every other
multi-event command in this kernel is built, and closing it would require the validator to look ahead
in the batch. Correct call for this codebase; noting it so it is not mistaken for a guarantee.

---

## Action items

1. Require evidence before a `decision` or `work` challenge consequence is emitted, and update
   `R4_SupersessionAndChallengeConsequencesReplayThroughTheFileStore` to supply it. (Finding 1)
2. Add status filters to `RepointDependencies` matching `AddDependencyInvalidations`. (Finding 2)
3. Optional, deferrable: render evidence direction in the Markdown projection (3); reject a pre-set
   `SupersededByClaimId` in `ValidateClaimAdded` (5); split the two failure messages in the claim
   consequence arm (6).
4. Record the work-item/decision consequence asymmetry (4) as a decision rather than leaving it
   implicit.
