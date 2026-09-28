# Task 3 validation: local findings MCP endpoint

Date: 2026-09-28. Worktree: `/Users/user/.codex/worktrees/structured-findings-contract/AILedger`. Branch: `codex/structured-findings-contract`. Implementation base: task 2, `00270fb`.

## Delivered boundary

The CLI now has a local stdio entry point, `findings serve <absolute-host-configuration-path>`. Its one MCP tool calls `IFindingsRecorder.RecordAsync` once per invocation. Production changes are confined to `Cli/Findings`, the early stdio dispatch in `Program.cs`, and embedding the unchanged frozen schemas in the CLI project. No Core, Storage, Providers, kernel command/replay policy, existing measurement implementation, test harness, or frozen fixture was changed.

The adapter validates raw UTF-8 JSON and retained duplicate members before building a typed request, applies all v1 shape/limit checks, obtains binding only from host configuration, and explicitly writes the frozen response envelope. Host grants are reread per invocation; attribution/destination are pinned per connection. The local host requires explicit grants and a safe resolved workspace/task path, while task 2 independently enforces kernel and run authority. Configuration and provider protection remain host responsibilities for task 4; this is not provider adoption.

Transport journals include pre-application rejections, application-attempt joins, returned receipt/event joins, separate response delivery, nullable observations and nested timing. They never include evidence prose or provider usage and never govern canonical truth. Diagnostic failure falls back to structured stderr without affecting the MCP response. See the [endpoint contract](task-3-endpoint.md) for composition, lifecycle, limits, and measurement semantics.

Development used the user's local workflow exception: no governed development task, governance ledger writes, agent dispatch, global tool installation, merge, or publication. Tests exercised only disposable ledgers and the existing provider fixtures. No live provider episode was run.

## Acceptance evidence

The focused findings suite passes **168 cases: the unchanged 86 task-2 cases plus 82 task-3 cases**. New endpoint tests use real handlers/reducers, storage and receipt replay. Several launch the actual built CLI in a separate OS process and communicate using newline-framed stdin/stdout; deterministic output/read/flush faults use the same server with injected streams or task 2's existing fault seam.

| Area | Evidence |
|---|---|
| Real protocol/discovery/response schema | `FindingsMcpEndToEndTests.RealStdioDiscoversFrozenSchemasAndPreservesHostileTextAndReceiptOnRestart`; actual handshake, discovery and tool call, embedded frozen schemas, response/text semantic equivalence, strict conformance interpreter over every frozen response assertion |
| Atomic prose and references | Same subprocess test: quotes, backticks, `$()`/pipes, Hebrew, emoji, multiline prose, local supports and existing refutes; claims remain open and text follows existing trim semantics |
| Strict pre-deserialization validation | `FindingsMcpParsingTests`: duplicate root/item/reference/escaped-name members, unknown/cased fields, actor/task/run/correlation/causation/root/grant/status spoofing, nulls/missing members, malformed surrogates and raw UTF-8, invalid numbers and JSON |
| Boundaries and limits | Same parsing suite plus `FindingsMcpBoundaryTests`: body/normalized size, exact 256 KiB acceptance, array/per-direction/aggregate references, scalar text bound, bounded frame recovery, eight in-flight calls, exact version numeric equivalence without rounding |
| Host versus kernel authority | Real process revokes the host grant on a live connection, then revokes kernel evidence capability; protected retries reveal no receipt and fresh refused batches leave no prefix. Pinned-root/actor/run/correlation/cause changes and missing host configuration deny before application entry |
| Untrusted MCP metadata | `_meta` cannot change author/grants; notifications cannot invoke a mutation; handshake required. Startup rejects oversized/duplicate configuration; default/invalid grants and run bindings remain denied |
| Concurrent retries | Two actual CLI processes with four in-flight calls each: eight attempts, one committed transaction, one pair of canonical events, complete JSON response frames |
| Lost responses/cancellation | Deterministic output failure and cancellation after real append, then actual CLI reconnect/retry: original receipt/transaction recovered, canonical log byte-identical. Lock-wait cancellation remains responsive and safe to retry |
| Error distinctions | Frozen envelopes validated for invalid request/reference, host denial, kernel refusal, missing task, capacity, corrupt history, pre-append storage failure, uncertain flush and committed-key attribution conflict |
| Observation failures and joins | Input failure before a complete frame, parser/protocol denials, transport journal failure with stderr fallback, absent application fields, application-attempt IDs matching task-2 journal, nested timings, no prose/cost duplication |
| Attribution and measurement | Original researcher/run correlation retained after run completion; new-run key reuse conflicts; original 19 output/31 input token observations and missing turns remain unchanged |
| Task-2 regression guarantees | All 86 existing contract/authority/atomicity/concurrency/crash/receipt/storage-history cases remain unchanged and pass |

Tests compare decoded structured/text JSON because JSON writers can escape `+` in an offset timestamp differently. An initial assertion compared raw spellings and failed 16 success-path cases even though values matched; it was corrected to semantic comparison. An initial test compile also attempted to use Core's internal request writer; the fixture now independently serializes the public request shape. Neither correction changed a frozen schema, fixture, baseline or production response contract.

## Validation results

- Solution and focused test builds outside the checkout: **zero warnings/errors** after the fixture compile correction.
- Focused repository-context findings suite: **168 passed, 0 failed, 0 skipped, 0 runner errors**.
- Required standard `dotnet test`: **main 1,617 passed / 241 failed; Memory 97 passed / 2 failed**. This is a failed run. The failures reproduce task 2's external-output repository-location/provider-precondition conditions. The extracted failure-name sets match the still-available task-2 standard-test log with no additions/removals; no findings case failed.
- Repository-aware full runner: **main 1,858 passed; Memory 99 passed; 0 failures, 0 skips, 0 runner errors**.
- Frozen fixture checker: **all 11 SHA-256 hashes unchanged**, verified before production edits and after implementation.
- Read-only baseline probe: **all four historical report cases and all four provider-cost samples matched**.
- Scope comparison: Core, Storage, Providers, the measurement tools, frozen schemas/fixtures, and `scripts/test-governed.sh` have no changes.
- `git diff --check`: clean.

The existing `ReconsiderationConsultantTests.R8_ConsultationRequiresARecordedSubjectRole` timing-sensitive fixture is unchanged. It passed on both final runs; this does not remove its previously documented race between independent clock reads.

Reproduction (output paths are disposable and must remain outside the checkout):

```sh
dotnet test AILedger.sln --artifacts-path /tmp/ailedger-task3-standard-tests -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false --verbosity minimal
UseSharedCompilation=false sh scripts/test-governed.sh all
sh scripts/test-governed.sh AILedger.Tests AILedger.Tests.Findings
python3 tools/FindingsBaseline/verify-fixtures.py
dotnet build tools/FindingsBaseline/FindingsBaseline.csproj --artifacts-path /tmp/ailedger-task3-baseline -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false
dotnet /tmp/ailedger-task3-baseline/bin/FindingsBaseline/debug/FindingsBaseline.dll tests/Fixtures/structured-findings-v1
```

The full standard/repository runs used authorized local IPC/application-data fixture access. `UseSharedCompilation=false` avoids compiler-server startup delays in the restricted environment; it changes no source/test semantics. The focused run used the same existing repository-context xUnit runner directly with `/tmp/ailedger-task3-build` artifacts after an incremental build. The repository wrapper itself remains unchanged.

Temporary logs: `/tmp/ailedger-task3-focused-direct.log`, `/tmp/ailedger-task3-focused-build.log`, `/tmp/ailedger-task3-standard-tests.log`, `/tmp/ailedger-task3-full-runner.log`. The wrapper's detailed suite/build logs are under `/private/var/folders/v3/pncbbv7x45jgp4bnd81fjnl80000gn/T/ailedger-tests.hc1oHK`. These paths may expire; this record preserves the results.

## Remaining limits

- Provider registration, real tool permissions, host configuration protection/provisioning and live Claude/Codex episodes are **task 4**, not completed here. No SDK interoperability matrix beyond the documented local protocol subset was run. No workflow speedup is claimed.
- Task 5 must connect transport observations to reports. Missing provider identity, application observations, terminal rows or journal writes stay missing; a local response flush is not client acknowledgement.
- The stdio host is a trusted local process, not a sandbox against a process that can rewrite its configuration or ledger. A changed binding cannot retrieve another actor's receipt; fresh-run recovery under the old key conflicts.
- Task 2's durability and compatibility limits remain: process-crash recovery rather than power-loss/distributed proof; **task 2 or newer general storage binaries** before enabling findings logs, because task-1 storage rejects singleton markers.
- The standard VSTest invocation remains red under external artifact paths for the documented existing repository-discovery failures. The timing-sensitive pre-existing fixture is not fixed by this scoped change.

Continue with the [task-4 handoff](../handoffs/structured-findings-task-4.md). Tasks 4–8 remain unimplemented.
