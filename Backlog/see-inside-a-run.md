a run records that it happened and nothing about what it cost

`self-scoring` dimension 8 asks for wall-clock time, agent runs, failed and retried runs, verifier
iterations, repeated searches, duplicate work, idle waits, unnecessary context construction,
operator turns, and work spent discovering what the kernel required.

the ledger can answer three of those. the rest happen inside a run and the kernel throws the
evidence away.

## the stream exists and is discarded

`AgentRunResult` in `ProviderContracts.cs` comes back from every launch carrying:

    ExitCode, FinalOutput, Events (ProviderEvent with RawJson), StandardError,
    DetectedVersion, EffectiveArguments, IsResume, Failure

`ProviderEvent.RawJson` is the provider's own event stream — for both `claude` and `codex` that is
where turn count, tool calls, and token usage live. the adapter retains up to 8 MB of it
(`SystemProcessRunner.cs:368`).

then `CliApplication.cs:939`:

    await WriteJsonAsync(result).ConfigureAwait(false);

it goes to stdout. `CLAUDE.md` tells an operator to background the launch and `wait`, so in ordinary
use that stream lands in a shell that is not reading it. nothing persists it.

what survives into the log is `run.completed`: the run id, the status, the provider session id, the
end time, whether the launcher authorised it, and the manifest hash and artifact count. seven
fields, none of them a cost.

## what the numbers already show is missing

    run.started        114     codex 57    claude 56    claude-code 1
    model recorded       0     of 114
    providerVersion    106     of 114
    run.completed      114     completed 98   failed 13   cancelled 2   protocolError 1

the comment on `AgentRun.Model` says the adapter knew the model and used to discard it. it is still
discarding it: 114 runs, zero models. so a lesson or a score earned against one cognition cannot be
told from another, which is the exact thing that field was added to prevent.

sixteen runs did not complete. which of the thirteen failures was a timeout, which was a provider
error, and which was an agent that did real work and then failed to record its required artifact is
not in the log — the last of those is a distinct path in `CliApplication.cs` that closes the run as
`Failed`, and the record cannot distinguish it from a crash.

## the second gap: a subagent produces no run at all

`CLAUDE.md` measures this directly. codex launched through the kernel produced eleven claims,
thirteen evidence records, seven decisions and six discarded alternatives. eight harness-spawned
agents the same day produced work the ledger records as zero runs.

that is the observability floor. an agent a lead spawns through its own harness is not slow to
measure, it is absent. `self-scoring` would score the governed half of a task's effort and report
the ungoverned half as effort that never happened — and a dimension that under-reports cost is
worse than one that reports none, because it reads as a measurement.

the comment on `AgentRun.ManifestHash` already states the honest limit of what the kernel can see:

> what the pair establishes is delivery, not reading. it cannot show that the child consumed it,
> and no field here can.

that is true of the manifest and it is true of everything else about a child's interior.

## the shape

three pieces, in order of how much they buy.

**persist the launch result.** write `AgentRunResult` to
`.ailedger/tasks/<id>/runs/<run-id>.json` at the moment it is already in hand, redacted the way it
already is. same reasoning as `record-the-refusals`: a file beside the log, not an event, not in
`GovernedTaskState`, never read at replay. it is telemetry.

**derive a small cost record from the stream and put it on `run.completed`.** turns, tool calls,
input and output tokens, and time to the run's first ledger write. all nullable trailing fields,
which is safe for replay by construction — every older event carries none. this is the one part
that belongs in the log, because a cost that only lives in a sidecar file cannot be compared across
tasks.

**fix `model`.** it is a one-line regression against a field whose comment explains why it matters.

what is deliberately not here: instrumenting a harness-spawned subagent. the kernel cannot see one
and should not pretend to. the answer to that gap is the rule `CLAUDE.md` already states — dispatch
through `provider launch`, not through the harness — and `self-scoring` should report ungoverned
effort as unmeasured rather than as zero.

## why this is a prerequisite and not a nice-to-have

`self-scoring`'s central demand is that governance effectiveness and governance cost stay
independently visible, and that neither be inferred from the other. effectiveness is mostly
measurable from the event log today. cost is mostly not.

shipping the scoring agent first means shipping confident cost numbers over the fraction of cost
the ledger happens to see, which is the failure the document names in its own words: a mechanism
that is expensive in one task should not automatically be labelled ceremony. an unmeasured cost
labels itself.

## cost

one file write on a path that already has the object. three nullable fields on an existing event
and its command-time twin. one regression fix.
