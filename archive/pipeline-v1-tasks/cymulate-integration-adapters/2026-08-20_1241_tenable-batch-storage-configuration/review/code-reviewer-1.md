# Code Review 1

## Verdict

Approved. No blocker, major, minor, or nit findings.

## Calibration

- Change type: collector configuration and production flow propagation with tests.
- Risk: low-to-medium. The change selects an existing emitter storage-scoping mode and does not modify persistence, recovery, retry, or concurrency implementations.

## Review Notes

- `TenableIoCollectorConfiguration.BatchScopedStorage` defaults to `false`, preserving the flat target-path behavior.
- `TenableIoCorrelatedFindingsFlow` passes the configuration value directly to the publisher, and the publisher passes it directly to `NdjsonBatchEmitter.Create`.
- The optional publisher constructor argument also defaults to `false`, preserving existing direct construction behavior.
- Tests cover the default value, production-level flat paths, resume numbering under the default, and explicit batch-scoped paths/storage URLs when enabled.
- The accepted absence of flat-dictionary binding was not raised as a finding.

## Validation Evidence

Focused test run:

```text
dotnet test src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.TenableIoCollector.Test/Cymulate.Integration.Adapters.Collectors.TenableIoCollector.Test.csproj --no-restore --filter "FullyQualifiedName~TenableIoCollectorConfigurationBuilderTests|FullyQualifiedName~TenableIoCorrelatedFlowTests|FullyQualifiedName~TenableIoCollectorTests"
Passed: 43, Failed: 0, Skipped: 0
```
