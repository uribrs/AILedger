# Task 6: prepared findings pilot

This tool exercises the existing MCP server, recorder, storage and retrospective. It launches no
provider. Fixtures and every write live in a new external disposable directory. The probe exits
nonzero on unexpected delivery, refusal, receipt, count, attribution, byte change or text/reference
mismatch. It preserves intermediate calls and canonical history for inspection.

## Reproduce the prepared-data trial

Run from the repository, using fresh external output paths:

```sh
dotnet build tools/FindingsPilot/FindingsPilot.csproj --artifacts-path /tmp/ailedger-task6-build -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false
dotnet /tmp/ailedger-task6-build/bin/FindingsPilot/debug/FindingsPilot.dll tools/FindingsPilot/Fixtures/RN1 /tmp/ailedger-task6-new-trial
```

Each dataset hash in `Fixtures/RN1/manifest.json` is verified before any fixture is opened. There
are seven repetitions of four five-finding batches and seven of one twenty-finding batch, each with
its associated evidence. An additional trial uses the historical accepted wording. Six recovery
scenarios cover a lost response, late missing existing reference, invalid local reference, revoked
AddEvidence capability, changed payload under a committed key, and both journals unavailable.

Every batch starts a new real in-process MCP server and recorder using memory streams, including
an initialization exchange. This measures the application/protocol/storage path, not provider
reasoning, subprocess/relay startup, network scheduling or a live model. The host explicitly permits
a runless researcher fixture with AddClaim/AddEvidence only; no provider identity or usage is invented.
The data was already prepared; incremental delivery does not prove incremental research behavior.

`summary.json` separates the recording wall window (including harness I/O/checkpoints between calls)
from the sum of sequential exchange intervals. Journal timings remain separate nested observations.
First repetitions include cold/JIT effects; all observations are retained. Runtime/OS caches, machine
load and the fixed incremental-before-single ordering make this a descriptive pilot, not a randomized
benchmark. Full-suite validation overlapped the final measured run; no isolated-load claim is made.

Each scenario retains `calls.json`, `early-checkpoint.json`, `retrospective.json`, canonical records
and available journals. Lost response means an injected output-stream write failure after commit.
Recovery reconstructs server/recorder objects; it is not an OS crash test. Assertions prove no prefix
on rejection, exact input strings/references, open claims, trusted attribution, unchanged canonical
bytes on replay/conflict, and canonical commits despite unavailable journals. Actual process-crash
and torn-append regression coverage remains in the unchanged task-2 tests.

## Historical provenance

`prepared.json` comes from the first denied RN1 script, before the eleven successful filing calls.
`recorded.json` comes from its forty canonical claim/evidence events. Preserve both: accepted text
is not necessarily identical to the initial prepared text. `prepared-command.txt` is inert historical
evidence, never a script to execute. `canonical-events.json` retains only the cited forty events.
`text-differences.json` records exact differences, without assuming semantic loss or its cause.

Re-extract read-only from the historical task directory into a NEW external directory:

```sh
python3 tools/FindingsPilot/extract_rn1.py /absolute/historical/task-directory /tmp/new-rn1-extraction
```

The extractor parses only the known data lines and expands the two literal citation aliases; it
never evaluates shell text or loads/repairs a historical state projection. It hashes both original
files before/after. Original paths, transcript sequences, event lines, tool IDs, result timestamps,
and input hashes are retained in the manifest. Historical observations are not revalidated claims
about today's Falcon implementation.

## Prepared live trial — requires separate authorization

`live_trial.py` prepares a new Git workspace with six source snapshots and a reviewable plan. Default
mode launches no model. It hashes the CLI, goal and inputs. The proposed trial is one provider episode,
240 seconds maximum, with four findings tool attempts: early record, deliberate missing-reference
refusal, corrected later record, identical replay. It must not edit source. Post-run checks verify
unchanged inputs, receipt identity, one completion, two committed batches, four events and report
populations; semantic correctness and the read/record ordering still require transcript review.

```sh
python3 tools/FindingsPilot/live_trial.py codex --cli /absolute/external-build/AILedger.Cli.dll --output /tmp/new-task6-live
# Only after explicit user approval for the billable episode:
python3 tools/FindingsPilot/live_trial.py codex --cli /absolute/external-build/AILedger.Cli.dll --output /tmp/new-task6-live --execute
```

An existing ledger/output execution is refused; failures never trigger another provider episode
implicitly. This uses existing production launch composition and exact grants. It supplies no global
approval bypass or expanded authority. The expected live retry follows observed success, so it must
not be described as lost-response recovery. A live trial does not make the historical comparison
controlled or establish cognitive/whole-workflow improvement.
