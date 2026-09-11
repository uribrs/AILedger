# The self-scoring rubric

How to score one archived governed task across the ten dimensions of `Backlog/self-scoring.md`.

You are a scoring agent. You have two things and nothing else: the output of
`ailedger retrospective build --task <id>`, and the task's own ledger. Everything below tells you
what each dimension may read, what it may cite, what you have to supply that no field carries, which
measures a `notMeasured` key takes away from it, and what forces it to `unmeasured`.

This document consumes the evidence-binding table fixed in the prompt contract for task
`2026-09-07_2136-workflow-retrospective`, revision 4. It does not author it. Every field named here
was read in real `retrospective build` output on the six calibration tasks under kernel 2.0.90 from
`b54a538`.

---

## 1. What you are given

**The projection.** `ailedger retrospective build --task <id>` has a vocabulary of nineteen
top-level blocks: `task`, `version`, `stage`, `wallClockHours`, `agentMinutes`, `events`, `runs`,
`cost`, `epistemic`, `causalChains`, `authorship`, `refusals`, `stages`, `artifacts`, `escalations`,
`lessons`, `workItems`, `notMeasured`, `coordinatorLoop`.

Eighteen of them are on every build. `refusals` is conditional: it is omitted when the refusal
journal was not measured, and that task carries `refusals` in its `notMeasured` list instead.
Calibration items 1 to 4 emit eighteen blocks for that reason; items 5 and 7 emit nineteen.

The projection scores nothing. It has no score field of any kind. It gives you counts, durations,
joins and a list of what it could not measure. Every score in your output is yours.

**The ledger.** The task's event log, reachable with `ailedger history --task <id>` and
`ailedger status --task <id>`. This is where you read a claim's statement, an evidence record's
citation, a waiver's reason text, an escalation's question, and any field of a run record that the
projection does not surface.

**Nothing else.** Do not read `Backlog/Priorities.md` or any prose verdict about the task. Two rows
of that document have been found wrong. Your evidence is the record.

---

## 2. What you produce

One table, exactly this header, exactly these ten rows, each once:

```
| dimension | score | confidence | controllable | evidence |
```

The cell vocabulary is frozen in the kernel and checked when the artifact is filed
(`src/AILedger.Core/Application/ArtifactRules.cs:280-292`):

- `score` — one of `0`, `1`, `2`, `3`, `4`, `5`, `unmeasured`
- `confidence` — one of `low`, `medium`, `high`
- `controllable` — one of `yes`, `no`, `not-applicable`
- `evidence` — any non-empty text

Row ids are `D1` through `D10` and map to the dimensions in the order below. A row with any other id
is refused. The kernel reads the score cell only to test that it is in the vocabulary. It never
reads it to decide anything. A body whose ten scores are all `0` is accepted exactly as one whose
scores are all `5`.

**Five fields per dimension, none optional.** The table carries four of them. The fifth,
**explanation**, goes in the per-dimension prose section beneath the table. A dimension with no
explanation is incomplete even though the table validator accepts it.

The same is true of a missing named absence. Where part of a dimension's evidence is in
`notMeasured`, the explanation must name every absent measure and the confidence must carry that
absence — guards 2 and 3 of section 3. The kernel cannot check either; both are conditions of a
well-formed dimension all the same.

1. **score** — the table's `score` cell
2. **evidence** — the table's `evidence` cell, naming fields, record ids, event ids or run ids
3. **explanation** — prose beneath the table, saying what the evidence means
4. **confidence** — the table's `confidence` cell
5. **whether the observed problem was under AILedger's control** — the table's `controllable` cell

`controllable` is about the *problem*, not the score. Use `yes` when what went wrong is something
AILedger's own planning, routing, briefing, gating or ergonomics caused. Use `no` when the cause is
outside it — a provider fault, an operator choice, the domain. Use `not-applicable` when no problem
was observed, or when the dimension is `unmeasured`.

Together, `explanation` and `controllable` are where **conduct** is recorded. The `score` cell
carries **effect** and nothing else — see "Score effect, not conduct" in section 5, which governs all
ten dimensions. A mechanism that performed every required step and achieved nothing scores low and
is normally `controllable: yes`; the steps it performed go in the explanation, where they belong.

### The 0–5 scale

From `Backlog/self-scoring.md`, unchanged:

| score | meaning |
|---|---|
| 0 | failed / absent |
| 1 | seriously deficient |
| 2 | weak |
| 3 | acceptable |
| 4 | strong |
| 5 | excellent |

### There is no overall number

Ten dimensional scores and nothing else. No weighted mean, no grade, no total, no rank. This
document supplies no weights and no formula, deliberately, because a single number destroys the
reading the whole capability exists to produce: governance effectiveness 5 with governance cost 2
means the system protected the work extremely well and made doing so painful. Do not compute one,
and do not put one in the prose.

---

## 3. The unmeasured rule

**A dimension is scored `unmeasured` in full only when all of its evidence is in `notMeasured`.
Where only part of its evidence is absent, the dimension is scored from the evidence it has, and
every absent measure is named in its explanation.**

That is the rule, and it is the rule in the prompt contract, section 4. It replaced a literal one —
"a dimension whose evidence appears in `notMeasured` is scored `unmeasured`" — which made D8
`unmeasured` on every task the projection can currently produce, including one where 12 of 15 runs
recorded cost and the token totals are real numbers. The `5 / 2` reading the whole capability exists
to produce — protected well, cost too much — could not have been produced even once under it.

Two things follow, and neither is optional.

**`unmeasured` still means nobody measured it, never `0` and never `3`.** A scoring run that reaches
for a middle number is reporting a governance failure where the record only says nobody measured it.
`unmeasured` is a legal score cell. Use it when it is true.

**D10 is `unmeasured` unconditionally, and that is a standing exception, not an instance of this
rule.** Nothing in the log speaks to outcome quality at all, so there is no partly-present evidence
to score from.

When you score a dimension `unmeasured` in full, the other four fields are still required: the
`evidence` cell names the `notMeasured` keys that forced it, `confidence` is `high` (you know it is
unmeasured), `controllable` is `not-applicable`, and the explanation says what would have to be
recorded for the dimension to be scoreable next time.

### The three guards on a partially scored dimension

A scored dimension must never be mistaken for a complete one. These three are checkable, and a
verifier will check each one.

1. **`unmeasured` in full only when everything is absent.** For every dimension you score
   `unmeasured`, every emitted field in its section 7 row must map to a key in this task's
   `notMeasured` list. D10 is the standing exception. If even one measure survives, you score the
   dimension.
2. **Naming is mandatory, not optional.** A partially scored dimension that does not list its absent
   measures in its explanation is **malformed**, in exactly the way a dimension missing its
   `confidence` field is malformed. A reader must be able to see what the score was *not* computed
   from without leaving the row. Name the measures, not the key: `governanceCost.tokens` is the key,
   and what it took away is `cost.turns`, `cost.outputTokens`,
   `cost.millisecondsToFirstLedgerWrite`, `cost.tokensInUncached`, `cost.tokensInCacheWrite` and
   `cost.tokensInCacheRead`.
3. **Confidence must carry the absence.** A dimension scored from half its evidence at `high`
   confidence is the specific failure these guards exist to prevent, and it is the one you will
   reach for. A dimension carrying a named absence does not carry `high` confidence unless its
   explanation says why the absent measures cannot change the score — and "they were not available"
   is not that reason.

An honest `unmeasured` dimension is a result. Do not score a dimension you cannot honestly score
from what remains; say so, and say why.

### An absent field is not an unmeasured field

Two different absences, and confusing them is the error this rubric was revised to remove.

- A key in the `notMeasured` list means **nobody measured this**. It takes away the measures it
  names, and it forces `unmeasured` only when those are all the measures the dimension has.
- A JSON field missing from the output because its value was null means **this did not happen**. It
  is an observation, not a gap.

`workItems[].providerThatVerifiedItsOwnWork` and `workItems[].completedWithoutVerificationReason`
are the live instances. Neither appeared on any of the six calibration tasks, because on those tasks
neither condition occurred. Their absence is a fact about the task, not a gap in the instrument.
Only the `notMeasured` list declares unmeasured.

### The twelve-key `notMeasured` vocabulary

Nine keys were observed emitted across the six calibration tasks. Three were not. The conditions
below are read from `src/AILedger.Core/Application/TaskRetrospective.cs:460-537`.

| key | added when | what it takes away | dimension | observed |
|---|---|---|---|---|
| `outcomeQuality` | unconditionally, on every task | everything D10 could have read | D10 — forces it | all six |
| `coordinatorCost` | `coordinatorLoop.tokenCost.absence` is non-null | `coordinatorLoop.tokenCost` | D8 — named absence | all six |
| `coordinatorLoop.reworkCausedByCoordinatorDecisions` | no dispatch names the coordinator record that prompted it | the attribution verdict on `coordinatorLoop.dispatches.rework`, not its volume counts | D8 — named absence | all six |
| `coordinatorLoop.failedDispatchesAttributableToCoordinator` | no launch carries its limit, the provider's terminal reason, or what ended it | the `judged` verdict on `coordinatorLoop.dispatches.failedDispatches`, not its `launches` count | D8 — named absence | all six |
| `coordinatorLoop.wastedDispatches` | no launch carries the provider's own terminal reason | the `judged` verdict on `coordinatorLoop.dispatches.wastedDispatches` | D8 — named absence | all six |
| `coordinatorLoop.sessionWallClock` | the history predates coordinator sessions, so no bracket exists | the bracketed session wall clock | D7 — named absence | all six |
| `coordinatorLoop.timeWithNoAgentRunning` | same condition as above | the idle time inside a bracket | D7 — named absence | all six |
| `governanceCost.tokens` | the task has runs and not one of them measured any of the four token fields | `cost.turns`, `.outputTokens`, `.millisecondsToFirstLedgerWrite`, `.tokensInUncached`, `.tokensInCacheWrite`, `.tokensInCacheRead` | D8 — named absence | items 1–4 and item 5 |
| `refusals` | the refusal journal is absent, or some of its rows would not parse | the whole `refusals` block **and** `coordinatorLoop.refusals` | D1, D5, D8 — named absence in each | items 1–4 |
| `runs.briefDelivered` | the task has runs and **not one** of them delivered a brief | `runs.briefDelivered` | D4 — would be a named absence | never |
| `runs.bySubjectRole` | **some** run in the task carries no subject role | `runs.bySubjectRole` | D2, D6 — would be a named absence | never |
| `learningBehaviour` | `lessons.recalled` is 0 | nothing emitted; it names no block | D9 | never |

`refusals` is the one key that reaches two blocks. On items 1–4 the top-level `refusals` block and
`coordinatorLoop.refusals` are both absent from the output entirely, so D8 loses
`coordinatorLoop.refusals.firstTime`, `.repeat` and `.repeatedKeys` under a key the table binds to
D1 and D5. Name that absence in D8 as well.

**`runs.briefDelivered` and `runs.bySubjectRole` are emitted on all six and must be read.**
`briefDelivered` carries 7, 5, 4, 4, 12 and 14 across the six tasks in the order of section 6, and
`bySubjectRole` carries a per-role run breakdown on every one of them. Their absence from every
`notMeasured` list says only that no calibration task is in the state that adds the key. It says
nothing about whether the field is emitted. Do not declare either absent.

**`learningBehaviour` is the one name unobserved in both senses.** It names no emitted block at all.
It is only a `notMeasured` key, and the key never fired, because all six tasks recalled at least one
lesson — 1, 3, 2, 1, 10 and 2. Dimension 9's data is the emitted `lessons` block.

---

## 4. Five run-record fields the projection does not surface

These are on `AgentRun` and no emitted field carries their values. The prompt contract names the same
set as four, grouping `model` and `providerVersion` as one line; they are listed separately here
because grouping them is what produced `AC22`, and because they behave differently in the record:

| field | what it says |
|---|---|
| `truncatedLines` | how many provider lines the drain cut at the one-megabyte cap. A nonzero count means the stream is incomplete and anything read from the cut line is missing its tail. Read nowhere in the projection. |
| `launchTimeoutSeconds` | the limit the launch was given |
| `terminalFailureReason` | why the run ended other than by completing, in the provider's own words |
| `providerVersion` | which provider build ran. Read nowhere in the projection. Present on run records across all six calibration tasks. |
| `model` | which cognition actually ran. Read nowhere in the projection, and **frequently absent** — see below. |

Dimensions 6 and 8 may cite them. **Cite them by run id with the field named, and say they come
from the run record and not from the projection.** Read them from the `run.completed` event in
`ailedger history --task <id>`, where they appear under exactly those camelCase keys.

One precision, because the distinction matters when you write the evidence cell.
`launchTimeoutSeconds` and `terminalFailureReason` *are* read inside the projection, by
`CoordinatorMeasurement` — but only as gate conditions deciding whether
`coordinatorLoop.dispatches.failedDispatches` and `.wastedDispatches` can be judged at all. Their
values are never emitted. So the citation rule above holds for all five: the value comes from the
run record.

**`model` and `providerVersion` are not present together, and an earlier revision of this document
said they were.** Corrected on validated claim `AC22` with `BE22`, and re-established independently
against the six event logs:

- `providerVersion` is carried by run records on all six calibration tasks.
- `model` is carried by **five run records in the whole calibration set**, all on item 7, all Claude
  worker runs: `R1`, `R3`, `R5`, `R8`, `R13`. It appears on no run record of items 1 to 5. Item 7's
  own verifier and reviewer runs do not carry it either, and neither do its nine Codex runs, which
  carry `providerVersion` without `model`.

So on five of the six tasks you **cannot** say which cognition performed a verification, and on the
sixth you can say it only for five of fifteen runs. Where D6 would lean on that, say the cognition is
unrecorded rather than inferring it from `providerVersion` or from the actor id. An actor id names
the seat, not the model that sat in it.

`launchTimeoutSeconds`, `terminalFailureReason` and `truncatedLines` are absent from all six,
because those tasks predate the fields.

---

## 5. Reading rules that apply to every dimension

These come from the operator's scoring principles. They are not optional and they are the part a
scoring run gets wrong most easily.

### Score effect, not conduct

**Every one of the ten dimensions scores what the mechanism achieved, not what it performed.** This
rule governs all ten. It is stated here once and then again in the words each dimension needs,
because it is the rule two calibration passes fell three points apart for want of. In practice it
bites on the nine you score: D10 is `unmeasured` on every task and carries no score to get wrong.

A dimension asks whether something worked. It does not ask whether the steps were taken. A
verification that conducted every required step in the required order and caught nothing that was
there did not verify — it ran. A cost recorded precisely is not a cost spent well. A role that
activated and contributed nothing did not utilise the capability. A claim validated against a
citation that does not bear on it was not tested.

Conduct is not discarded. It moves to the two fields built for it:

- the **explanation** says what was performed, in what order, and why it did or did not produce the
  effect. That is where "the verifier ran, the reviewer ran, and the ordering was correct" belongs;
- the **`controllable`** cell says whether AILedger's own planning, routing, briefing, gating or
  ergonomics caused the shortfall. A mechanism that ran exactly as designed and achieved nothing is
  normally `controllable: yes`, because what failed is the design of the mechanism, not its
  execution.

A rubric that scores "the reviewer ran" as 4 can never tell you where governance became ceremony,
and finding that is the whole purpose of scoring at all. Where the effect cannot be seen from the
record, say so, lower the confidence, and do not substitute the conduct you can see for the effect
you cannot.

One clarification against reading rule 2 below, which asks whether a failure was "detected, recorded,
responded to, repaired, re-verified". Detection, response, repair and re-verification are effects and
are scored. Recording is conduct: a failure recorded and then not acted on is a low score with a good
paper trail, and the trail belongs in the explanation.

### The eight reading rules

1. **Do not reward apparent infallibility.** A task where an assumption was wrong, evidence refuted
   it, the decision changed and the work was corrected may be governed better than a task where
   nobody challenged anything. A clean ledger is not a high score.
2. **Failure history is not failure.** Cancelled runs, rejected claims, abandoned work items and
   repaired defects are not negative because they exist. Score what happened after the failure
   became knowable: was it detected, recorded, responded to, repaired, re-verified.
3. **Model confidence is not system confidence.** An implementation agent believing its work correct
   is not evidence of anything. Look for whether AILedger prevented an actor from substituting its
   own confidence for independent evidence.
4. **Effectiveness and cost are independent.** Never infer expensive from ceremonial, and never
   infer efficient from defect-caught. Dimensions 1 and 8 are scored separately and may legitimately
   diverge by three points.
5. **Distinguish productive friction from avoidable friction.** A gate that stopped an agent
   overruling a verifier is productive. A search repeated because the brief did not carry the
   evidence is avoidable.
6. **Distinguish observation from causal inference.** A verifier finding a defect proves that
   verification detected it. It does not prove no other mechanism would have. Say which you are
   claiming.
7. **A count is not a finding.** Every score must name a record. "Eleven runs" is not evidence for
   any dimension; "run R11 filed verifier output A5 whose claim VC1 was validated" is.
8. **One occurrence is a candidate, not a policy.** A ceremony candidate or a proposed workflow
   lesson accumulates citations across tasks. Never recommend removing a mechanism on one task's
   evidence.

---

## 6. The calibration set

Six archived tasks, used to calibrate this rubric. Referred to below by the short names in this
table.

| short name | task id |
|---|---|
| item 1 | `2026-09-07_2136-run-manifest-hash` |
| item 2 | `2026-09-07_2136-lesson-citations` |
| item 3 | `2026-09-07_2136-status-owed` |
| item 4 | `2026-09-07_2136-kernel-version-stamp` |
| item 5 | `2026-09-08_1048-refusal-journal` |
| item 7 | `2026-09-09_1010-retrospective-projection` |

They form three instrumentation tiers: items 1–4 carry nine `notMeasured` keys with `refusals` null
and `costRecorded` 0; item 5 carries eight with four refusal rows; item 7 carries seven with sixteen
refusal rows and cost recorded on 12 of 15 runs. Section 8 gives the per-dimension named absences
each tier produces, which is what a scoring run is measured on.

---

## 7. The ten dimensions

Each dimension states four things: the emitted fields that feed it, the records it may cite, what
you must supply beyond those fields, and what a `notMeasured` key takes away from it — which for
every dimension but D10 is less than all of its evidence, so the dimension is scored with that
absence named.

### D1 — Governance effectiveness

*Did AILedger's mechanisms materially affect the work, or were they merely present?*

**Emitted fields it reads.** `refusals.total`, `refusals.bySite`, `refusals.byActor`,
`refusals.byCommand`; `stages.transitions`, `stages.waivers`, `stages.waiverReasons`;
`workItems[].hasCompletedWorkingRun`, `workItems[].verifierRanAfterLatestWork`; `artifacts.byKind`;
`causalChains`.

**Records it may cite.** Refusal rows by actor, command and message. The full waiver reason text
from `stage.prerequisites-waived`. Claim ids of findings a verifier or reviewer raised. Work item
ids. Causal chain endpoints, which are record ids on both sides.

**What you must supply.** Whether a mechanism *changed behaviour*. A refusal count is a count of
attempts blocked; it does not say whether the blocked actor then did the right thing, routed around
it, or stopped. Read the records after each refusal and say which. A waiver is the same: read its
reason and judge whether it names a real ordering defect or an inconvenience. You may not infer
effect from frequency.

**Effect, not conduct.** This dimension's score is the behaviour that changed, never the mechanisms
that were present. A task where every stage fired, every arm held and nothing an actor did was
different for it scores low, and it is `controllable: yes` — a mechanism that is present and inert
is what ceremony looks like from inside the log. Say in the explanation which mechanisms ran; put in
the score only what they altered.

**What `notMeasured` takes away.** `refusals` takes away `refusals.total`, `.bySite`, `.byActor` and
`.byCommand` — the four measures, and nothing else. `stages.transitions`, `stages.waivers`,
`stages.waiverReasons`, both `workItems[]` flags, `artifacts.byKind` and `causalChains` all remain.
**Score D1 with those four named absent; do not mark it `unmeasured`.** On items 1–4 that is the
live case: the refusal journal is absent, so you score the dimension from waivers, stage
transitions, work-item flags, artifact kinds and causal chains, and you say in the explanation that
the refusal evidence was not available and that the score therefore rests on the stage and work-item
mechanisms alone. Confidence is not `high` in that state.

**What forces it to `unmeasured`.** Nothing short of all of the above being absent, which no
calibration task is.

### D2 — Capability utilization

*Which roles should have activated, which did, and did they contribute or merely run?*

**Emitted fields it reads.** `runs.bySubjectRole`, `runs.byActor`, `runs.byProvider`, `runs.total`;
`authorship`; `artifacts.byKind`; `epistemic.challenges`; `stages.waiverReasons`.

**Records it may cite.** Run ids with their subject role. Artifact ids by kind and producer. Claim,
evidence, decision and alternative ids grouped by the actor that wrote them. Challenge ids.

**What you must supply.** The distinction between **role ran** and **role contributed**. A run
exists in `runs.bySubjectRole`; a contribution is a record in the log carrying that actor's id that
changed something — a claim later validated, a finding repaired, a challenge that overturned a
decision, an artifact another actor consumed. Join the runs to the records yourself. You may not
infer contribution from a run existing, and you may not infer ceremony from a low `authorship` count
alone, because one decisive claim outweighs forty routine writes.

**Effect, not conduct.** A role that activated and contributed nothing did not utilise the
capability, and the score says so. Every role firing in the right order is conduct; it belongs in the
explanation. The score is what those roles produced that the task would not otherwise have had.

**What `notMeasured` takes away, and what forces `unmeasured`.** `runs.bySubjectRole` is added only
when some run in the task carries no subject role. No calibration task is in that state, so nothing
is absent here on any of the six. If the key did fire it would take away one measure of five and
would be a **named absence**, not a force: `runs.byActor`, `runs.byProvider`, `runs.total`,
`authorship`, `artifacts.byKind`, `epistemic.challenges` and `stages.waiverReasons` would remain.

### D3 — Epistemic quality

*Were assumptions identified, tested, and revised when evidence changed?*

**Emitted fields it reads.** `epistemic.claims` with `open`, `validated`, `rejected`, `superseded`;
`epistemic.openClaimsWithSupportingEvidence`; `epistemic.evidence` with `total`, `supportsOnly`,
`refutesOnly`, `both`, `neither`, and `bySourceType`; `epistemic.decisions` with `proposed`,
`accepted`, `superseded`, `invalidated`; `epistemic.alternatives`; `epistemic.challenges`;
`causalChains`.

**Records it may cite.** Claim ids with their statements. Evidence ids with their citations and
source types. Decision ids. Alternative ids with their rejection reasons. Causal chains of kind
`rejectedClaimInvalidatedDecision`, `rejectedClaimInvalidatedWorkItem`,
`claimSupersededRepointedDependents` and `challengeOverturnedDecision`.

**What you must supply.** Whether the evidence actually bears on the claim it names. The kernel
checks that a resolution names an evidence record by direction; it does not check that the citation
supports the statement. Read both and say. Also judge `bySourceType`: a task whose claims all rest
on `source-read` records demonstrated nothing empirical, whatever the validated count says. And
apply reading rule 1 here hardest — an all-validated ledger with no rejections and no alternatives
is weaker epistemic evidence than one that changed its mind with a citation.

**`bySourceType` reports canonicalised buckets, not the ledger's own strings.** Validated claim
`AC23` with `BE23`. `--source-type` is free text, and the projection folds some of what it finds
into a smaller set of bucket names: on item 4 the ledger carries `source-read` 7, `live-run` 5,
`test-run` 5, `local-probe` 2 and `live-probe` 2, while the projection emits `source-read` 7,
`live-run` 5, `test-run` 5 and `local-probe` 4. The totals agree at 21; `live-probe` has been folded
into `local-probe` and does not appear. So a bucket name is **not necessarily a string any evidence
record carries**. Cite the bucket as a bucket, or read the evidence records' own `sourceType` values
from the ledger and cite those. Do not attribute a bucket name to a record as if the record said it.

**Effect, not conduct.** The score is whether assumptions were actually tested and revised, not
whether claim, evidence and alternative records exist in the required shapes. A ledger of validated
claims whose citations do not bear on their statements has performed the epistemic ritual and
established nothing, and that is `controllable: yes`.

Distinguish an open claim carrying supporting evidence from an ignored, untested one.
`openClaimsWithSupportingEvidence` separates epistemic work performed from administrative
disposition not completed.

**What forces it to `unmeasured`.** None — this dimension is never `unmeasured`.

### D4 — Planning and decision quality

*Did scope and decomposition match the work, and were briefs concrete enough to act on?*

**Emitted fields it reads.** `workItems[].scopeCount`; `coordinatorLoop.misScopedWorkItems`,
`coordinatorLoop.supersededDecisions`, `coordinatorLoop.reopenedFindings`;
`epistemic.decisions.superseded`, `epistemic.decisions.invalidated`; `artifacts.supersessions`;
`runs.briefDelivered`, `runs.diedBeforeBriefing`.

**Records it may cite.** Work item ids with their abandon reasons, which
`coordinatorLoop.misScopedWorkItems` carries in full alongside the replacing item and the areas each
held. Artifact ids of superseded contracts and plans. Decision ids. Run ids.

**What you must supply.** **Brief concreteness, which no field measures.** `briefDelivered` counts
runs where a manifest was fully built and handed to the adapter. That is delivery, not content, and
it cannot show the child read it. To judge whether a brief was concrete you must read the constraint
or artifact the run was briefed with and compare it against what the run produced. You may not infer
concreteness from `briefDelivered`, and you may not infer it from `manifestArtifactCount`.

Also supply the avoidability verdict on each mis-scope. The projection reports the pair and
explicitly declines to judge it: `work.abandoned` carries free text and names no coordinator record,
which the output states as `missingDimension: coordinatorDecisionAttribution`. Read the reason text
and say whether the original scope was a foreseeable error or a reasonable revision.

**Effect, not conduct.** The score is whether the plan and the briefs produced work that landed
first time, not whether a plan was filed and briefs were delivered. A brief delivered to every run
that nonetheless had to be corrected, re-issued or worked around scores low, and the correction is
`controllable: yes` — the brief is AILedger's own output. `briefDelivered` at 100 percent is conduct
and belongs in the explanation.

**What `notMeasured` takes away, and what forces `unmeasured`.** `runs.briefDelivered` is added only
when the task has runs and not one of them delivered a brief. No calibration task is in that state,
so nothing is absent here on any of the six. If the key did fire it would take away
`runs.briefDelivered` alone and would be a **named absence**, not a force: the scope counts, the
three `coordinatorLoop` measures, the decision counts, `artifacts.supersessions` and
`runs.diedBeforeBriefing` would remain.

### D5 — Execution quality

*Did work stay in scope, converge after defects, and escalate rather than invent?*

**Emitted fields it reads.** `workItems[].status`, `workItems[].scopeCount`; `runs.byStatus`;
`escalations[]` with `id`, `kind`, `status`, `openHours` and `workItem`;
`coordinatorLoop.misScopedWorkItems`; `refusals.byCommand`.

**Records it may cite.** Work item ids and statuses. Escalation ids with their questions, options
and recommendations. Run ids. Refusal rows by command.

**What you must supply.** Two judgements no field carries.

First, whether each escalation was a real unknown or an avoidable hand-back. An escalation that
names two options and a recommendation on a question only the operator can answer is correct. One
asking something the repository already settles is avoidable friction, and it is `controllable: yes`.

Second, what a non-completed run means. **Do not read `runs.byStatus.failed` or `.cancelled` as
execution failure.** A run whose agent returned an adverse verdict is a run that did its job; the
kernel records that verdict as a finding and the run may still show `failed`. A `protocolError` is a
provider transport fault, not an agent fault. Read `terminalFailureReason` from the run record where
it exists, and where it does not, say the cause is unattributable rather than guessing.

**Effect, not conduct.** The score is whether the work converged and stayed inside its scope, not
whether the escalation and refusal machinery was exercised. An escalation raised in the correct shape
on a question the repository already settles is conduct performed and effect not achieved: it scores
against this dimension and it is `controllable: yes`.

**What `notMeasured` takes away.** `refusals` takes away `refusals.byCommand`, which is the only
refusal measure this dimension reads. `workItems[].status`, `workItems[].scopeCount`,
`runs.byStatus`, `escalations[]` and `coordinatorLoop.misScopedWorkItems` all remain. **Score D5 with
that one measure named absent; do not mark it `unmeasured`.** What you lose is the ability to say
which commands an actor was blocked on, so an invention-rather-than-escalation pattern that showed
up only as refusals is invisible. Say that in the explanation.

**What forces it to `unmeasured`.** Nothing short of all of the above being absent, which no
calibration task is.

### D6 — Verification and review quality

*Did verification and review activate, stay independent, and find distinct things?*

**Emitted fields it reads.** `runs.bySubjectRole`; `artifacts.byKind.verifierOutput`,
`artifacts.byKind.codeReviewOutput`; `artifacts.supersessions`;
`workItems[].verifierRanAfterLatestWork`.

**Records it may cite.** The verifier's and reviewer's claims **by claim id**, which is how you
establish that they found different things. Artifact ids of verifier and review outputs. Run ids
with their subject roles and actors. And the run-record fields of section 4, cited by run id with
the field named: `truncatedLines` says whether the run's own stream was complete, `providerVersion`
says which provider build ran, and `model` says which cognition performed the check — **on the five
run records in the whole calibration set that carry it.** On every other run, and on five of the six
tasks entirely, the cognition is unrecorded. Say that rather than inferring it from the actor id or
from `providerVersion` (`AC22`, and section 4).

**What you must supply.** Whether verifier and reviewer contributed **distinct** value. Two runs
finding the same defect is one finding and one duplicated cost. Read both sets of claims and
compare. Supply also whether each finding was repaired and whether the repair was demonstrated, by
following the finding's claim id forward to the record that resolved it and the run that proved it.

Do not infer independence from a provider difference alone. Independence is about what the actor was
given: a reviewer handed the verifier's output is not independent of it whatever provider it ran on.

Apply the operator's asymmetry. A verifier finding a real defect before completion is *positive*
evidence for the mechanism; do not penalise the discovery as though the defect had escaped. Penalise
ignored findings, unjustified dismissal, false repair, repeated failure on the same root cause, and
a defect that escaped a stage explicitly responsible for catching it.

**Effect, not conduct — and this dimension is where the omission cost most.** The score is what
verification and review *caught*, never that they ran. Two calibration passes scored item 4 `D6` at 1
and at 4 on the same record: one read the effect, that verification missed eight defects a reviewer
then found; the other read the conduct, that the verifier ran, the reviewer ran and the ordering was
correct. The first reading is the one this rubric wants.

So, stated as a rule: **a verification that conducted every required step in the required order and
caught nothing that was there scores low, and it is `controllable: yes`.** It is controllable because
what failed is AILedger's own design of the check — its brief, its scope, its ordering, what it was
given to look at — and not the fact that it happened. Ran-in-the-right-order goes in the explanation.
A stage that a defect walked past is the strongest single signal this dimension carries, and a clean
ordering with nothing found is not evidence that there was nothing to find; establish which it was
from the record, and where you cannot, say so and lower the confidence.

**What `notMeasured` takes away, and what forces `unmeasured`.** `runs.bySubjectRole`, on the same
condition as D2: added only when some run carries no subject role, which no calibration task does,
so nothing is absent here on any of the six. If the key did fire it would take away
`runs.bySubjectRole` alone and would be a **named absence**, not a force — the verifier and review
artifact counts, `artifacts.supersessions`, `workItems[].verifierRanAfterLatestWork` and the claims
cited by claim id would remain, and those are the measures this dimension leans on hardest.

### D7 — Operator burden

*How often did the operator have to act, and how much of that needed operator authority?*

**Emitted fields it reads.** `authorship`; `runs.operatorFilingRuns`;
`coordinatorLoop.delays.toFindingDisposed`, `coordinatorLoop.delays.toNextDispatch`, each with
`measured`, `unmatched`, `minMinutes`, `medianMinutes`, `p95Minutes` and `maxMinutes`;
`coordinatorLoop.ratios.eventsPerDeliveredWorkItem` and `.eventsPerFindingDisposed`;
`escalations[].openHours`; `stages.waivers`.

**Records it may cite.** Escalation ids with their questions and resolutions. Waiver reason text.
Event ids of operator writes. Work item ids in `coordinatorLoop.byActor`.

**What you must supply.** Which operator writes required operator authority and which the kernel
could have handled. This is the whole dimension and no field carries it. The operator is by design
the largest writer in any governed task — it resolves every claim, dispatches every run and holds
every waiver — so a high `authorship` count is not burden. Burden is the subset that AILedger should
have done itself: an agent stopping to report status when the governed next action was already
determined, an operator re-explaining an obligation the task state already held, a decision the
kernel had the evidence to make.

Read `coordinatorLoop.delays.toNextDispatch` as a measure of the loop, not of the operator. A long
gap may be a person sleeping.

**Effect, not conduct.** The score is the burden the operator actually carried, not the count of
operator actions the design requires. An operator action the kernel could have taken itself is
burden whether or not the process followed its own rules in demanding it, and it is
`controllable: yes`. An operator action that only operator authority could take is not burden at any
count. A task that routed every hand-back through the correct command still imposed every one of
them.

**What `notMeasured` takes away.** `coordinatorLoop.sessionWallClock` and
`coordinatorLoop.timeWithNoAgentRunning` — **for the bracketed part only.** These two take away the
bracketed session measures and nothing else. `authorship`, `runs.operatorFilingRuns`, both delay
distributions, both ratios, `escalations[].openHours` and `stages.waivers` all remain. **Score D7
from those, and name the two absent measures in the explanation.** Both keys appear on all six
calibration tasks, because no archived task carries a bracketed coordinator session, so on all six
D7 is a partially scored dimension and guards 2 and 3 apply to it.

**What forces it to `unmeasured`.** Nothing short of all of the above being absent, which no
calibration task is.

### D8 — Governance cost and convergence efficiency

*What did governance cost, and how much of that cost bought confidence?*

**Emitted fields it reads.** `wallClockHours`; `agentMinutes`; `cost.turns`, `cost.outputTokens`,
`cost.millisecondsToFirstLedgerWrite`, `cost.tokensInUncached`, `cost.tokensInCacheWrite`,
`cost.tokensInCacheRead`, **each with its own `runsMeasured`**; `runs.costRecorded`,
`runs.byStatus`, `runs.diedBeforeBriefing`; `coordinatorLoop.tokenCost`;
`coordinatorLoop.dispatches.rework`, `.failedDispatches`, `.wastedDispatches`;
`coordinatorLoop.refusals.firstTime`, `.repeat`, `.repeatedKeys`.

**Records it may cite.** Run ids. The repeated refusal keys, which carry actor, command, site,
message, occurrences and repeats. And the run-record fields of section 4, cited by run id with the
field named: `launchTimeoutSeconds` and `terminalFailureReason` are what separate a run that died at
a limit the coordinator set too low from one that failed on its own merits, and `truncatedLines` is
what separates a complete stream from a degraded one whose cost figures are missing their tail.

**What you must supply.** The classification, which is the dimension's actual content. Assign each
substantial cost to one of four buckets, naming a run id or a record for every assignment:

- **productive** — generated confidence, correction, evidence or preserved reasoning
- **necessary but non-discriminating** — reasonable given the risk, found nothing
- **avoidable** — caused by poor orchestration or ergonomics
- **ceremony candidate** — a repeated mechanism contributing none of the above

`coordinatorLoop.refusals.repeat` with its `repeatedKeys` is the clearest avoidable-cost signal the
projection emits: a refusal hit four times by the same actor with the same message is three
repetitions the first should have taught. Read `runsMeasured` on every cost figure before quoting
it — a total over 5 of 15 runs is not the task's cost.

**Effect, not conduct — the second place the omission cost most.** The score is what the cost
bought, never how well the cost was recorded. Two calibration passes scored item 7 `D8` at 1 and at
3 on the same record: one read the effect, that the work took 202 agent-minutes with fourteen
dispatches naming no cause; the other read the conduct, that cost was recorded on twelve of fifteen
runs. The first reading is the one this rubric wants.

So, stated as a rule: **recording cost well is not the same as spending it well.** Good
instrumentation raises your *confidence*, never the *score*. A task that measured its own cost
precisely and spent it on repetition scores low, and it is `controllable: yes`. A task that spent
little and measured none of it is a low-confidence score, not a low one. Keep the two apart: the
`score` cell answers what the cost bought, the `confidence` cell answers how much of the cost you
could see, and the explanation carries the instrumentation.

**What `notMeasured` takes away.** Five keys bear on this dimension directly, and a sixth —
`refusals`, bound to D1 and D5 — reaches it as well. **None of the six forces it.** Each takes away
the measures it names and leaves the rest, which is the same qualifier D7 carries. **D8 is scored on
all six calibration tasks.**

| key | what it takes away | what survives it |
|---|---|---|
| `coordinatorCost` | `coordinatorLoop.tokenCost`, which reports `noCoordinatorUsageSupplied` | everything else in the dimension |
| `coordinatorLoop.reworkCausedByCoordinatorDecisions` | the attribution verdict on `dispatches.rework` | its volume counts — `dispatches`, `joined`, `namingNothing` |
| `coordinatorLoop.failedDispatchesAttributableToCoordinator` | the `judged` verdict on `dispatches.failedDispatches` | its `launches` count |
| `coordinatorLoop.wastedDispatches` | the `judged` verdict on `dispatches.wastedDispatches` | its `unclassified` count |
| `governanceCost.tokens` | `cost.turns`, `.outputTokens`, `.millisecondsToFirstLedgerWrite`, `.tokensInUncached`, `.tokensInCacheWrite`, `.tokensInCacheRead` | `cost.runsMeasured` and `cost.runsUnmeasured`, which still say how much of the task went unmeasured |
| `refusals`, where present | `coordinatorLoop.refusals.firstTime`, `.repeat` and `.repeatedKeys` | nothing of the repeat-refusal signal; it is wholly gone |

What always survives, on every task: `wallClockHours`, `agentMinutes`, `runs.costRecorded`,
`runs.byStatus`, `runs.diedBeforeBriefing`, and the dispatch **volumes**. That is enough to score
the dimension, and it is not enough to score it confidently — which is what guard 3 is for.

The four-bucket classification is still the dimension's content. Where a bucket assignment would
need a measure that is absent, say so and do not assign it. A dispatch whose `judged` verdict is
absent cannot be called avoidable: the volume says ten dispatches named nothing, and naming nothing
is not the same as being caused by a coordinator decision.

**What forces it to `unmeasured`.** Nothing on any task the projection can currently produce. If
`wallClockHours`, `agentMinutes`, every `runs.*` cost measure and every dispatch volume were absent
as well, the dimension would have no evidence left and would be `unmeasured` — no calibration task
is in that state.

Per tier, the absences you must name — see section 8 for why this is the discrimination basis:

- **items 1–4** — `coordinatorLoop.tokenCost`; the three attribution verdicts; the six `cost.*`
  figures; and all three `coordinatorLoop.refusals` measures. Score from `wallClockHours`,
  `agentMinutes`, `runs.costRecorded` (0 of 11 on item 1), `runs.byStatus`,
  `runs.diedBeforeBriefing` and the dispatch volumes. Confidence `low`.
- **item 5** — `coordinatorLoop.tokenCost`; the three attribution verdicts; the six `cost.*` figures.
  The repeat-refusal signal is present here and is real evidence of avoidable cost. Confidence `low`
  to `medium`.
- **item 7** — `coordinatorLoop.tokenCost` and the three attribution verdicts only. The six `cost.*`
  figures are present on 12 of 15 runs, and `cost.turns` on 5 of 15 — quote `runsMeasured` with
  every one of them. Confidence `medium`.

### D9 — Learning behaviour

*Were prior lessons recalled, and did they change anything?*

**Emitted fields it reads.** `lessons.recalled`, `lessons.cited`, `lessons.minted`, `lessons.marks`;
`causalChains` of kind `lessonCitedByRecord`.

**Records it may cite.** Lesson ids, which are of the form `<sourceTaskId>:<kind>:<recordId>` and
carry the source task in the id. The claim, decision or alternative id each chain points to — the
chain's `detail` field says which of the three record kinds it was. Minted lesson ids.

**What you must supply.** Whether a recalled lesson **materially affected** the work. A
`lessonCitedByRecord` chain proves a record named the lesson; it does not prove the lesson changed
the outcome. Read the citing record and say whether it would have been written the same way without
it. Supply also whether the task repeated a known failure mode despite recalling the relevant
lesson, and if so whether the cause was lack of knowledge, poor context delivery, weak enforcement,
or execution failure.

Note the population. `lessons.recalled` and `lessons.cited` and the chains all describe **inherited**
lessons only — those whose source task is not this one. A lesson this task minted at archive is not
counted as something it learned from.

A recalled lesson is not valuable because it appeared in context.

**Effect, not conduct.** The score is whether recall changed the work, not whether recall ran. Ten
lessons recalled and none that altered a decision is the recall mechanism working mechanically and
achieving nothing, and it is `controllable: yes` — what failed is selection and delivery, both of
which are AILedger's. The counts go in the explanation; the score is the change they produced.

**What `notMeasured` takes away, and what forces `unmeasured`.** `learningBehaviour` is added only
when `lessons.recalled` is 0. No calibration task is in that state — the six recall 1, 3, 2, 1, 10
and 2 — so nothing is absent here on any of the six. This is the one key that names no emitted block
at all: D9's data is the `lessons` block, which is emitted either way. Where the key does fire it
declares the **inherited-lesson** half of the dimension unmeasured; `lessons.minted` and
`lessons.marks` still describe what the task contributed, so name the absence rather than marking
the dimension `unmeasured`.

### D10 — Outcome quality

*Did the delivered result satisfy the contract?*

**Emitted fields it reads.** None.

**Records it may cite.** None, for scoring purposes.

**What you must supply.** Nothing. Do not score this dimension.

**What forces it to `unmeasured`.** `outcomeQuality`, unconditionally, on every task. Nothing in the
log says whether the delivered result works. **This is the standing exception of section 3, not an
instance of its rule** — the dimension has no evidence at all, so there is no partly-present
evidence to score from and no absence to name inside a score.

`epistemic.evidence.bySourceType` is the nearest proxy — a task resting entirely on `source-read`
records demonstrated less than one carrying `test-run` and `live-run` records — and it is
**explicitly not a substitute**. Do not score D10 from it. If you mention it, remember that its
buckets are canonicalised and are not necessarily strings any evidence record carries (`AC23`, and
the caution under D3). You may mention it in the explanation as
context; you may not convert it into a number. D10's row reads `unmeasured`, `high`,
`not-applicable`, and an evidence cell naming the `outcomeQuality` key.

---

## 8. Named absences are the discrimination basis

Settled. Escalation `HX1` on task `2026-09-07_2136-workflow-retrospective` asked whether a dimension
whose evidence is only partly in `notMeasured` is `unmeasured` in full or scored from what remains.
The operator chose the second, with the three guards of section 3. Prompt contract revision 4
carries it.

This section records the consequence a scoring run has to get right.

**What separates the three tiers is no longer the set of `unmeasured` dimensions.** Under the
literal rule it was, and it stopped working: items 5 and 7 differ only by `governanceCost.tokens`,
which is a D8 key, and D8 was `unmeasured` on both, so tiers two and three were indistinguishable.
Under the reworded rule D8 is scored on both, and the difference does not disappear — it moves
inside the dimension, and appears as a named absence.

**The discrimination basis is therefore the set of named absent measures, per dimension.** Only D10
is `unmeasured` on any of the six calibration tasks. Everything else separates at measure level:

| | items 1–4 | item 5 | item 7 |
|---|---|---|---|
| `unmeasured` dimensions | D10 | D10 | D10 |
| D1 named absences | the four `refusals.*` measures | none | none |
| D5 named absences | `refusals.byCommand` | none | none |
| D7 named absences | the two bracketed session measures | same | same |
| D8 named absences | `tokenCost`, three attribution verdicts, six `cost.*` figures, three `coordinatorLoop.refusals` measures | `tokenCost`, three attribution verdicts, six `cost.*` figures | `tokenCost`, three attribution verdicts |
| `notMeasured` keys | 9 | 8 | 7 |

Read the bottom four rows as sets, not one dimension at a time. The three tiers are distinguished by
their combined named-absence sets, and no single dimension separates all three. D1 and D5 separate
items 1 to 4 from the other two and are identical on item 5 and item 7. D8 is the dimension that
separates that pair, by exactly the six `cost.*` figures that `governanceCost.tokens` takes away.
**A scoring run whose named-absence sets are identical across the three tiers has failed
discrimination whatever its scores look like.**

One consequence for the coordinator half of D8 specifically, settled as escalation `EX21`:
`coordinatorLoop.tokenCost` returns `noCoordinatorUsageSupplied` on every archived task, because no
archived task carries a bracketed coordinator session. That is the correct value today. Score the
coordinator half as absent against `coordinatorLoop.tokenCost.absence`, name it, and score the
dispatched-agent half from the measures that are there.

---

## 9. What you may never do

- Compute an overall score, a mean, a grade or a rank.
- Score conduct in place of effect: credit a mechanism for having run, for having run in the correct
  order, or for having recorded itself well. Conduct goes in the explanation and in `controllable`.
- Read a bucket name in `epistemic.evidence.bySourceType` as a string an evidence record carries.
- Say which cognition performed a run from `model` unless that run record carries it. Five run
  records in the whole calibration set do.
- Score a dimension `0` or `3` when all of its evidence is in `notMeasured`.
- Score a dimension `unmeasured` when only part of its evidence is in `notMeasured`. D10 is the one
  standing exception.
- Score a dimension partially without naming every absent measure in its explanation.
- Carry `high` confidence on a dimension with a named absence, unless the explanation says why the
  absent measures cannot change the score.
- Treat a field missing from the JSON because its value was null as unmeasured.
- Declare `runs.briefDelivered` or `runs.bySubjectRole` absent. They are emitted.
- Cite a run-record field without naming the run id and the field.
- Read a score of your own as input to any other dimension.
- Recommend removing a governance mechanism on one task's evidence.
- Score outcome quality.
