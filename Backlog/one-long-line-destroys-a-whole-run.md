# One overlong line destroys a whole run, and everything it had recorded

## The asymmetry

`AgentAdapterBase` reads a provider's stdout line by line and enforces three limits. Two of them
degrade gracefully. One does not.

    MaximumCharactersPerLine        1 MiB     ends the run as ProtocolError
    MaximumRetainedCharacters       8 MiB     stops retaining, run continues
    MaximumCharactersPerStream                stops reading, run continues

A line this kernel cannot **parse** is tolerated: `RunCostReader` catches `JsonException` and
records no measurement, and the run finishes. A line this kernel cannot **hold** kills it. The two
failures are the same class of event — a provider emitted something the launcher did not expect —
and they are handled at opposite extremes.

Recorded as validated claim `C5` in `2026-09-09_1010-retrospective-projection`.

## What it costs, measured in that one task

Two of fifteen runs, both verifiers, both dead with nothing in the record.

    R6    codex verifier on W3   killed by the line limit
    R14   codex verifier on W6   killed at 7m29s, "Provider output contained an overlong line"

`R14` wrote **zero ledger events and no verifier file**. The command that produced the line was the
in-process xunit host running the full suite with an empty filter — the one command a verifier of a
code change is certain to run. It had almost finished the job; the finding died with the process.

That is a 13% loss rate in one task from a single formatting property of a subprocess's stdout, and
it falls hardest on exactly the role whose whole output is a long document.

## Why the current behaviour is not obviously wrong

The limit exists for a reason: a runaway provider can emit an unbounded single line and the reader
would buffer it into memory with no ceiling. Refusing is safer than growing without limit, and the
stream and retention caps show the author already thought about degrading rather than dying — so
the line cap being fatal looks deliberate rather than overlooked.

The problem is not that a ceiling exists. It is that hitting it is treated as *the provider spoke a
protocol this kernel cannot trust* rather than *one line was too long to keep*. Nothing about an
oversized line makes the preceding four hundred lines untrustworthy.

## The fix

Truncate the line, mark the stream as truncated, and let the run finish.

- Keep the first `MaximumCharactersPerLine` characters, discard the remainder of that line, and
  continue reading at the next newline.
- Record that it happened on the run: a count of truncated lines, the same shape as
  `unreadableRows` on the retrospective's refusals — which exists because of the identical rule
  that absence and partiality must not read as completeness.
- `ProtocolError` stays for what it names: a stream this kernel cannot follow at all.

A truncated line loses at most the tail of one tool result. A `ProtocolError` loses the run, the
findings, and the time.

## The cheap mitigation that does not need code

Until then, a brief can tell the agent to bound its own output per line:

    <command> 2>&1 | cut -c1-2000 | tail -60

`tail` and `head` do not help — they bound the number of lines, and the limit is on one line's
length. This is `K16` in the task above, added after `R14` died. It works, and it is the wrong
place for the rule: every future brief has to remember it, and the one command that triggers it is
the one every verifier runs.

## Compounds with

- `the-ledger-is-written-at-the-end-or-not-at-all` (row 36). A run that files its findings in one
  burst at the end loses all of them to a line printed a minute earlier. Both defects have to be
  fixed for either fix to be worth much.
- `cancelled-means-four-different-things`. `ProtocolError` is a fifth thing a lost run can mean,
  and it reads like a governance failure when it is a formatting one.

## Acceptance criteria

- An overlong line is truncated and the run continues to a terminal status of its own.
- The truncation is counted and visible on the run record, not only in the sidecar.
- `ProtocolError` is still raised for a stream that cannot be followed.
- A test emits a line above the cap mid-stream and asserts that events after it are still read.
