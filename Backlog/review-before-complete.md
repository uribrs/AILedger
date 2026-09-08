completing a work item locks its code reviewer out forever

`work complete` requires a completed working run and a completed verifier run. it does not require a
code review. `Learn` does — its arm demands a completed CodeReviewer run for code-bearing work.

so the natural order is: work, verify, complete the item, then walk the stages. and that order makes
the review impossible.

## the trap, proved in the ledger

three refusals, in sequence, from task `2026-09-07_2136-run-manifest-hash` after W1 completed:

    run start --subject claude-review --work W1
      Cannot start a run for work item in status 'Completed'.

    artifact record --kind CodeReviewOutput --run <task-wide run> --work W1
      Artifact work item must match producer run.

    artifact record --kind CodeReviewOutput --run <task-wide run>       (no --work)
      A 'CodeReviewOutput' artifact must name its work item.

and the escape closes too:

    run complete --run <task-wide run> --status completed
      Completing CodeReviewer run requires its matching 'CodeReviewOutput' artifact.

recorded as RC4 and RC5 with RE7 and RE8. every route is refused, and each refusal is individually
correct. a reviewer cannot start on a finished item; a work-scoped artifact must name its item; a
reviewer run cannot close without its output. together they mean **a code-bearing work item that was
completed before its review can never receive a governed one.**

the only remaining route into `Learn` is the operator waiver, which is how this task reached Archive.

## why it is not obvious before you hit it

`CLAUDE.md` documents the sequence as work, verify, complete — three commands — and mentions a code
reviewer as optional: "optionally reviewed by a code reviewer". read that way, completing first is
the documented path. the code reviewer looks like something you may add, not something whose window
closes.

the kernel already refuses a code reviewer that starts *too early* — before a verifier run has
completed. it does not refuse one that would be too late, because nothing knows a review is coming.

## the fix, smallest first

- **`work complete` refuses code-bearing work without a completed CodeReviewer run**, symmetric with
  the verifier requirement it already imposes. one arm, and the refusal names what is missing the way
  the verifier one does. this is the honest version: if `Learn` needs the review, completion needed
  it too.
- if that is too strong, **let a reviewer run start against a completed item**. the item's status
  blocks new runs to protect its directory scope, and a reviewer reads rather than writes, so the
  narrower rule is that Completed blocks working runs and not review ones.

the first is preferable. it makes the requirement visible at the moment someone claims the work is
done, which is the same argument `attention-items-as-a-gate` makes for named tests.

## what it must not do

not refuse documentation-only work. the arm keys on code-bearing, which the Learn arm already
defines as an item holding any `ResourceScope`, so the predicate exists and is already written.

and `--without-verification` should waive both requirements together, as it does today, so an
operator who decides no review is warranted still has one flag and one recorded reason.
