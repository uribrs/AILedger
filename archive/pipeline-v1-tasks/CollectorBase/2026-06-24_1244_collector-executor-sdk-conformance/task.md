# Task: Make CollectorExecutor a fully conforming SDK collector (big-bang)

Refactor CollectorExecutor so it implements the SDK + Shared.Orchestration collector
contract **exactly** like the native collectors (Falcon / Qualys / DefenderVm), with no
divergence. CollectorExecutor stays a generic, vendor-agnostic engine — all vendor
specifics remain in YAML profiles + named strategies.

## Why
CollectorExecutor today bypasses the contract: the adapter implements only
`IIntegrationAdapter<ICollectorCapability>, IResumableAdapter`, and `ProcessAsync` calls a
hand-rolled `CollectorExecutorRunner.RunAsync` directly instead of routing through
`AdapterBusEntrypointRunner`. That divergence is the root cause of a confirmed defect: the
output page number is derived from the pagination cursor (`page + 1`), which resets to 1 per
`for_each` chunk, so every chunk overwrites `assets_000001.json`. A live Tenable run reported
54,937 records across 55 pages but only the last chunk's 937 records survived on disk.

Conforming to the contract (monotonic per-emit-target page counter, byte-sliced egress,
bus-driven orchestration + resume) fixes the defect as a consequence, not a patch.

## Outcome
- Adapter implements the full collector interface set and routes through the Shared pipeline.
- Resume goes through `CollectorResumeRunner` + `CollectorResumeDefinition`.
- Output files are contiguous per type (`assets_000001..N`, `findings_000001..M`), gap-free,
  byte-bounded by the egress limit.

## Out of scope
- Production RUN-envelope ingress (deferred).
- Local Runner generic cred/input passthrough (already implemented + validated).
