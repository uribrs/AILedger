# Execution Notes

- Contract created for production Falcon cursor recovery fixes and observability work.
- Requested skills used: prompt-contract-designer, contract-driven-execution, task-orchestrator.
- Fixed resumed month segmentation so the first resumed segment uses checkpointed `MonthSegmentStartUtc` and `MonthSegmentEndExclusiveUtc` exactly. This prevents changing the filter ceiling while reusing a saved CrowdStrike `after` cursor.
- Added a repeated non-empty `after` guard in `FalconSpotlightVulnerabilitiesRunner`. When the response repeats the requested cursor after publishing a non-empty page, the runner logs `Reason=repeated_after_token`, drops the cursor, rebuilds the query from the current watermark and boundary IDs, and continues.
- Extended existing cursor rejection fallback behavior to share the same structured watermark reset logging.
- Added page-level visibility logs with segment bounds, current floor, watermark, cursor presence flags, best-effort cursor sort timestamps, raw/unique counts, segment progress percent, and day progress percent.
- Added best-effort cursor timestamp decoding from base64/base64url JSON `s[0]`. This is telemetry only; correctness still relies on checkpointed segment bounds, cursors, watermarks, and boundary IDs.
- Extended the local Falcon recovery simulator with `repeated-after-fallback` and CrowdStrike-shaped mock cursors that expose sort timestamps for observability validation.
- Updated simulator usage instructions in `ai/active/2026-04-30_0954_falcon-recovery-mock-simulation/simulator_usage.md`.

## Validation

- `dotnet test src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.FalconCollector.Test/Cymulate.Integration.Adapters.Collectors.FalconCollector.Test.csproj --no-restore`
  - Passed: 51/51.
- `dotnet build src/Cymulate.Integration.Adapters/Tools/Cymulate.Integration.Adapters.Tools.LocalAdapterRunner/Cymulate.Integration.Adapters.Tools.LocalAdapterRunner.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false`
  - Passed: 0 warnings, 0 errors.
- `dotnet src/Cymulate.Integration.Adapters/Tools/Cymulate.Integration.Adapters.Tools.LocalAdapterRunner/bin/Debug/net8.0/Cymulate.Integration.Adapters.Tools.LocalAdapterRunner.dll --falcon-recovery-simulation all --output-dir logs/falcon-recovery-mock-cursor-fixes --timeout-minutes 5 --dump-payload`
  - Passed: 4/4 scenarios (`same-month-after`, `cursor-expired-fallback`, `month-boundary-after`, `repeated-after-fallback`).
- `git diff --check`
  - Passed.

## Reference Artifacts

- Final simulator log: `logs/local-adapter-runner-20260430-104727.log`.
- Final simulator output: `logs/falcon-recovery-mock-cursor-fixes`.
- Repeated cursor evidence:
  - Log line 561: `Reason=repeated_after_token`, `CurrentFloorUtc=2026-04-11T00:00:00Z`.
  - Log line 562: repeated requested/response cursor timestamps both decode to `2026-04-11T00:00:00Z`.
  - Log line 564: fallback request uses `updated_timestamp:>='2026-04-11T00:00:00Z'`.
  - Log line 568: only `v-apr-004` is published after fallback, proving boundary dedupe skipped the already-published `v-apr-003`.
