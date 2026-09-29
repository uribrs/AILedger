# Task 2 validation: atomic findings operation

Date: 2026-09-28. Worktree: `/Users/user/.codex/worktrees/structured-findings-contract/AILedger`. Branch: `codex/structured-findings-contract`. Implementation base: `a62053c48adb6fe8d194a3c130e8530544c5287d` (task 1; production source equivalent to `b72437a`).

## Delivered boundary

`FileGovernedTaskService` implements `AILedger.Core.Findings.IFindingsRecorder.RecordAsync(binding, request, cancellationToken)`. Existing `IGovernedTaskService.ExecuteAsync` callers remain source-compatible. New production files are local to `Core/Findings` and `Storage/Findings`, with a narrow shared append/replay change. No domain event, capability, role, stage, command rule, or replay-time domain prerequisite was added or weakened.

The request is copied and validated before awaiting; the fingerprint uses the task-1 fixed-order canonical writer. The service takes the existing task lease once, replays committed groups, validates the trusted binding, and checks for an authorized existing receipt. A new submission allocates IDs under that lease and applies each generated command through the existing handler to candidate state. Aggregate event/byte limits include receipt metadata. Nothing in that candidate batch is appended until all commands pass.

The receipt is in `_ailedgerFindingsReceipt` on the first event of a completely marked append group, including singleton batches. The shared group parser withholds incomplete tails and validates committed receipt structure, version, attribution, event maps, counts, time, and duplicate keys. Findings reads also verify recorded run ownership. Retry authorization checks the original operation shape through `AuthorizationPolicy`; it does not execute mutation validation again. Receipt lookup precedes mutation budgets, including when those budgets have subsequently been lowered. A retry re-flushes a visible group before acknowledging it.

`findings-attempts.jsonl` is best-effort observation under the existing lease. It records identities, build identity, outcome, timing, and receipt/event joins without evidence prose or provider usage. `FindingsResult.CollectionStatus` reports `collected` or `unavailable`; it is not a new field in the v1 transport response. Candidate validation and append timings are nullable, and pre-lock failures have no journal row. Service kernel refusals retain one ordinary refusal row for the failing generated command, with original committed version and unmodified kernel message.

Development used no governed task, ledger writes to govern development, agent dispatch, global installation, merge, or publication. Tests wrote disposable ledgers only. The full suite's existing provider tests use their test fixtures; no live provider pilot was performed.

## Acceptance coverage

All **86 new findings cases passed**, using actual handlers/reducers, serialized logs, and disposable roots. The existing test assembly's probe entry point has one additional dispatch to a findings-only helper; existing refusal probes remain intact.

| Acceptance area | Evidence |
|---|---|
| Findings/evidence, local/existing references, trim/text fidelity, open claims | `FindingsOperationTests.BatchUsesRealDomainRulesAndPreservesTextReferencesAndProvenance`; shell characters, Hebrew, emoji, quotes and internal newlines remain data |
| Claims-only, evidence-only, recalled lessons | Singleton marker/cap test and both lesson-citation tests in `FindingsAuthorityTests`; current recalled-lesson validation is exercised |
| Shape, Unicode, limits, duplicate keys, references | `FindingsContractTests`: null/missing typed values, malformed surrogates, exact ASCII identifiers, per-array/per-direction/aggregate/body bounds, unknown local references |
| Canonical equivalence and conflicts | Frozen ASCII SHA-256 vector; explicit Unicode/escaping/null bytes and independent hash; real retry from reordered/escaped JSON; text/order/whitespace/reference/binding conflicts |
| Late failure and authority | Last evidence command rejects after earlier candidate commands succeeded; byte-identical canonical log, no claims/evidence prefix, exactly one original refusal; missing and revoked capabilities tested |
| Stable receipt access | Lost response, new service instance, task advancement, archive, deleted projections, same/lowered caps; original receipt version retained; host grant revocation and original-shape capability checks deny disclosure |
| Concurrency | Ten same-key calls plus competing different keys and ordinary command writes; separate OS processes concurrently submit the same key and ordinary command writes; one transaction and contiguous versions |
| Crash/retry | Before-write cancellation, mid-line tear, complete-prefix tear, missing final newline, failed flush after complete write, failed retry flush, process exit after prefix/full OS write; replay or safe fresh commit as appropriate |
| Projection and telemetry failure | Unwritable projection/attempt paths leave successful receipt recovery intact and report observation unavailable |
| History corruption | Eighteen receipt mutations, duplicate keys across valid groups, interior incomplete groups, malformed event objects; no fallback commit |
| Authorship/run measurement | Operator-dispatched researcher writes retain researcher authorship and `CorrelationId == R1`; completed-run retry/fresh recording stays legal, reported usage unchanged, absent turns stay absent |
| Historical readers | Existing unmarked/marked command groups replay alongside findings; old typed-reader/reducer and old-storage probe described below; frozen reports unchanged |

A crash probe initially exited before a small `FileStream` write had drained its managed buffer, correctly leaving no visible group. The final probe drains that buffer with `FlushAsync`, then exits before the service's durability flush/response; it tests the intended visible-prefix/full-group boundary. Tests do not claim to reproduce physical power loss.

The archive fixture also established that the current kernel permits new claims at Archive. Task 2 preserves that policy. Receipt recovery after archive does not depend on inventing a new archive prohibition.

## Validation results

- Solution/test build outside the checkout: zero warnings/errors.
- Focused findings suite: **86 passed, 0 failed, 0 skipped, 0 runner errors**.
- Full standard `dotnet test AILedger.sln --artifacts-path /tmp/ailedger-task2-standard-tests -m:1 -p:NuGetAudit=false --verbosity minimal`: **AILedger.Tests 1,535 passed / 241 failed; Memory 97 passed / 2 failed**. External-output test-host working directories reproduce the repository-location/provider-precondition failures documented in task 1. No findings test failed. This is a failed run, not a green suite.
- Full repository runner, `sh scripts/test-governed.sh all` with required test filesystem access: **AILedger.Tests 1,776 passed; Memory 99 passed; 0 failures, 0 skips, 0 runner errors**. This includes all 86 findings cases and the previously timing-sensitive test on this run.
- Initial sandboxed VSTest could not open its local IPC socket. An initial sandboxed repository-wrapper run stopped at Memory's application-data write test (98 passed / 1 dynamic-skip reported as failure by the custom runner). The authorized runs above/below used the access those existing tests require; no tests or expected outputs were changed to bypass these environment conditions.
- Frozen fixture checker: all **11 hashes unchanged**.
- Read-only baseline probe: all **four historical report cases and four provider-cost samples matched**. No fixture, measurement tool, expected output, or historical ledger was regenerated or edited.
- `git diff --check`: clean.

The task-1 timing-sensitive `ReconsiderationConsultantTests.R8_ConsultationRequiresARecordedSubjectRole` fixture remains unchanged. Its two independent `UtcNow` values still make a future timing-dependent failure possible; a passing run does not fix that pre-existing issue.

Reproduction (all builds remain outside the checkout):

```sh
sh scripts/test-governed.sh AILedger.Tests AILedger.Tests.Findings
sh scripts/test-governed.sh all
python3 tools/FindingsBaseline/verify-fixtures.py
dotnet build tools/FindingsBaseline/FindingsBaseline.csproj --artifacts-path /tmp/ailedger-task2-baseline -m:1 -p:NuGetAudit=false
dotnet /tmp/ailedger-task2-baseline/bin/FindingsBaseline/debug/FindingsBaseline.dll tests/Fixtures/structured-findings-v1
```

Temporary run diagnostics: `/tmp/ailedger-task2-focused.log`, `/tmp/ailedger-task2-standard-tests.log`, `/tmp/ailedger-task2-full-runner-authorized.log`. These paths may expire; this document records the results.

## Compatibility and remaining limitations

A separate probe compiled task-1 `Core`/`Storage` from `git archive a62053c` outside the worktree, then consumed two disposable logs written by task 2:

- Old `LedgerJson` deserialization plus `TaskReducer` accepted both singleton and multi-event findings groups, yielding versions 3 and 4.
- Old `FileGovernedTaskService` accepted the multi-event group, appended an ordinary CLI claim, and the new service still recovered the original version-4 receipt.
- Old `FileGovernedTaskService` rejected the singleton group because its marker reader requires count >= 2. New service retry still recovered that version-3 receipt.

Thus the oldest **tested typed reader/reducer** is task-1 source (`a62053c`, production equivalent to `b72437a`). The minimum supported **general storage reader/writer for findings-enabled logs is task 2**. Upgrade all storage writers/readers before enabling the endpoint. Do not advertise arbitrary old-binary mixed deployment or run an old writer's repair logic against new singleton groups. Additive JSON-field tolerance alone is insufficient.

Durability remains that of the existing local filesystem append, WriteThrough and disk-flush implementation. This is process-crash/retry recovery, not distributed transactions or a power-loss proof. Receipt consistency checks are not cryptographic authentication against a process that can rewrite ledger files. A fully rewritten history or removal of all identifying metadata cannot in general be distinguished from legitimate legacy history without another trusted record; that broader threat is outside this service contract.

Telemetry may be absent, truncated, or unwritable. A crash can omit the terminal row; success cannot be inferred from that absence. Shape/grant failures before locking return collection unavailable rather than taking another lock. Application `validation_ms` measures candidate command validation; `append_ms` measures the append call including preparation/tail handling/flush; missing phases stay null, including replay's absent candidate phase. Transport/provider/session observation and measurement joins remain tasks 3–5.

No MCP endpoint, provider adoption, YAML configuration, live provider trial, interface speedup claim, or workflow retirement is delivered here. Continue with the [task 3 handoff](../handoffs/structured-findings-task-3.md).
