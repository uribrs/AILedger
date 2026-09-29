# Task 10 validation — inspection and readiness

2026-09-29. Worked directly in the requested `structured-findings-contract/AILedger`
worktree on `codex/structured-findings-contract`. Initial status was clean at `924ea16`;
there were no subsequent commits. The installed identity was checked as `2.0.172 from
6ebb55a`, and its package SHA-256 was verified as
`647327dfccb34a8777e0cb31b60202d80eb2f0a1f4fa22f7690d3f7e8bf42027`.
That package was preserved externally before installation; the installation receipt
records its durable rollback location.

## What is verified

The [contract and coverage table](../structured-inspection-v1.md) define three bounded
read tools and seven readiness actions. No read refreshes a governed brief, repairs
projections/tails, resolves a judgment, changes a grant or appends a canonical event.
The existing complete-group parser, role/work/assurance selector, recorded capability
policy, recording candidate builders and command handler remain authoritative.
Readiness output is never consumed as execution authority. Physical assurance-candidate
inspection remains explicitly unknown; dispatch and run completion remain unsupported.

35 additional cases (34 inspection cases plus a second production-launch provider case)
cover typed claims/evidence/decisions/receipt retrieval, pagination and full chunk digests,
surrogate-safe bounds, unrelated-current-record recovery, denied/protected references,
actor/run/role authority, stale ledger and changed cognitive content at one version,
missing/stale briefs, all four recording candidates, exact refusal parity, key conflicts,
event/byte capacity, work/stage gates, dependency invalidation and assurance isolation.
Tests change capability, expected claim state and dependencies between readiness and
execution. They compare every fixture file around reads, including deliberately damaged
projections and torn tails. Corrupt or missing facts do not become successful checks.

Both trusted provider paths use real local relay IPC with scripted clients. The production
launcher is tested with Codex and Claude identities and retains one provider-completion
usage record while separating inspection from mutation observations. No external model
or billable episode is launched. Rapid repeated retrieval exercises slot accounting;
actual excess concurrent requests and cancellation remain bounded.

## Results

| Check | Result |
|---|---|
| Focused inspection/provider/transport tests | 127 passed before the final test-drain correction; final full suites include that correction |
| Full `dotnet test AILedger.sln` | 2,182 main + 99 Memory passed; no failures/skips |
| Repository runner | 2,182 main + 99 Memory passed; no failures/skips/runner errors |
| Corrected concurrency fixture, repository runner | 1 passed after the full runner's build; final dotnet suite includes this corrected fixture |
| Frozen fixture hashes | 11 unchanged |
| Frozen historical reports | Four cases matched |
| Frozen provider costs | Four samples matched |
| Development executable inspection probe | Passed with a fresh disposable fixture |
| Existing disposition executable probe | Passed; original four-tool configuration remains compatible |
| Whitespace/build | Clean diff; no compiler warnings/errors |

The full runner exercised the final production implementation. Its build preceded the
last test-only correction from an obsolete lock filename to `TaskWorkspaceLayout.LockFileName`;
the corrected case was then verified through that runner separately. The final full
`dotnet test` includes both corrected fixtures. Serial test collection settings were used
for the final standard run; no tests, assertions or expected baselines were skipped or recaptured.

## Failures and corrections retained

The first compile of the new tests found a missing `AILedger.Storage` import. The first
focused run passed 40 and failed five: four fixtures violated existing stage/self-assignment
rules or truncated an entire opening transaction, and the application did not yet map
`InvalidDataException` to a structured unavailable result. Fixtures and error mapping were
corrected; the next 45 focused cases passed without changing governance rules.

The executable probe found a real transport race: rapid sequential reads could reach the
eight-call limit while already-delivered replies were still writing bounded diagnostic tails.
The host now drains one delivered reply when capacity is full; actual outstanding requests
remain bounded. The executable probe and 127 focused cases then passed.

Initial full runs exposed environment/test boundaries: sandboxed VSTest could not bind its
IPC socket, and the Memory fixture could not write its disposable platform data directory.
Required checks were rerun with those permissions. A superseded parallel run encountered
the existing 250-ms diagnostic deadline under contention and was stopped after source
changes. A subsequent run passed 2,180 main cases and failed two concurrency fixtures:
the new relay test read its journal before remote host disposal, and an existing cancellation
test held `.mutation.lock` while production uses `.writer.lock`. The former now drains the
host before checking all 36 rows; the latter locks the actual configured writer lock.
Neither assertion was weakened. Final results above supersede these intermediate runs.

No frozen output was recaptured. Existing findings-v1, alternatives-v1, artifact-submission-v1,
claim-dispositions-v1 schemas and historical fixtures have no changes. The only shared storage
refactoring extracts the existing append serialization unchanged for prospective sizing.
No new replay rule, role default, assignment migration or relaxed dependency/assurance rule exists.

## Reproduction

Builds and runtime fixtures remain outside the checkout:

```sh
dotnet test AILedger.sln --settings /tmp/ailedger-task10/serial.runsettings --artifacts-path /tmp/ailedger-task10/verified-final -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false --verbosity minimal
UseSharedCompilation=false sh scripts/test-governed.sh all
UseSharedCompilation=false sh scripts/test-governed.sh AILedger.Tests BoundConcurrencyRejectsExcessCallsAndKeepsCancellationResponsive
python3 tools/FindingsBaseline/verify-fixtures.py
dotnet build tools/FindingsBaseline/FindingsBaseline.csproj --artifacts-path /tmp/ailedger-task10/baseline-final -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false
dotnet /tmp/ailedger-task10/baseline-final/bin/FindingsBaseline/debug/FindingsBaseline.dll tests/Fixtures/structured-findings-v1
python3 tools/InspectionProbe/run.py /absolute/path/to/ailedger
python3 tools/ClaimDispositionsProbe/run.py /absolute/path/to/ailedger
```

The external runsettings contain `<RunSettings><xUnit><MaxParallelThreads>1</MaxParallelThreads><ParallelizeTestCollections>false</ParallelizeTestCollections></xUnit></RunSettings>`.
The new executable probe checks seven narrowly granted tools, trusted binding, typed and chunked
retrieval, receipt identity, no projection repair, readiness/stale/unsupported distinctions,
execution revalidation after a changed claim, spoof/revocation refusal and separate measurement
populations. Usage remains explicitly unknown in this non-provider probe.

## Delivery status

Implementation verification and exact installation are separate from client acceptance.
The scoped source commit is packed externally, tested through a separate candidate tool
installation, then installed under Uri's advance authorization. A separate installation
receipt records actual identity, package/payload/executable hashes and fresh probes.
Do not reinstall just for the subsequent documentation receipt.

Tasks 7–10 client trials remain pending. Tasks 1–6 remain accepted. No kernel development
records, agent dispatch, global instruction/skill changes, merge, publication, historical-task
migration, model episode or task-11–14 implementation was performed. The task-9 historical
simulation remains a mechanical comparison, not a client trial or a cost/judgment/delivery claim.

Final log hashes (external scratch paths may expire):

- `/tmp/ailedger-task10/full-verified.log`: `095a4eef55468569a3865fa7b7c2dd36d2a1645156364eab264d05a7383e73db`.
- `/tmp/ailedger-task10/runner-final.log`: `61944d937d1e21611783955be529d974c7518c7bc881d52c76f7299496ec8aca`.
- `/tmp/ailedger-task10/runner-concurrency-final.log`: `de7b98fee1c95fba012d50c0be150bed582aa95d297a7a8243ca7438f29f4192`.
- `/tmp/ailedger-task10/focused-final.log`: `988b3007700849687dece88fb3c6e192bbf59dbf120f78af056b03c1853d182d`.
- `/tmp/ailedger-task10/baseline-final.log`: `1549f160669268c1f6416f029c065ffd1783ad2cd122e8a576e5c195963ba56f`.
- `/tmp/ailedger-task10/hashes-verified.log`: `84a31c5bca17ff72e93c03ddbbfb267b541278a52bc89eb5679f9fe861046b73`.
- `/tmp/ailedger-task10/development-probe-final.log`: `ca046d5c369aee6a318936613706ad4fe6658551dee15689a84560e07d52ad2d`.
- `/tmp/ailedger-task10/dispositions-regression-probe.log`: `6b6f1afe97962fa62db0b1da509f69eb7834f029db3baa26ae915f94aa572d43`.
