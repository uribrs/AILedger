# Task 13 development validation

Implementation verification is separate from installation and client acceptance. Tasks 7–13 client
trials remain pending. No model/provider episode, cost benefit or judgment improvement is claimed.

## Starting identity and unchanged history

Work began on clean `codex/structured-findings-contract` at
`f62d943583d7ba85e7b936f94e98949b5860b859` (task-12 documentation). The installed CLI independently
reported `2.0.178 from 142bab5`; its package SHA-256 was
`7177ea11e95132cffdc5a7558c226b765d77772725b05922cc223f19e8a72b24`.
No governed development task/events or development agents were created. Disposable runtime fixtures
exercise authorization. Existing event/replay contracts and frozen expected outputs were unchanged.

## Test results

* Full .NET suite: **2,284 main + 99 memory tests passed**, no skips, before the final resource-input
  grant guard. That final guard and the complete task-13 group subsequently passed **49 tests**.
* Final-source repository runner: **2,285 main + 99 memory tests passed**, no skips/errors.
* The focused rebuild reported no compiler warnings/errors. Builds were outside the checkout under
  `/tmp/ailedger-task13/` or the repository runner's external temporary directory.
* Frozen fixtures: **11 SHA-256 checks passed**. Original graph (47 events), original Axonius
  (97 events), missing/truncated-refusal variants (97 events each), and four provider-cost samples
  matched their existing expected output. Nothing was recaptured or regenerated.
* Actual local MCP development probe passed discovery, scoped calls, receipts, independent acceptance,
  change invalidation, restart, hard host/process-group interruption and unknown reconciliation.
  The exact packaged/installed executable probes are recorded separately in [installation](installation.md).

One intermediate full-suite run failed two existing telemetry-count assertions:
`ClaimDispositionsMeasurementTests.LostResponseRetryHasOneCanonicalCommitDespiteCollectorFailures(false,false)`
and `FindingsMcpBoundaryTests.ExactWireLimitIsAcceptedAndOversizedFrameDoesNotPoisonNextMessage`.
Both expected two observed attempt rows and received one. The targeted rerun passed all 13 matched
tests; a subsequent complete run passed 2,284 + 99. No existing assertion or telemetry behavior was
weakened. The cause was not established; timing/resource contention is a possibility, not a finding.

## Meaningful task-13 coverage

The new tests exercise actual file capture and content identity, criteria/source/candidate changes,
transitive dependencies, unrelated accepted areas, revoked/expired authority, role and directory-grant
containment (including aliases), self-approval, unsupported authored passes, unavailable/failed tests,
mutated test inputs, complete-schema-but-uninspected reports, stable partial checkpoints, missing
endings, lost commit acknowledgments, conflicting retries and concurrent identical writes.

Positive cases require separate actual principals and sessions, host read/test receipts and an explicit
acceptance operation. Contradictions survive supersession; an authorized attributable disposition is
required. A new contradiction in an endorsed dependency invalidates its consumer without changing bytes.
Targeted synthesis can preserve a cross-area contradiction without becoming a mandatory stage.

Scripted local clients call the real MCP stdio server and authenticated provider relay. Tests inspect
actual Codex and Claude adapter invocations, exact narrow tool grants, guidance, host-owned identities,
forbidden caller identity fields and the unchanged seven-tool default. No real model was invoked.

## Reproduction

```sh
dotnet test AILedger.sln --artifacts-path /tmp/task13-dotnet-build -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false
UseSharedCompilation=false sh scripts/test-governed.sh all
python3 tools/FindingsBaseline/verify-fixtures.py
dotnet build tools/FindingsBaseline/FindingsBaseline.csproj --artifacts-path /tmp/task13-baseline-build -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false
dotnet /tmp/task13-baseline-build/bin/FindingsBaseline/debug/FindingsBaseline.dll tests/Fixtures/structured-findings-v1
python3 tools/AssuranceProbe/run.py /Users/user/.local/bin/ailedger /tmp/task13-fresh-probe
```

Run the suites sequentially. On a restricted host the actual IPC, local process and sandbox probes
need permission to use those existing local mechanisms. No network/model access is necessary.
Check the fixture directory against the repository if moved. The retained installation receipt names
the exact implementation commit; the later documentation commit is not another installed runtime.

## Limits exposed, not erased

The bounded input closure is explicit UTF-8 content, at most 32 paths/64 KiB per area. Undeclared
inputs, irrelevant tests, false human/model inspection assertions, external runtime dependencies and
operator/OS-owner tampering are outside what mechanical receipts prove. The host is not a hostile-code
sandbox or model executor. Task 12 remains offline/read-only; external or existing authorized execution
produces implementation candidates. Requested model labels are not observed model identity, and usage
stays unknown. A check's exit zero does not establish semantic correctness.

The probe deliberately retains a repaired candidate with an extra newline and a shell test that
ignores trailing newlines; **that repair remains unaccepted**. Uri's real trial must assess actual
findings and test relevance rather than treating these scripted observations as client acceptance.
See [checkpoint F](trial.md) for the candidate-change and interruption protocol and unmet prerequisites
for any real/billable sessions. Task 14 was not implemented.
