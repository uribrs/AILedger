`Cancelled` is four endings wearing one status

`AgentRunStatus.Cancelled` currently means all of these:

- an operator stopped a run deliberately
- a run hit its `--timeout-seconds` and died
- the host killed the launcher, in this case for memory pressure
- an operator-held document-filing run finished its job and had no honest status available

`CLAUDE.md` already warns about the second: *a run that dies at its timeout comes back Cancelled,
which reads like a governance failure and is not one*. the warning exists because the status misleads.

## measured in one task

`2026-09-08_1048-refusal-journal` holds five cancelled runs and no two mean the same thing:

    R2, R5, R7, R12   operator runs that filed artifacts successfully, then had no status to close with
    R14               nine minutes of verification killed by the host for memory, no evidence recorded

four successes and one environmental failure. a retrospective counting cancelled runs as governance
events counts all five, and `self-scoring` asks directly for failed and retried runs as a cost
signal. it would read five and the true number is one — or zero, depending on whether an out-of-memory
kill is a governance cost at all.

## why not just add statuses

because two of the four are not run endings, they are missing features.

R2, R5, R7 and R12 exist because filing a `PromptContract` or an `OrchestrationPlan` requires an
active producer run, and an operator-held run has no provider session to close with. fixing that
entry removes four of the five cancellations in this task without touching the enum.

what genuinely remains is distinguishing a deliberate stop from a timeout from an external kill. the
launcher knows which it saw: `OperationCanceledException` with the caller's token cancelled, the
adapter's own timeout elapsing, and the process dying are three different code paths in
`CliApplication.LaunchProviderAsync`, and it currently maps all of them to one status.

## the shape

either a nullable reason on `run.completed`, trailing and optional so every existing history still
replays, or two new statuses — `TimedOut` and `Terminated` — which is a larger change because the
replay validator has to accept histories that predate them and the completion gate asks about
`Completed` only.

the nullable reason is smaller and says the true thing. it also fits beside the cost fields
`see-inside-a-run` wants on the same event, and both are for the same reader.

## cost

one nullable field, its command-time twin, and the launcher passing what it already knows. do the
coordinator's-run entry first — it removes four fifths of the confusion for free.

## the whole ledger, classified

every non-`completed` run in every task, with its provider, its subject role, whether a brief reached
the adapter, and how long it ran:

    182 runs      18 cancelled      18 failed

### cancelled splits cleanly in two, and one half is not a cancellation

    provider none, operator, 0 min, no brief      10
    real provider, ran 0.9 to 20 min              8

the ten are operator filing runs. they exist only because filing a `PromptContract` or an
`OrchestrationPlan` needs an active producer run, and an operator-held run has no provider session,
so it can never be closed `completed` — `the-coordinators-run-cannot-close.md`. every one of them
did its job. 56% of this ledger's cancellations are a workaround being recorded as a failure.

the eight are real: agents that ran for between one and twenty minutes and were stopped. two of them
are the host memory kills that `the-launch-does-not-ask-if-the-host-can-hold-it.md` is about.

### failed splits in two as well

    no brief delivered, under 0.5 min             8
    brief delivered, ran 8 to 22 min              6

the eight short ones died before or during request construction, so nothing ever reached the
provider. that is a launch failure, not an agent failure, and it is the state `manifestHash` was
added to make visible.

the six long ones are the honest failures: four verifiers and a code reviewer that ran a full
verification and never filed the output their role requires, which the launcher then closes as
`Failed`, plus one worker. those are the only rows in either status column that describe an agent
failing at its work.

### two of the four meanings are already separable, and two are not

this matters for how the entry gets fixed. no new event field is needed to tell apart:

- **an operator filing run** — `provider` is `none`, uniquely
- **a launch that died before briefing** — `manifestHash` is null *and* the run lasted under a minute

the duration is load-bearing in the second one. `manifestHash` is null for every run recorded before
that field existed, so `ledger-learning/RB5` at 51.9 minutes and
`2026-09-07_1352-decompose-command-handler/RT1` at 83.1 minutes read as unbriefed and were not; they
predate the field. absence of a brief means "died before briefing" only for runs recorded after it
shipped, which is the same retroactivity limit `measure-before-scoring.md` hit.

what is *not* separable from the record: a host kill from an operator interrupt, and a run that
filed no output from a run that failed at its work. both need something the launcher knows and does
not write down.

so the fix is smaller than a new status enum. a nullable reason on `run.completed`, set by the
launcher from what it already knows at the moment it closes the run — no session, timed out, killed,
required artifact missing, provider exited non-zero — separates all four, and leaves the two the
record can already answer answerable without it.
