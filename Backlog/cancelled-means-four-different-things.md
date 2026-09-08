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
