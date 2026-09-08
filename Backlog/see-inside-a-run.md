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

## two things on the same event that this item did not take

both found while settling the served-model question in `2026-09-08_1428-run-cost`, both read off the
providers' own schemas rather than guessed, and both on the terminal event the launcher already has
in hand.

### claude states the price and codex does not

claude's result event carries `total_cost_usd` beside `duration_api_ms` and `modelUsage`. codex's
exec stream carries no cost field in any currency — eleven occurrences of `total_token_usage` and
nothing else (C24, E32, E33).

that is a direct money figure, which is stronger than reconstructing cost from three token buckets
at rates that change. it is also asymmetric in a way the token buckets are not: C6 settled that the
two providers' token conventions are documented and opposite, so they map. there is no mapping for a
number one provider simply does not state.

so a dollar field would be populated for claude and null for codex, and any comparison across
providers reads as "the claude runs are the ones that cost money". record it if it is recorded at
all as what it is — one provider's own price for its own run, never a cross-provider measure — and
keep the token buckets as the comparable figure.

### the subagent count is already on the stream

claude's result event carries `subagent_stats`, optional, beside `permission_denials` and
`queued_turn_count`, and the object holds a `subagentCount` and a `transcript_ref` naming a session
file and a project directory key (C25, E34).

`CLAUDE.md` says a harness-spawned agent is invisible to this ledger and cites the measurement: one
day, eight harness-spawned agents, zero runs recorded. that is still true of what those agents *did*.
it is no longer true that the kernel cannot know they existed. the provider counted them and the
launcher throws the count away with the rest of the stream.

one launched run is not one cognition. a run that spawned six subagents cost roughly seven agents'
worth of tokens and the ledger records it as one run, which is a governance-cost error in a known
direction, the same direction as the coordinator's invisibility in `measure-before-scoring.md`.

### why neither is in this item

both are fields on `run.completed`, which is `AILedger.Core`, and that half shipped as W1. W2 holds
`src/AILedger.Cli` and `tests`. Adding an event field to close a work item that is already completed
would mean reopening the replay-safety review for two fields nobody has yet needed. they are the
natural third work item on this task, after the populating half proves the reader works at all.

### and a defect the same reading turned up, which outranks both

One stdout line that is valid JSON but **not an object** aborts the entire provider run (C27, E36,
E37).

`ProviderProtocol.GetString` calls `element.TryGetProperty` before checking `ValueKind`, and
`TryGetProperty` throws `InvalidOperationException` on anything that is not an object. Both
`ParseCodex` and `ParseClaude` call it on the root as their first act, so `[]`, `"text"`, `42`,
`true` or `null` on one line throws.

The chain from there:

    DrainAsync awaits the line callback with no handler   → the stdout task faults
    AwaitFailFastAsync                                    → propagates
    RethrowAfterCleanupAsync                              → kills the child, rethrows the original
    AgentAdapterBase catches OperationCanceledException
      and InvalidDataException only                       → InvalidOperationException escapes
    CliApplication's catch-all                            → run closed Failed, launch throws

Every event already collected is discarded, the child is killed, and the operator gets a generic
launch failure.

**The asymmetry is the point.** A line that is not JSON at all — a bare `{` — is caught as
`JsonException`, sets `parseFailure`, and the run keeps collecting and then ends with
`"Malformed provider JSONL: …"`, which is exactly the right behaviour and is already written. A line
that is valid JSON of the wrong shape is fatal. The adapter has a designed tolerant path for a bad
line and it only covers half the ways a line can be bad.

With W2 in place the loss grows: the cost read and the provider sidecar both live on the terminal
path, so an aborted stream now throws away the measurements as well as the events.

**The fix is one line** — add `InvalidOperationException` to the catch at
`AgentAdapterBase.cs:125`, so the case takes the path that already exists for it. That one catch is
complete cover: `ProviderProtocol.ReadString` and `ReadNestedString` share the same unguarded
`TryGetProperty` on the root, but both are reached only through `ReadFinalOutput`, which is called
inside that same try, and only once `GetString(root, "type")` has already succeeded — so a
non-object root can never get that far. ALT8 records the
alternative and why it lost: guarding `ValueKind` inside `ProviderProtocol` would make the line parse
as type `unknown` and swallow genuinely broken output, adding a second mechanism where one already
works.

**The test that proves it** must assert the tolerant behaviour, not just the absence of a throw: a
stream of three lines where the middle one is `[]` must produce a run whose events include the first
and third, whose failure message names malformed JSONL, and whose status is `Failed` — not a run that
threw out of `RunAsync`.

Not fixed on the spot because `tests` is held by W2 and a one-line change to the provider stream
without a test is how the four tests that passed while proving nothing got written. It is the first
thing in W3.
