# Claude Implementation Prompt — Falcon Prevention Policy Enrichment

You are a senior .NET engineer implementing a focused extension to
FalconCollector in:

`/Users/user/Dev/cymulate-integration-adapters`

Implement the task fully: production code, tests, relevant documentation, and
verification. Do not merely produce another plan.

## Source of truth

Read these first, in this order:

1. `AGENTS.md`
2. `ai/README.md`
3. `ai/active/2026-07-26_1655_falcon-prevention-policy-enrichment/state.json`
4. every Markdown file in that task directory
5. `FALCON_PREVENTION_POLICY_IMPLEMENTATION_PLAN.md`
6. the repo-local collector flow, recovery, and test skills referenced by
   `ai/README.md`
7. the current FalconCollector docs, implementation, and closest tests

`FALCON_PREVENTION_POLICY_IMPLEMENTATION_PLAN.md` is authoritative.
`FALCON_POLICY_COLLECTION_PLAN.md` is older broad research and is explicitly
superseded for delivery. Do not implement its multi-family scope.

## Goal

Enrich every Discover host emitted by both `CollectAssets` and
`CollectFindings` with its CrowdStrike Prevention Policy assignment and the
full Prevention Policy entity. Embed the metadata as `device_policies` inside
the asset/host body. Do not create a standalone policy flow or output.

## Non-negotiable scope

- Prevention Policy only.
- Existing Assets and correlated Findings flows only.
- No other policy families.
- No detections/alerts/incidents.
- No recursive rule-group hydration.
- No parser/database/UI work.
- No generic policy framework.
- Do not alter production finding shaping, chunking, or contract except for the
  enriched `host`.
- Do not modify Shared infrastructure unless you first prove the existing
  Falcon/Shared mechanisms cannot express the behavior.

## Endpoint sequence

For each bounded Discover host unit:

1. Existing `GET /discover/combined/hosts/v1`.
2. Batched `POST /devices/entities/devices/v2` with
   `{"ids":["<aid>", ...]}`.
3. Join Discover `aid` to device `device_id`.
4. Extract only `device_policies.prevention`.
5. Deduplicate policy IDs and hydrate run-cache misses with
   `GET /policy/entities/prevention/v1?ids=<id>`, using repeated `ids`
   parameters and conservative chunks of at most 100.
6. In Findings, then execute the existing AID-scoped Spotlight flow.

Use the existing Falcon session, OAuth, HTTP retry/rate-limit/circuit-breaker,
logging, publisher, checkpoint, and recovery components. Do not add a custom
`HttpClient`, token manager, retry loop, or attempt counter.

## Exact output behavior

Every host gets one root `device_policies` property. Replace an existing
property; never create duplicate JSON properties.

Resolved:

```json
"device_policies": {
  "schema_version": 1,
  "collection_status": "complete",
  "prevention": {
    "assignment": {
      "...raw assignment fields, including unknown future fields...": "..."
    },
    "definition_status": "resolved",
    "definition": {
      "...raw policy entity including nested prevention_settings...": "..."
    }
  }
}
```

Successful no assignment/unmanaged/unmatched/null resources:

```json
"device_policies": {
  "schema_version": 1,
  "collection_status": "complete",
  "prevention": null
}
```

Disabled:

```json
"device_policies": {
  "schema_version": 1,
  "collection_status": "disabled",
  "prevention": null
}
```

Definitions:

- successful returned ID: `definition_status: "resolved"`;
- successful 2xx missing requested ID: `"not_found"`;
- exhausted transient definition failure: `"unavailable"`.

For not-found/unavailable, preserve the assignment and set `definition: null`.
Derive `collection_status`: complete for empty/resolved/not-found; partial for
unavailable; disabled only when configured off.

Preserve raw assignment and policy definition fields. Keep `rule_groups` as IDs.
Never drop a host because policy is absent.

## Assets integration

The current asset parser streams. Convert one Discover page only into a bounded
two-stage operation:

1. materialize detached normalized host `JsonObject`s for one page;
2. preserve all current filters/counters/watermarks/boundary AIDs/cursor state;
3. enrich the page as a unit;
4. publish using the existing NDJSON path;
5. checkpoint only after successful publication;
6. release the page.

Never buffer multiple pages. Preserve empty terminal pages, segmentation,
cursor re-anchoring, counts, output naming, and resume behavior.

## Findings integration

Use the existing `freshHosts` AID-batch boundary:

1. enrich the batch before constructing `HostFindingsAccumulator` and before
   calling Spotlight;
2. one device lookup per batch, not per host/finding/chunk;
3. hydrate only cache misses;
4. keep existing Discover next-page prefetch;
5. keep existing Spotlight scroll, finding stripping, grouping, chunking,
   zero-finding envelopes, atomic batch publication, and checkpoint behavior;
6. let the existing deep clone copy the enriched host into every chunk.

The same source host must be canonically equal in Assets output and in the
Findings record's `host`. The findings record stays:

`{ aid, chunk, isLastChunk, findingsInChunk, host, findings[] }`.

The lab probe retained raw `apps`, `suppression_info`, and `host_info`; the
production collector removes them. Preserve production behavior.

## Failure semantics

- Assignment lookup success with no match/assignment: publish complete empty.
- Assignment transient failure after request retry exhaustion: publish nothing
  for the affected page/batch, do not checkpoint it, and throw into existing
  Falcon scheduled recovery. In Findings, do not call Spotlight for that batch.
- Definition transient failure after request retry exhaustion: retain the
  assignment, publish partial/unavailable, and checkpoint normally.
- 401/403 for required endpoints: fail validation/run; never degrade to empty or
  partial.
- Malformed vendor contract: fail rather than silently inventing status.
- Do not add custom recovery counts. Existing progress-aware Falcon recovery
  owns deferral and terminal exhaustion.

Cache only resolved definitions and confirmed 2xx not-found results. Never
cache unavailable, exceptions, malformed responses, or authorization failures.
The cache is run-scoped and not checkpointed.

## Configuration and access probe

Add a default-enabled setting following current Falcon conventions, intended
name `EnablePreventionPolicyEnrichment`. When false, make no policy endpoint
calls and attach the visible disabled envelope.

Extend staged access probing using a representative managed AID:

- Discover representative host;
- device details;
- Prevention definition when an assignment exists;
- existing Spotlight probe as applicable.

If the tenant has no representative assignment, report that the definition
scope could not be fully exercised; do not fabricate an ID. Required endpoint
401/403 must not pass.

## Engineering constraints

- Inspect `git status` first and preserve all user changes.
- Keep the collector shell thin and vendor logic under Falcon, preferably close
  together under `Flows/Policies/`.
- Use small methods/classes with single responsibilities and no forced
  abstraction.
- Extend `FalconUrls` for endpoint construction.
- Preserve checkpoint formats unless persisted traversal state truly changes.
  No bump is expected.
- Do not stage, commit, push, or alter credentials.
- You may use the local lab probe and `appsettings.local` for verification, but
  never print, copy, log, or commit secrets.
- Update task `state.json` and execution notes as work progresses.

## Required verification

Add focused tests that lock:

- request bodies, query construction, batching, cancellation, and malformed
  response behavior;
- exact `aid`/`device_id` join and Prevention-only extraction;
- null/empty/partial/reordered device resources;
- raw assignment and nested definition preservation;
- empty/resolved/not-found/unavailable/disabled envelopes;
- derived collection status;
- cache dedup across hosts/pages/batches and no caching of unavailable;
- no-policy host preservation;
- assignment failure means no publish/checkpoint and existing recovery;
- definition failure means partial publish/checkpoint;
- Assets enrichment once per page without changing traversal semantics;
- Findings enrichment once per existing AID batch before Spotlight;
- enriched host in every chunk and canonical host parity between flows;
- unchanged finding shaping, chunking, zero-finding behavior, prefetch,
  publication, watermark, cursor recovery, and checkpointing;
- access probe and configuration behavior;
- 401/403 behavior.

Run formatting if required by repository convention, build the affected
projects, run the targeted Falcon test suite, and perform the repo-local final
collector review. Fix blocking findings.

## Evidence

API/docs:

- `/Users/user/Dev/crowdstrikepolicies/docs`
- `/Users/user/Dev/crowdstrikepolicies/ssp`

Probe:

- `/Users/user/Dev/Uri/localprojects/IntegrationProbes/Integrations/Falcon`
- `/Users/user/Dev/Uri/localprojects/IntegrationProbes/ProbeResults/FalconPreventionPolicyFlow_20260726_131018_150Z`

The probe is API-shape evidence, not a production-code template.

## Finish condition and response

Finish only when implementation, required tests, targeted build/tests, final
review, touched documentation, and task state are complete.

Report:

1. behavior implemented;
2. files changed;
3. exact verification commands and results;
4. any remaining risks or assumptions.

Do not widen scope. Do not stop at a plan. Ask a question only if a genuinely
missing fact would force a contract-breaking guess after local code/docs/probe
evidence has been exhausted.

