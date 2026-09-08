refuse a supersession whose replacement is not validated yet

`claim resolve --status superseded --superseded-by <id>` derives whether the supersession is a
**refinement** or a **correction** from state at that instant, and never from what the actor says.
That derivation is right. What is missing is a warning, because the two outcomes are not
comparable in cost and one of them cannot be undone.

`ClaimRules.cs:126-133`:

    refinement  when the replacement is already Validated and nothing refutes the original
    correction  otherwise

A refinement emits `ClaimDependenciesRepointed` and dependent work moves onto the replacement. A
correction emits dependency invalidations: **every dependent decision becomes `invalidated` and
every dependent work item becomes `blocked`.**

And a blocked item is terminal. `WorkItemRules.cs:244-254` refuses to unblock an item that depends on
a superseded claim, and says so plainly — "Add a replacement work item on a current claim instead of
unblocking this one". The completion gate at line 83 refuses a blocked item before it reaches any
verification check, so `--without-verification` does not reach it either.

## what it cost, measured

On `2026-09-08_1428-run-cost`, W2 had three completed runs against it — a worker, a codex verifier
that found a command injection, and a repair run still live. The verifier's report disposed C21 as
`REJECTED`, correctly: C21 claimed exact write attribution for the correlation mechanism, and
exactness fails for a run id containing a quote.

The right record for that is a refinement — the mechanism holds, its exactness is conditional on a
guard. So: add C29 with the condition stated, validate it, supersede C21 by it.

Three commands were issued in one batch. The middle one — validating C29 — was **refused** by the
evidence-direction rule, because it named the three evidence records that support C21 rather than the
one that supports C29. The third command ran anyway. C29 was still `open` at that instant, so the
outcome landed as `correction`.

    C21   superseded, outcome "correction"
    D5    invalidated
    W2    blocked

The code was fine. The event is append-only, so there is no path back to a refinement. W2 must now be
abandoned and replaced by an item on the current claim, and its worker and verifier runs re-done
against the new item — re-verifying code that a verifier has already read.

One command in the wrong order, and the cost is a re-verification cycle.

## why this is not just operator error

It is operator error. It is also the exact shape `self-scoring` calls avoidable friction: cost caused
by ergonomics rather than by anything that bought confidence. Three things compound it.

- **The consequence is invisible at the moment of the command.** Nothing in the output says
  "correction" until the event is written. The operator learns the outcome from `state.json`
  afterwards.
- **The default is the expensive one.** The comment above `DeriveSupersession` says it out loud:
  "Correction is the default; refinement has to be earned." That is the right default for
  *epistemics* and the wrong default for a command whose two outcomes differ by a re-verification
  cycle.
- **A batch that half-failed left the sequence broken.** The refused middle command printed an error
  and the third command still ran. That is shell behaviour, not kernel behaviour, but the kernel is
  where the guard can go.

## the shape

One command-time predicate, at the point of supersession:

    ailedger claim resolve --id C21 --status superseded --superseded-by C29
    error: replacement claim 'C29' is 'open'. A supersession by an unvalidated claim is recorded as a
           correction, which invalidates dependent decisions and blocks dependent work items, and
           cannot be undone. Validate 'C29' first, or pass --accept-correction to proceed.

The flag matters as much as the refusal. A correction is sometimes exactly right — a claim that was
simply wrong should invalidate what rested on it — so this must not become a rule that forces every
supersession to look like a refinement. It must make the operator say which one they mean.

Command-time only, per LD16. Replay must keep accepting every correction already on disk, including
this one.

## what it must not become

Do not make `unblock` accept a superseded dependency. That rule is load-bearing: work resting on a
claim that changed has to be re-established, and clearing the block would be exactly the
false-completion the kernel exists to refuse. The fix belongs at the moment the correction is
created, not at the moment its consequence is inconvenient.

Do not add a repoint command either. Repointing after the fact would let an operator convert a
recorded correction into a refinement, which is rewriting what the task knew and when.

## cost

One predicate and one flag in `CommandHandler`. No event change, no replay change, no new rule in the
validator.
