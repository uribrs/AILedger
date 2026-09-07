# Orchestration Plan

## Complexity Decision

- Path: direct
- Rationale: Dependency order is near-linear (URLs → clients → enricher → flow wiring →
  config/probe → tests) and coupling is high. The enricher's API shape is defined by
  both call sites at once, and the Assets change is delicate surgery on a streaming
  publish/watermark/checkpoint boundary that must not regress. Worker boundaries would
  be fuzzy and coordination cost would exceed the benefit.

## Research Decisions

None needed. Every assumption in `assumptions.md` is VALIDATED, and local API-shape
evidence is on disk and verified to exist:

- `/Users/user/Dev/crowdstrikepolicies/docs` and `/ssp` (vendor research);
- lab probe `FalconPreventionPolicyFlow_20260726_131018_150Z` with real captured
  `01-discover-response.json`, `02-device-details-response.json`,
  `03-prevention-policy-response.json`, `04-spotlight-page-001-response.json`, plus
  `collect-assets-output.json` / `collect-findings-output.json` and `endpoint-trace.json`.

Execution must read the probe captures for exact field shapes rather than inventing
them, and must not copy the probe's raw finding payload over production shaping.

## Worker Plan

Not applicable — direct path.

## Grounded reconnaissance (completed in main thread)

Confirmed before execution, so the plan is not guessing at boundaries:

- `AdapterHttpClient` already exposes `PostStreamAsync`/`PostStringAsync` — no new
  transport needed for `POST /devices/entities/devices/v2`.
- `FalconUrls` currently holds only `BuildAssetsUrl` and
  `BuildCorrelatedSpotlightBaseUrl`; both new endpoints belong there.
- **Findings flow is the easy call site.** `FalconDiscoverHostScroller` already
  materializes `DiscoverHost(string Aid, JsonObject Host, DateTime? LastSeenUtc)`, and
  `FalconFindingsFlow.CollectAsync` chunks `freshHosts` into `aidBatchSize` batches at
  `Flows/Findings/FalconFindingsFlow.cs:192`. Enrichment inserts immediately inside that
  `foreach`, before the `accumulators` dictionary is built at line 196-201 and before
  `batchScroller.EmitBatchRecordsAsync` at line 206.
- **Assets flow is the risky call site.** `FalconAssetsScrollRunner.ProcessPageAsync`
  streams straight from the HTTP stream into the publisher: `FalconAssetsPageParser`
  yields `ReadOnlyMemory<byte>` single-line UTF-8 rows consumed by
  `CollectorNdjsonPublisher.PublishAssetsUtf8PageAsync`. Parser state
  (`ResourcesSeen`, `MaxLastSeenUtc`, `MaxLastSeenIds`) and reader state
  (`AfterToken`, `Total`) are only valid *after* the publisher drains the sequence.
  Materializing the page first inverts that ordering, which is safe (the reader is
  drained during materialization so `AfterToken`/`Total` become available earlier) but
  every dependent branch must be re-checked:
  - `parser.ResourcesSeen == 0` → empty-terminal-page snapshot;
  - `publishResult.RecordCount == 0` → no-publishable-terminal-page snapshot;
  - `counters.Page--` decrements on both terminal branches;
  - watermark advance uses the *emitted* rows, not all seen rows;
  - `counters.TotalHosts += publishResult.RecordCount`.
- Config convention is a capability constant on `FalconCollectorConfiguration`
  (`BatchScopedStorage` is the model), so `EnablePreventionPolicyEnrichment` is a
  hardcoded `bool` init property — default `true` per the plan — not a payload knob.
- `FalconAccessProber` has `ProbeHostsAsync` / `ProbeSpotlightAsync` /
  `EnsureSpotlightProbedAsync` over a private `ProbeAsync`; the staged policy probe
  extends this class.
- Test project holds 15 files; the change-relevant classes are `FalconUrlsTests`,
  `FalconAccessProberTests`, `FalconCorrelatedFindingsTests`, `FalconCollectorTests`,
  `FalconCollectorConfigurationBuilderTests`, plus new policy-focused test files.

## Execution Sequence

Follows plan §11, with the placement decision already fixed:

1. Extend `FalconUrls` with the device-entities and prevention-definition builders.
2. Add `Flows/Policies/`: device client, prevention definition client, run-scoped
   cache, envelope builder, and the single shared enricher.
3. Wire `CollectFindings` at the existing AID-batch boundary (before accumulators and
   Spotlight).
4. Convert one Assets page to bounded materialize → enrich → publish → checkpoint,
   preserving every branch listed above.
5. Add the `EnablePreventionPolicyEnrichment` capability constant and the disabled
   envelope path.
6. Extend the staged access probe.
7. Add the §12 test matrix beside the new components and update the touched existing
   fixtures.
8. Build, run change-filtered Falcon tests, update `ai/skills` collector skill docs and
   task state.

## Verification Obligations

- Cross-check against `prompt_contract.md` Success Criteria and plan §13 acceptance
  criteria.
- Task-specific verification points:
  - exactly one `device_policies` per emitted host in both flows, existing property
    replaced not duplicated;
  - canonical equality of the same source host between Assets and Findings outputs;
  - asymmetric failure contract: assignment failure = no publish, no checkpoint,
    no Spotlight for that batch; definition failure = partial publish + checkpoint;
  - one assignment call per page / per AID batch — never per host, finding, or chunk;
  - cache never converts an outage into `not_found`;
  - Assets traversal invariants intact: empty terminal page, segmentation, watermark +
    boundary AIDs, cursor re-anchor, counters, target naming, only one page in memory;
  - Findings invariants intact: prefetch, finding stripping, grouping, chunking,
    zero-finding envelopes, atomic batch publish, checkpoint;
  - 401/403 and malformed contract never degrade to empty/partial;
  - no per-csproj version bump; no commit or push.
- Never run the full FalconCollector suite; filter to change-relevant classes.
