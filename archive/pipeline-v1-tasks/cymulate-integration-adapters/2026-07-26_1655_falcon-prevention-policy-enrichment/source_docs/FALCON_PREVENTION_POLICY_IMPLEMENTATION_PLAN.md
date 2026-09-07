# Falcon Prevention Policy Enrichment — Implementation Plan

Status: authoritative implementation plan  
Scope: FalconCollector, Prevention Policy only  
Supersedes for delivery: `FALCON_POLICY_COLLECTION_PLAN.md`

The older policy plan remains useful as research history, but it is not an
implementation specification. It covers a much larger policy universe than the
agreed first delivery. If the two documents disagree, this document wins.

## 1. Objective

Enrich every CrowdStrike Discover host emitted by FalconCollector with its
assigned Prevention Policy and the corresponding policy definition.

Policy is metadata within the asset body. It is not a standalone entity or
collection flow.

The same enriched host must appear in both existing collection paths:

- `CollectAssets`: the emitted Discover asset contains `device_policies`.
- `CollectFindings`: the correlated record's `host` contains the identical
  `device_policies`; Spotlight findings remain beside that host.

An asset is never dropped merely because it has no Prevention Policy.

## 2. Locked Scope

### In scope

- CrowdStrike Prevention Policy only.
- The existing `CollectAssets` and correlated `CollectFindings` flows.
- The relationship between Discover `aid` and Hosts `device_id`.
- Assignment metadata from `device_policies.prevention`.
- Full first-level Prevention Policy entity hydration, including nested
  `prevention_settings`.
- Run-scoped definition deduplication and caching.
- Existing Falcon auth, HTTP session, retry, rate-limit, publishing,
  checkpoint, and recovery infrastructure.
- Access probing for the additional required permissions.
- Focused unit and flow tests.
- A default-enabled operational switch for this enrichment.

### Out of scope

- Every policy family other than Prevention.
- A generic multi-family policy catalog or route abstraction.
- A standalone Policies flow, output, or parser entity.
- Tenant-wide policy-member enumeration.
- Recursive hydration of IOA/rule-group IDs.
- Detection, alert, or incident collection.
- Associating an individual Spotlight finding with a policy.
- Parser, database, or UI changes.
- Changes to the production finding shape other than enriching `host`.
- Shared infrastructure changes unless existing APIs genuinely cannot express
  the required behavior.

## 3. Vendor Request Sequence

For a bounded unit of Discover hosts:

1. Fetch Discover hosts using the existing request:
   `GET /discover/combined/hosts/v1`.
2. Extract non-empty `aid` values.
3. Resolve device policy assignments in one batched request:
   `POST /devices/entities/devices/v2`
   with body:

   ```json
   {
     "ids": ["aid-1", "aid-2"]
   }
   ```

4. Join Discover `aid` to the returned device entity's `device_id`.
5. Read only `device_policies.prevention`.
6. Deduplicate non-empty Prevention Policy IDs across the unit and the
   run-scoped cache.
7. Hydrate cache misses with:
   `GET /policy/entities/prevention/v1?ids=<policy-id>`.
   Multiple IDs use the vendor's repeated `ids` query convention. Use
   conservative chunks of at most 100 IDs unless current local evidence proves
   a smaller limit is required.
8. Attach the envelope to each detached Discover host.
9. In `CollectAssets`, publish the enriched assets.
10. In `CollectFindings`, continue with the existing one-pass, AID-scoped
    Spotlight request and place the enriched host in every correlated chunk.

Vendor limits relevant to the design:

- Device entity lookup supports up to 5,000 IDs. Existing flow bounds are lower:
  an asset page is at most 1,000 hosts and the findings AID batch defaults to
  250.
- Prevention definition requests use a conservative 100-ID chunk.
- Respect server rate-limit and `Retry-After` signals through the existing
  session stack. Do not hardcode a rate window from one observed tenant.

Required CrowdStrike permissions:

- existing Discover Hosts read permission;
- Hosts read permission for `/devices/entities/devices/v2`;
- Prevention Policies read permission;
- existing Spotlight permission for `CollectFindings`.

## 4. Output Contract

### 4.1 Resolved assignment and definition

Add or replace one `device_policies` property at the root of the Discover host:

```json
{
  "aid": "0b494393291945c7be1cee66e8c73802",
  "...existing Discover properties...": "...",
  "device_policies": {
    "schema_version": 1,
    "collection_status": "complete",
    "prevention": {
      "assignment": {
        "policy_type": "prevention",
        "policy_id": "8f590b7a2c95462da6278527f162aa94",
        "applied": true,
        "settings_hash": "...",
        "assigned_date": "...",
        "applied_date": "...",
        "rule_groups": ["..."]
      },
      "definition_status": "resolved",
      "definition": {
        "id": "8f590b7a2c95462da6278527f162aa94",
        "name": "Phase 3 - optimal protection",
        "prevention_settings": []
      }
    }
  }
}
```

Contract rules:

- Preserve the vendor assignment object without projecting away unknown or
  future fields.
- Preserve the returned policy definition without flattening or trimming its
  nested `prevention_settings`.
- Keep `rule_groups` as identifiers. Do not recursively hydrate them.
- If the source host already has `device_policies`, replace it with this
  collector-owned envelope. Never emit duplicate JSON properties.
- Given the same Discover source data and successful policy calls, the enriched
  host emitted by both flows must be canonically equal.

### 4.2 Successful lookup with no assignment

Unmanaged hosts, unmatched device IDs, `resources: null`, `resources: []`, and
device entities without `device_policies.prevention` are successful empty
results:

```json
"device_policies": {
  "schema_version": 1,
  "collection_status": "complete",
  "prevention": null
}
```

The host is published. It is not filtered out.

### 4.3 Definition statuses

- `resolved`: the requested policy definition was returned successfully.
- `not_found`: a successful 2xx definition response omitted the requested ID.
- `unavailable`: definition hydration exhausted transient request handling.

For `not_found`, retain the assignment and set `definition` to `null`.
For `unavailable`, retain the assignment and set `definition` to `null`.

### 4.4 Envelope collection status

Derive status from the result rather than independently mutating it in several
code paths:

- `complete`: assignment lookup succeeded and either there is no Prevention
  assignment, or its definition is `resolved`/`not_found`.
- `partial`: a Prevention assignment exists and its definition status is
  `unavailable`.
- `disabled`: enrichment is disabled by configuration.

Disabled shape:

```json
"device_policies": {
  "schema_version": 1,
  "collection_status": "disabled",
  "prevention": null
}
```

## 5. Flow Architecture

### 5.1 `CollectAssets`

The current asset parser streams normalized records. Policy correlation needs
the AIDs before publication, so change only one page into a bounded two-stage
operation:

1. Fetch and parse one Discover page.
2. Materialize detached `JsonObject` records for that page only.
3. Preserve all existing filtering, counters, cursor, watermark, and boundary
   AID calculations.
4. Enrich the page's hosts as one policy unit.
5. Publish the existing NDJSON page.
6. Advance the checkpoint only after successful publication.
7. Release the page and continue.

Do not buffer multiple pages. A page is bounded by the current page-size
configuration (maximum 1,000).

Preserve:

- empty terminal-page behavior;
- month/date segmentation;
- last-seen watermark and boundary-AID deduplication;
- cursor-expiry re-anchoring;
- totals and progress reporting;
- existing target naming and publisher behavior.

### 5.2 `CollectFindings`

The correlated findings flow already buffers `freshHosts` into bounded AID
batches and performs one Spotlight scroll for each batch.

For each existing AID batch:

1. Enrich the hosts before creating `HostFindingsAccumulator` instances.
2. Perform one device assignment lookup for the batch.
3. Hydrate only run-cache misses.
4. Continue through the existing AID-scoped Spotlight scroll.
5. Preserve the existing record contract:

   ```json
   {
     "aid": "...",
     "chunk": 0,
     "isLastChunk": true,
     "findingsInChunk": 12,
     "host": {
       "...Discover properties...": "...",
       "device_policies": {}
     },
     "findings": []
   }
   ```

6. Let the existing deep clone reproduce the enriched host in every chunk.
7. Keep zero-finding host records.
8. Publish/checkpoint at the existing atomic AID-batch boundary.

If assignment lookup fails, do not start Spotlight for that unpublished batch.
Do not make policy requests per finding or per output chunk.

Keep current finding shaping exactly as production implements it:
`apps`, `suppression_info`, and `host_info` are removed. The probe's raw finding
payload is evidence, not a production shaping template.

## 6. Component Placement

Keep CrowdStrike-specific logic under FalconCollector, close to the flows, for
example:

```text
FalconCollector/
  Flows/
    Policies/
      FalconPreventionPolicyEnricher.cs
      FalconDevicePolicyClient.cs
      FalconPreventionPolicyClient.cs
```

Exact names may follow the nearest existing conventions. Responsibilities must
remain small:

- device client: batch request and response parsing;
- prevention client: definition request and response parsing;
- enricher: join, deduplication, cache coordination, and envelope construction;
- flows: select the bounded unit, invoke enrichment, publish, and checkpoint.

Extend `FalconUrls` for both new endpoints. Reuse the existing `SessionSpec`,
OAuth, HTTP/retry/rate-limit/circuit-breaker pipeline, logging, NDJSON
publisher, and recovery system. Do not create a separate `HttpClient`, token
manager, retry loop, or generic policy framework.

## 7. Cache and Memory

The cache lives for one collection run and is not persisted in a checkpoint.
Key it by Prevention Policy ID.

Cache only:

- resolved definitions;
- confirmed `not_found` results from successful 2xx responses.

Never cache:

- `unavailable`;
- transport exceptions;
- 429/5xx exhaustion;
- malformed responses;
- authorization failures.

The number of unique Prevention definitions is normally tiny compared with the
host and finding population. The material RAM change is bounded asset-page
buffering and the already-existing findings batch, not policy-ID deduplication.

## 8. Failure and Checkpoint Semantics

| Condition | Output | Checkpoint |
|---|---|---|
| Assignment lookup succeeds, no matching device/assignment | `complete`, `prevention: null` | Advance after publish |
| Definition returned | `complete`, `resolved` | Advance after publish |
| Definition 2xx omits requested ID | `complete`, `not_found` | Advance after publish |
| Definition transient failure exhausts request handling | `partial`, `unavailable` | Advance after publish |
| Assignment transient failure exhausts request handling | No output for affected page/batch | Do not advance; use existing Falcon recovery |
| Required endpoint returns 401/403 | Fail validation/run | Do not disguise as empty/partial |
| Malformed assignment/definition contract | Fail the unit/run | Do not silently misclassify |
| Enrichment disabled | `disabled`, `prevention: null` | Normal flow |

Assignment data is the relationship itself, so an unknown assignment is not an
honest empty policy envelope. Let the existing Falcon scheduled recovery
re-enter from the unchanged watermark/checkpoint. Do not add another attempt
counter or recovery budget.

Definition hydration is supplemental once the assignment is known. A transient
definition failure may publish as partial so the core asset/findings flow does
not depend on the definition endpoint's availability.

The current Falcon recovery framework owns progress-aware deferral and terminal
budget exhaustion. Do not invent a special “publish unavailable when recovery
budget ends” path.

No checkpoint format bump is expected because persisted traversal position does
not change. If implementation reveals a persisted shape change, stop and
justify the versioning and compatibility behavior before making it.

## 9. Access Probe and Dry Run

Extend the existing staged Falcon access probe:

1. obtain a representative managed Discover AID;
2. call device details for that AID;
3. if a Prevention assignment exists, call the Prevention definition endpoint;
4. preserve existing Spotlight probing where relevant.

If the tenant has no representative AID or Prevention assignment, report that
the definition permission could not be fully exercised. Do not invent an ID.
Dry run must not publish collection data.

## 10. Configuration

Add a default-enabled Falcon setting named consistently with existing settings;
the intended name is `EnablePreventionPolicyEnrichment`.

- omitted: enabled;
- `true`: enabled;
- `false`: attach the visible `disabled` envelope without calling policy
  endpoints.

Prefer a constant 100-ID definition chunk unless configurability is clearly
valuable. Do not add tuning knobs merely because a constant exists.

## 11. Implementation Sequence

1. Read `AGENTS.md`, `ai/README.md`, the repo-local collector flow/recovery/test
   skills, Falcon docs, and current Falcon code/tests.
2. Confirm the current asset and findings publish/checkpoint boundaries before
   editing.
3. Add endpoint URL builders and focused clients.
4. Add the run-scoped enricher/cache and exact JSON envelope.
5. Extend the access probe.
6. Integrate one bounded assets page.
7. Integrate one existing findings AID batch before Spotlight.
8. Add configuration mapping/default behavior.
9. Add contract, flow, failure, cache, and recovery regression tests.
10. Run formatting/build/targeted Falcon tests.
11. Run the repo-local collector final review and fix blocking findings.
12. Update touched docs and the task-state files.

## 12. Required Tests

### Clients and mapping

- device request body and batching;
- exact `aid` to `device_id` join;
- `resources: null`, empty, partial, and reordered results;
- extraction of Prevention only;
- unknown assignment fields preserved;
- nested policy definition preserved;
- multiple requested definitions and missing IDs;
- malformed response and cancellation behavior.

### Envelope and cache

- resolved, not-found, unavailable, empty, and disabled shapes;
- `complete`/`partial` derived consistently;
- existing `device_policies` replaced;
- no policy never drops a host;
- resolved and not-found cached;
- unavailable never cached;
- duplicate policy IDs hydrated once across hosts and bounded units.

### Assets flow

- policy metadata appears inside every emitted asset;
- one assignment call per non-empty page;
- only one Discover page is materialized at a time;
- assignment failure produces no page publication/checkpoint;
- definition failure publishes partial and checkpoints;
- watermark, boundary AIDs, cursor recovery, counts, and terminal-page behavior
  remain unchanged.

### Findings flow

- policy metadata appears inside `host` in every correlated chunk;
- same source host is canonically equal between assets and findings outputs;
- one assignment call per existing AID batch;
- no request per finding or chunk;
- policy cache works across batches/pages;
- zero-finding hosts remain emitted;
- assignment failure occurs before Spotlight;
- existing finding stripping, chunking, AID grouping, prefetch, atomic publish,
  and checkpoint behavior remain unchanged.

### Access and configuration

- required endpoint probe sequencing;
- no representative assignment behavior;
- 401/403 is not converted to empty/partial;
- setting omitted/true/false behavior.

Update the closest existing Falcon test fixtures, including URL, access probe,
collector, correlated findings, and recovery-continuation tests, while placing
new policy-focused tests beside the new components.

## 13. Acceptance Criteria

- Every published Discover asset in both supported flows has exactly one
  `device_policies` envelope.
- Only Prevention Policy is collected.
- A successful lack of assignment publishes a complete empty envelope.
- Assignment uncertainty never advances an unpublished unit's checkpoint.
- Definition unavailability publishes a partial, truthful envelope.
- Cache semantics cannot turn an outage into `not_found`.
- Asset and findings-host policy shapes are identical.
- Existing finding output, traversal, recovery, and publication contracts are
  preserved.
- No standalone policy records or unrelated policy families are introduced.
- Falcon targeted tests and build pass.
- No credentials or tenant secrets appear in source, logs, fixtures, or final
  reports.

## 14. Evidence Available to the Implementer

Local research:

- `/Users/user/Dev/crowdstrikepolicies/docs`
- `/Users/user/Dev/crowdstrikepolicies/ssp`

End-to-end lab probe:

- `/Users/user/Dev/Uri/localprojects/IntegrationProbes/Integrations/Falcon`
- `/Users/user/Dev/Uri/localprojects/IntegrationProbes/ProbeResults/FalconPreventionPolicyFlow_20260726_131018_150Z`

The probe demonstrated one host with:

- AID `0b494393291945c7be1cee66e8c73802`;
- Prevention Policy `Phase 3 - optimal protection`;
- policy ID `8f590b7a2c95462da6278527f162aa94`;
- 19 setting groups and 64 settings;
- 1,164 Spotlight findings.

It also verified that the canonical host in the assets output equals the host in
the findings output. Treat the probe as API-shape evidence. Do not copy its raw
finding payload over the collector's existing production shaping.

