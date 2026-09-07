# Execution Notes

- Contract created for Falcon partial page success behavior.
- Added optional Shared `PartialFlowSuccessContext<TRequest>` / `TryBuildPartialSuccessResult` wiring for terminal non-retryable flow failures.
- Wired Falcon to return partial DONE after at least one completed page, including `partialSuccess`, `collectedPages`, processed counters, error code/message, and exception type in the result payload.
- Added structured Falcon warning log with collected pages, processed items, processed findings, flow, and error code.
- Preserved retryable failure behavior and failures before any completed page.
- Fixed Falcon assets no-segmentation path to run one unsegmented pass instead of zero passes.
- Updated Falcon regression tests for assets and findings terminal failures after first page.
- Ran `dotnet test src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.FalconCollector.Test/Cymulate.Integration.Adapters.Collectors.FalconCollector.Test.csproj --no-restore`: passed 50/50; NU1900 vulnerability-source warnings occurred because CodeArtifact was unavailable from the sandbox.
- Verifier subagent reported no blocking issues; residual risk is limited to no shared-runner cross-collector regression test for future opt-in collectors.
- Updated future-facing documentation in collector architecture docs, Shared orchestration docs, and repo-local collector skills.
- No tests rerun for the documentation-only update; the previous Falcon targeted test run remains the code verification baseline.
