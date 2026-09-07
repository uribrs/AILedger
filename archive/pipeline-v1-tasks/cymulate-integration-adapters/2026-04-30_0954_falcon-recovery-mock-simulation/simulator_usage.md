# Falcon Recovery Mock Simulator Usage

This simulator runs the real `FalconCollector` through `LocalAdapterRunner` while pointing it at a local CrowdStrike-shaped mock HTTP server. It is meant for recovery behavior only: token, probes, Spotlight pagination, checkpoint capture, resume, and local published batch files.

## Code Entry Points

- CLI option: `--falcon-recovery-simulation <scenario>`
- Runner wiring: `src/Cymulate.Integration.Adapters/Tools/Cymulate.Integration.Adapters.Tools.LocalAdapterRunner/CollectorRunner.cs`
- Mock simulator: `src/Cymulate.Integration.Adapters/Tools/Cymulate.Integration.Adapters.Tools.LocalAdapterRunner/FalconRecoverySimulationRunner.cs`

## Build

From repo root:

```bash
dotnet build src/Cymulate.Integration.Adapters/Tools/Cymulate.Integration.Adapters.Tools.LocalAdapterRunner/Cymulate.Integration.Adapters.Tools.LocalAdapterRunner.csproj
```

## Run All Scenarios

After build, run the DLL directly:

```bash
dotnet src/Cymulate.Integration.Adapters/Tools/Cymulate.Integration.Adapters.Tools.LocalAdapterRunner/bin/Debug/net8.0/Cymulate.Integration.Adapters.Tools.LocalAdapterRunner.dll \
  --falcon-recovery-simulation all \
  --output-dir logs/falcon-recovery-mock \
  --timeout-minutes 5 \
  --dump-payload
```

The simulator binds a loopback HTTP server. If the local sandbox denies loopback binding, rerun with approval/escalation.

## Scenarios

`same-month-after`

Fails after checkpointing April page 1 with the saved April cursor, then resumes from that cursor and continues through March and February.

`cursor-expired-fallback`

Fails after checkpointing April page 1. On resume, the mock returns 404 for the saved April cursor, then verifies that recovery falls back and still completes without republishing the first April page.

`month-boundary-after`

Completes April, fails after checkpointing March page 1 with the saved March cursor, then resumes from that cursor and continues through February.

`repeated-after-fallback`

Fails after checkpointing April page 1. On resume, the mock returns a non-empty page and repeats the same April cursor token. The collector should publish that page once, drop the unsafe cursor, switch to watermark fallback, and continue without looping.

## Outputs

Default output root:

```text
logs/falcon-recovery-mock/
```

Each scenario writes:

```text
logs/falcon-recovery-mock/<scenario>/checkpoint-after-attempt-1.json
logs/falcon-recovery-mock/<scenario>/summary.json
logs/falcon-recovery-mock/<scenario>/attempt-1/collector-run/findings_*.json
logs/falcon-recovery-mock/<scenario>/attempt-2/collector-run/findings_*.json
```

Local runner logs are written beside other runner logs:

```text
logs/local-adapter-runner-<timestamp>.log
```

## What To Inspect

Resume log line:

```text
Resuming Falcon findings collection from checkpoint
```

Checkpoint snapshots:

```text
Falcon recovery simulation checkpoint
```

Scenario completion:

```text
Falcon recovery simulation scenario completed
Falcon recovery simulation completed
```

Published batches:

```bash
rg -n '"id"|"aid"|"updated_timestamp"' logs/falcon-recovery-mock
```

Cursor fallback evidence:

```bash
rg -n 'FalconCursorExpiredException|mock expired Spotlight cursor|repeated_after_token' logs/local-adapter-runner-*.log
```

Month resume evidence:

```bash
rg -n 'MonthSegmentStartUtc|MonthSegmentEndExclusiveUtc|CursorSortTimestampUtc' logs/local-adapter-runner-*.log
```

## Expected High-Level Result

- All scenarios should complete with `InitialSuccess=true` and `ResumeSuccess=true`.
- `same-month-after` should publish April page 1 in attempt 1 and only `v-apr-003` for April in attempt 2.
- `cursor-expired-fallback` should show a simulated 404 for the saved April cursor, then still publish only the remaining April item.
- `month-boundary-after` should resume March using the saved March cursor, not restart April.
- `repeated-after-fallback` should show the repeated cursor guard, then continue with watermark fallback and publish `v-apr-004` without repeating `v-apr-003`.
- Mock cursors are base64 JSON with an `s[0]` epoch timestamp, so runner logs should include non-null `CursorSortTimestampUtc` values when paging with a cursor.

## Current Limitation

The open/current-month segment ceiling is produced from `DateTime.UtcNow`. In a very fast local run, the resumed request can happen inside the same logged second as the original request, so the active-ceiling drift bug may only be visible at sub-second precision in checkpoint state. To make that bug deterministic, add either an injected clock or a deliberate delay between attempt 1 and attempt 2.
