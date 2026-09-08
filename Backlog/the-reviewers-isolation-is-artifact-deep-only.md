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
