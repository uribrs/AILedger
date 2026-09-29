# Measurement continuity: task 1 inventory and frozen baseline

This is the baseline for [the findings interface](../structured-findings-v1.md), captured against source `b72437ad3a91a8dea0503d0010582ea676533566`. It preserves measurements and their limitations; it does not benchmark the unimplemented interface.

## Existing instruments and implementation owners

| Instrument / source | Preserve | Regression coverage / next owner |
|---|---|---|
| `Core/Application/TaskRetrospective.cs` | Deterministic task/run durations, per-field token coverage, epistemic/causal joins, build partitions, `notMeasured` | `Core/TaskRetrospectiveTests.cs`, `Cli/TaskRetrospectiveCliTests.cs`; task 2 checks frozen outputs, task 5 verifies full path |
| `Core/Application/CoordinatorMeasurement.cs` | Explicit session brackets, idle/dispatch intervals, inside-run filtering, reversal/rework evidence, unattributable cases | `Core/CoordinatorMeasurementTests.cs`; keep historical semantics, map new episode measures only in task 7 |
| `Core/Runs/Telemetry/RunCostReader.cs` | Provider-specific uncached/cache-write/cache-read buckets; real zero versus absent field; Codex turn count absent when unreported | `Runs/Telemetry/RunCostReaderTests.cs`, `Runs/Records/RunCostRecordTests.cs`, `Cli/RunCostLaunchTests.cs`; task 4 preserves capture, task 5 compares |
| `Cli/Providers/ProviderLauncher.cs`, `ProviderRunRecorder.cs` | Raw result sidecars, run outcome/reason/timeouts, model/session identity, first-write duration, manifest identity/count, truncation indicators | `Providers/ProviderEventTimingTests.cs`, `Providers/LaunchTimeoutTerminationTests.cs`, `Cli/RunManifestLaunchTests.cs`; task 2 preserves event correlation, task 4 preserves real launch inputs |
| `Cli/CoordinatorUsageReader.cs` | Explicit transcript source, provenance/session checks, named unreadability/absence; supported-harness limits | Existing retrospective CLI/core tests; no access to private transcript roots in this task's baseline |
| `Storage/RefusalJournal.cs`, `Core/Application/RefusalRuleKey.cs` | Original command/actor/reason/build boundary, recurrence semantics, unreadable rows, best-effort isolation | `Storage/RefusalJournalTests.cs`, `RefusalJournalConcurrencyTests.cs`, `RefusalRepetitionTests.cs`, `Cli/RefusalJournalLaunchTests.cs`, `Core/RefusalRuleKeyTests.cs`; task 2 preserves service recording |
| `Core/Application/TaskCloseoutEvidence.cs` | All assurance revisions and their producing run/provider/candidate links; completeness separated from verdict | `Artifacts/Retrospectives/TaskCloseoutEvidenceTests.cs`; task 5 keeps consumers working |
| `docs/self-scoring-rubric.md`, `Core/Artifacts/Logic/WorkflowRetrospectiveRules.cs` | Ten-dimension evaluation, evidence bindings, historical reports, distinctions between measured facts and agent judgment | `Artifacts/Retrospectives/WorkflowRetrospectiveArtifactTests.cs`, `WorkflowRetrospectiveReplayBoundaryTests.cs`, `Cli/WorkflowRetrospectiveCommandTests.cs`; no rubric change here |
| `Cli/Audit/AuditCliCommands.cs` | Existing old-workflow closeout diagnostics remain available | Preserve first-delivery behavior; do not apply old-stage completeness assumptions to later stage-free episodes |
| `Memory/Ingestion/EventHistorySourceReader.cs`, `RefusalJournalSourceReader.cs` | Rebuildable search over canonical observations, incremental identities/checkpoints, no authority added by indexing | `AILedger.Memory.Tests/Ingestion/EventHistoryIncrementalTests.cs`, `RefusalCheckpointTests.cs`; task 2 checks additive raw envelope metadata, task 5 checks new telemetry only if indexed |

Source paths above are relative to `src/` unless explicitly prefixed with `docs/`; test paths are relative to `tests/AILedger.Tests/` except the Memory suite. No global measurement abstraction or replacement report format is required.

## Frozen artifacts

Fixtures live at [tests/Fixtures/structured-findings-v1](../../tests/Fixtures/structured-findings-v1/README.md). `manifest.json` names their provenance and SHA-256 digests. Original logs were copied byte-for-byte from the original checkout, never modified or replayed through a mutating service. Expected reports were produced by existing pure reducers/projections using the small [FindingsBaseline](../../tools/FindingsBaseline/Program.cs) harness, not by the `ailedger` CLI.

| Case | Inputs | Expected distinction |
|---|---|---|
| `graph-original` | 47 real events; no refusal journal supplied | Five runs, three failed; two runs report any cost, three do not. Unreported turns/cost stay missing. No coordinating session is inferred. |
| `axonius-original` | 97 real events and four refusal rows | Six completed runs, both provider cost shapes, explicit coordinator session, four refusals, two assurance revisions. Cost coverage differs by field. |
| `axonius-refusals-unavailable` | Same exact 97-event log, no journal supplied | Refusal data becomes explicitly unmeasured; task truth is unchanged. This is a controlled input variant, not a different historical account. |
| `axonius-refusals-truncated` | Same log; original journal plus one deliberately malformed row | Four readable refusals plus one unreadable row; never report the readable subset as complete. |
| Provider samples | Existing test sample values for Claude and Codex, empty stream, malformed terminal JSON | Claude reports 125 turns; Codex has no reported turn count. Codex uncached input is 156,775; cache write is reported as zero by the reader. Missing terminal usage stays absent. |

These inputs cover a useful slice, not the entire measurement system. In particular they do not establish correctness of private coordinator-transcript ingestion, all avoidability branches, provider admission denials, future MCP capture, or crash recovery. Existing targeted tests cover additional branches; task 2 adds the failure matrix from the design, and tasks 4–6 supply live-provider measurement.

Expected output is a historical baseline, not an unquestionable oracle. If a pre-existing measurement defect is discovered, keep the captured output and record a deliberate, versioned expectation change with evidence. Do not recapture just to make a regression pass.

## Measurement contract across new boundaries

| Boundary | Captured by | Identity and outcome |
|---|---|---|
| Tool invocation or rejection before the application | MCP/provider adapter, tasks 3–4 | Attempt/request identity when available, provider/session identity from host, elapsed interval, actual permission/protocol boundary |
| Application admission / candidate validation | Findings recorder, task 2 | Attempt ID, scoped request key, actor/task/run binding, fingerprint, item path, original kernel refusal; no false claim of a commit |
| Canonical append | Existing storage boundary, task 2 | Receipt transaction and event IDs, committed version/timestamp, all-or-nothing group visibility |
| Replay of an already committed request | Findings recorder, task 2 | New attempt, original receipt/transaction, no new domain events or duplicated run cost |
| Provider run closes | Existing launcher, preserved in task 4 | Original usage, outcome/reason, manifest, run correlation and first-write observation |
| Reports over all of the above | Existing readers plus small additions, task 5 | Separate nested elapsed intervals, attempts, commits, events and model turns; coverage and missing data remain visible |

Run correlation must remain in each `LedgerEvent.CorrelationId`. Batch correlation belongs in the new receipt/attempt metadata. Counting granular events as tool interactions would make batching look as expensive as the CLI; counting only successful tool calls would hide retries and denials. Provider spend must not be multiplied by either count. Refusal count alone is not a usefulness verdict: task 6 inspects the triggering action, prevented consequence where evidenced, recovery time, and whether content was lost or altered.

Existing `RefusalJournal` is not canonical state. New attempt telemetry must likewise remain observational and best-effort. A dropped write can leave a collection gap; successful canonical commit is proven by its receipt, not by a terminal telemetry line. Do not make tool responsiveness or findings durability depend on a metrics writer.

## Reproduce

From the worktree root:

```sh
python3 tools/FindingsBaseline/verify-fixtures.py
dotnet build tools/FindingsBaseline/FindingsBaseline.csproj --artifacts-path /tmp/ailedger-findings-baseline -m:1 -p:NuGetAudit=false
dotnet /tmp/ailedger-findings-baseline/bin/FindingsBaseline/debug/FindingsBaseline.dll tests/Fixtures/structured-findings-v1
```

The harness compares JSON semantically so member formatting/order does not create false differences. It does not implement torn-event handling; its inputs are complete frozen histories and the real storage tests own crash semantics. `--capture` is an explicit baseline-authoring option, not the verification procedure.

Run `dotnet test` for implementation work. If external build artifacts cause the test host to use `/tmp` as its current directory, repository-location and provider-precondition tests fail before exercising their target behavior. Use the existing `sh scripts/test-governed.sh all` runner to retain the checkout context; despite its name it is a .NET build/xUnit wrapper, not a governed task launcher. See task 1's validation record for actual results.
