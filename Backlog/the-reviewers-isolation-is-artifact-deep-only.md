the reviewer's isolation stops at the artifact boundary

`cognitive/skills/code-reviewer/SKILL.md` is unambiguous about what makes the role worth running:

> This skill is invoked in isolation. Its independence from the verifier and from the user's intent
> is the entire point. A correct implementation can still be unsafe code, and the reviewer must be
> free to say so without being anchored by "the requirement was met."

and it instructs the reviewer to stop rather than review under contamination:

> If you find yourself being asked to consider any of the forbidden items, stop and ask the
> orchestrator to re-invoke the skill with the minimal context bundle only.

the kernel enforces half of that.

## what it withholds and what it hands over

`ContextAssembler.ReviewerExclusions` drops five artifact kinds for a `CodeReviewer` subject —
`UserRequest`, `PromptContract`, `OrchestrationPlan`, `VerifierOutput`, and an earlier
`CodeReviewOutput`. that last exclusion carries a comment which states the principle exactly: *a
second pass that reads the first is not a second opinion.*

everything else is appended for every role. a reviewer on `2026-09-08_1428-run-cost` received:

    rules 1   skill 2   taskGoal 2   constraint 4   claim 3   decision 2
    evidence 3   workItem 1   stopCondition 1   alternative 3   lesson 10

the skill forbids the user request. the manifest hands over the task goal, which is the same
intention in fewer words. it also hands over every claim, every accepted decision, every rejected
alternative and the evidence behind them — which is the implementer's reasoning, in full.

## the leak that makes the artifact exclusion pointless

withholding the `VerifierOutput` artifact buys nothing, because a coordinator who writes the
verifier's findings into a constraint routes them straight past the filter.

that is not hypothetical. on the task above, constraint `K10` was a repair brief quoting the
verifier's three findings verbatim. run `R8`'s predecessor `R7` refused:

> Not performed. The invocation violated the mandatory isolation boundary of the `code-reviewer`
> skill by supplying the task goal, active constraints, claims, decisions, rejected alternatives,
> and a verifier repair brief.

it inspected no files and made no statement about the code, which is precisely what the skill told
it to do. the orchestration was wrong and the agent was right.

## it fails in the other direction too

`R18` refused with the verifier brief already superseded, so the contamination this entry was
written about was gone. it refused anyway, on broader ground:

> It includes the task goal and implementation framing, which the skill explicitly forbids, and it
> supplies no code artifact paths or diff, risk classification, change type, stack bundle, or
> accepted tradeoffs. Reviewing the repository by discovery would both invent the review target and
> preserve the anchoring the isolated pass exists to avoid.

so the manifest gives a reviewer too much *and* none of what it needs. the skill's required inputs
are the exact paths or diff, a risk classification, a change type, a stack indicator, the accepted
tradeoffs, and `taskPath`. the manifest supplies none of the first five.

that means a reviewer that does review has discovered its own target — and the skill says a
discovered target is an invented one. removing the verifier brief was necessary and not sufficient.

## what this costs retrospectively

every code review this repository has run was given more than the skill permits. `RC1` — the public
lock-free method that a future caller would reach for — was found by a reviewer holding the task's
claims and decisions. the finding was good. its independence was not the independence the skill was
written to guarantee, and nothing in the record says so.

that matters for `self-scoring` more than for the reviews themselves: a dimension that scores
whether the reviewer contributed distinct value cannot distinguish a genuinely independent catch
from one anchored by the material it was handed.

## the shape

two changes, and the second is the one that holds.

**widen the exclusion to the context kinds, not just artifact kinds.** a `CodeReviewer` subject gets
the rules, its own skill, the work item, its resource scope, the stop conditions, and the accepted
technical tradeoffs. it does not get `TaskGoal`, `Claim`, `Decision`, `Alternative` or `Evidence`.
the skill's own list of permitted inputs — code artifacts, risk classification, change type, stack
indicators, accepted tradeoffs, `taskPath` — is the specification, and it is already written down.

**give the reviewer a channel for accepted tradeoffs that is not the constraint list.** the reason
`K10` leaked is that a constraint is the only way a coordinator can reach a launched agent, so
everything becomes a constraint. an accepted tradeoff is a legitimate reviewer input; a verifier's
findings are not; and today they arrive through the same pipe. until they are separable, the
exclusion can be defeated by a coordinator doing its job.

## what must not happen

do not fix this by having the coordinator write vaguer constraints. the refusal is the mechanism
working, and a constraint list edited to slip past a filter is worse than the leak — it hides the
contamination instead of blocking it.

## cost

one predicate in `ContextAssembler`, plus a decision about where accepted tradeoffs live. no event,
no state, no replay counterpart — context assembly is a read.

the honest part of the cost is retrospective: it invalidates nothing already shipped, but it means
no review in this repository has yet been the thing the skill describes.

## the mismatch, counted

`ContextArtifactKind` has 18 members. `ReviewerExclusions` withholds 6 — `UserRequest`,
`PromptContract`, `OrchestrationPlan`, `VerifierOutput`, `CodeReviewOutput`, `Escalation` — so a code
reviewer's manifest carries the other 12: `Rules`, `Skill`, `TaskGoal`, `Constraint`, `Claim`,
`Decision`, `Evidence`, `WorkItem`, `StopCondition`, `Alternative`, `Lesson`, `LessonMark`.

`cognitive/skills/code-reviewer/SKILL.md:13-19` says the skill receives **only** five things.

| the skill requires | what the manifest carries |
|---|---|
| the code artifacts — file paths or diffs | `WorkItem.ResourceScope`, which is directories. no diff exists anywhere in the kernel |
| risk classification and change type | nothing. neither concept exists in any record |
| tech stack / language indicators | nothing explicit. inferable from the scope paths, which is the reviewer discovering its own target |
| accepted tradeoffs that constrain what is reviewable | `Alternative` and accepted `Decision`, which are *rejected* approaches and *chosen* designs — a different thing from a constraint on what can be reviewed |
| `taskPath`, only so the output lands correctly | present |

one and a half of five.

`SKILL.md:21-27` says the skill must **not** receive five things. two of the five arrive structurally
and cannot be withheld by an artifact-kind filter:

| the skill forbids | how it arrives anyway |
|---|---|
| the original user request | withheld correctly |
| prompt contract, success criteria | withheld correctly |
| orchestration plan, worker decomposition | withheld correctly |
| verifier output, verdict, **or repair history** | the verdict is withheld; the repair history arrives as `Claim`, `Decision` and `Constraint`, which is what R18 refused over |
| any framing of the form "this satisfied the requirement" | `TaskGoal` is the user's intent in one sentence, and it is in the always-included set |

so the filter is not merely shallow. it withholds the four artifacts a reviewer would have read
anyway and admits the two things the skill names as disqualifying.

## the shape

**invert the filter.** `ReviewerExclusions` is a deny-list over an enum that grows: `CodeReviewOutput`
and `Escalation` were both appended after it was written, and each had to be remembered separately —
the comment on `Escalation` records the reasoning, which means someone caught it, which means someone
could have missed it. A code reviewer should get an allow-list, so a new kind defaults to *not*
reaching it. The skill already states that list: code, risk, stack, tradeoffs, output path.

**give it the diff it is supposed to review.** This is the missing input that matters most, because
its absence is what forces the reviewer to discover its own target. The kernel has everything needed
to produce one: the work item names its directory scope, the runs against that item have timestamps,
and the launcher already shells out to git for the version stamp. `git diff` over the item's scope,
bounded by the first working run on the item, is a computable artifact and it is the one thing a
reviewer cannot proceed without.

**the two concepts the kernel does not have.** Change type and risk level have no record. Derive the
first — which projects the scope touches, whether tests moved with the source — and leave the second
alone for now: a risk level an agent types is a number it can move without doing the work, which is
the argument `TaskDebt.cs:8` already settled for `status`.

## what this does not fix

Nothing here stops a coordinator writing verifier findings into a constraint, which is the other half
of this entry and the reason R18 refused. An allow-list that admits constraints admits that route
again. Either constraints are withheld from a reviewer too — and then a repair brief has no channel
to it at all — or the repair brief stops being a constraint. That is a real decision and it belongs
to whoever opens this item, not to this entry.

## the reviewer notices half the time, and that is worse than always refusing

Four code-reviewer runs on `2026-09-08_1428-run-cost`, all given a manifest built the same way:

    CR7   "Not performed. The invocation violated the mandatory isolation boundary"    refused
    CR8   "Change type: shared library... Risk: High... Verdict: Changes requested"    reviewed
    CR13  "Code review 3 — negative provider counts can strand completion"             reviewed
    CR18  "BLOCKED — no technical code review was performed"                           refused

The manifest breached the skill's contract in all four. Two runs noticed and two did not.

The two that proceeded were not idle. CR13's finding became RC3, a real defect: a provider reporting
a negative count could strand a run's completion. That review was worth having.

So the cost of this defect is not "the reviewer refuses and no review happens". It is worse and much
harder to see: **the reviews that do happen have unknown independence.** CR8 and CR13 read the task
goal, the claims, the decisions and the repair history before forming a verdict. Their findings may be
excellent — RC3 was — and nothing in the record says whether they were reached independently or
anchored by what the manifest told them the team already believed.

A mechanism that fails loudly half the time and silently the other half cannot be assessed by whether
its output looks useful. `self-scoring` asks whether verifier and reviewer "contribute distinct
value"; on this evidence the honest answer for the reviewer is that nobody can tell, and that is the
finding.

It also means the refusals are the healthy outcome and should not be read as reviewer failure. Two
runs applied their own contract correctly against a kernel that violated it. The other two did the
work anyway.
