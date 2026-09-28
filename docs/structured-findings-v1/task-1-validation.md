# Task 1 validation record

Date: 2026-09-28. Source baseline: `b72437ad3a91a8dea0503d0010582ea676533566`. Worktree branch: `codex/structured-findings-contract`.

Task 1 changes documentation, contract schemas/examples, frozen fixtures, and the read-only baseline harness. There are no changes under `src/` or to existing test implementations. No production ledger, installed tool, provider configuration, kernel skill, or global instruction was changed. This was source-grounded design and local verification by one agent, not an independent multi-agent review.

## Passing checks

- Built `tools/FindingsBaseline/FindingsBaseline.csproj` with SDK 10.0.301 targeting .NET 8: zero warnings/errors. Build artifacts went to `/tmp/ailedger-task1-build`.
- Captured and then independently reran four report cases and four provider-cost samples; all frozen outputs matched existing reducers/projections. The source logs contain 144 events total, reused across the journal-availability variants.
- Verified all 11 frozen data-file SHA-256 hashes. Checked the three historical source files against their original checkout bytes when finalizing the fixture manifest.
- Independently checked five graph runs / three unmeasured costs, four Axonius refusals, two assurance revisions, and one unreadable row in the synthetic truncated-journal case.
- Changed an expected cost count in a disposable copy: verification failed with `Baseline changed`, confirming the probe detects a semantic difference. No frozen fixture was edited by that check.
- Validated both JSON Schemas under Draft 2020-12 and the request/success/error examples, including date-time format. Checked the ASCII canonical fingerprint example against its stated SHA-256. Future task 2 must still implement and verify the canonical writer, including Unicode and null/default equivalence vectors.
- Checked new document links, task numbering, Markdown fences, JSON parseability, and Git whitespace. The harness uses the existing measurement implementations; it does not independently establish that every existing metric is correct.

## Existing test-suite baseline

The standard `dotnet test AILedger.sln --artifacts-path /tmp/ailedger-task1-tests -m:1 -p:NuGetAudit=false --verbosity minimal` run failed: core/CLI 1,448 passed / 242 failed; Memory 97 passed / 2 failed. The external-output test host selected `/tmp/.../debug` as its current directory. Observed failures included inability to locate the repository and Codex adapter repository preconditions. Do not present this run as a passing suite or interpret its failure count as new interface defects.

Reran using the repository's existing `sh scripts/test-governed.sh all` in the worktree. This is the in-process xUnit runner, not a kernel task launcher:

| Suite | Passed | Failed | Skipped / runner errors |
|---|---:|---:|---:|
| AILedger.Memory.Tests | 99 | 0 | 0 / 0 |
| AILedger.Tests | 1,689 | 1 | 0 / 0 |

The remaining failure was `ReconsiderationConsultantTests.R8_ConsultationRequiresARecordedSubjectRole`. Its unchanged fixture constructs `LedgerEvent.RecordedAt` and `AgentRun.StartedAt` with two separate `DateTimeOffset.UtcNow` calls (lines 100 and 104); `RunEventValidator` requires exact equality. The full run failed at replay before reaching the consultation behavior. A subsequent isolated rerun passed, consistent with a timing-sensitive existing fixture. No fixture or rule was changed to obtain a green result. Task 2 should track this known baseline condition separately from its own failures; the full suite was **not completely green**.

Temporary diagnostics from this session:

- `/tmp/ailedger-task1-tests.log`: standard test run.
- `/tmp/ailedger-task1-fallback.log`: existing-runner invocation summary.
- `/private/var/folders/v3/pncbbv7x45jgp4bnd81fjnl80000gn/T/ailedger-tests.s4AR99/AILedger.Tests.log`: full in-process core/CLI log.
- `/tmp/ailedger-task1-known-failure.log`: isolated passing rerun.

These temporary paths may expire. The observed results and source-level limitation above are the durable handoff; they are not substitutes for rerunning tests after task 2 changes code.

## Remaining implementation obligations

There is no executing `record_findings` operation yet. Atomic retry receipt behavior, typed authority binding, new attempt telemetry, fault injection, same-key concurrency and metadata corruption handling remain task 2 work. MCP and provider behavior remain tasks 3–4. The baseline does not prove shell-friction reduction, refusal usefulness, or better cognition; those are task 6 and later experiment questions.
