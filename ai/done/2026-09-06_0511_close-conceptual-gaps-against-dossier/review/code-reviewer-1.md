# Code Review — kernel extension (escalations, alternatives, constraints, work lifecycle)

Reviewed: `git diff 87ca8b14652371103950095b621acd2e987635f9` plus the six new test files.
Stack: C# / .NET 8. Change type: feature logic on a persistence + replay boundary.
Risk level: **High** for the event/replay surface, **Medium** for CLI and projections.

`dotnet test AILedger.sln` — **137 passed, 0 failed, 0 skipped** (2s).

## Verdict

No blockers. Replay integrity is sound and I could not break it. Two findings are material:
`work block` is a one-way door with no exit, and the assembled context now hands a `CodeReviewer`
the escalation recommendation and the rejected alternatives — content the same class deliberately
withholds from that role.

---

## Findings, ranked

### Major 1 — `work block` is a one-way door; blocking on an escalation is a permanent trap

**Problem.** `Blocked` has no exit. Every path out is refused:

- `work complete` — refused (`CommandHandler.cs:643`, `TaskTransitionValidator.cs:659`).
- `run start` — refused (`CommandHandler.cs:383`, `TaskTransitionValidator.cs:441`).
- `work block` again — accepted, but leaves the status `Blocked`.
- Claim rejection — moves it to `Stale`, which is equally terminal.

There is no `work unblock` / `work resume` command, no reducer arm that leaves `Blocked` for a
workable status, and no CLI verb for one. The refusal message says the item must "be repaired
first" (`CommandHandler.cs:644`), but the kernel has no repair operation.

The sharpest case is the one the diff itself invites. `work block --escalation ID` is the documented
way to park work on an open operator question. Once the operator answers it, the work item is still
dead. Reproduced against the real file store:

```
work block    --id wi2 --reason "awaiting decision" --escalation es3   -> ok
escalation resolve --id es3 --status resolved --resolution "A"          -> ok
work complete --id wi2  -> error: Work item in status 'Blocked' cannot be completed before it is repaired.
run start --work wi2    -> error: Cannot start a run for work item in status 'Blocked'.
```

**Impact.** The only recovery is to add a replacement work item and abandon the old one, losing its
run history and dependency edges. `docs/operator-guide.md` documents `work block` without saying it
is irreversible, so the trap is not signposted. Half of `Blocked` was already unreachable before
this diff (invalidation-set `Blocked`), but this diff makes it operator-reachable on purpose and
pairs it with an escalation that is explicitly expected to be resolved.

**Fix.** Smallest change that closes it: one `UnblockWorkItemCommand` / `WorkItemUnblocked` event
that moves `Blocked` (not `Stale`) back to `Paused` and clears `BlockReason`, with the matching
arm in `TaskReducer.Apply`, `TaskTransitionValidator.Validate`, `AuthorizationPolicy`
(`Capability.ManageWork`), and the CLI table. Guard it on the cited escalation no longer being
`Open`, mirroring `ValidateWorkItemBlocked`. Local patch, not a refactor — it is one more arm in
each of the five switch sites the diff already touched.

If you would rather not add a command, the alternative is to reject `work block` when the item is
already `Blocked`, and treat blocking as advisory metadata rather than a status. That is a worse
fit for the rest of the model; I recommend the command.

---

### Major 2 — open escalations and rejected alternatives bypass the code-reviewer isolation boundary

**Problem.** `ContextAssembler.AlwaysIncludedKinds` gained `Escalation` and `Alternative`
(`ContextAssembler.cs:16-19`), but `ReviewerExclusions` (`ContextAssembler.cs:22-29`) was not
touched. `ReviewerExclusions` exists to keep `UserRequest`, `PromptContract`, `OrchestrationPlan`
and `VerifierOutput` away from a `CodeReviewer`. An open escalation carries the question, the
options, and — for a `BusinessDecision` — the implementer's *recommendation*. A rejected alternative
carries "we considered X and discarded it because Y".

Reproduced, building context as an actor with role `CodeReviewer`:

```
escalation | es2  | 'BusinessDecision awaiting the operator: Ship v1 or wait?
                    Options: Ship v1 | Wait for v2
                    Recommended: Ship v1'
alternative | alt1 | 'Rejected: Use Postgres
                     Because: operator wants no server'
```

**Impact.** This is exactly the anchoring the exclusion list is built to prevent — the reviewer is
told what the implementer wants the answer to be before forming an independent one. The severity is
tied to this repo's purpose rather than to any runtime risk: everything else still works.

**Fix.** Add `ContextArtifactKind.Escalation` to `ReviewerExclusions`. `Alternative` is the closer
call — a discarded approach with a durable technical rationale is legitimate reviewer input, and
excluding it costs real signal. My recommendation: exclude `Escalation`, keep `Alternative`, and
say so in `docs/architecture.md`, which currently claims both are unconditionally eligible.
Local patch, one line plus a doc sentence.

---

### Minor 3 — the `operator-authority` constraint was deleted and nothing replaces it

`CognitiveArtifactLoader.cs` lost the hardcoded `Constraint` artifact ("Only the operator may assign
roles or change governed resource scope"). The loader now emits only `Rules`, `Skill` and
`StopCondition`; nothing else in the CLI supplies a `Constraint` artifact. Verified: a fresh task
with no ledger constraints produces a context manifest containing **zero** `constraint` artifacts.

Replacing a hardcoded string with operator-governed state is the right direction, but the invariant
it stated is not optional and is now absent by default. `tests/.../ConstraintContextTests.cs`
asserts that state constraints *can* carry it, not that anything does.

**Fix.** Either seed the constraint on `task.open` (a `ConstraintAdded` event emitted alongside
`TaskOpened`, provenance source `task.open`), or add it to `cognitive/RULES.md` so the rules
artifact carries it. The first is more faithful to the new model. Local patch either way.

---

### Minor 4 — `TrueUnknown` escalations accept empty and duplicate options, and render as noise

The empty-option, uniqueness and count checks live inside the `BusinessDecision` arm of the switch
in both copies (`CommandHandler.cs:493-516`, `TaskTransitionValidator.cs:530-553`). A `TrueUnknown`
escalation therefore takes anything. Reproduced:

```
escalation raise --kind true-unknown --question "Q?" --evidence e1 --option "" --option ""   -> accepted
```

state.json stores `"options": ["", ""]`; the context artifact renders as:

```
TrueUnknown awaiting the operator: Q?
Options:  | 
```

Both copies agree, so there is no replay hazard — this is a content-quality defect in the artifact
the whole feature exists to produce.

**Fix.** Hoist the empty-and-duplicate checks above the `switch` in both files; leave the
"at least two" and "recommendation names one" rules inside the `BusinessDecision` arm. Local patch,
and it must land in both copies together.

---

## Observations (no action required)

- **`AddConstraint` scope uniqueness is checked on untrimmed input at command time, trimmed at
  replay time.** `CommandHandler.cs:568` runs `EnsureUnique(command.Scope, …)` on the raw values;
  `TaskTransitionValidator.cs:583` runs it on the trimmed values that were stored. `["a", " a"]`
  passes the first and fails the second. It fails closed inside `ApplyEvents`, before
  `AppendEventsAsync`, with an identical message, so nothing is persisted and the operator sees a
  correct error. This mirrors the pre-existing `AddWorkItem` / `ResourceScope` pattern. Harmless
  today; it is the shape a real divergence would take, so it is worth trimming before the
  uniqueness check if either site is touched again.

- **`Constraint.Scope` never narrows anything.** `Constraint` is in `AlwaysIncludedKinds`, so
  `IsRelevant` returns `true` before scope is consulted. The field is captured, validated, stored
  and surfaced as `RelatedIds`, but has no filtering effect. Given the operator guide describes
  `--scope` as free text, this is defensible as documentation-in-state; just be aware the name
  promises filtering it does not do.

- **Escalation and constraint `RelatedIds` widen the relevance set.** `BuildStateArtifacts` feeds
  every state artifact's `RelatedIds` into `relevantIds`, which then decides which *external*
  artifacts survive `IsRelevant`. Escalations contribute their `WorkItemId` and evidence ids;
  constraints contribute arbitrary scope strings. Building context for work item A can therefore
  admit an external artifact related to work item B, because an escalation on B is always included.
  Currently inert — the CLI supplies only `Rules`, `Skill` and `StopCondition`, and skills are
  role-gated separately. It becomes real the moment `PromptContract` or `VerifierOutput` artifacts
  are supplied, which is also when Major 2 gets worse.

- **`Escalation` and `Alternative` sort last in the manifest.** The `ContextArtifactKind` comment
  correctly documents that declaration order is manifest sort order, and appending was the right
  call for compatibility. The consequence is that an open operator-blocking question lands below
  the work item and the stop conditions in the emitted bundle. Worth a thought if manifest ordering
  is meant to convey priority to a consumer.

- **A manually blocked item that later goes `Stale` keeps its `BlockReason`.** `InvalidateWorkItem`
  (`TaskReducer.cs:113`) only sets `Status`. Deterministic and replay-safe, so purely cosmetic —
  but a `Stale` item displaying `blockReason: "awaiting decision"` is misleading in `state.json`.
  Ties into Major 1: if you add an unblock command, clear the reason there too.

- **`ManageWork` is not in any non-operator default role**, so in practice only the operator can
  invoke `work complete` and `work block`. Consistent with `work add` already being operator-only,
  and both rule copies agree. Noting it because the operator guide's framing ("assert completion
  when you are satisfied") reads as if a lead could do it.

---

## What holds up

These were the things most likely to be wrong, and they are not. Stated with the evidence, since
the calibration note asks for replay integrity specifically.

- **The two rule expressions agree for all seven new commands.** I compared
  `CommandHandler.RaiseEscalation/ResolveEscalation/RecordAlternative/AddConstraint/
  SupersedeConstraint/CompleteWorkItem/BlockWorkItem` against their
  `TaskTransitionValidator.Validate*` counterparts predicate by predicate. Identity, existence,
  status-precondition, payload, provenance and authority checks all match, including the two
  operator-only gates (`ResolveEscalation`, `ManageConstraints`) which are enforced both in
  `AuthorizationPolicy` and via `operatorRequired: true` at replay. The only divergence I found is
  the untrimmed-scope one above, and it fails closed.

- **The `WorkItemInvalidated` relaxation is symmetric.** `CommandHandler.cs:800` filters
  `is not Stale`; `TaskTransitionValidator.cs:395` throws only on `Stale`. The expected-status
  computation (`Active ? Blocked : Stale`) is identical in both. The change is a relaxation, so
  pre-existing event logs still replay.

- **No new event type can produce unreplayable state.** Verified empirically, not just by reading:
  after committing all seven new commands through the real file store, deleting `state.json` and
  forcing a full replay produced a **byte-identical** file. `GovernedTaskState`'s three new
  dictionaries are appended after `Runs`, the `ContextArtifactKind` members are appended, and both
  sites carry a comment saying why. Empty `IReadOnlyList` fields serialize as `[]` rather than being
  omitted (confirmed in `events.jsonl`: `"options":[]`), so there is no null-collection hazard on
  the replay path. `EventRegistrationTests.R2` guards the validator switch against a future
  omission, which is the failure mode that would make a task permanently unreadable.

- **Ordering and determinism in context assembly and projections are correct.** Every enumeration
  over the new dictionaries — `ContextAssembler.BuildStateArtifacts`, both `ToArtifact` related-id
  lists, and all three new `MarkdownTaskProjectionWriter` sections — uses an explicit `OrderBy` with
  `StringComparer.Ordinal`. No reliance on `Dictionary` enumeration order. `HasOpenRun` and
  `HasOpenEscalation` use `Any`, where order is irrelevant. `Environment.NewLine` in the new
  `ToArtifact` overloads matches the pre-existing overloads; it makes bundle content
  platform-dependent, but consistently so, and no hash is taken over it.

- **The `CompleteRun` → `Paused` change is right, and its guard survived.** The
  `is not (Blocked or Stale)` check at `TaskReducer.cs:147` is preserved, so a run finishing on an
  invalidated item does not resurrect it. Separating "the process exited" from "the work is done" is
  the correct call and `WorkLifecycleTests` pins it.

- **Concurrency is unchanged and was not weakened.** All new work is pure, static, in-memory
  reduction under the existing per-task file lease. Nothing was added to the async or IO path.
