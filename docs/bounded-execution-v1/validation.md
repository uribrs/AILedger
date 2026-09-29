# Task-12 validation — bounded execution and recovery

Developed directly on `codex/structured-findings-contract`, initially clean at
`d520d159283ba5db0ba8e23d5380ee356cf9022f`. The installed tool was checked separately as
**2.0.176 from 4a0f117**, with package SHA-256
`e2ea2ea4bd0d3b6d9f88e13e38f0adafa6d3da314c76e2036cd61a613cc31c9b` and executable SHA-256
`9afe9e608cd2a19cb3512c3f86fe5d6262bc5242d9dfbcc848ea2578294a57d7`.
The prior package is retained at `/Users/user/.local/share/ailedger/rollback/ailedger.cli.2.0.176.nupkg`.

## Verified boundary

The [contract](../bounded-execution-v1.md) describes the explicit offline execution path, trusted
host binding, version checks, durable inline result receipts, bounded retrieval-only follow-ups,
process/authored/acceptance distinction and interrupted-output recovery. The existing governed
launcher and recording backend remain unchanged. No task/run history is invented or waived to
admit this path. No dependency, assurance, event/replay, role-default or historical measurement
contract changes. Task 13 is untouched.

32 new test cases cover successful authored output without acceptance, exact execution retry,
partial result preservation after failure/cancellation/timeout/host death, unknown-outcome
reconciliation, same-key result conflicts, missing/revoked/expired grants, stale inputs, executable
identity, untrusted sources, spend and read/invocation/retry limits, unsupported operations,
nonconvergent reads, duplicate/partial usage, material authored stops, changed-file inventory,
corrupt committed records, visible pending writes and a committed submission with a lost
acknowledgment. Existing task-10 and task-8 tests continue authorization/isolation/receipt coverage.

| Check | Result |
|---|---|
| Full `dotnet test AILedger.sln` | 2,236 main + 99 Memory passed; zero failures/skips |
| Repository runner, final isolated run | 2,236 main + 99 Memory passed; zero failures/skips/errors |
| Frozen historical fixture hashes | 11 unchanged |
| Frozen historical reports | Four cases matched |
| Frozen provider costs | Four samples matched |
| Existing four frozen/current handoff loss checks | Passed in full suite; fixtures unchanged |
| Actual CLI scripted provider/recovery probes | Seven cases passed; repeated for exact package at installation |
| Build | Zero compiler warnings/errors |

The CLI probe creates fresh disposable ledgers from committed frozen sources, then prepares an
explicit `read-only-audit` spec. It exercises real SIGTERM, hard host/process-group kill,
reconciliation and resume; deadline exhaustion; nonzero provider exit; versioned omitted-context
retrieval and bounded follow-up; unchanged execution replay; and denied authority-file reads,
file writes and network access. It asserts unchanged source history and the same checkpoint
receipt before/after recovery. Unavailable provider usage remains null. These are mechanical
checks, not a model experiment, client acceptance or evidence of improved cognition/cost/time.

One repository-runner attempt overlapped the full VSTest suite and failed the unchanged
`FindingsMcpBoundaryTests.BoundConcurrencyRejectsExcessCallsAndKeepsCancellationResponsive`
assertion that stderr was empty; stderr contained a transport-observation row. The same test
passed in isolation, and VSTest passed it in its full suite. The complete repository suite then passed alone
with 2,236 main and 99 Memory tests. This records the observed timing sensitivity, not a demonstrated causal
explanation or a reason to change expected behavior. No existing test/baseline was weakened.

The shell sandbox initially lacked the root-directory read needed by the macOS loader and had
an unreadable working directory; the final profile permits only that directory metadata/listing,
starts in `/`, and retains denied source-file/network/write boundaries. The scripted Ruby client
uses `--disable-gems` so it does not need ambient user/system gem-directory grants. No permissive
fallback was introduced. The outer tool sandbox prevents applying a child sandbox and VSTest's
local IPC socket, so those checks were run with their required execution permissions. A hardware
crash removed temporary build/log files during development; verification was regenerated from
surviving source rather than treating the interrupted run as complete.

## Reproduction

All builds and fixtures are outside the checkout. Use a new probe output directory:

```sh
dotnet test AILedger.sln --settings /tmp/ailedger-task12/serial.runsettings \
  --artifacts-path /tmp/ailedger-task12/build -m:1 -p:NuGetAudit=false \
  -p:UseSharedCompilation=false --verbosity minimal
UseSharedCompilation=false sh scripts/test-governed.sh all
python3 tools/FindingsBaseline/verify-fixtures.py
dotnet build tools/FindingsBaseline/FindingsBaseline.csproj \
  --artifacts-path /tmp/ailedger-task12/baseline -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false
dotnet /tmp/ailedger-task12/baseline/bin/FindingsBaseline/debug/FindingsBaseline.dll tests/Fixtures/structured-findings-v1
python3 tools/EpisodeProbe/run.py /absolute/ailedger /tmp/ailedger-task12-fresh-probe
```

Runsettings: `<RunSettings><xUnit><MaxParallelThreads>1</MaxParallelThreads><ParallelizeTestCollections>false</ParallelizeTestCollections></xUnit></RunSettings>`.
The repository runner independently builds externally and runs the actual xUnit suites. No
expected baseline was regenerated. Existing provider tests and new process probes use scripted
providers, never a real/billable model.

## Remaining limits

The installed slice is macOS-only and offline. No Codex/Claude billable episode adapter or spend
cap integration is implemented. Source pins mean explicitly authorized snapshot bytes, not a
live external repository freshness guarantee. Provider usage is reported, optional and not a
billing audit. Host death requires explicit confirmation that the old provider stopped; its ending
stays unknown. A removed scratch directory produces unknown inventory. Pipe data never received
by the host cannot be reconstructed. Persisted deadlines cannot be reset by resume. The local
operator/OS owns the trusted grant and store; this is not a multi-tenant authority service.

The JSON/host-file authoring step and operator CLI recovery remain visible ergonomics limits.
Authored checks are not independent assurance. Grant validation and a successful process cannot
prove semantic completeness. Tasks 7–12 client trials remain pending. Installation is recorded
separately, with exact commit/package/payload/executable identity and fresh probes; see the
[installation receipt](installation.md) and [trial instructions](trial.md).

Final retained verification log SHA-256 values (temporary paths may expire):

- `full-final.log`: `926be4fb0fe163492673af40033e29e247f05989d7bdc3f8525c9e3cd39dbe79`.
- `runner-isolated-final.log`: `443fc0a39f124e414912619c2dda3bbbcb141e507c731e89458e2fa2de04baee`.
- `concurrency-recheck.log`: `12549f80b0d7cc9d47c7ff681cc3d26b649cc8bbd9904700744be0d759e0a636`.
