# Prompt Contract

Role:
You are a senior .NET engineer working in the cymulate-integration-adapters repo (worktree `.claude/worktrees/dev-work`, branch `batchful-uploads-uuid`).

Goal:
`BatchScopedStorage.BeginPage` announces a deterministic RFC 4122 UUIDv5 string in `Metadata["instanceBatchId"]` instead of the folder segment, with storage layout and all path logic unchanged, and the four named test suites green.

Context:
- File: `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/DataPipeline/Egress/BatchScopedStorage.cs`. Currently `BeginPage` writes the folder segment (`batch_000003`) to `InstanceBatchIdMetadataKey`.
- UUID = v5(namespace `b584b489-7c3d-4caf-97eb-49d7ff6d78fb`, name = `"{pristine base URL}/batch_{page:D6}"` UTF-8). Base URL is the value `ResolveBaseUrl` returns inside `BeginPage`.
- RFC 4122 §4.3: SHA-1 over (namespace GUID in network byte order + name bytes); take first 16 bytes; set version nibble 5 in byte 6, variant 10x in byte 8; convert back to `Guid` handling its mixed-endian layout; announce `ToString()` (lowercase "D").
- Test suites and known literal assertions:
  - `UnitTests/Shared/Cymulate.Integration.Adapters.Shared.Tests/DataPipeline/Egress/BatchScopedStorageTests.cs` (lines ~49, ~141, ~147)
  - `UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.QualysCollector.Test/QualysBatchScopedStorageTests.cs` (~55, ~75)
  - `UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.InsightVmCloudCollector.Test/InsightVmCloudBatchScopedStorageTests.cs` (~60, ~85, ~110)
- Docs to update: `InstanceBatchIdMetadataKey` XML doc; `Shared/.../DataPipeline/Egress/README.md` (mentions instanceBatchId).

Constraints:
- See `constraints.md` — binding. Highlights: path logic byte-identical; deterministic v5 only; pristine base as name input; frozen namespace; no new packages; no version bumps; filtered test runs only.

Success Criteria:
- `BeginPage` announces a parseable, lowercase UUID; same (base, page) always yields the same value, including after a scoped-URL resume round-trip.
- Different page or different base yields a different value.
- `RestoreBase` still removes the key; dud-page flow unchanged.
- `dotnet build` of the solution succeeds.
- Filtered `dotnet test` passes for: BatchScopedStorageTests, QualysBatchScopedStorageTests, InsightVmCloudBatchScopedStorageTests, AdapterFailureDecisionExecutorTests.
- New determinism pin tests exist in the Shared suite covering: same-inputs equality, resume round-trip equality, cross-page and cross-base inequality, Guid parseability, RestoreBase key removal.

Execution Rules:
- Do not assume missing data; the design is settled — do not redesign.
- Respect constraints strictly; mirror neighboring code style.
- Update `state.json` step statuses and append to `execution_notes.md` as work proceeds.

Output Format:
- Code edits on branch `batchful-uploads-uuid`; `execution_notes.md` appended with what changed and test results.

Stop Conditions:
- Goal achieved (build + 4 suites green).
- A constraint cannot be satisfied without violating another — stop and surface.
- Test hang per repo policy — stop, don't poll-loop.
