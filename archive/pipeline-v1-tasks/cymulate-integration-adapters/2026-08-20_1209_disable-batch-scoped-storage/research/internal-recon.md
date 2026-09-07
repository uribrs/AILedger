# Durable sources read

- Task contract/state: `task.md`, `prompt_contract.md`, `assumptions.md`, `decisions.md`, `constraints.md`, and `state.json` under this task directory.
- Repository guidance: `CLAUDE.md`.
- Collector guidance: `ai/skills/collector-execution-and-recovery/SKILL.md`, `ai/skills/collector-flow-patterns/SKILL.md`, `ai/skills/collector-tests/SKILL.md`, and `ai/skills/collector-review/SKILL.md`.
- Nearest production/test exemplars: the three identified production opt-ins, InsightVM Cloud and Qualys batch-scoped-storage component tests, Tenable.io correlated flow/collector tests, and Falcon's existing default-off configuration/tests found by the production/source scan.

# Files in scope

- InsightVM Cloud production: `src/Cymulate.Integration.Adapters/Collectors/InsightVmCloudCollector/Processing/Configuration/InsightVmCloudCollectorConfiguration.cs` — change the capability default from `true` to `false` and rewrite its XML comment to describe flat run-root storage as the production default.
- InsightVM Cloud tests: `src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.InsightVmCloudCollector.Test/InsightVmCloudBatchScopedStorageTests.cs` — rename/rewrite the configuration-default assertion to expect `false`; retain the explicit `true` publisher tests as shared-capability coverage and retain the existing explicit flat-layout test.
- Qualys production: `src/Cymulate.Integration.Adapters/Collectors/QualysCollector/Processing/Configuration/QualysCollectorConfiguration.cs` — change the capability default from `true` to `false` and rewrite its XML comment to describe flat run-root storage as the production default.
- Qualys tests: `src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.QualysCollector.Test/QualysBatchScopedStorageTests.cs` — rename/rewrite the configuration-default assertion to expect `false`; retain explicit `true` emitter tests as capability/lifecycle coverage and retain the existing explicit flat-layout test.
- Tenable.io production: `src/Cymulate.Integration.Adapters/Collectors/TenableIoCollector/Flows/Findings/Correlated/TenableIoCorrelatedBatchPublisher.cs` — change the sole hard-coded emitter argument to `batchScopedStorage: false` and update the class/constructor comments that currently say scoping is enabled.
- Tenable.io production comment alignment: `src/Cymulate.Integration.Adapters/Collectors/TenableIoCollector/Flows/Findings/Correlated/TenableIoCorrelatedFindingsFlow.cs` — remove the obsolete claim that sequential publication is required because the emitter rewrites the progress-context storage URL; keep the real ordered publish/checkpoint requirement.
- Tenable.io focused flow tests: `src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.TenableIoCollector.Test/TenableIoCorrelatedFlowTests.cs` — change the atomic-object test, path helper, storage URL assertions, capture comments, and stale empty-batch-folder comment to assert flat `findings_{page:D6}.json` objects at the run root. The same helper covers resume and in-invocation retry path assertions.
- Tenable.io collector integration tests: `src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.TenableIoCollector.Test/TenableIoCollectorTests.cs` — change `CorrelatedObjectPath` and its comment to the flat run-root layout; its callers cover initial correlated publication and resumed page numbering.
- No other production source needs modification. The precise scan currently returns exactly the two configuration defaults and the Tenable.io emitter call (plus the Tenable comment that quotes the call): `rg -n --glob '*.cs' '(BatchScopedStorage\s*\{[^}]*\}\s*=\s*true|BatchScopedStorage\s*=\s*true|batchScopedStorage\s*:\s*true)' src/Cymulate.Integration.Adapters/Collectors`.

# Patterns to mirror

- Mirror Falcon's production-default shape: `FalconCollectorConfiguration.BatchScopedStorage` and `FindingsFlowRunConfig.BatchScopedStorage` both default to `false`, while explicit configuration/tests can still opt into and verify the reusable IntegrationInfra emission capability.
- For InsightVM Cloud and Qualys, make only the configuration default and default assertion change. Their existing publishers already accept `bool batchScopedStorage = false`, their flows simply forward configuration, and their explicit `true` tests should remain to prevent accidental removal of the capability.
- For Tenable.io, preserve one atomic object per chunk/page and monotonic numbering; only remove the `batch_{page:D6}` path segment and scoped metadata. The expected flat target is `<run-prefix>/findings_{page:D6}.json`, and the live `storageUrl` remains the bare run URL for every publish.
- Keep comments about output layout factual and default-oriented. Do not describe self-contained pages as a reason they are currently scoped after the default is disabled.
- Exact focused commands from repository root:
  - `dotnet test src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.InsightVmCloudCollector.Test/Cymulate.Integration.Adapters.Collectors.InsightVmCloudCollector.Test.csproj --filter "FullyQualifiedName~InsightVmCloudBatchScopedStorageTests"`
  - `dotnet test src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.QualysCollector.Test/Cymulate.Integration.Adapters.Collectors.QualysCollector.Test.csproj --filter "FullyQualifiedName~QualysBatchScopedStorageTests"`
  - `dotnet test src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.TenableIoCollector.Test/Cymulate.Integration.Adapters.Collectors.TenableIoCollector.Test.csproj --filter "FullyQualifiedName~TenableIoCorrelatedFlowTests|FullyQualifiedName~TenableIoCollectorTests"`
- Collector-project verification commands:
  - `dotnet test src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.InsightVmCloudCollector.Test/Cymulate.Integration.Adapters.Collectors.InsightVmCloudCollector.Test.csproj`
  - `dotnet test src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.QualysCollector.Test/Cymulate.Integration.Adapters.Collectors.QualysCollector.Test.csproj`
  - `dotnet test src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.TenableIoCollector.Test/Cymulate.Integration.Adapters.Collectors.TenableIoCollector.Test.csproj`
- Final build command: `dotnet build src/Cymulate.Integration.Adapters/Cymulate.Integration.Adapters.sln`.

# Shared surface to freeze

- Do not change IntegrationInfra `BatchScopedStorage`, `NdjsonBatchEmitter`, publisher lifecycle, deterministic batch ID/path logic, or shared tests.
- Do not change Falcon's default-off property, configuration builder opt-in, flow behavior, or explicit scoped-storage tests.
- Do not change InsightVM Cloud/Qualys retrieval, parsing, pagination, page numbering, dud-page handling, checkpointing, progress totals, or publisher APIs.
- Do not change Tenable.io correlated envelope grammar, one-object-per-chunk boundary, staging spine, claim markers, export/resume state, checkpoint adjacency, page numbering, or sweep semantics.
- Production verification is scoped to `src/Cymulate.Integration.Adapters/Collectors`; literal `true` values in unit tests remain valid only where they deliberately exercise the preserved opt-in mechanism.

# Disjoint sets available

- `insightvm-cloud`: `InsightVmCloudCollectorConfiguration.cs` and `InsightVmCloudBatchScopedStorageTests.cs`; verify with the InsightVM Cloud focused command and then its whole test project.
- `qualys`: `QualysCollectorConfiguration.cs` and `QualysBatchScopedStorageTests.cs`; verify with the Qualys focused command and then its whole test project.
- `tenable-io`: `TenableIoCorrelatedBatchPublisher.cs`, `TenableIoCorrelatedFindingsFlow.cs`, `TenableIoCorrelatedFlowTests.cs`, and `TenableIoCollectorTests.cs`; verify with the Tenable.io focused command and then its whole test project.
- These sets have no overlapping product/test files. The production source scan and solution build are shared final verification and should run only after all sets land.

# Landmines

- A repository-wide scan will intentionally still find explicit `batchScopedStorage: true` in unit tests (including Falcon, InsightVM Cloud, and Qualys). The success criterion is no production collector default or hard-coded emitter call set to `true`; do not delete opt-in coverage to make an over-broad scan green.
- Tenable.io has no configuration property for this behavior: changing only configuration defaults misses its hard-coded emitter call.
- Tenable.io path expectations are centralized in two different helpers (`ExpectedTargetPath` and `CorrelatedObjectPath`). Updating only the conspicuous test at the top leaves retry/resume/integration assertions stale.
- Tenable.io's storage URL also backs the staged asset spine. Disable emitter scoping, but do not remove or rename the run-root `storageUrl`, `RequireBaseStorageUrl`, or `ResolveBaseUrl` logic.
- Flat storage does not permit resetting page numbers. `findings_000008.json` on resume remains required to avoid overwriting earlier objects.
- The Tenable.io correlated flow must remain sequential for ordered upload/checkpoint advancement even though the progress-context folder-race rationale disappears.
- Qualys's explicit scoped tests include checkpoint-writer and dud-page metadata behavior; they cover the preserved shared mechanism and should not be converted wholesale to `false`.
- NuGet restore may require CodeArtifact authentication per `CLAUDE.md`; treat 401/403 restore errors as environment/auth failures, not code failures.
