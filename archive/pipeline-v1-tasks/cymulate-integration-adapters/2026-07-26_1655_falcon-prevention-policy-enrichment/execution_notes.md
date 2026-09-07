# Execution Notes

Implementation complete. Branch `feature/falcon-prevention-policy-enrichment`; nothing staged,
committed, or pushed.

## What was built

New `Collectors/FalconCollector/Flows/Policies/`:

- `FalconDevicePolicyClient` — batched `POST /devices/entities/devices/v2`, body written with a
  `Utf8JsonWriter` so the wire property stays lowercase `ids`; response streamed and projected
  down to `device_policies.prevention` keyed by `device_id`.
- `FalconPreventionPolicyClient` — `GET /policy/entities/prevention/v1` with repeated `ids`
  parameters, chunked at `MaxIdsPerRequest = 100`. Never invents a status: the caller compares
  requested vs returned IDs.
- `FalconPreventionPolicyCache` — run-scoped, keyed by policy ID. A stored `null` is a confirmed
  2xx not-found; absence means unknown. `SelectMisses` does the dedup.
- `FalconDevicePoliciesEnvelope` — `PreventionDefinitionStatus` enum plus the only place the wire
  vocabulary is spelled. `Attach` uses the `JsonObject` indexer, so an existing `device_policies`
  is replaced and can never be duplicated.
- `FalconPolicyEnricher` — the single shared enricher (one instance per run, owns the cache).
  Also defines `PolicyEnrichmentTarget(string? Aid, JsonObject Host)`, the unit both flows pass.

Touched:

- `FalconUrls` — `BuildDeviceEntitiesUrl`, `BuildPreventionPolicyUrl`.
- `FalconAssetsPageParser` — `EnumerateNormalizedRecordsAsync` (streaming) became
  `MaterializePageAsync` returning `List<JsonNode>`. Single caller, so the method was converted,
  not duplicated. Filtering/watermark/boundary-ID logic is byte-for-byte unchanged.
- `FalconAssetsScrollRunner` — materialize → enrich → publish. Everything after the publish call
  (TotalExpected, AfterToken, both terminal-page branches, `counters.Page--`, watermark advance,
  `OnPagePublished`, depth cap) is untouched and still runs in the original order.
- `FalconAssetsFlow` / `FalconFindingsFlow` — construct one enricher per run and call it at their
  bounded unit. Findings enriches inside the existing `freshHosts.Chunk(aidBatchSize)` loop,
  before the accumulators and before Spotlight.
- `AidExtractor` — added a `JsonObject` overload reusing the existing combined-`id` helper (the
  assets flow enriches materialized nodes; findings reuses the AID the scroller already extracted).
- `FalconAccessProber` — `ProbePreventionPolicyAsync`: Discover representative host → device
  details → definition only when an assignment exists. Wired into `InitializeAsync` after the
  hosts probe, gated on the config switch.
- Config: `EnablePreventionPolicyEnrichment` capability constant (default `true`) on
  `FalconCollectorConfiguration`, threaded through `FalconFlowRunPreparer` into both flow run
  configs, with an optional `enablePreventionPolicyEnrichment` metadata override mirroring the
  existing `batchScopedStorage` pattern.

## Decisions taken during execution

1. **POST body via `Utf8JsonWriter`, content as `StringContent`.** `JsonContent.Create` of a
   record would emit `"Ids"`, and `StringContent` is a buffered byte array, so it survives the
   session's Polly retries. Matches the CortexXdr/InsightVmCloud house style.
2. **Assets page materialized as `List<JsonNode>`, not `List<JsonObject>`.** The streaming
   implementation emitted every surviving row including non-objects; `JsonNode` preserves that
   exactly, and only object rows become enrichment targets.
3. **`TotalExpected`/`AfterToken` assignments left after the publish call.** Moving them earlier
   would have changed the TotalItems visible on the first page's progress event. Keeping them in
   place makes the diff behavior-preserving.
4. **Assets rows now serialize via `JsonNode.ToJsonString()`** (was
   `NormalizedUtf8Json.SerializeToSingleLine(JsonElement)`) — the same path
   `FalconCorrelatedRecord` already uses, which is what makes cross-flow byte parity hold. Proven
   by `Policy_SameSourceHost_IsCanonicallyEqualAcrossAssetsAndFindings`.
5. **Assignment and definition are deep-cloned per host.** A `JsonNode` has one parent, and the
   cached definition is shared by every host on that policy; without the clone the second host
   would throw.
6. **Definition-outage classification is narrow:** only `HttpRequestException` whose status is
   not 401/403. Cancellation and malformed-contract exceptions are different types and propagate
   untouched, so neither can be laundered into `unavailable`.
7. **Judgment beyond the plan's table — assignment present but no readable `policy_id`.** The raw
   assignment is preserved and `definition_status` is `not_found` (there is no ID to hydrate, and
   nothing was requested, so `unavailable` would falsely imply retryability). Failing the whole
   page over one odd host would be disproportionate. A non-object `prevention` is still a hard
   failure. Covered by `AssignmentWithoutPolicyId_KeepsTheAssignment_AndReportsNoDefinition`.
8. **Metadata override added for the switch.** The plan wanted omitted/true/false honored;
   `batchScopedStorage` shows the house pattern is a hardcoded capability-constant default plus
   an optional override. This also makes the disabled path testable end to end.
9. **Existing flow fixtures now route the device endpoint.** Enrichment is on by default, so 16
   `FalconCollectorTests` factories and the `FalconCorrelatedFindingsTests` factory needed a
   neutral "no assignments" response. This was a harness gap, not a production concern — no
   production behavior was weakened to make tests pass.

## Verification

All commands run from `src/Cymulate.Integration.Adapters`.

| Command | Result |
|---|---|
| `dotnet build Collectors/FalconCollector/Cymulate.Integration.Adapters.Collectors.FalconCollector.csproj` | Build succeeded |
| `dotnet build Cymulate.Integration.Adapters.sln` | Build succeeded |
| `dotnet test ...FalconCollector.Test.csproj --filter "FullyQualifiedName~FalconPolicyEnrichmentTests"` | 36/36 passed |
| `dotnet test ... --filter "FalconPolicyEnrichmentTests\|FalconUrlsTests\|FalconAccessProberTests\|FalconCorrelatedFindingsTests\|FalconCollectorTests\|FalconCollectorConfigurationBuilderTests\|FalconResumeRunnerTests\|FalconRecoveryContinuationTests\|FalconTacticalLoggingTests"` | **135 passed, 1 skipped, 0 failed** |

The single skip is pre-existing. The full FalconCollector suite was deliberately NOT run (the
risky simulation classes hang in this harness); the filter above covers every class touched by
this change plus the assets-resume and recovery-continuation guards.

Tests added: 36 in the new `FalconPolicyEnrichmentTests`, 5 assets-flow tests in
`FalconCollectorTests`, 5 findings-flow tests in `FalconCorrelatedFindingsTests`, 5 staged-probe
tests in `FalconAccessProberTests`, 3 config tests in `FalconCollectorConfigurationBuilderTests`.

Vendor shapes were taken from the lab probe captures
(`FalconPreventionPolicyFlow_20260726_131018_150Z`): device `device_id` equals the Discover
`aid`, the assignment carries `policy_type/policy_id/applied/settings_hash/assigned_date/
applied_date/rule_groups`, and the definition is ~32 KB with 19 setting groups plus vendor-expanded
`ioa_rule_groups`. The emitted envelope matches `collect-assets-output.json` exactly.

## Documentation updated

- `FalconDocs/CollectorDocs/04-architecture.md` — page materialization and the shared-enricher
  section (placement, per-flow call sites, failure asymmetry, config switch).
- `FalconDocs/CollectorDocs/05-testing.md` — coverage map rows for the policy tests and the
  extended access probe.
- `ai/skills/collector-flow-patterns/SKILL.md` — shared cross-flow host enrichment pattern, plus
  hard rules for call budget, failure split, envelope self-description, and the
  streaming-vs-materialization tradeoff.
- `ai/skills/collector-tests/SKILL.md` — new fixture reference and hard rules for testing
  enrichment (call budget, cache, harness-gap rule, cross-flow parity).

## Review passes and repairs

Verifier verdict: PASS WITH GAPS (`review/verifier-1.md`). Code review: no blockers, 2 major
findings (`review/code-reviewer-1.md`). Both reviewers independently flagged the same real defect.

Repaired:

1. **Over-broad definition-outage classification** (verifier LOW-MED #5 + code-review MAJOR #1 —
   convergent, so treated as confirmed). `IsTransientDefinitionOutage` degraded *every* non-401/403
   status, so a deterministic 400/404/422 was reported as `unavailable`; combined with the correct
   never-cache-an-outage rule, a persistent request error would re-pay a full Polly retry cycle per
   bounded unit for the whole run while emitting `partial` everywhere. Now only 5xx, 429, and a
   status-less transport fault qualify; everything else propagates and fails the run. Locked by
   `OnlyServerSideUnavailability_DegradesDefinitionToUnavailable` (4 statuses) and
   `DeterministicDefinitionRequestErrors_Fail_RatherThanMasqueradingAsAnOutage` (3 statuses).
2. **Null-resource-row regression** (code-review MINOR #3). Materialization threw on a bare `null`
   element where the streaming path emitted it verbatim, failing the whole page. Page rows are now
   `List<JsonNode?>` and a null row round-trips to the literal `null` line. Locked by
   `ProcessAsync_Assets_NonObjectResourceRow_IsStillEmittedVerbatim`.
3. **Connection test did not cover the new permissions** (verifier LOW-MED #3).
   `FalconConfigurationValidationService` kept its own inline probes, so a client missing
   Prevention-Policies read passed "Test Connection" and only failed at the first run's init. It now
   reuses `FalconAccessProber.ProbePreventionPolicyAsync` (no new endpoint, no duplicated logic) and
   adds a permissions hint on 401/403 from either policy endpoint.
4. **Missing §12 test: only one page materialized at a time** (verifier MEDIUM #1). Added
   `ProcessAsync_Assets_MaterializesOnlyOnePageAtATime`, which records how many pages had been
   published at each page fetch and asserts strict `0,1,2` interleaving — a buffering or prefetching
   implementation would show `0,0,0`.
5. **Missing assertion: checkpoint not advanced on assignment failure** (verifier MEDIUM-LOW #2).
   Asserted as "no checkpoint claims progress" (Page 0, TotalItems 0, null watermark) rather than
   "no checkpoint at all", because recovery legitimately snapshots state.
6. **Cosmetic**: dangling `<see cref="FalconPolicyFlowTests"/>` replaced with the real fixture
   pointers; endpoint URL assertions moved into `FalconUrlsTests` where §12 expects them.

Reviewed and deliberately NOT changed:

- **Device-endpoint 404 on unresolvable AIDs** (code-review MAJOR #2, self-labelled "vendor behavior
  unverified"). The concern is that the assets flow can derive an AID from the 32-hex-suffix
  heuristic, and a 404 would then fail the page. I searched the local CrowdStrike docs/probe
  captures and found **no evidence** either way about that endpoint's unknown-ID behavior (the probe
  shows the documented shape: 200 with `errors: null`). Mapping 404 → "no assignments" on
  speculation would directly violate the plan's rule that a failure must never be disguised as an
  empty result, so it stays unchanged and is listed as a residual risk instead.
- `JsonNode.Parse(x.GetRawText())` detach cost (code-review MINOR #4). `JsonObject.Create(el.Clone())`
  would avoid the string copy, but the existing `FalconDiscoverHostScroller` uses exactly this
  pattern; diverging for a micro-optimization costs house-style consistency. Noted, not taken.
- Disabled switch still materializes the page and still writes a `disabled` envelope (MINOR #5).
  Intentional: materialization is now the page pipeline itself, and the visible disabled envelope is
  required by plan §4.4. The switch is a policy-*call* switch, and is documented as such.
- The envelope replacing a vendor-supplied `device_policies` (MINOR #6) is explicitly mandated by
  plan §4.1 ("replace it with this collector-owned envelope").

Post-repair verification: `dotnet build Cymulate.Integration.Adapters.sln` succeeded; the
change-relevant filter now reports **146 passed, 1 pre-existing skip, 0 failed** (was 135).

## Live run 2026-07-26 18:30 — BLOCKED, root cause not yet fully isolated

`logs/local-adapter-runner-20260726-183026.log`, correlation
`e1b72bd4-0321-4a6e-a639-d0351cb7a29b`, findings flow, real tenant. Run failed;
`status: "failed"`, 0 assets, 0 findings, no batch published.

Sequence observed:

1. Access probes fine — Discover `limit=1` 200, Spotlight `limit=1` 200.
2. Staged policy probe fine — `POST /devices/entities/devices/v2` with **1** AID → **200**,
   then `GET /policy/entities/prevention/v1?ids=bbfbd7b0…` → **200**. So auth, permissions,
   region, request shape and the definition endpoint are all confirmed good against the live tenant.
3. Discover page (`limit=1000`) → 200.
4. First aid-batch, `Aids=250` → **HTTP 400**, not retried ("status is not retryable"), which
   propagated exactly as designed: nothing published, no checkpoint, run failed.

The failure mode is therefore correct-by-design behavior on an unexpected vendor response — the
enrichment is refusing to publish hosts whose policy it could not resolve. The defect is that we
send a request the vendor rejects.

**Key evidence:** the 400 body is not a plain rejection — it carries a populated `resources`
array with a complete, valid device entity (including `device_policies.prevention`). The vendor
processed the request and objected to part of it.

Two candidate causes remain:

- **A — per-request ID cap below 250.** Would invalidate the "one device lookup per bounded unit"
  design; note the assets flow would send up to 1,000, so it is worse there. Argues against A:
  a cap violation would normally return no resources at all.
- **B — one or more submitted IDs the vendor rejects.** This is exactly the risk the code reviewer
  raised as MAJOR #2 and I declined to act on for lack of evidence; the populated-`resources`
  shape now favours it. Candidate source: `AidExtractor` accepts the `aid`/`device_id` fields
  verbatim (any non-blank string) and accepts a 32-hex suffix of the combined `id`, which its own
  comment notes is not a real AID for unmanaged Discover entities. Discover is queried with
  `facet=third_party`, so unmanaged/third-party entities are in the page.

The `errors` list that would name the cause sits after `resources` in the body and was cut by the
2,000-char snippet budget. Changed: policy requests now use their own `AdapterHttpClient` with a
16,000-char budget (`FalconPolicyEnricher.PolicyErrorSnippetChars`), so the next run prints the
vendor's per-ID errors. `FalconPolicyEnricher` now takes `IHttpSession` instead of an
`AdapterHttpClient` to own that budget; both flows pass their session.

**Deliberately not "fixed" yet.** The two plausible remedies pull in opposite directions and one of
them can silently corrupt output:

- chunking the device lookup fixes A but not B (a rejected ID still fails its chunk);
- tolerating a partial-success 400 by consuming `resources` fixes B's symptom, but if the vendor
  omits *valid* hosts from a rejected batch, those hosts would publish as "no policy" when they
  actually have one — a silent false negative, which is precisely what the plan's
  never-disguise-a-failure rule exists to prevent.

Choosing between them without the `errors` payload would be guessing. Next step is one re-run to
capture it.

### Second run 2026-07-26 18:39 — same 400, and it exposed a Shared defect

`logs/local-adapter-runner-20260726-183902.log`, correlation
`28d90120-9271-4561-bd5c-19366f869e72`. Identical failure: `Aids=250` → 400. The raised 16,000-char
budget delivered only ~3.7 KB, which is why the `errors` list still never appeared.

**Root cause of the blindness — a pre-existing bug in Shared.**
`StreamedResponseBodyReader.ReadFirstCharsAsync` performed a SINGLE `reader.ReadAsync`, which returns
as soon as any data is available. On a chunked network response that is one buffer, so
`maxErrorSnippetChars` was an upper bound that was essentially never reached: every vendor error body
in every collector has been silently cut to roughly one buffer, not to the configured budget.

Fixed to loop until the budget is filled or the stream ends. This is the one Shared change in this
task, and it meets the contract's bar — the existing mechanism provably could not express its own
documented behavior. Proven both ways: the new `StreamedResponseBodyReaderTests` fails against the old
implementation (512 chars returned for a 16,000 budget) and passes against the fix. The other consumer
(`MicrosoftGraphClient`) only gains longer, complete error snippets.

Even with the fix, a 250-AID response is ~1 MB (a device entity is ~3.8 KB), so `errors` still will
not fit a sane log budget. The remaining diagnostic step must therefore shrink the request, not grow
the budget: run one findings collection with `aidBatchSize` set to 2–3 so the whole response, `errors`
included, lands inside the 16,000-char budget. Set it under
`Collectors:FalconCollector:Credentials` in `appsettings.local.json` (the loader turns that section
into the collector config dict, and `FalconCollectorConfigurationBuilder` reads `aidBatchSize` from it).

Outcomes and what each implies:

- small batch **succeeds** → cause A (per-request ID cap); chunk the device lookup, and note the
  assets flow sends up to 1,000 IDs so it is the more exposed of the two flows;
- small batch **fails with a visible `errors` list** → cause B; the list names the offending IDs, and
  the fix is to stop sending identifiers that are not real AIDs (`AidExtractor` currently accepts the
  `aid`/`device_id` fields verbatim and a 32-hex suffix of the combined `id`).

## ROOT CAUSE CONFIRMED 2026-07-26 — we send identifiers that are not agent IDs

Settled by documentation plus three live experiments. No inference left.

**The ID cap is not the problem.** FalconPy's endpoint table, generated from CrowdStrike's OpenAPI
spec, documents `PostDeviceDetailsV2` / `POST /devices/entities/devices/v2` as
"Supports up to a maximum 5000 IDs" (the GET variant on the same path caps at 100 — we use POST).
250 is 20x under the limit. The plan's "5,000" assumption was *correct about the cap* but was
sourced from a `batch_size: int = 5000` default in `crowdstrike_poc/build_manifest.py` that the POC
never exercised: it samples `sample_hosts_per_policy: int = 5`, so it only ever sent tens of IDs —
and it sourced them from `query_combined_policy_members`, i.e. real managed sensor AIDs. The POC also
never performs the Discover-host → device-entity direction at all (its only Discover use goes the
other way, querying Discover *applications* by known `host.aid`). So this direction was never
validated by anything except the one-managed-host lab probe.

**What is actually wrong.** `AidExtractor.ExtractAid` falls back to the 32-hex suffix of Discover's
combined `id` when no `aid` field is present. For unmanaged and unsupported Discover entities that
suffix is a well-formed 32-hex string that is NOT an agent ID — it is Discover's own entity hash.
CrowdStrike rejects it verbatim: `{"code":400,"message":"invalid device id [<value>]"}`.

Live audit of the failing window (`last_seen >= 2026-06-26`, 375 Discover hosts):

| entity_type | hosts | identifier source |
|---|---|---|
| managed | 41 | real `aid` field |
| unmanaged | 285 | `id`-suffix heuristic (1 skipped, no usable id) |
| unsupported | 49 | `id`-suffix heuristic |

374 identifiers were being sent, of which **333 (89%) were not device IDs**. Overlap between the
managed-`aid` set and the heuristic set is **zero**, so the `aid` field is a clean discriminator:
present ⟺ managed.

Causation experiments against the live endpoint:

- **A** — the 41 real managed AIDs → **HTTP 200**, 41 resources, 0 errors.
- **B** — 2 heuristic identifiers → **HTTP 400**, two `invalid device id` errors.
- **C** — 1 managed + 1 heuristic → **HTTP 400** with 1 resource AND 1 error, reproducing the exact
  production shape (partial resources alongside the rejection) and proving one bad ID poisons an
  otherwise-valid batch.

**My earlier hypothesis was wrong in its mechanism.** I predicted malformed/non-hex identifiers;
every identifier is canonical 32-hex. The values are well-formed but refer to non-sensor entities.
The code reviewer's MAJOR #2 was right as originally stated, and my refinement of it was not.

**Fix (not yet implemented, awaiting go-ahead).** Only identifiers that came from the real
`aid`/`device_id` field may reach the device endpoint; a host whose AID exists only via the
`id`-suffix heuristic gets the `complete` / `prevention: null` envelope with no vendor call. This is
semantically correct rather than a workaround: an unmanaged or unsupported asset has no Falcon
sensor, therefore no prevention policy, and plan §4.2 already classifies that as a successful empty
result. It also cuts the device-lookup payload by ~89% in this window.

Corollaries:

- Do NOT chunk the device lookup — the documented cap is 5000 and both bounded units (≤1000 assets
  page, ≤250 AID batch) sit far below it. Chunking would only spread bad IDs across more requests.
- Do NOT add 400-tolerance. Once only real AIDs are sent, a 400 is a true anomaly and must keep
  failing loudly.
- The heuristic AIDs are also fed to Spotlight filters by the pre-existing findings flow, where they
  are wasteful but harmless (an unmanaged entity simply matches no findings). Only the Hosts device
  endpoint validates them, which is why this surfaced now.

## FIX IMPLEMENTED AND VERIFIED LIVE — 2026-07-26 19:19

Change: only an explicitly-stated AID may reach the device endpoint.

- `AidExtractor.ExtractSensorAid` (new, `JsonObject` + `JsonElement`) returns the AID only from the
  `aid`/`device_id` fields and never derives one from the combined `id`. `ExtractAid` keeps the
  derivation and stays the best-effort value used for Spotlight scoping, where a wrong guess is
  harmless. The two confidence levels are now documented on the type.
- `DiscoverHost` gained `SensorAid`; the Discover scroller populates it. The findings flow passes
  `SensorAid` to the enricher while Spotlight keeps using `Aid` — so pre-existing traversal behavior
  is untouched.
- Assets `BuildPolicyTargets` uses `ExtractSensorAid`.
- `FalconAccessProber` now asks Discover for a managed entity specifically
  (`+entity_type:'managed'`, limit 1) and extracts via `ExtractSensorAid`. This closes a latent bug
  in my own earlier code: the probe took whichever host sorted first, which in a real tenant is
  almost always unmanaged, so the probe would itself have 400'd. The filter was confirmed against
  the live tenant before use (HTTP 200, one managed host with an `aid`).
- Probe duplication removed: the staged policy probe now runs only in
  `FalconConfigurationValidationService` (which precedes init on every run AND is the connection-test
  entry point), not also in `InitializeAsync`. Per-run probe calls to the policy endpoints drop from
  2+2 to 1+1.

### Live verification (real tenant, `--base-date 2026-06-26`)

`CollectFindings` — **Success=True, Hosts=374, Findings=78933**:

| metric | value |
|---|---|
| records / distinct aids / chunk-0 records | 396 / 374 / 374 |
| collection_status | `complete` x396 (no partial, no disabled, none missing) |
| prevention | `resolved` x63 records, `null` x333 |

The counts reconcile exactly with the audit: 41 managed hosts spread over 63 records (22 extra
chunks) plus 333 unmanaged/unsupported hosts at one record each = 396; 41 + 333 = 374 hosts. The
sampled unmanaged host is `deda4f86d4b43849bf818b9f48705c09` — the exact identifier CrowdStrike
rejected as "invalid device id" in experiment B — now correctly carrying the empty envelope. A
sampled managed host resolved to `platform_default` with 19 setting groups / 64 settings.

`CollectAssets` — **Success=True, Total=375** (two pages; live churn added one host):

| metric | value |
|---|---|
| entity_type | managed 41, unmanaged 285, unsupported 49 |
| collection_status | `complete` x375 |
| prevention | `resolved` x41 (every managed host), `null` x334 |
| records with a duplicated `device_policies` property | 0 |

Tests: 152 passed, 1 pre-existing skip, 0 failed; solution build clean. New tests lock the
managed/unmanaged split using the real identifier shapes observed live
(`UnmanagedEntity_IsNeverSentToTheDeviceEndpoint_AndStillGetsAnEmptyEnvelope`,
`MixedUnit_SendsOnlyTheManagedAids_SoOneUnmanagedEntityCannotPoisonTheBatch`,
`SensorAidExtraction_TrustsOnlyExplicitlyStatedAids`).

## Residual risks

0. **Device endpoint 404 semantics are unverified** (see above). If
   `POST /devices/entities/devices/v2` answers 4xx rather than 200-with-empty-resources when none of
   the submitted AIDs resolve, an unmanaged-heavy assets page whose AIDs came from the combined-`id`
   heuristic would fail the run where it previously published. First live run against a tenant with
   unmanaged Discover entities will settle it; if confirmed, the fix is a narrow 404 → "no
   assignments" mapping in `FalconDevicePolicyClient`.
1. **Memory.** A full 1,000-host assets page where every host has a distinct fat policy holds
   ~32 MB of definition clones plus the page itself. Real tenants share a handful of policies, and
   the plan explicitly accepted bounded page buffering, but this is the one new memory
   characteristic. The egress heap defenses are unchanged.
2. **Not exercised against a live tenant.** Every vendor interaction is verified against the probe
   captures and unit fixtures; no local run against the lab tenant was performed.
3. **Checkpoint formats untouched**, as expected — persisted traversal state did not change, so no
   version bump. No csproj version was bumped (operator's call).
