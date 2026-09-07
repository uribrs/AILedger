Role:
You are a senior .NET engineer expert in the Cymulate integration-adapters platform — its SDK
(`Cymulate.Integration.Sdk`), the Shared substrate (`Cymulate.Integration.Adapters.Shared`:
Orchestration, DataPipeline/Egress, Recovery, Session), and the collector contract that the
native collectors (Falcon/Qualys/DefenderVm) implement.

Goal:
Refactor CollectorExecutor to conform EXACTLY to the SDK + Shared.Orchestration collector
contract, with no divergence, while keeping it a generic vendor-agnostic engine. As a
consequence, eliminate the multi-step `for_each` output-page collision by adopting the
native monotonic per-emit-target, byte-sliced page-numbering contract.

Context:
- Repo: /Users/user/Dev/Uri/localprojects/CollectorBase (POC lab; net8.0; solution CollectorBase.slnx).
- Today the adapter implements only `IIntegrationAdapter<ICollectorCapability>, IResumableAdapter`
  and `ProcessAsync` calls `CollectorExecutorRunner.RunAsync` directly — bypassing the contract.
- Native collectors uniformly: implement IAssets+IFindings+ICollectorAdapter+ICollectorEventSink
  +IResumableAdapter+IAsyncDisposable; route ProcessAsync through
  `CollectorBusEntrypointDefinitionBuilder.Build(new DelegateCollectorBusEntrypointSource<TRequest>{...})`
  -> `AdapterBusEntrypointRunner.RunAsync`; resume via `CollectorResumeRunner` +
  `CollectorResumeDefinition<TState>`; publish via `CollectorNdjsonPublisher.Publish{Assets|Findings}Utf8PageAsync`
  with a monotonic per-stream page counter persisted in checkpoint state (Page/AssetsPage/FindingsPage/
  PublishedPageCount), byte-sliced to `ThrottlingOptions.MaxBytesPerBatch`.
- Reference (read-only): adapters monorepo Collectors/{FalconCollector,QualysCollector,DefenderVmCollector};
  TenableIoCollector/Flows/Assets/TenableIoAssetsFlow.cs ProcessSingleChunkAsync. Shared lives in this
  repo under Shared/ (same code).

Constraints:
- See constraints.md (authoritative). Key: no divergence from contract shape; reuse Shared; no vendor
  identity; bus runner supplies the progress context; per-emit-target persisted page counter decoupled
  from pagination cursor; one publish call = one file, byte-sliced by the caller; resume idempotent.
- Do NOT touch: production RUN-envelope ingress; the local Runner cred/input passthrough.

Success Criteria:
1. `dotnet build CollectorBase.slnx` clean.
2. `CollectorExecutorAdapter` implements the full interface set and routes `ProcessAsync` through
   `AdapterBusEntrypointRunner` via `DelegateCollectorBusEntrypointSource` with all 13 required delegates
   (VendorName, ExtractRequest, GetFlowName, IsConfigured, BuildConfiguration, SetConfiguration,
   ValidateConfigurationAsync, InitializeAsync, ShutdownAsync, CollectAssetsAsync, CollectFindingsAsync,
   BuildSuccessResult, RaiseError) + common optional subset.
3. `ResumeAsync`/`CanResumeFrom` go through `CollectorResumeRunner` + `CollectorResumeDefinition<CheckpointState>`.
4. Output page number is a persisted, per-emit-target, monotonic counter, byte-sliced to
   `MaxBytesPerBatch`; verified by re-running the Tenable assets profile and confirming contiguous
   `assets_000001..N` with all ~54,937 records present (no overwrite).
5. Existing `Tests/CollectorExecutor.Test` pass.

Execution Rules:
- Do not assume missing data; mark assumptions in assumptions.md and resolve OPEN ones during execution.
- Respect constraints strictly. Preserve the interpreter (templating/JSON-XML nav/pagination/mapping/
  strategy registry) — re-house it under the contract, do not rewrite it.
- Model wiring exactly on the native collectors; cite them when in doubt rather than inventing shape.
- Prefer built-in Shared mechanisms over any new custom logic.

Output Format:
- Code edits to: CollectorExecutorAdapter.cs, Execution/CollectorExecutorRunner.cs,
  Checkpointing/CheckpointState.cs, Seams/Seams.cs (+ a new CollectorExecutorRequest type if needed).
- Append progress + decisions to execution_notes.md; keep state.json steps in sync.

Stop Conditions:
- All success criteria met and verified.
- A required Shared contract member differs from what this brief assumes (surface the mismatch).
- A constraint cannot be satisfied without divergence (stop and report).
