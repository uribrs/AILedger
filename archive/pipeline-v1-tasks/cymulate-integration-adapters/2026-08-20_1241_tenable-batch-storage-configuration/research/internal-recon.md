# Internal recon — TenableIo batch-scoped storage configuration

## Durable sources read

- `CLAUDE.md` — repository architecture, collector configuration conventions, build/test commands, and the Infra ownership boundary.
- Task artifacts: `task.md`, `prompt_contract.md`, `constraints.md`, `assumptions.md`, `decisions.md`, and `state.json`.
- `src/Cymulate.Integration.Adapters/Collectors/TenableIoCollector/Processing/Configuration/TenableIoCollectorConfiguration.cs` — the single TenableIo settings record and its defaults.
- `src/Cymulate.Integration.Adapters/Collectors/TenableIoCollector/Processing/Configuration/TenableIoCollectorConfigurationBuilder.cs` — flat dictionary to configuration/session construction.
- `src/Cymulate.Integration.Adapters/Collectors/TenableIoCollector/Processing/Triggers/TenableIoCollectorConfigurationMapper.cs` — platform event/credentials/metadata to flat configuration dictionary.
- `src/Cymulate.Integration.Adapters/Collectors/TenableIoCollector/TenableIoCollector.cs` — collector configuration storage and correlated-flow construction.
- `src/Cymulate.Integration.Adapters/Collectors/TenableIoCollector/Flows/Findings/Correlated/TenableIoCorrelatedFindingsFlow.cs` — correlated-flow composition and the sole production publisher construction site.
- `src/Cymulate.Integration.Adapters/Collectors/TenableIoCollector/Flows/Findings/Correlated/TenableIoCorrelatedBatchPublisher.cs` — emitter ownership and the current hard-coded `batchScopedStorage: false` argument.
- `src/Cymulate.Integration.Adapters/Collectors/TenableIoCollector/Documentation/04-architecture.md` and `Documentation/05-testing.md` — collector-specific configuration map and focused test guidance. These documents contain some stale pre-correlated-flow descriptions, so current source is authoritative where they differ.
- `src/Cymulate.Integration.Adapters/Collectors/QualysCollector/Processing/Configuration/QualysCollectorConfiguration.cs` and `Flows/Findings/QualysFindingsFlow.cs` — nearest emitter-based pattern: configuration owns `BatchScopedStorage`; flow forwards the boolean to its publisher.
- `src/Cymulate.Integration.Adapters/Collectors/InsightVmCloudCollector/Processing/Configuration/InsightVmCloudCollectorConfiguration.cs` and `Processing/InsightVmCloudCollectorFlowRunner.cs` — second pattern: configuration owns the flag and composition forwards it.
- `src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.TenableIoCollector.Test/TenableIoCollectorConfigurationBuilderTests.cs` — focused configuration construction tests.
- `src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.TenableIoCollector.Test/TenableIoCorrelatedFlowTests.cs` — end-to-end correlated-flow harness, publish capture, and existing batch-folder assertion.
- `src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.TenableIoCollector.Test/TenableIoCorrelatedScaleAndRetryTests.cs` and `TenableIoAssetSpineTests.cs` — additional direct configuration/flow construction sites checked for signature impact.
- Current `git diff` and `git status` — establishes the three pre-existing edits that must be preserved.

## Files in scope

Production cluster:

1. `src/Cymulate.Integration.Adapters/Collectors/TenableIoCollector/Processing/Configuration/TenableIoCollectorConfiguration.cs`
   - Add `BatchScopedStorage` with initializer `false`.
2. `src/Cymulate.Integration.Adapters/Collectors/TenableIoCollector/Flows/Findings/Correlated/TenableIoCorrelatedFindingsFlow.cs`
   - Forward `_config.BatchScopedStorage` at the one production publisher construction site.
   - Make the existing unconditional batch-scoping remarks conditional if touched; their current wording assumes scoping is always enabled.
3. `src/Cymulate.Integration.Adapters/Collectors/TenableIoCollector/Flows/Findings/Correlated/TenableIoCorrelatedBatchPublisher.cs`
   - Accept the boolean and pass it to `NdjsonBatchEmitter.Create` instead of a literal.
   - Update the class XML/comment that currently embeds `batchScopedStorage: true` and says the class never names `BatchScopedStorage`; that text is already stale relative to the worktree's literal `false`.

Focused test cluster:

4. `src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.TenableIoCollector.Test/TenableIoCollectorConfigurationBuilderTests.cs`
   - Assert the built/default configuration has `BatchScopedStorage == false` (or add an equivalent direct-default test).
5. `src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.TenableIoCollector.Test/TenableIoCorrelatedFlowTests.cs`
   - Let the harness supply a configuration override.
   - Run the existing scoped-folder assertion with `BatchScopedStorage = true`; this proves configuration-to-flow-to-publisher-to-emitter propagation.
   - The remaining default-config paths continue to exercise flat storage. A focused flat-path assertion is useful if the implementation wants an explicit runtime proof in addition to the default-value assertion.

No builder/trigger change is required for the narrow request. Both comparison collectors expose this flag as a configuration-owned capability/default and do not bind it from their flat credential dictionary. If runtime platform metadata must toggle TenableIo's flag, `TenableIoCollectorConfigurationBuilder` would need `BatchScopedStorage = GetBool(dict, "batchScopedStorage", false)`, but that is a broader interpretation than “like the others” and is not needed to remove the hard-coded publisher literal.

## Patterns to mirror

- Mirror `QualysFindingsFlow`: the flow receives the collector configuration, extracts `configuration.BatchScopedStorage`, and gives a plain boolean to the publisher. The publisher should not own or depend on the whole collector configuration record.
- Keep `NdjsonBatchEmitter.Create(context.Services, batchScopedStorage)` inside `TenableIoCorrelatedBatchPublisher`; emitter lifecycle ownership stays where it is.
- Prefer an optional final publisher constructor parameter defaulted to `false`, e.g. after `eventSink`. This keeps the direct test construction in `CreateVulnPhase` source-compatible while the production flow explicitly forwards `_config.BatchScopedStorage`.
- Use the existing `TenableIoCorrelatedFlowTests.PublishCapture`: it records both target path and the live `storageUrl`, so a configured-true flow can prove actual emitter propagation without vendor connectivity or new test doubles.
- Use record `with` syntax in the scoped test (`CreateConfig() with { BatchScopedStorage = true }`) rather than creating a second large configuration fixture.

Construction/binding map:

`PlatformEvent` → `TenableIoCollectorConfigurationMapper.BuildConfiguration` (flat dictionary) → `TenableIoCollector.SetConfiguration` → `TenableIoCollectorConfigurationBuilder.Build` → collector `_configuration` → `TenableIoCollector.CollectFindingsInternalAsync` → `new TenableIoCorrelatedFindingsFlow(..., _configuration, ...)` → flow `_config` → `new TenableIoCorrelatedBatchPublisher(..., _config.BatchScopedStorage)` → `NdjsonBatchEmitter.Create(..., batchScopedStorage)`.

Publisher call sites:

- Production: `TenableIoCorrelatedFindingsFlow.CollectAsync` (one site).
- Direct test construction: `TenableIoCorrelatedFlowTests.CreateVulnPhase` (one site).
- No other publisher construction sites were found.

## Shared surface to freeze

- Do not edit the existing InsightVM Cloud and Qualys boolean flips:
  - `InsightVmCloudCollectorConfiguration.BatchScopedStorage: true → false`
  - `QualysCollectorConfiguration.BatchScopedStorage: true → false`
- Do not change `NdjsonBatchEmitter`, `BatchScopedStorage`, Infra emission behavior, target-path builders, or object-store services.
- Do not change TenableIo storage layout, `AdapterProgressContext` metadata rules, staging spine paths, checkpoints, page numbering, retry behavior, resume behavior, or cleanup.
- Keep `TenableIoCorrelatedFindingsFlow`'s public/internal construction contract intact except for forwarding the already-owned configuration value internally.
- Keep all changes within TenableIo source/tests plus this task artifact.

## Disjoint sets available

- Production and tests are **one tight overlapping cluster**, not safely separable for concurrent edits. The production signature and test harness/call sites overlap directly, and the existing scoped-storage test must change in lockstep with the default behavior.
- Within the production cluster, the configuration record and publisher are mechanically distinct, but splitting them adds coordination cost for a three-file seam and risks transient compilation failures. One executor should own the whole TenableIo implementation/test set.
- Independent verification and isolated review remain naturally disjoint after implementation.

## Landmines

- The current worktree changes TenableIo's literal from `true` to `false`, but `Flow_PublishesOneAtomicObjectPerChunk_ScopedToItsOwnBatchFolder` still assumes the default is scoped and expects `/batch_000001` and `/batch_000002`. It must explicitly opt into `true` when proving propagation; otherwise it is expected to fail.
- The publisher XML documentation still spells `batchScopedStorage: true`; merely replacing the code literal would leave contradictory documentation.
- `TenableIoCorrelatedFindingsFlow.CorrelateAndSweepAsync` remarks say the emitter scopes every page unconditionally. With a default-false configuration that statement is only true when enabled.
- A configuration default test alone does not prove wiring. Conversely, a publisher constructor test alone does not prove the collector configuration reaches it. The configured-true correlated-flow assertion is the valuable propagation gate.
- Adding dictionary binding would create a new operator-controlled behavior surface and differ from the current InsightVM/Qualys pattern. Do not add it unless the parent task explicitly interprets “configuration” as runtime metadata configurability.
- Do not “fix” other stale TenableIo documentation in this task. The collector docs predate recent correlated-flow work and broad cleanup would violate the narrow contract.
- The full test suite may require a valid CodeArtifact token if restore is needed. Use `--no-restore` first against the already-restored worktree; a 401/403 is feed authentication, not a source defect.

Exact focused verification commands from repository root:

```bash
dotnet test src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.TenableIoCollector.Test/Cymulate.Integration.Adapters.Collectors.TenableIoCollector.Test.csproj --no-restore --filter "FullyQualifiedName~TenableIoCollectorConfigurationBuilderTests|FullyQualifiedName~Flow_PublishesOneAtomicObjectPerChunk_ScopedToItsOwnBatchFolder"

dotnet test src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.TenableIoCollector.Test/Cymulate.Integration.Adapters.Collectors.TenableIoCollector.Test.csproj --no-restore

dotnet build src/Cymulate.Integration.Adapters/Cymulate.Integration.Adapters.sln --no-restore

git diff --check
```

After implementation, also scan the TenableIo production cluster to ensure no emitter literal remains:

```bash
rg -n "batchScopedStorage:\s*(true|false)|BatchScopedStorage" src/Cymulate.Integration.Adapters/Collectors/TenableIoCollector
```
