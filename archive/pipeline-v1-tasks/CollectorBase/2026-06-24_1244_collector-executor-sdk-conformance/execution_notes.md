# Execution Notes

## Research / contract shapes resolved (read from this repo's Shared)
- A6 — RESOLVED: `CollectorResumeDefinition<TState> where TState : class` requires `TryLoadState(IReadOnlyDictionary<string,string>)->(bool,TState?)`, `RunAsync(TState, AdapterProgressContext, CT)->Task<int>` (the runner creates + restores the progress context), `LogResume`, `BuildResultData`, `SuccessMessagePrefix`, + optional Classify/Retry/Resilience/Recover. `CheckpointState` works directly as `TState`. Resume `RunAsync` has no PlatformEvent param → capture the parsed request + platformEvent in the closure (as native does).
- A7 — RESOLVED: one `CheckpointState` carries BOTH `AssetsPage` + `FindingsPage`; the runner bumps the one matching the emit target. Confirmed `CollectorNdjsonPublisher.PublishUtf8PageAsync` = exactly ONE file per call (target path built from `pageNumber`); the caller must do byte-slicing (egress owns the LIMIT VALUE via `ThrottlingOptions.MaxBytesPerBatch`, not the slicing).

## DONE + verified — page-numbering contract (S5, S6) — the confirmed defect fix, done the native way
Changed files:
- `Checkpointing/CheckpointState.cs`: added `AssetsPage` / `FindingsPage` (last published page number per target; additive, backward-compatible — old checkpoints deserialize to 0).
- `Seams/Seams.cs` (`RunContext`): added `AssetsPage` / `FindingsPage` counters + `MaxBytesPerBatch`.
- `Execution/CollectorExecutorRunner.cs`:
  - Resolve `ThrottlingOptions.Resolve(_context.Services).MaxBytesPerBatch` once per run into `RunContext`.
  - Publish path now slices each emit to the byte budget (`SliceByBytes`, serialize-once) and assigns a
    MONOTONIC per-target page number (`++runCtx.AssetsPage` / `++runCtx.FindingsPage`) to each slice — one
    publish call == one file. Page number is the OUTPUT sequence, fully decoupled from the pagination cursor.
  - Counters seeded from checkpoint on resume and persisted in every `WriteCheckpoint` → contiguous, gap-free
    per-type numbering; mid-step re-run re-publishes from the persisted base (deterministic idempotent overwrite).
  - Replaced `ToNdjson` with `SliceByBytes` + `ToBytesAsync` (serialize each record once; no double-serialize).

Verification:
- `dotnet build CollectorBase.slnx` → clean (0 errors).
- `dotnet test CollectorBase.slnx` → 43/43 pass (non-breaking).
- Live Tenable assets run (out_verify/) to confirm contiguous `assets_000001..N` with all records present — see state.json S7 for result.

## NOT LANDED — structural conformance (S1–S4) — flagged, not half-built
The adapter interface-set + `AdapterBusEntrypointRunner` routing + `CollectorResumeRunner`/`CollectorResumeDefinition`
wiring is DESIGNED (all Shared contract shapes resolved above; native wiring surveyed) but NOT implemented in this
pass. Reason (surfaced per the contract's Stop Conditions / operator discipline): it is a behavior-changing refactor,
not a wrapper — the bus pipeline requires the collect delegates to RETURN `Task<int>` and THROW classified exceptions
on failure, whereas `CollectorExecutorRunner.RunAsync` today OWNS the terminal model (returns `AdapterResult`, converts
server-waits to `PartialResult`, handles defer/partial-success/cancel internally) and creates its OWN progress context
instead of consuming a bus-provided one. Conforming means inverting progress-context ownership and translating the
entire terminal/failure model onto the bus runner + resilience strategy + `TryBuildPartialSuccessResult`. That cannot
be landed and stabilized safely in this pass without risking a broken build — so it is reported, not improvised.

Recommended next execution (well-bounded by the resolved design):
1. Add `CollectorExecutorRequest` (TRequest: profile + yaml + inputs + config + resolved flow).
2. Split `RunAsync` into: `ExtractRequest` (parse payload) + `CollectStreamAsync(ev, busProgress, req, flow, ct)->int`
   that runs the existing step loop on the bus-provided progress and throws classified failures / lets
   `ServerSuggestedRetryDelayException` propagate.
3. Rewrite `CollectorExecutorAdapter`: full interface set (IAssets+IFindings+ICollectorAdapter+ICollectorEventSink
   +IResumableAdapter+IAsyncDisposable) + 5 events; ProcessAsync via `CollectorBusEntrypointDefinitionBuilder` +
   `DelegateCollectorBusEntrypointSource` (13 delegates); ResumeAsync via `CollectorResumeRunner` +
   `CollectorResumeDefinition<CheckpointState>`.

## Net state
- Repo builds clean; tests green; the user-facing multi-step output-collision defect is fixed and (pending S7) verified.
- The hand-rolled adapter path remains in place — i.e. the divergence the contract targets is NOT yet removed.
  S1–S4 remain open with the design fully resolved.
