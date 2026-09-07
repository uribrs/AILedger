# Verifier 1 — close-claim-supersession-and-challenge-consequence

Verified against working tree vs `310061fefbe858dd79ee536d583fe22541aed27c` (state.json baseRef).
Work is uncommitted; `git status` shows only the expected 13 modified and 2 new source files plus
the task directory.

## Commands I ran myself

- `dotnet build AILedger.sln --no-restore` — Build succeeded, 0 Warning(s), 0 Error(s).
- `dotnet test AILedger.sln` — **153 passed, 0 failed, 0 skipped**. Baseline was 140.
- Each of the five attention-item tests individually — all passed (table below).
- Nine adversarial probes I wrote and then deleted (results below).
- A real CLI run through `src/AILedger.Cli` driving a refinement end to end, then reading back
  `events.jsonl`, `state.json` and `assumptions.md`.

## Success Criteria — one at a time

### Gap A — evidenced, recoverable supersession

| Criterion | Verdict | Evidence |
|---|---|---|
| `Claim` carries the id of the claim that superseded it | met | `GovernanceModels.cs:138` `ClaimId? SupersededByClaimId = null`, appended last so no property reorder; written by `TaskReducer.ResolveClaim` (`TaskReducer.cs:99`) |
| Resolving to `Superseded` requires naming the replacement | met | `CommandHandler.cs:204-207`; CLI run: `claim resolve --status superseded` without `--superseded-by` returned `error: Superseding a claim requires naming the claim that replaces it.` |
| Replacement must exist, must not be the claim itself, must not be `Rejected`/`Superseded` | met | `CommandHandler.cs:209-219`. Existence and self-reference and `Rejected` are covered by `ClaimSupersessionTests.SupersedingRequiresANamedReplacementThatIsNotItselfAndIsCurrent`. The `Superseded` replacement case was **not** in the suite — I wrote a probe for it and it was refused with `cannot replace another claim`. |
| Resolving to `Validated` or `Rejected` refuses a supplied replacement | met | `CommandHandler.cs:193-201`. Suite covers `Validated` only; my probe covered `Rejected` and it was refused. |
| Two outcomes, kernel-derived, not actor-declared | met | `ResolveClaimCommand` has no outcome field (`Commands.cs:60`); the only input is the replacement id. `CommandHandler.DeriveSupersession` (`CommandHandler.cs:226-236`) computes it. |
| `refinement` only when replacement is `Validated` **and** nothing refutes the original | met | `CommandHandler.cs:231-235`; `ClaimSupersessionTests.EvidenceRefutingTheOriginalForcesACorrectionEvenWhenTheReplacementIsValidated` proves the second conjunct is load-bearing; `InvalidationTests.SupersedingByAnUnvalidatedClaimIsACorrectionAndMarksDependentWorkStale` proves the first |
| correction cascades as today; refinement re-points | met | `CommandHandler.cs:177-186`; `TaskReducer.RepointDependencies` (`TaskReducer.cs:185-206`); asserted in `ClaimSupersessionTests.AnEarnedRefinementRepointsDependentsInsteadOfInvalidatingThem` (work stays `Proposed`, decision stays `Proposed`, both now depend on `C2`) |
| Outcome recorded on the emitted event | met | `Events.cs:46` `SupersessionOutcome? Outcome`; my CLI run persisted `"outcome":"refinement"` in `events.jsonl` |
| Both rule copies agree | met | See "Mirror parity" below — the two derivations are textually identical |
| Tests prove refusals, both outcomes, the boundary, and survival of a real replay | **met with gaps** | Both outcomes, the boundary, and replay are proven (`NewCommandReplayTests.R4_...` drives both outcomes through `FileGovernedTaskService` and reads back from a fresh instance). Two refusals are claimed by test names but not actually exercised — see Backlog 1. |

### Gap B — a supported challenge causes a state change

| Criterion | Verdict | Evidence |
|---|---|---|
| target `decision` → invalidated when `Proposed`/`Accepted` | met | `CommandHandler.cs:433-444` emits `DecisionOverturned`; `TaskReducer.OverturnDecision` (`TaskReducer.cs:211-215`) sets `Invalidated`; `ChallengeConsequenceTests.SupportingAChallengeAgainstADecisionOverturnsIt` |
| target `work` → blocked, reason names the challenge | met | `CommandHandler.cs:447-460`; test asserts `BlockReason` contains `CH1` |
| target `claim` → rejected with the challenge's evidence, cascading | met | `CommandHandler.cs:428-429`; test asserts claim `Rejected` and dependent `W1` → `Stale` |
| Claim challenge refused unless its evidence refutes the claim | met | `CommandHandler.cs:419-427` requires **every** cited piece to refute and at least one. My mixed-evidence probe (one refuting, one supporting) was refused. |
| Refused when the target's consequence is already true, naming why | met | Claim `CommandHandler.cs:411-416`, decision `:436-441`, work `:450-455`. `R5_...` covers the claim branch only; decision and work branches untested (Backlog 1). |
| `Rejected`/`Withdrawn` emit only `ChallengeDisposed` | met | `CommandHandler.cs:388-391` guards on `Supported`. Test covers `Rejected`; `Withdrawn` is untested despite the test name (Backlog 1). |
| Both rule copies agree | met | See "Mirror parity" |
| Tests prove three consequences, the evidence-direction refusal, and the claim cascade | met | All four named tests exist and pass |

### Hygiene

| Criterion | Verdict | Evidence |
|---|---|---|
| `AgentRunStatus.Pending` and `WorkItemStatus.Ready` removed | met | `GovernanceModels.cs:95-108`; `grep -rn "AgentRunStatus.Pending\|WorkItemStatus.Ready"` over `src` and `tests` returns nothing |
| Every guard simplified, no dead branch left | met (logic) | Ten sites updated across `CommandHandler.cs:533,559,563,857,1019` and `TaskTransitionValidator.cs:502,521-522,722,915`. No branch now tests a removed member. One **message** still names the removed status — `TaskTransitionValidator.cs:524` reads "Only an active or pending run can transition to a terminal status." while its `CommandHandler` twin was reworded. Logic is correct; the text is stale. Backlog 2. |

### Whole task

| Criterion | Verdict | Evidence |
|---|---|---|
| Every new/changed event replays from a fresh `FileGovernedTaskService` | met | `R4_SupersessionAndChallengeConsequencesReplayThroughTheFileStore` builds a second service over the same root and re-reads; my CLI run produced a `state.json` consistent with the log |
| Consequence events appear in the Markdown projections | met | Claim line gains `Superseded by \`C2\`.` (`MarkdownTaskProjectionWriter.cs:121-124`, asserted in R4 and seen in my CLI run). Decision overturn shows as `**Invalidated**`, re-pointing shows in `Depends on:`, work block shows as `**Blocked**`. The block *reason* is not rendered, but it never was for `work block` either — no regression. |
| `dotnet build` zero warnings, `dotnet test` passes | met | 0 warnings; 153/153 |
| `docs/architecture.md` and `docs/operator-guide.md` describe both behaviours | **NOT MET** | `docs/operator-guide.md` gained two good sections (`:154-186`). `docs/architecture.md` gained **nothing** — its only change is the enum wording at `:42`. Worse, its invariant row at `:39` now states the opposite of the refinement path. See Repair 1. |

## Mirror parity — the derivation

Requested specifically, so quoting both:

`CommandHandler.DeriveSupersession` (`CommandHandler.cs:231-236`)
```
var replacementIsValidated = replacement.Status == ClaimStatus.Validated;
var somethingRefutesTheOriginal = state.Evidence.Values.Any(item => item.Refutes.Contains(superseded.Id));
return replacementIsValidated && !somethingRefutesTheOriginal ? Refinement : Correction;
```

`TaskTransitionValidator.ValidateClaimResolved` (`TaskTransitionValidator.cs:254-257`)
```
var expected = replacement.Status == ClaimStatus.Validated &&
               !state.Evidence.Values.Any(item => item.Refutes.Contains(resolved.ClaimId))
    ? Refinement : Correction;
```

`superseded.Id`, `command.ClaimId` and `resolved.ClaimId` are the same value. The predicates are
identical. Both run against pre-`ClaimResolved` state (command time; replay validates before the
reducer applies), and `ClaimResolved` changes neither the replacement's status nor any `Evidence`
record, so the derivation is stable across the batch. A third copy of the same predicate guards
`ClaimDependenciesRepointed` at `TaskTransitionValidator.cs:797-803`, evaluated after `ClaimResolved`
has been applied — same inputs, same result.

Gap B has no single mirrored predicate: the consequence selection lives only in
`CommandHandler.AddChallengeConsequence`, and replay validates each emitted event on its own terms.
Every command-time guard is at least as strict as its replay counterpart, so nothing the handler
commits can fail to replay:

| Emitted event | Command-time guard | Replay guard | Direction |
|---|---|---|---|
| `ClaimResolved(Rejected)` | claim not `Rejected`/`Superseded`, all challenge evidence refutes, actor holds `ResolveClaim` | `EnsureClaimResolution`, per-evidence refute check, `RequireAuthority(ResolveClaim)` | handler ⊇ validator |
| `DecisionInvalidated` / `WorkItemInvalidated` (cascade) | actor holds `ResolveClaim` | `RequireAuthority(ResolveClaim)` (`:365`, `:444`) | equal |
| `DecisionOverturned` | decision `Proposed`/`Accepted`, actor holds `ResolveDecision` | same, plus challenge must be `Supported` and target the decision (`:806-823`) | validator stricter, and satisfied because `ChallengeDisposed` is emitted first |
| `WorkItemBlocked` | not `Blocked`/`Stale`/`Completed`, actor holds `ManageWork` | not `Completed`, `RequireAuthority(ManageWork)` | handler stricter |

R1 was real and is closed: `RequireConsequenceCapability` (`CommandHandler.cs:467-474`) demands the
consequence's own capability at command time. I confirmed no capability is implied by role —
`RequireAuthority` (`:826-842`) and `AuthorizationPolicy` grant nothing to an Operator implicitly —
so the two sides use the same test. I also confirmed the consequence path grants no authority an
actor could not exercise directly: `BlockWorkItemCommand` needs only `ManageWork`
(`AuthorizationPolicy.cs:64`), with no owner or operator gate.

## Adversarial probes — trying to defeat the refinement gate

Nine probes written, compiled, run, then deleted (tree is clean). All nine behaved as the design
claims:

1. Forged `ClaimResolved(Superseded, C2, Refinement)` while `C2` is only `Open` → refused,
   `does not match the state-derived outcome`.
2. Forged `Correction` where state derives `Refinement` → also refused. The check is symmetric, so
   the outcome column cannot be forged in either direction.
3. Forged `ClaimDependenciesRepointed` after a genuine correction → refused,
   `only re-pointed when the replacement is validated`.
4. Forged `DecisionOverturned` against an open (undisposed) challenge → refused.
5. Replacement that is itself `Superseded` → refused.
6. `Rejected` resolution carrying a replacement id → refused.
7. **The A9 question.** The only door to `Refinement` is a `Validated` replacement.
   `ResolveClaim` refuses `Validated` with zero evidence (`CommandHandler.cs:146-149`) and refuses
   evidence whose `Supports` does not name the claim (`:151-166`). I tried both and both were
   refused; the supersession that followed came out `Correction`. There is no other route: a new
   claim must be created `Open` (`AddClaim` hard-codes it, and `ValidateClaimAdded:176` refuses
   anything else on replay). **An actor cannot reach the refinement path without first putting a
   directed, supporting evidence record on the replacement.**
8. Claim challenge with mixed supporting/refuting evidence → refused.
9. Re-pointing a dependent that already named both claims → `Distinct()` prevents a duplicate.

The gate is procedural, not epistemic: nothing checks that the evidence record's *content* is true,
so a determined agent can still buy the cheap branch for the price of writing one evidence record and
one validation. That is what the operator's decision asked for, and it is a genuine cost the
destructive default no longer carries. Noted, not a finding.

## Enum removal — no dead branch, no persisted breakage

`LedgerJson.CreateOptions` registers `JsonStringEnumConverter(JsonNamingPolicy.CamelCase,
allowIntegerValues: false)` (`LedgerJson.cs:18`), so enum members persist as camelCase **strings**
and integers are refused on read. Deleting members 0 of `AgentRunStatus` and 1 of `WorkItemStatus`
shifts ordinals but cannot shift any persisted value. There is no `*.jsonl` fixture in the
repository. The only surviving textual references are prose: `TaskTransitionValidator.cs:524` and
`docs/operator-guide.md:325`.

## Constraints

| Constraint | Held | Note |
|---|---|---|
| Scope limited to the two gaps plus the enum hygiene | yes | Diff touches nothing else; six unrelated findings went to `## Backlog` in `execution_notes.md` as required |
| Do not collapse the `CommandHandler` / `TaskTransitionValidator` duplication | yes | Both files grew (+161 / +109); no shared helper was extracted; the mirrors are written out twice by hand |
| Every rule in both copies, agreeing | yes | See Mirror parity |
| New event types registered in the validator switch | yes | `TaskTransitionValidator.cs:84-89`, `TaskReducer.cs:37-38`, `Events.cs:39-40`; `R2_...` enumerates `JsonDerivedTypeAttribute` reflectively and passes |
| Do not reorder or rename `GovernedTaskState` properties | yes | `TaskState.cs:3-25` unchanged. `Claim` gained one property appended last, so the byte-compared shape is stable |
| Do not modify `cognitive/` or `src/AILedger.Providers` | yes | Neither appears in the diff |
| Do not weaken authorization, causal invalidation, or code-reviewer isolation | yes | Authorization was **strengthened** (R1). Causal invalidation is untouched on the correction path; `EnsureDependenciesAreCurrent` is unmodified. Context assembly and code-reviewer isolation are not in the diff at all |
| Simplest mechanism, no hardening beyond criteria | yes | Two new event types, one new enum, one new claim field, one command parameter |
| Do not commit or push | yes | `git log` head is still `310061f` |

## Assumption Disposition

| id | status | name | citation | actor |
|---|---|---|---|---|
| A1 | VALIDATED | replacement-id-is-the-better-gate | Supersession is now recoverable from state alone: `Claim.SupersededByClaimId` (`GovernanceModels.cs:138`) survives replay (`NewCommandReplayTests.cs:136`) and renders in `assumptions.md` (`MarkdownTaskProjectionWriter.cs:121-124`); confirmed in my own CLI run, which produced `- \`C1\` — **Superseded** — ... Superseded by \`C2\`.` | verifier |
| A2 | REJECTED | challenge-consequence-needs-no-new-event | A new event type was required for the decision branch. `DecisionOverturned` (`Events.cs:67`, registered `Events.cs:40`) exists because `ValidateDecisionInvalidated` (`TaskTransitionValidator.cs:369-373`) demands a rejected or superseded **dependency claim**, which a challenge-caused invalidation does not have. The contract's stop condition was scoped to a new *claim status*, not a new event type, so execution correctly continued; `decisions.md` names this exact contingency and it was taken | verifier |
| A3 | VALIDATED | reuse-evidence-direction-rule-for-challenges | `CommandHandler.cs:419-427` requires every cited piece of challenge evidence to refute the claim and at least one to exist; `ChallengeConsequenceTests.AClaimChallengeCannotBeSupportedUnlessItsEvidenceRefutesTheClaim` passes, and my mixed-evidence probe was refused | verifier |
| A4 | VALIDATED | enum-removal-is-safe | Enums persist as camelCase strings with `allowIntegerValues: false` (`LedgerJson.cs:18`), so the ordinal shift cannot reach disk; no `*.jsonl` fixture exists in the repo; `grep` finds zero remaining code references; full suite 153/153 | verifier |
| A5 | VALIDATED | consequences-do-not-disturb-archive | The archive prerequisite tests `ChallengeStatus.Open` (`TaskTransitionValidator.cs:915-916`, `CommandHandler.cs:1024-1027`), and `ChallengeDisposed` is appended before any consequence (`CommandHandler.cs:387-391`), so a disposed challenge stops blocking archive exactly as before; that predicate is unchanged apart from the `AgentRunStatus.Pending` removal | verifier |
| A6 | REJECTED | new-field-is-contained-to-handler-reducer-state | The prior-art lesson said a new field is contained to the handler, reducer and state. `SupersededByClaimId` reached eight files: `GovernanceModels.cs`, `Commands.cs`, `Events.cs`, `CommandHandler.cs`, `TaskReducer.cs`, `TaskTransitionValidator.cs` (+60 lines of mirrored rules), `CliApplication.cs`, `MarkdownTaskProjectionWriter.cs`. The replay validator and the operator surface are the two the lesson omits | verifier |
| A7 | REJECTED | terminal-records-are-inert | `TaskReducer.RepointDependencies` (`TaskReducer.cs:188,197`) selects dependents purely by `DependsOnClaims.Contains(...)` with **no status filter**, unlike `CommandHandler.AddDependencyInvalidations` (`CommandHandler.cs:957,968`) which filters to current records. A `Completed` work item or an `Invalidated` decision therefore has its dependency list silently rewritten by a later refinement. Harmless to the guards, but it is a mutation of a terminal record. Backlog 3 | verifier |
| A8 | VALIDATED | one-command-branching-on-a-type-column | `CommandHandler.AddChallengeConsequence` (`CommandHandler.cs:398-465`) serves three different outcomes from one command by switching on `challenge.TargetType`, and its alias set (`"work" or "workitem" or "work-item"`) is identical to `EnsureChallengeTargetExists` (`TaskTransitionValidator.cs:893-899`), so the `default:` arm is unreachable from a validly raised challenge | verifier |
| A9 | VALIDATED | refinement-requires-directed-evidence | Probe 7 above. `Refinement` requires `replacement.Status == Validated`; `Validated` requires at least one evidence record whose `Supports` names the replacement (`CommandHandler.cs:146-166`, mirrored `TaskTransitionValidator.cs:196-212`); a claim cannot be born `Validated` (`ValidateClaimAdded`, `TaskTransitionValidator.cs:176`); and a forged `Outcome` is refused in both directions at replay (probes 1 and 2) | verifier |
| A10 | VALIDATED | repoint-is-expressible-without-relaxing-currency | `ClaimDependenciesRepointed` is accepted at replay by `ValidateClaimDependenciesRepointed` (`TaskTransitionValidator.cs:779-804`) with `EnsureDependenciesAreCurrent` (`:879-889`) left unmodified — the rule is satisfied rather than relaxed, because a refinement re-points onto a `Validated` claim. `R4_SupersessionAndChallengeConsequencesReplayThroughTheFileStore` passes on a fresh service instance | verifier |

## Attention Item Disposition

| id | final disposition | name | evidence |
|---|---|---|---|
| R1 | handled | challenge-capability-replay-trap | Ran `ChallengeConsequenceTests.R1_SupportingAChallengeRequiresTheConsequenceCapabilityAtCommandTime` — Passed (1/1). `RequireConsequenceCapability` (`CommandHandler.cs:467-474`) demands `ResolveClaim`/`ResolveDecision`/`ManageWork` per branch, matching `RequireAuthority` in each replay validator exactly; I checked that no capability is implied by the Operator role on either side, and that the `ClaimResolved` cascade events (`DecisionInvalidated`, `WorkItemInvalidated`) also require only `ResolveClaim`, which the claim branch already demands |
| R2 | handled | new-event-unregistered | Ran `EventRegistrationTests.R2_EveryDerivedEventTypeIsAcceptedByTheTransitionValidator` — Passed (1/1). The test reflects over `JsonDerivedTypeAttribute`, so it covers `ClaimDependenciesRepointed` and `DecisionOverturned` automatically. Both are registered in all three places: `Events.cs:39-40`, `TaskReducer.cs:37-38`, `TaskTransitionValidator.cs:84-89`. My CLI run persisted `"eventType":"claim.dependencies-repointed"` and it replayed |
| R3 | handled | claim-evidence-overwrite | Ran `ChallengeConsequenceTests.R3_ChallengeRejectionPreservesEvidenceAlreadyOnTheClaim` — Passed (1/1). `TaskReducer.ResolveClaim` (`TaskReducer.cs:88-98`) now unions `current.EvidenceIds` with the event's and de-duplicates, so a challenge rejection keeps the supporting evidence the claim already carried |
| R4 | handled | refinement-repoint-replay-parity | Ran `NewCommandReplayTests.R4_SupersessionAndChallengeConsequencesReplayThroughTheFileStore` — Passed (1/1), 383 ms. It drives a refinement chain, a correction chain and a supported decision challenge through `FileGovernedTaskService`, then reads state from a second service instance and asserts the replacement id, both re-pointed dependency lists, both work statuses, the invalidated decision, and the projection text. I independently reproduced the refinement through the real CLI and inspected `events.jsonl` and `state.json` |
| R5 | handled | consequence-already-true | Ran `ChallengeConsequenceTests.R5_SupportingAChallengeIsRefusedWhenItsConsequenceIsAlreadyTrue` — Passed (1/1). The refusal names the reason (`is already 'Rejected'; supporting this challenge would change nothing`) rather than failing incidentally inside `EnsureClaimResolution`. The equivalent guards exist for the decision (`CommandHandler.cs:436-441`) and work (`:450-455`) branches but are not covered by a test — Backlog 1 |

## Decision Drift

`decisions.md` has eight entries. Seven landed as written; one drifted and was not written back.

1. **Supersession names its replacement rather than requiring generic evidence.** No drift.
   `ResolveClaimCommand.SupersededByClaimId` (`Commands.cs:60`) is the only new input, and
   `Superseded` still requires no evidence of its own.
2. **Supersession is two behaviours, refinement and correction.** No drift. `SupersessionOutcome`
   (`GovernanceModels.cs:125-129`) carries the operator's reasoning in its comment.
3. **Derived from state, never declared by the resolving actor.** No drift, and stronger than
   promised: the command has no outcome field at all, and the replay validator re-derives and
   refuses a mismatch in **both** directions (probes 1 and 2).
4. **Destructive default; the cheap path carries the entry condition.** No drift.
   `CommandHandler.cs:231-235` returns `Correction` unless both conditions hold.
5. **A supported challenge reuses existing causal machinery rather than introducing a parallel one.**
   **DRIFT.** The claim and work branches reuse `ClaimResolved` and `WorkItemBlocked` as decided.
   The decision branch could not: `ValidateDecisionInvalidated` (`TaskTransitionValidator.cs:369-373`)
   requires the decision to have a rejected or superseded **dependency claim**, which a challenge
   against the decision itself does not supply. A new `DecisionOverturned` event was introduced.
   This was the contingency written into the last line of `decisions.md`, and recon predicted it as
   design-invalidating, so the choice is right. But the Execution Rules require a deviation to be
   recorded in **both** `decisions.md` and `execution_notes.md`, and only `execution_notes.md` has
   it. `decisions.md` still reads as though no parallel machinery was introduced, and
   `assumptions.md` still carries A2 as OPEN rather than refuted. Documentation drift, not a
   behavioural one. Backlog 4.
6. **Evidence-direction rule governs the challenge path too.** No drift, and implemented more
   strictly than stated: every piece of the challenge's evidence must refute, not merely one.
7. **Remove the two enum members rather than finding a use for them.** No drift in logic; one stale
   message survives (Backlog 2).
8. **Proceeding on unverified: a supported claim challenge is a normal rejection, no new claim
   status needed.** Held. No claim status was added.

## Repairs

**Repair 1 — `docs/architecture.md` describes neither new behaviour, and now misstates one.**
Violates the Whole-task Success Criterion "`docs/architecture.md` and `docs/operator-guide.md`
describe both behaviours." The file's only change in this diff is the enum wording at `:42`.

- `docs/architecture.md:39` reads: "Rejecting or superseding a claim emits invalidation events for
  dependent current decisions and work; active work becomes `Blocked`, other dependent work becomes
  `Stale`". After this change that is false for a refinement, which emits no invalidation and
  re-points dependents instead. The row needs the correction/refinement split and the state-derived
  entry condition.
- There is no rule-to-mechanism row for the challenge consequence. Challenges appear in the table
  only as an archive prerequisite (`:52`). The invariant "a supported challenge must change
  something" — the entire point of Gap B — is unrecorded.

Two rows of that table. `docs/operator-guide.md` already has the prose to draw on.

## Backlog — not repairs, recorded and not fixed here

1. **Two tests claim coverage they do not have.**
   `ClaimSupersessionTests.ValidatedAndRejectedResolutionsRejectASuppliedReplacement` exercises only
   `Validated`; `ChallengeConsequenceTests.RejectedAndWithdrawnDispositionsStillEmitOnlyTheDisposalEvent`
   exercises only `Rejected`. Also untested: a replacement that is itself `Superseded`, and the
   already-terminal refusal on the decision and work branches. I probed the first three myself and
   the behaviour is correct — the code is right, the names overstate the suite.
2. **`TaskTransitionValidator.cs:524` still says "Only an active or pending run can transition to a
   terminal status"** after `AgentRunStatus.Pending` was deleted. Its `CommandHandler` twin
   (`:561`) was reworded; this copy was missed, so a matched pair of messages has newly diverged.
   `docs/operator-guide.md:325` has the same stale "active/pending run" phrasing.
   `execution_notes.md` claims the message was reworded — it was reworded in one of the two copies.
3. **Re-pointing does not filter by status** (`TaskReducer.cs:188,197`), so a `Completed` work item
   or an `Invalidated` decision has its `DependsOnClaims` rewritten to name a claim it never
   depended on. The criterion as written says "dependent decisions and work items are re-pointed",
   without a filter, so the implementation matches the contract. But in an audit ledger, editing the
   dependency list of a finished record is a provenance distortion, and the sibling
   `AddDependencyInvalidations` does filter. Worth a decision next time this file is open.
4. **`decisions.md` and `assumptions.md` were not written back** after the A2 contingency was taken
   (see Decision Drift 5). Closeout should mark A2 refuted and amend decision 5.
5. **Replay cannot tell that a supported claim or work challenge produced its consequence.**
   `DecisionOverturned` carries its `ChallengeId` and the validator checks the link
   (`TaskTransitionValidator.cs:817-822`); the `ClaimResolved` and `WorkItemBlocked` consequences
   carry no back-reference, so a hand-edited log containing `ChallengeDisposed(Supported)` with the
   consequence removed replays cleanly. Command-time behaviour is correct, which is what the
   criteria ask for; only a forged log can exploit this.
6. The six items already recorded under `## Backlog` in `execution_notes.md` stand; I found nothing
   that contradicts them.

## Verdict

**requires-repair** — one item: Repair 1, `docs/architecture.md`.

Everything else holds. The two gaps are genuinely closed. The refinement gate survived nine
adversarial probes, the two rule copies derive the outcome identically, a forged outcome is refused
at replay in both directions, both new event types are registered in all three places, the enum
removal touches no persisted value, and every constraint in `constraints.md` held — including the
two the lead named specifically: the `CommandHandler` / `TaskTransitionValidator` duplication is
intact and hand-written twice, and authorization was strengthened rather than weakened.
