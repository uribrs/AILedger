# Dispatch observation

These CLI options change presentation only. Admission, brief freshness, role scope,
confinement, required artifacts, verification and acceptance remain kernel decisions.

## Compact launch results

Append `--compact` to an otherwise authorized `provider launch` or `provider resume`:

```sh
ailedger provider launch --task TASK --actor operator --subject SUBJECT \
  --run RUN --provider codex --cognitive-root cognitive --compact
```

The JSON summary separates the provider outcome, acknowledged ledger status, recording
state and result retention. It includes the existing next-action contract and retained
result path. No transcript is embedded. If retaining a returned provider result fails,
the CLI falls back to full output so the recovery copy is not lost. Exit codes are unchanged.
Without `--compact`, existing full JSON output remains compatible. Failures additionally
start with a one-line run/status/diagnostic summary on stderr.

`Unconfirmed` means admission or closure is uncertain/refused; provider success cannot
establish ledger completion. A terminal ledger status is not proof of process termination,
work completion, acceptance or permission to retry. Follow the existing recovery rules.

## Watch an admitted run

```sh
ailedger run watch --task TASK --run RUN
ailedger run watch --task TASK --run RUN --since 42 --json
```

The watcher reads the existing event log and prints start/completion events and subject
writes whose correlation equals the named run ID. It prints ledger status changes and
exits at a recorded terminal status, including when the run already ended. Exit zero
means observation finished, not that the run succeeded. `--json` emits one compact object
per line. `--since` is a task version, and suppresses earlier events, not the current status.
An unknown run or a cursor beyond the current task version is refused.

Ctrl-C stops only the watcher. It never cancels, closes, resumes or launches a run and
does not refresh a brief. Uncorrelated writes cannot be attributed to the run and are
omitted. For all task events, including activity before admission, use the existing feed:

```sh
ailedger history --task TASK --follow --since 42
```

These are authorized operator CLI reads. They do not add a provider inspection grant,
cross-task access, a process transcript stream or permission to read hidden ledger files.

## Briefing

Build and read the brief initially; refresh when context changes or a refusal identifies
stale served skills. The gate does not mechanically require rebuilding before every
launch with unchanged served skills. Display options never build or waive a brief.
This release updates coordinator/orchestrator guidance and its manifest hashes, so actors
briefed against the previous versions must refresh before their next gated operation.

## Validation (2026-10-08)

The final full suite passed 2,721 tests and failed one previously observed process-cancellation
race (`ProviderStartupTests.ClosedStdinWithLiveChildKeepsCancellationAndTimeoutDistinct`,
the cancellation case). All seven new ergonomics tests passed, including refusal before admission,
unconfirmed closure despite provider success, retention-failure recovery, event filtering and
watcher cancellation without ledger mutation. The main suite ran 2,623 cases; memory tests ran 99.
Both cases of the cancellation test passed in isolation afterward. No assertion or process
behavior was weakened for this change.

A built-CLI smoke check preserved `evidence add --summary TEXT`, refused an unbriefed
`provider launch --compact` without creating a run, and watched the earlier governed recon's
recorded completion. During development, the first display flag collided with the existing
evidence option; broad testing exposed it and the final flag was renamed to `--compact`.
