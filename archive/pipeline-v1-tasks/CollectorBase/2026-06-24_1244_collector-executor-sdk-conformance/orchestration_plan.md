# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: Tightly-coupled refactor of CollectorExecutorAdapter + CollectorExecutorRunner +
  CheckpointState + Seams (RunContext) that must change as one unit — the adapter's delegates
  invoke the runner, the runner's publish path uses the new per-target counters, and resume
  threads adapter + runner + state together. Decomposition would only add coordination overhead
  and fuzzy boundaries. One coherent execution context is correct.

## Research Decisions
- None needed. The OPEN assumptions (A6: CollectorResumeDefinition accepted state type + run
  delegate; A7: combined-flow per-target counters) are internal Shared-contract shapes whose
  source is present in this repo (Shared/.../Orchestration/Collectors/Recovery/). Resolve by
  reading during execution, not external research.

## Worker Plan
Not applicable — direct path.

## Synthesis Approach
Not applicable — direct path.

## Execution Approach (for contract-driven-execution)
Recommended ordering within the single execution context:
1. Read the reference wiring first: native FalconCollector/QualysCollector/DefenderVmCollector
   adapters (ProcessAsync + ResumeAsync), and Shared CollectorBusEntrypointDefinitionBuilder /
   DelegateCollectorBusEntrypointSource / AdapterBusEntrypointRunner / CollectorResumeRunner +
   CollectorResumeDefinition / CollectorNdjsonPublisher / ThrottlingOptions. Resolve A6 here.
2. Add per-emit-target page counters to RunContext (Seams.cs) + CheckpointState (AssetsPage/
   FindingsPage + counter-base for resume idempotency). Resolve A7 here.
3. Rework the publish path in CollectorExecutorRunner: byte-slice emitted records to
   ThrottlingOptions.MaxBytesPerBatch, publish one page per slice, increment the target counter
   per page, decouple from the pagination cursor.
4. Introduce CollectorExecutorRequest (TRequest); make the per-stream step loop the body of
   CollectAssetsAsync/CollectFindingsAsync driven by the bus-provided AdapterProgressContext.
5. Rewrite CollectorExecutorAdapter: full interface set + 5 events; ProcessAsync via builder +
   DelegateCollectorBusEntrypointSource + AdapterBusEntrypointRunner; ResumeAsync/CanResumeFrom
   via CollectorResumeRunner + CollectorResumeDefinition<CheckpointState>; ICollectorEventSink.
6. Build; fix; run tests; live/harness verification of contiguous per-target numbering.

## Verification Obligations
- Cross-check against prompt_contract.md Success Criteria 1–5.
- SC4 is the load-bearing behavioral check: a multi-chunk for_each must emit contiguous,
  gap-free per-type files with NO overwrite (all records present). Prefer the live Tenable
  assets run; fall back to the in-process test harness if creds are unavailable.
- Confirm no vendor identity leaked into the engine (constraint).
- Confirm OPEN assumptions A6/A7 were resolved (moved to VALIDATED/REJECTED or into decisions.md).
