# Task 5 validation: measurement continuity

Date: 2026-09-28. Worktree: `/Users/user/.codex/worktrees/structured-findings-contract/AILedger`.
Branch: `codex/structured-findings-contract`. Task-4 base: `a9e8fa8`.

Task 5 meets its acceptance requirements. The existing retrospective has an opt-in findings section;
historical/default outputs are unchanged. See the [measurement contract](task-5-measurement.md) and
[task-6 handoff](../handoffs/structured-findings-task-6.md).

Development used no kernel workflow, governed development task, governance ledger writes, agent
dispatch, global installation, merge or publication. Kernel writes in tests belong to disposable
fixtures only. All builds are outside the checkout. No new billable provider episode was launched.
The initial worktree was clean; only task-5 source/tests/docs/probe/backlog changes are included.

## Historical compatibility

- All **11 frozen hashes** verified before implementation and after final source changes.
- The read-only `FindingsBaseline` probe matched **all four historical report cases and all four
  provider-cost samples** against the unchanged expected outputs. No capture mode was used.
- The default `TaskRetrospectiveReport` has no new serialized field. `Findings` uses an explicit
  null-ignore attribute and appears only when selected. The CLI test removes that one field from
  an extended report and compares the entire remaining JSON to the ordinary report.
- TaskRetrospective/CoordinatorMeasurement calculations, RunCostReader, ProviderRunRecorder,
  refusal capture/rule grouping, TaskCloseoutEvidence, scoring rubric/rules and Memory ingestion
  implementations are unchanged. Their original tests are included in the full passing suite.
- The new journal reader is observational only. The canonical receipt read reuses task 2's exact
  complete-group parser. Neither the frozen MCP response nor application/transport capture changed.

## Existing real provider observations

The comparison probe reread the task-4 live Claude and Codex artifacts through the new reports.
It used temporary copies because the existing CLI state read can repair derived projections.
It hashed every original episode file before/after and verified unchanged canonical bytes in the
copies. These are new **reader checks on existing live observations**, not new provider trials.

| Retained measurement | Claude | Codex |
|---|---|---|
| Distinct observed tool calls | 3 | 3 |
| Application attempts | 3 | 3 |
| Canonical findings transactions / granular events | 1 / 2 | 1 / 2 |
| Tool sequence | commit, identical replay, kernel refusal | commit, identical replay, kernel refusal |
| Original run completions | 1 | 1 |
| Actual joined session | `89f132d5-b3cf-4feb-8988-a1cb23251069` | `01a0e81c-06b3-7432-ae47-3201b3ec6ef3` |
| Transport session observation | null | null |
| Model turns | 8 | absent |
| Output tokens | 2,256 | 729 |
| Uncached / cache-write / cache-read input | 14 / 54,994 / 318,958 | 20,829 / 0 / 164,224 |
| First ledger write | 33,882 ms | 18,899 ms |
| Manifest artifacts | 8 | 8 |
| Launch completion / timeout / truncated lines | completed / false / 0 | completed / false / 0 |

The probe compares the entire completion to the preserved acceptance record, including manifest
hash, and compares each original retrospective cost total and measured population. It checks
sidecar session/truncation/timeout against completion, all tool-to-application joins, canonical
transaction/event IDs, replay flag and the kernel refusal's `not_committed` boundary. No cost is
multiplied by attempts or events. Codex served model remains absent on completion.

Claude's additional protocol observation is retained in transport rows separately from tool calls;
its method and exact negotiated MCP version remain unobserved. Neither report infers those fields.
The journals have no completeness watermark, so no detected gaps in these episodes is not a claim
of a complete census. Original artifact locations and provenance remain in task-4 validation:
`/tmp/ailedger-task4-live-{claude,codex}-2/` (temporary; may expire).

## Regression acceptance

**28 new measurement cases passed.** They use real disposable journals, recorder/kernel/storage
and MCP streams, plus deliberately damaged copies/fault paths. The existing production-launch test
also now checks default-path measurement and run/session/manifest joins with a scripted model
result and real relay. It is not presented as live-provider acceptance.

| Scenario | Verified behavior |
|---|---|
| Lost response and stable-key retry | Two observed calls/application attempts, one canonical transaction and two granular events; committed response delivery can fail |
| Application journal blocked, transport journal blocked, both blocked | Commit and exact receipt recovery remain successful; readable observations retained, missing populations stay null, coverage gaps explicit |
| Missing/empty/inaccessible/truncated/malformed/unsupported/incomplete/wrong-task/duplicate telemetry | Invalid/unobserved rows never become success or fabricated zero counts; source/line gaps remain with readable subsets |
| Failed flush with later receipt recovery | Original unknown outcome remains unknown; independent canonical/application join proves recovered commit |
| Pre-application binding failure vs late kernel refusal | First has no application ID; second joins its real attempt and retains one original refusal with no committed prefix |
| Application exception | No fabricated attempt ID or success; entry without a result is an explicit gap |
| Conflicting transaction/application/correlation keys | No silent reconciliation to a convenient receipt; conflicting or missing joins are exposed |
| Run/session/model/cost | No start-session enrichment before completion; observed completion session joins without altering raw rows; requested model separate from served model; turns absent for Codex, cache-write zero retained |
| Provider stream truncation | Positive truncation count creates an explicit gap without rewriting retained usage/outcome fields |
| Torn canonical group / corrupt receipt / changed snapshot | Storage-owned group rules determine visibility; corrupt/unavailable/mixed snapshots never fall back to telemetry as proof |
| CLI integration | Explicit standalone diagnostics directory works; the only JSON difference is the opt-in findings section; canonical event bytes remain unchanged |

## Final validation results

- Focused `FindingsMeasurement`: **28 passed, 0 failed, 0 skipped, 0 runner errors**.
- Final repository-aware full suite: **main 1,895 passed; Memory 99 passed; zero failures, skips or
  runner errors**. Final directory:
  `/private/var/folders/v3/pncbbv7x45jgp4bnd81fjnl80000gn/T/ailedger-tests.Y751kK`.
- Final standard `dotnet test`: **main 1,653 passed / 242 failed; Memory 97 passed / 2 failed**.
  This invocation failed. Its named xUnit failure set is identical to task 4's saved final log;
  the external-output repository/cognitive-root discovery failures remain. No new measurement
  test failed. Do not describe this invocation as green.
- Existing independent-clock `ReconsiderationConsultantTests.R8_ConsultationRequiresARecordedSubjectRole`
  passed in the final runs. Its timing race was not changed or claimed fixed.
- Final external solution/runner/baseline builds: **zero warnings and errors**.
- Frozen checker: **11 unchanged hashes**. Read-only baseline: **four report cases and four
  provider-cost samples matched**. Existing provider-observation comparison: **both episodes passed**.
- `git diff --check`: clean.

Earlier failures remain part of the execution record: an initial new test used an unavailable CLI
constructor; a fixture tried to complete a started run with a different session; another fixture
mistook an application grant denial for a pre-application binding failure. The fixtures were
corrected without changing kernel/endpoint rules. A sandboxed findings run passed 208 of 212 tests
and failed four existing local-socket host cases with permission denied; the full authorized suite
above includes all of them passing. An earlier full suite before the final six cases passed 1,889
main and 99 Memory tests. These intermediate results do not substitute for the final suite.

Reproduction (the wrapper is build/xUnit infrastructure, not a governed task launcher):

```sh
UseSharedCompilation=false sh scripts/test-governed.sh AILedger.Tests FindingsMeasurement
UseSharedCompilation=false sh scripts/test-governed.sh all
dotnet test AILedger.sln --artifacts-path /tmp/ailedger-task5-standard-final -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false --verbosity minimal
python3 tools/FindingsBaseline/verify-fixtures.py
dotnet build tools/FindingsBaseline/FindingsBaseline.csproj --artifacts-path /tmp/ailedger-task5-baseline-final -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false
dotnet /tmp/ailedger-task5-baseline-final/bin/FindingsBaseline/debug/FindingsBaseline.dll tests/Fixtures/structured-findings-v1
python3 tools/FindingsBaseline/verify-provider-measurement.py --cli /absolute/external-build/AILedger.Cli.dll /tmp/ailedger-task4-live-claude-2 /tmp/ailedger-task4-live-codex-2
```

Final temporary logs: `/tmp/ailedger-task5-full-final.log`,
`/tmp/ailedger-task5-standard-final.log`, `/tmp/ailedger-task5-focused-final.log`,
`/tmp/ailedger-task5-baseline-final.log`, `/tmp/ailedger-task5-live-observations.log`.

## Remaining limitations and boundaries

No new telemetry census, protocol-version capture, private transcript ingestion, provider-denial
classifier, refusal-ID join or client acknowledgement exists. Failed writes/crashes can be wholly
invisible; report counts are lower bounds. Missing observations remain absent. New journals are
not indexed by Memory. A standalone directory must be explicitly selected if it differs from
the production convention. Read bounds and unsupported schema/rows produce gaps rather than totals.

Kernel policy, exact provider grants and trusted in-memory binding remain unchanged. The local
relay is not an OS sandbox against direct process/ledger authority. Task-2-or-newer general storage
is still required; task-1 singleton-marker incompatibility remains. Durability is still local
process-crash/retry recovery, not distributed/power-loss proof. Report joins do not widen receipt
access for a denied agent or rewrite historical bindings. No task 6–8 pilot, speedup/cognitive
claim, YAML, broader batch operation, workflow retirement or rollout is included.
