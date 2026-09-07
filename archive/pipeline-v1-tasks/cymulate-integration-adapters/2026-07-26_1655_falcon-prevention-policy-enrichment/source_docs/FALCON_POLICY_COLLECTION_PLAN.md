# Falcon Policy Collection Plan

## Objective

Enrich every Falcon asset obtained from CrowdStrike Discover with the policies
assigned to that asset.

Policy enrichment is part of both existing collection paths:

- `CollectAssets`: enrich every emitted Discover asset.
- `CollectFindings`: enrich the Discover host embedded in every correlated
  host/findings record.

Policies are metadata on the asset body. This change does not introduce a
standalone policy flow, policy output file, or policy entity publication.

## Locked decisions

1. Discover remains the source of the asset population.
2. The CrowdStrike agent ID (`aid`) is the join key between a Discover asset
   and Hosts policy assignments.
3. Policy lookup is asset-driven. The collector will not download every policy
   membership list and correlate the tenant in memory.
4. The property added to a Discover host is `device_policies`.
5. A host with no matching Hosts API record is valid and receives an empty
   policy envelope.
6. Policy assignments are retained even when their definition cannot be
   resolved through a supported public policy endpoint.
7. Existing Shared session, authentication, retry, throttling, circuit-breaker,
   publication, and recovery mechanisms remain authoritative.
8. The current Atlassian design's separate-policy-flow proposal is not part of
   this collector change. It can be revisited later without invalidating the
   embedded asset metadata.

## Verified baseline

The local POC, its documentation, the SSP material, the Atlassian technical
design, and live API responses were reviewed before producing this plan.

The live tenant verified the relationship directly:

- 368 Discover assets were returned by the broad Discover query.
- 42 Discover assets were Falcon-managed.
- 42 Hosts API devices were returned.
- 42 prevention-policy member devices were returned.
- The three managed-device ID sets were identical.

The same run observed:

- 12 assignment slots.
- 46 distinct policy IDs.
- 460 asset-to-policy assignment edges.
- 16 distinct prevention policy definitions.

This explains the earlier result of 42: it was the managed Falcon host
population, not the total Discover asset population.

## Proposed asset contract

The collector should add one versioned object directly to the raw Discover host:

```json
{
  "aid": "0123456789abcdef",
  "hostname": "example-host",
  "device_policies": {
    "schema_version": 1,
    "assignments": [
      {
        "policy_type": "prevention",
        "policy_id": "policy-id",
        "applied": true,
        "assigned_date": "2026-07-01T10:00:00Z",
        "applied_date": "2026-07-01T10:02:00Z",
        "settings_hash": "hash-from-crowdstrike",
        "definition_status": "resolved",
        "definition": {
          "id": "policy-id",
          "name": "Workstation prevention",
          "prevention_settings": {}
        }
      },
      {
        "policy_type": "global_config",
        "policy_id": "another-policy-id",
        "applied": true,
        "definition_status": "unsupported"
      }
    ]
  }
}
```

Contract details:

- Preserve CrowdStrike assignment fields without translating their values.
- Preserve the raw `policy_type` value returned for the device.
- Put the unmodified first-level policy entity under `definition`.
- Add collector-owned `definition_status` with one of:
  - `resolved`
  - `not_found`
  - `unsupported`
- Omit `definition` unless its status is `resolved`.
- Sort assignments by `policy_type`, then `policy_id`, to make replay output
  deterministic.
- Use this exact empty result for unmanaged or unmatched assets:

```json
{
  "device_policies": {
    "schema_version": 1,
    "assignments": []
  }
}
```

The `device_policies` property must be replaced if CrowdStrike ever supplies a
property with that name; do not emit duplicate JSON properties.

In `CollectFindings`, the envelope remains inside `host`:

```json
{
  "aid": "0123456789abcdef",
  "chunk": 0,
  "isLastChunk": true,
  "findingsInChunk": 3,
  "host": {
    "aid": "0123456789abcdef",
    "device_policies": {
      "schema_version": 1,
      "assignments": []
    }
  },
  "findings": []
}
```

## API plan

### 1. Device-to-policy assignments

Use:

```text
POST /devices/entities/devices/v2
```

Input:

- The `aid` values already present in the current Discover page or findings
  host batch.
- Maximum accepted IDs: 5,000.
- Current collector boundaries are already smaller:
  - Discover assets page: at most 1,000.
  - Findings AID batch: currently 250 by default.

Join:

```text
Discover asset.aid == device detail.device_id
```

Relevant device-detail fields observed live:

- `device_id`
- `device_policies`
- For every assignment:
  - `policy_id`
  - `policy_type`
  - `applied`
  - `assigned_date`
  - `applied_date`
  - `settings_hash`
- Type-specific assignment fields may also be present and must be preserved,
  including:
  - firewall: `rule_set_id`
  - prevention: `rule_groups`
  - sensor update: `uninstall_protection`

No-match behavior observed live:

```json
{
  "resources": null,
  "errors": [],
  "meta": {
    "pagination": {
      "total": 0
    }
  }
}
```

The implementation must accept both `resources: null` and `resources: []` as a
successful empty result. Missing individual IDs are also successful empty
assignments for those assets.

### 2. First-level policy definitions

After reading assignments, group uncached policy IDs by canonical policy family
and call the corresponding entity route:

| Assignment family | Definition endpoint |
| --- | --- |
| `prevention` | `GET /policy/entities/prevention/v1` |
| `sensor_update` | `GET /policy/entities/sensor-update/v2` |
| `firewall` | `GET /policy/entities/firewall/v1` |
| `device_control` | `GET /policy/entities/device-control/v2` |
| `remote_response` | `GET /policy/entities/response/v1` |
| `content-update` | `GET /policy/entities/content-update/v1` |

Keep aliases in one route catalog rather than scattering string comparisons
through both flows. At minimum, the catalog should normalize hyphen/underscore
variants while preserving the original value in output.

These six assignment slots were also observed, but no equivalent supported
first-level public entity endpoint was established:

- `application-abuse-prevention`
- `exposure-management`
- `global_config`
- `host-retention`
- `logscale-collector`
- `system-tray`

Emit those assignments with `definition_status: "unsupported"`. They must not
disappear merely because they cannot be hydrated.

### Endpoint count

- Assignment-only enrichment needs one additional route type.
- The planned first-level envelope needs one Hosts route plus up to six policy
  entity route types: seven route types in total.
- Calls per batch are not fixed. There is one device-detail call, followed only
  by definition calls for cache misses in the policy families present.

### Definition response shapes

All verified entity responses use the CrowdStrike wrapper:

```json
{
  "meta": {},
  "resources": [],
  "errors": []
}
```

The definition body differs by family and should be preserved as vendor JSON:

| Family | Notable definition content |
| --- | --- |
| Prevention | nested `prevention_settings` groups and settings |
| Sensor update | `settings` |
| Firewall | `rule_set_id`; this is not the firewall rule set itself |
| Device control | `usb_settings` |
| Response | nested `settings` |
| Content update | `settings.ring_assignment_settings` |

No common typed settings model should be forced across these shapes. The
collector only needs a small typed assignment/route model around detached
`JsonObject` definition bodies.

### Pagination and batching

- `POST /devices/entities/devices/v2` is an entity lookup and has no pagination.
- The six policy entity endpoints are entity lookups and have no pagination.
- Send policy definition IDs in conservative chunks of 100, matching the proven
  POC behavior. Put this behind a validated collector setting only if operations
  needs to tune it.
- Do not use the combined policy-member listing endpoints for the collection
  path. Although they support `limit`/`offset`, they create an unnecessary
  tenant-wide scan.
- A direct single-device FQL filter on a combined membership endpoint worked
  live, but a comma-separated multi-device attempt did not. It is therefore not
  the batching mechanism for this design.

### Rate limiting and retries

All seven planned route types returned these headers in the live verification:

```text
x-ratelimit-limit: 6000
x-ratelimit-remaining: 5999
```

The time window was not proven, so `6000` must not be hard-coded as a scheduling
assumption.

Implementation rules:

- Send every new request through the existing Falcon `SessionSpec` transport.
- Let Shared retry/throttling honor response status and rate-limit headers.
- Do not add a policy-specific retry loop, delay, token bucket, or OAuth client.
- Treat `429` and transient `5xx` responses through the existing classifier.
- Include endpoint family, ID count, attempt/result classification, and
  remaining-rate-limit information in tactical logs without logging credentials
  or full policy bodies.

### Required access

The OAuth client needs:

- Hosts read access for device details.
- Read access for every enabled policy family:
  - prevention
  - sensor update
  - firewall
  - device control
  - response
  - content update

Exact CrowdStrike console scope labels should be captured in the collector
documentation during implementation because labels can differ from route names.
Access validation must identify the failing policy family rather than returning
a generic Falcon access error.

## Collector architecture

Add a policy-enrichment component close to Falcon rather than placing
vendor-specific behavior in Shared:

```text
FalconCollector/
  Flows/
    Policies/
      FalconPolicyEnricher.cs
      FalconDevicePolicyClient.cs
      FalconPolicyDefinitionClient.cs
      FalconPolicyRouteCatalog.cs
      FalconPolicyDefinitionCache.cs
```

Responsibilities:

- `FalconDevicePolicyClient`
  - Posts a bounded AID batch to the Hosts API.
  - Returns device-policy assignments keyed by `device_id`.
  - Normalizes `null` resources to an empty result.
- `FalconPolicyDefinitionClient`
  - Groups definition requests by route and chunks IDs.
  - Returns raw definitions keyed by canonical family and policy ID.
- `FalconPolicyRouteCatalog`
  - Owns family aliases, definition routes, and unsupported-family knowledge.
- `FalconPolicyDefinitionCache`
  - Run-scoped cache keyed by `(canonical policy family, policy ID)`.
  - Caches resolved definitions and confirmed not-found results.
  - Is not static and is not written to recovery state.
- `FalconPolicyEnricher`
  - Coordinates assignment lookup, definition hydration, and attachment.
  - Produces the same output for the same source responses.
  - Does not publish, checkpoint, authenticate, or retry independently.

Extend `FalconUrls` with the seven route builders. Construct the clients and
run-scoped cache through the collector's existing composition path.

## Flow integration

### CollectAssets

The current assets parser streams records directly from the Discover response.
Policy enrichment needs the AIDs before records can be emitted, so change one
page into a bounded two-stage operation:

1. Read and normalize the Discover page into detached `JsonObject` records.
2. Retain existing counters, filtering, watermark, and cursor observations.
3. Extract managed AIDs from that page.
4. Enrich all page records in one device-detail request plus definition-cache
   misses.
5. Serialize and publish the enriched page through the existing NDJSON
   publisher.
6. Advance the existing checkpoint only after publication succeeds.

The memory increase is bounded by the existing maximum page size of 1,000. Do
not buffer multiple Discover pages.

Likely touch points:

- `Flows/Assets/FalconAssetsPageParser.cs`
- `Flows/Assets/FalconAssetsScrollRunner.cs`
- collector composition/wiring

### CollectFindings

The findings flow already buffers a bounded Discover host batch before Spotlight
correlation:

1. Take the current `freshHosts` AID batch.
2. Enrich each batch host before creating `HostFindingsAccumulator`.
3. Run the existing Spotlight scroller.
4. Let `FalconCorrelatedRecord.Build` deep-clone the already enriched host into
   every findings chunk.
5. Publish and checkpoint at the existing atomic AID-batch boundary.

No policy lookup should occur once per findings chunk; all chunks for one host
reuse the enriched host object.

Likely touch points:

- `Flows/Findings/FalconFindingsFlow.cs`
- `Flows/Findings/Correlated/HostFindingsAccumulator.cs` only if its constructor
  contract needs to make enriched-host ownership explicit
- collector composition/wiring

## Failure semantics

Policy metadata is required output, so the collector must not silently publish a
page or findings batch without it.

| Condition | Behavior |
| --- | --- |
| Discover asset has no AID | Emit the empty envelope |
| Hosts lookup has no matching device | Emit the empty envelope |
| Hosts response has `resources: null` or `[]` | Treat as successful empty lookup |
| Assignment family has no supported definition route | Retain assignment; mark `unsupported` |
| Supported definition ID is absent from a successful response | Retain assignment; mark `not_found` |
| Definition response contains per-ID API errors | Classify them; do not silently convert authorization/transient failures to `not_found` |
| `401` or `403` | Fail validation/run with the endpoint family identified |
| `429` or retryable `5xx` | Use Shared retry/throttling; fail the unpublished unit if exhausted |
| Malformed assignment or definition wrapper | Fail the unpublished page/batch with a bounded diagnostic |
| Cancellation or timeout | Follow the existing Falcon partial-success and recovery path |

Unsupported definition families are a known capability boundary, not a partial
failure.

## Access probing and dry run

Update `FalconAccessProber` so dry run validates the new required path without
publishing:

1. Read one Discover host as today.
2. If it has an AID, request its device details.
3. Extract the policy families and IDs returned for that device.
4. Probe each supported definition family represented by that device.
5. If no managed/AID-bearing host is available, report that Hosts/policy access
   could not be fully exercised; do not manufacture IDs.

Because one sampled host may not contain all six supported families, document
this as a representative probe. Authorization failures encountered later must
still retain their precise family classification.

## Recovery and deterministic replay

- Keep the assets checkpoint boundary after a fully enriched page is published.
- Keep the findings checkpoint boundary after the fully enriched AID batch is
  published.
- Do not checkpoint the definition cache. Rebuilding it after resume is safe and
  simpler.
- Do not advance a watermark or cursor after enrichment but before publication.
- Sort assignments and keep stable definition attachment so a replay produces
  byte-stable policy ordering.
- No checkpoint schema change is currently required. Do not bump the checkpoint
  version unless implementation changes persisted position/state.
- On a crash before publication, repeat the AID lookup and definition cache
  misses for that unit. On a crash after publication, the existing checkpoint
  prevents that unit from being intentionally replayed.

## Delivery slices

### Slice 1: Pin the contract and assignment enrichment

- Add output contract fixtures for populated, unsupported, unmatched, and
  unmanaged hosts.
- Add the Hosts device-details route and client.
- Add `FalconPolicyEnricher` with assignment attachment.
- Integrate it into assets and findings at their existing atomic boundaries.
- Add dry-run Hosts validation.
- Emit all definitions as `unsupported` temporarily only in a development
  branch; do not release this slice as the complete feature.

Exit condition: both flows produce deterministic `device_policies.assignments`
from device details and preserve recovery behavior.

### Slice 2: Hydrate six supported definition families

- Add the route catalog and six entity route builders.
- Add definition grouping, chunking, and run-scoped caching.
- Attach raw definitions and status.
- Extend dry-run and failure classification per family.
- Add request-count and cache-hit tests.

Exit condition: every supported assignment is resolved or explicitly
`not_found`; unsupported families remain visible.

### Slice 3: Operational hardening and rollout

- Add structured metrics/logs for:
  - assets considered
  - AIDs requested and matched
  - assignments emitted by family
  - resolved/not-found/unsupported definitions
  - definition cache hits/misses
  - policy API calls and exhausted retries
- Update Falcon collector documentation and credential requirements.
- Run focused tests, the full Falcon test project, build, and a real-connection
  smoke test.
- Compare a sample collector payload with the known POC output and live tenant.

Exit condition: the release evidence proves both flows, empty behavior,
permissions, retry behavior, request bounds, and recovery continuation.

## Test plan

### Unit tests

Add focused tests for:

- Route and alias mapping for all 12 observed assignment slots.
- Device-detail request batching and the 5,000-ID guard.
- `resources: null`, empty resources, partial ID matches, and duplicate device
  records.
- Preservation of all assignment fields, including unknown future fields.
- Definition grouping by family and 100-ID chunking.
- Cache key isolation between policy families sharing the same ID text.
- Cache reuse across multiple assets, pages, and findings batches in one run.
- Definition status: `resolved`, `not_found`, and `unsupported`.
- Stable assignment ordering.
- Replacement of an existing `device_policies` property.
- Cancellation propagation and malformed JSON.

Suggested new test files:

```text
FalconDevicePolicyClientTests.cs
FalconPolicyDefinitionClientTests.cs
FalconPolicyEnricherTests.cs
FalconPolicyRouteCatalogTests.cs
```

### Flow and contract tests

Extend current Falcon tests to prove:

- Assets output contains `device_policies` inside each asset.
- Findings output contains it inside `host`, in every chunk.
- Policy calls happen once per assets page or findings AID batch, not per record
  or finding.
- A findings host with multiple chunks receives identical policy metadata.
- Empty/unmanaged hosts are still published.
- A policy lookup failure prevents publication/checkpoint of that unit.
- A resume restarts at the correct page/batch and safely rebuilds the cache.
- Existing Discover filtering, boundary deduplication, counters, and watermarks
  are unchanged.
- Dry run performs representative policy access checks and publishes nothing.

Update at least:

- `FalconAccessProberTests.cs`
- `FalconCollectorTests.cs`
- `FalconCorrelatedFindingsTests.cs`
- `FalconRecoveryContinuationTests.cs`
- `FalconUrlsTests.cs`

### Verification commands

```bash
dotnet build src/Cymulate.Integration.Adapters/Collectors/FalconCollector/Cymulate.Integration.Adapters.Collectors.FalconCollector.csproj

dotnet test src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.FalconCollector.Test/Cymulate.Integration.Adapters.Collectors.FalconCollector.Test.csproj
```

Then run the local adapter runner against a controlled Falcon tenant and inspect
both assets and findings payloads.

## Acceptance criteria

The feature is complete when:

1. Every Discover asset emitted by `CollectAssets` contains one
   `device_policies` envelope.
2. Every Discover host embedded by `CollectFindings` contains the same envelope.
3. Managed hosts are joined by exact `aid`/`device_id`, without a tenant-wide
   policy-member scan.
4. All CrowdStrike assignment fields are preserved.
5. Definitions from all six supported first-level entity families are embedded
   when available.
6. Assignments from unsupported families remain present and explicitly marked.
7. Unmanaged/unmatched assets produce a successful empty envelope.
8. New HTTP calls use the existing session and resilience infrastructure.
9. Requests are bounded by current page/batch sizes, definitions are chunked,
   and definitions are cached for the run.
10. A failed enrichment cannot advance the corresponding publication
    checkpoint.
11. Dry run exercises the available policy path and publishes no data.
12. Existing Falcon unit tests remain green and new contract/recovery tests pass.

## Out of scope for this delivery

- A standalone Policies flow or policy publication stream.
- A tenant-wide scan of combined policy membership endpoints.
- Host-group collection unless a later consumer explicitly requires it.
- Recursive firewall rule-set hydration.
- Recursive prevention IOA rule-group hydration.
- Interpretation or normalization of vendor settings into a cross-vendor policy
  model.
- Policy-to-finding causality or attribution.
- Unsupported/private CrowdStrike web UI endpoints.
- Parser, database, or UI work outside this collector repository.

Those can be decomposed into later work. The versioned embedded envelope leaves
room for them without making this first collector change carry the entire policy
universe on its back. That universe has enough luggage already.

## References

- Local POC and research: `/Users/user/Dev/crowdstrikepolicies`
- Local POC documentation: `/Users/user/Dev/crowdstrikepolicies/docs`
- Local SSP material: `/Users/user/Dev/crowdstrikepolicies/ssp`
- Atlassian: `Policies - Technical Design`, page ID `2251718658`
- Falcon collector architecture:
  `src/Cymulate.Integration.Adapters/Collectors/FalconCollector/FalconDocs/CollectorDocs`
- Shared session/authentication:
  `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Session/docs/Collector-Session-Auth.md`
- Shared publishing:
  `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/DataPipeline/Egress/README.md`
- Shared orchestration:
  `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/README.md`
