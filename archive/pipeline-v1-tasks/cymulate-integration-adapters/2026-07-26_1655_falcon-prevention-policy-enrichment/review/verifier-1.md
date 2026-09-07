# Verifier 1 — Falcon Prevention Policy Enrichment

Verified by independent inspection of the working tree, plus an independent build and filtered
test run. Claims in `execution_notes.md` were re-derived from source, not accepted.

## 1. Verdict

**PASS WITH GAPS** — the mainline contract (envelope shapes, failure asymmetry, call budget,
cache semantics, cross-flow parity, traversal preservation) is really implemented and really
tested; the gaps are three missing/weak test assertions, two edge-case classification calls, and
one un-extended connection-test path. No mainline functional defect found.

---

## 2. Success-criteria coverage

Paths below are relative to `/Users/user/Dev/cymulate-integration-adapters/src/Cymulate.Integration.Adapters/`
unless absolute.

| Success criterion | Covered | Evidence |
|---|---|---|
| Every emitted Discover host in both flows has exactly one `device_policies` envelope; existing property replaced, never duplicated | YES | `Collectors/FalconCollector/Flows/Policies/FalconDevicePoliciesEnvelope.cs:85` (`host[PropertyName] = envelope`, indexer overwrite). Enricher attaches to **every** target on both the assignment and no-assignment branch: `FalconPolicyEnricher.cs:82`, `:87`. Tests: `ExistingDevicePoliciesProperty_IsReplaced_NotDuplicated`, `ProcessAsync_Assets_EnrichesEveryPublishedHost_WithOneDeviceLookupPerPage` (asserts all 3 published records), `Policy_EnrichedHost_AppearsInEveryChunkOfAMultiChunkHost`. I additionally confirmed empirically that `JsonNode` indexer replacement emits a single property. |
| No-policy hosts are retained | YES | `FalconPolicyEnricher.cs:80-84` attaches `Empty()` and continues; nothing filters. Tests: `SuccessfulLookupWithNoResources_PublishesCompleteEmptyEnvelope` (3 theory cases), `ProcessAsync_Assets_HostWithoutPreventionAssignment_IsStillPublished`. |
| Asymmetric assignment/definition failure contract | YES | Assignment: no catch anywhere in `FalconPolicyEnricher.EnrichAsync`; exception propagates out of `FalconAssetsScrollRunner.cs:279` **before** `PublishAssetsUtf8PageAsync` and out of `FalconFindingsFlow.cs:201-203` **before** accumulators/Spotlight. Definition: `FalconPolicyEnricher.cs:137-153` catches only `HttpRequestException` non-401/403 and marks that chunk `unavailable`. Tests: `ProcessAsync_Assets_WhenAssignmentLookupFails_PublishesNoPage`, `ProcessAsync_Assets_WhenDefinitionFails_PublishesPartialEnvelope_AndCompletes`, `Policy_AssignmentFailure_SkipsSpotlightAndPublishesNothing` (asserts `spotlightScrollCalls == 0`). |
| Bounded + deduplicated lookup — one assignment call per page / per AID batch | YES | `FalconPolicyEnricher.FetchAssignmentsAsync` dedups AIDs into one call (`:102-115`); `FalconDevicePolicyClient` issues exactly one `PostStreamAsync`. Tests assert request bodies in order: `deviceBodies.Should().Equal("""{"ids":["aid-1","aid-2"]}""", """{"ids":["aid-3"]}""")` (FalconCollectorTests.cs:1707), `Policy_OneAssignmentLookupPerAidBatch_CarryingThatBatchsAids`, `deviceCalls.Should().Be(1)` across a 3-chunk host. |
| Shared enricher is the single implementation; same source host canonically equal in both outputs | YES | One class, constructed once per run in each flow (`FalconAssetsFlow.cs:52`, `FalconFindingsFlow.cs:114`); no per-flow duplicate logic. Parity is **genuinely asserted**, not claimed: `Policy_SameSourceHost_IsCanonicallyEqualAcrossAssetsAndFindings` (FalconCorrelatedFindingsTests.cs:1780) runs both flows against one shared route table and compares the full serialized host string. |
| Existing traversal / naming / prefetch / watermark / cursor recovery / segmentation / publication / checkpoint invariants preserved | YES (see §4 note 1 for the untested memory claim) | `git diff` of `FalconAssetsScrollRunner.cs` contains only two hunks; everything after the publish call is byte-identical: `counters.TotalExpected`/`resourcesReader.Total` (:290-294), `scroll.AfterToken` (:296), empty-terminal-page branch + `OnTerminalSnapshotWithoutPublishedPage` + `counters.Page--` (:298-318), no-publishable-records branch (:320-344), watermark advance (:369-373), `OnPagePublished` (:375-388), depth cap (:392-400), `pageNumber: counters.Page` target naming (:285). `FalconFindingsFlow.cs` diff adds only the enrich call and a private helper. `Recovery/` untouched. Verified separately: the serialization swap (`NormalizedUtf8Json.SerializeToSingleLine(JsonElement)` → `JsonNode.ToJsonString()`) is byte-identical — I ran both paths over a record with non-ASCII, `<`, a trailing-zero decimal, and duplicate keys and got identical output. |
| Focused client/envelope/cache/flow/access/recovery tests pass, covering §12 | MOSTLY — see §3 | 36 component tests + 5 assets-flow + 5 findings-flow + 5 access-probe + 3 config. Two §12 bullets not asserted (§4 notes 1 and 2). |
| Targeted Falcon build/test pass; repo-local final collector review pass | PARTIAL | My independent run: `Failed: 0, Passed: 133, Skipped: 1` (the skip, `Discover_PrefetchInFlight_WhenPublishFails_...`, is confirmed pre-existing at `HEAD`). Solution build succeeds. **The final collector review has not run** — `state.json` step `run_final_collector_review` is `pending`, `codeReviewerRun: false`. |
| Touched documentation (incl. `ai/skills`) and task state current | YES | `FalconDocs/CollectorDocs/04-architecture.md` (+37, accurate: materialization, placement, per-flow call sites, failure asymmetry, config switch), `05-testing.md` (+3), `ai/skills/collector-flow-patterns/SKILL.md` (+5, real generalized rules), `ai/skills/collector-tests/SKILL.md` (+4). `execution_notes.md` and `state.json` updated. |

### Envelope shape vs plan §4 — checked field by field

| Case | Required | Implemented |
|---|---|---|
| resolved | `schema_version:1`, `collection_status:"complete"`, `prevention:{assignment, definition_status:"resolved", definition}` | `FalconDevicePoliciesEnvelope.cs:62-73`, `:88-93`. Key order matches the plan example exactly. |
| empty (complete) | `complete` + `prevention:null` | `Empty()` :47 |
| not_found | assignment retained, `definition:null`, `complete` | `ForAssignment(..., NotFound, null)`; `collectionStatus` only becomes `partial` for `Unavailable` (:69-71) |
| unavailable (partial) | assignment retained, `definition:null`, `partial` | same site; `unavailableIds` branch `FalconPolicyEnricher.cs:192-198` |
| disabled | `disabled` + `prevention:null`, no endpoint call | `Disabled()` :40; `AttachDisabled` before any HTTP (`FalconPolicyEnricher.cs:61-65`). Test asserts `policyEndpointCalls == 0`. |
| `schema_version` | 1 | `SchemaVersion = 1` :33 |

Derived `collection_status` is computed in exactly one place (`:69-71`) — no independent mutation
across code paths, as §4.4 demands.

### Cache semantics vs plan §7

- Only resolved + confirmed-2xx-not-found stored: `FalconPolicyEnricher.cs:155-166` (store happens
  only on the non-throwing path, after a successful response).
- `unavailable` never cached: the `catch` block at `:137-153` `continue`s without touching the cache.
  Asserted by `UnavailableDefinition_IsNeverCached_AndIsRetriedByTheNextUnit`.
- Run-scoped, not checkpointed: `private readonly FalconPreventionPolicyCache _cache = new();` field of
  the per-run enricher; no `Recovery/` file touched.
- A stored `null` means confirmed not-found, absence means unknown (`FalconPreventionPolicyCache.cs:18`) —
  so a cache hit can never fabricate `not_found` from an outage.

### 401/403 and malformed contracts

- 401/403 on the definition endpoint is explicitly excluded from the degrade filter
  (`IsTransientDefinitionOutage`, `FalconPolicyEnricher.cs:241`) and propagates.
- 401/403 on the assignment endpoint has no catch at all.
- `AdapterHttpRequestFailedException : HttpRequestException` with `StatusCode` set
  (`Shared/.../TransportErrorHandling/AdapterHttpRequestFailedException.cs:9,19`), so the filter
  genuinely sees vendor status codes in production — this is not a test-only artifact.
- Malformed contracts throw `InvalidOperationException` (non-object resource, unusable `device_id`,
  non-object `prevention`, definition without `id`) — a different type, so it cannot be laundered
  into `unavailable`. Tests: `MalformedAssignmentResponse_Fails_...` (3 cases),
  `MalformedDefinitionResponse_Fails_...` (2 cases), `AuthorizationFailureOn{Assignment,Definition}_...`
  (2×2 cases), `CancellationDuringDefinitionHydration_Propagates_...`.

---

## 3. Plan §12 required-test coverage

### Clients and mapping

| Required | Covered | Test |
|---|---|---|
| device request body and batching | YES | `DeviceRequestBody_UsesLowercaseIdsArray`, `AssignmentLookup_PostsIdsBody_ToDeviceEntitiesEndpoint`, `AssignmentLookup_IsOneRequestPerUnit_AndDeduplicatesAids` |
| exact `aid` → `device_id` join | YES | `Join_MatchesDiscoverAidToDeviceId_AndExtractsPreventionOnly` |
| `resources: null`, empty, partial, reordered | YES | `SuccessfulLookupWithNoResources_PublishesCompleteEmptyEnvelope` (Theory), `Join_IgnoresUnrelatedDeviceIds_AndToleratesReorderedResources` |
| Prevention-only extraction | YES | `Join_MatchesDiscoverAidToDeviceId_AndExtractsPreventionOnly` (fixture also carries `sensor_update`/`firewall`) |
| unknown assignment fields preserved | YES | `RawAssignmentAndNestedDefinition_ArePreservedIncludingUnknownFields` |
| nested definition preserved | YES | same test (`prevention_settings` + `ioa_rule_groups`) |
| multiple requested definitions, missing IDs | YES | `DefinitionHydration_ChunksAtOneHundredIds`, `SuccessfulDefinitionResponseOmittingRequestedId_IsCompleteAndNotFound` |
| malformed response and cancellation | PARTIAL | Malformed: both clients covered. Cancellation: only the **definition** path (`CancellationDuringDefinitionHydration_Propagates_...`); no device-path cancellation test. Cosmetic — the device path has no catch at all, so there is nothing that could swallow it. |

### Envelope and cache

| Required | Covered | Test |
|---|---|---|
| resolved / not-found / unavailable / empty / disabled shapes | YES | `ResolvedDefinition_IsCompleteAndResolved`, `SuccessfulDefinitionResponseOmittingRequestedId_IsCompleteAndNotFound`, `DefinitionOutage_IsPartialAndUnavailable_AndRetainsAssignment`, `SuccessfulLookupWithNoResources_...`, `Disabled_AttachesVisibleDisabledEnvelope_AndCallsNoPolicyEndpoint` |
| `complete`/`partial` derived consistently | YES | the four status tests above assert both fields together |
| existing `device_policies` replaced | YES | `ExistingDevicePoliciesProperty_IsReplaced_NotDuplicated` |
| no policy never drops a host | YES | `HostsWithoutAid_AreNotSentToTheVendor_ButStillGetAnEnvelope`, `ProcessAsync_Assets_HostWithoutPreventionAssignment_IsStillPublished` |
| resolved and not-found cached | YES | `ConfirmedNotFound_IsCached_AndNotRerequested`, `Cache_StoresResolvedAndNotFound_AndReportsOnlyUnknownIdsAsMisses` |
| unavailable never cached | YES | `UnavailableDefinition_IsNeverCached_AndIsRetriedByTheNextUnit` |
| duplicate IDs hydrated once across hosts and units | YES | `DuplicatePolicyIds_AreHydratedOnce_AcrossHostsAndUnits`; flow-level too (`definitionCalls.Should().Be(1)` across two assets pages) |

### Assets flow

| Required | Covered | Test |
|---|---|---|
| policy metadata in every emitted asset | YES | `ProcessAsync_Assets_EnrichesEveryPublishedHost_WithOneDeviceLookupPerPage` (loops all published records) |
| one assignment call per non-empty page | YES | same test, ordered body equality |
| **only one Discover page materialized at a time** | **NO TEST** | Structurally true (page-scoped `List<JsonNode>` inside `ProcessPageAsync`), but nothing asserts it. See §4 note 1. |
| assignment failure → no publication/checkpoint | PARTIAL | `ProcessAsync_Assets_WhenAssignmentLookupFails_PublishesNoPage` asserts `StreamBatches` empty and `Success == false`; the **checkpoint half is not asserted**. See §4 note 2. |
| definition failure → partial + checkpoints | PARTIAL | `ProcessAsync_Assets_WhenDefinitionFails_PublishesPartialEnvelope_AndCompletes` asserts publication + envelope; checkpoint write is implied by publication, not asserted. |
| watermark, boundary AIDs, cursor recovery, counts, terminal-page behavior unchanged | YES (regression) | No new test, but the code after publish is provably unchanged (two-hunk diff) and `FalconCollectorTests` + `FalconResumeRunnerTests` + `FalconRecoveryContinuationTests` all pass with no assertion weakened (`git diff` of the test files contains **zero** removed assertion lines — only one xmldoc line changed). |

### Findings flow

| Required | Covered | Test |
|---|---|---|
| policy metadata in `host` in every correlated chunk | YES | `Policy_EnrichedHost_AppearsInEveryChunkOfAMultiChunkHost` |
| same source host canonically equal across flows | YES | `Policy_SameSourceHost_IsCanonicallyEqualAcrossAssetsAndFindings` — real dual-flow run + string comparison |
| one assignment call per existing AID batch | YES | `Policy_OneAssignmentLookupPerAidBatch_CarryingThatBatchsAids` |
| no request per finding or chunk | YES | `deviceCalls.Should().Be(1)` with `chunkFindingsCap: 2` and 3 findings |
| cache works across batches/pages | YES | `definitionCalls` assertion in the per-batch test; `DuplicatePolicyIds_..._AcrossHostsAndUnits` |
| zero-finding hosts still emitted | YES | `Policy_ZeroFindingHost_StillCarriesTheEnvelope` |
| assignment failure occurs before Spotlight | YES | `Policy_AssignmentFailure_SkipsSpotlightAndPublishesNothing` (`spotlightScrollCalls == 0`) |
| existing stripping, chunking, grouping, prefetch, atomic publish, checkpoint unchanged | YES (regression) | `FalconFindingsFlow.cs` diff is additive only; all pre-existing `FalconCorrelatedFindingsTests` pass unmodified |

### Access and configuration

| Required | Covered | Test |
|---|---|---|
| required endpoint probe sequencing | YES | `ProbePreventionPolicyAsync_ExercisesDiscoverThenDeviceThenDefinition` |
| no representative assignment behavior | YES | `ProbePreventionPolicyAsync_WithNoRepresentativeHost_StopsAfterDiscover_WithoutFabricatingAnId`, `..._WhenRepresentativeHostHasNoAssignment_StopsAfterDeviceDetails` |
| 401/403 not converted to empty/partial | YES | `..._WhenDeviceEndpointDeniesAccess_Throws` / `..._WhenDefinitionEndpointDeniesAccess_Throws` (Theory ×2 each), plus the two enricher-level authorization theories |
| setting omitted/true/false | YES | `Build_WhenPreventionPolicyEnrichmentIsOmitted_DefaultsToEnabled`, `Build_HonorsPreventionPolicyEnrichmentOverride` (Theory) |
| "update the closest existing fixtures, **including URL**…" | PARTIAL | `FalconUrlsTests.cs` is unmodified; the URL assertions live in `FalconPolicyEnrichmentTests` instead (`PreventionUrl_RepeatsIdsParameterPerPolicy`). Coverage exists, placement instruction not followed literally. Cosmetic. |

---

## 4. Gaps / drift / contradictions

Ranked by severity. Notes 1–3 are real; 4–5 are real but narrow; 6–8 are cosmetic/process.

### 1. (MEDIUM, test gap) No test proves "only one Discover page is materialized at a time"

Plan §12 lists this as a required assets-flow test; §5.1 states "Do not buffer multiple pages."
It is structurally true — `pageRecords` is a local in `ProcessPageAsync`
(`Flows/Assets/FalconAssetsScrollRunner.cs:270-278`), released when the scope exits — but nothing
asserts it, and the two-page test that could have (`FalconCollectorTests.cs:1660`) records assets
requests only as a counter, not into the same ordered log as `deviceBodies`, so it cannot
distinguish `fetch→enrich→publish→fetch` from `fetch→fetch→enrich→enrich`. Grep for
`materiali|OneAtATime|callOrder|requestOrder` across the three test files returns nothing.

The cheap fix is a single ordered request log asserting
`assets(1) → device(1) → publish(1) → assets(2) → device(2)`.

### 2. (MEDIUM-LOW, test gap) "Checkpoint not advanced" is never asserted on assignment failure

Plan §12 (assets) and §13 both require it ("Assignment uncertainty never advances an unpublished
unit's checkpoint"). Both failure tests assert only `StreamBatches.Should().BeEmpty()` and
`Success == false`. The harness already exposes checkpoints — `PublishCapture.Checkpoints`
(`FalconCollectorTests.cs:65`), and `CollectorEventCapture.Checkpoints`
(`UnitTests/Collectors/Collectors.Tests.Infrastructure/CollectorEventCapture.cs:14`) — so the
assertion was available.

Nuance worth respecting if this is fixed: a plain `Checkpoints.Should().BeEmpty()` would likely be
wrong, because Falcon's resilience executor deliberately snapshots state via `AdvancePage(0,0)` on
a recoverable failure. The correct assertion is that no checkpoint records the failed page as
completed / no watermark advance — not emptiness.

### 3. (LOW-MEDIUM, real behavior gap) The connection-test path was not extended

`FalconAccessProber.ProbePreventionPolicyAsync` runs from `InitializeAsync`
(`FalconCollector.cs:331-338`), i.e. on every real run. But the operator-facing validation path is
a **separate** inline probe implementation —
`Processing/Validation/FalconConfigurationValidationService.cs:33-60` builds its own Hosts
(and conditional Spotlight) probe URLs and was not touched. Consequence: an API client missing
Hosts-read-for-`/devices/entities/devices/v2` or Prevention-Policies-read passes "Test Connection"
green and then fails at the first collection run's init.

Plan §9 says "extend the existing staged Falcon access probe" (singular), which was done, so this
is arguably in-contract — but it is a real operator-visible gap and should be disclosed.

### 4. (LOW, new failure mode introduced by the streaming→materialization conversion)

A Discover host row with **duplicate top-level JSON property names** now fails the whole page.
`JsonNode.Parse` is lazy and tolerates duplicates, but
`FalconDevicePoliciesEnvelope.Attach`'s indexer write forces `JsonObject` materialization, which
throws `ArgumentException`. I verified this empirically:

- `{"aid":"a1","device_policies":{"old":1}}` + indexer write → `{"aid":"a1","device_policies":{"new":1}}` (correct, single property)
- `{"aid":"a1","dup":1,"dup":2}` + indexer write → **`ArgumentException`**

Previously such a row streamed through `NormalizedUtf8Json.SerializeToSingleLine(JsonElement)`
untouched. This is a pathological vendor payload and assets-only (the findings flow already held
hosts as `JsonObject`), so severity is low — but the blast radius is the entire page, not one host,
and the change did not consider it.

### 5. (LOW, classification drift) Every non-401/403 `HttpRequestException` is treated as transient

`FalconPolicyEnricher.cs:241`:
`exception.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)`.

That folds HTTP `400` and `404` from the definition endpoint into `partial`/`unavailable`, which will
recur on every run forever rather than failing loudly. Plan §8 distinguishes "definition transient
failure exhausts request handling" (→ partial) from "malformed assignment/definition contract" (→ fail
the unit/run); a `400` is closer to the latter. Low severity — it degrades rather than corrupts, and
the envelope stays truthful about the definition being unavailable — but consider excluding 4xx other
than 408/429.

### 6. (Note, not a defect) A >60s `Retry-After` on the definition endpoint defers the unit instead of publishing `partial`

I checked whether the degrade filter could swallow the vendor-delay signal. It cannot:
`ServerSuggestedRetryDelayException` derives directly from `System.Exception`, not
`HttpRequestException` (verified by reflecting over
`cymulate.http.package.defensivetoolkit/2.0.2`). Falcon opts into the 60s in-process threshold
(`FalconCollectorConfiguration.cs:42-43`), so a sub-minute `Retry-After` is slept in-process and, on
eventual exhaustion, becomes `unavailable` as intended; a delay above 60s propagates and defers the
whole page/batch via `ServerSuggestedRetryDelayPolicy`.

That diverges from §8's row in letter (a definition failure "advances after publish") but is correct
in spirit — a vendor-supplied delay is not "exhausted request handling", and deferring is the house
pattern. No change recommended; disclose only.

### 7. (Cosmetic) Stale cross-reference to a non-existent test class

`FalconPolicyEnrichmentTests.cs:19` points flow-level coverage at `FalconPolicyFlowTests`, which
does not exist. The flow tests live in `FalconCollectorTests` and `FalconCorrelatedFindingsTests`.

### 8. (Process) Success criteria not yet fully closed

`state.json` has `run_final_collector_review: pending`, `verifierRun: false`,
`codeReviewerRun: false`. The contract's success criteria include "repo-local final collector review
pass". That step is downstream of this verifier in the pipeline, so it is sequencing rather than
drift — but the task cannot be reported complete until it runs.

### Executor judgment calls — assessed

- **Decision #7 (assignment present, no readable `policy_id` → `not_found`).** Defensible, with a
  vocabulary stretch worth disclosing. §4.3 defines `not_found` as "a successful 2xx definition
  response omitted the requested ID" — here nothing was requested. But the raw assignment is
  preserved (§4.1), the envelope stays `complete` (§4.4 — the definition is not `unavailable`), and
  `unavailable` would falsely advertise retryability. The narrower alternative (fail the page over one
  odd host) is disproportionate, and a non-object `prevention` is still a hard failure
  (`FalconDevicePolicyClient.cs:99-103`). Accept. Covered by
  `AssignmentWithoutPolicyId_KeepsTheAssignment_AndReportsNoDefinition`.
- **Decision #8 (metadata override for the switch).** Not drift. Plan §10 explicitly requires
  omitted/`true`/`false` behavior, which cannot be honored without a read path. The default remains a
  hardcoded capability constant on `FalconCollectorConfiguration` (`:136`), and the override mirrors
  the adjacent `batchScopedStorage` pattern. Accept.
- **Decision #4 (serialization swap to `JsonNode.ToJsonString()`).** This was the largest latent risk
  in the change — a silent NDJSON encoding change for every asset. I tested it directly and it is
  byte-identical for non-ASCII escaping (`é`), HTML escaping (`<`), trailing-zero decimals
  (`1.50` preserved verbatim), property order, and duplicate keys. No regression. See note 4 for the
  one case where the surrounding materialization does change behavior.
- **Decision #9 (routing the device endpoint in 16+ existing fixtures).** Verified honest: `git diff`
  over the test files shows **no removed assertion lines** — only added routes plus one changed xmldoc
  line. No production behavior was weakened to make tests pass.

---

## 5. Constraint compliance

| Constraint | Status | Evidence |
|---|---|---|
| No branch creation, staging, commit, or push | PASS | `HEAD` is still `c7231e3` (the pre-existing merge commit); `git diff --cached --stat` is empty; branch is `feature/falcon-prevention-policy-enrichment` |
| Prevention family only; no multi-family abstraction or route catalog | PASS | Only `device_policies.prevention` is read (`FalconDevicePolicyClient.cs:91-103`); fixtures deliberately include `sensor_update`/`firewall` and assert they are ignored. Two URL builders, no catalog. |
| No standalone policy flow / output / parser entity | PASS | No new flow registration, no new topic, no new output file kind; envelope is nested in the host |
| No rule-group recursion | PASS | `rule_groups` copied verbatim; no follow-up request. `ioa_rule_groups` is whatever the vendor already expands inline. |
| No Shared infrastructure changes | PASS | `git status` lists no file under `Shared/` |
| No new `HttpClient`, token manager, retry loop, or attempt counter | PASS | Both clients take the existing `AdapterHttpClient`; no Polly, no counters |
| `FalconUrls` extended for both endpoints | PASS | `BuildDeviceEntitiesUrl`, `BuildPreventionPolicyUrl` (`Processing/Urls/FalconUrls.cs:44-70`) |
| No checkpoint format change / no version bump | PASS | `Recovery/` untouched; `FalconCheckpointKeys`/`Serializer`/`Deserializer` unmodified |
| No csproj version bump | PASS | No `.csproj` and no `Directory.Build.props` in `git status` |
| `Flows/Policies/` dedicated space (operator-mandated) | PASS | 5 files, 674 lines total |
| ONE shared enricher, not a flow, not per-flow duplicated | PASS | `FalconPolicyEnricher` constructed once per run in each flow; flows contribute only unit selection + call site |
| Both flows enrich on every run; neither may skip when the switch is on | PASS | Unconditional calls at `FalconAssetsScrollRunner.cs:279` and `FalconFindingsFlow.cs:201`; the only gate is the config switch, inside the enricher |
| Three policy docs copied into the workitem folder | PASS | `source_docs/` holds all three; repo-root working copies untouched |
| Superseded `FALCON_POLICY_COLLECTION_PLAN.md` scope not implemented | PASS | No other policy family, no member enumeration, no generic catalog |
| No overengineering / no new framework | PASS | 674 lines across 5 small classes; longest method ~45 lines; no interfaces, no DI registration, no generics |
| Dry run must not publish collection data | PASS | Both flows return before enrichment on dry run (`FalconAssetsScrollRunner.cs:153-161`, `FalconFindingsFlow.cs:98-108`) |
| Never run the full FalconCollector suite | PASS | I ran a filtered set of 8 classes only |
| No credentials/tenant secrets in source, fixtures, or reports | PASS | Fixtures use the probe's already-public lab AID/policy ID and `test-client-id`/`test-client-secret` |
| Preserve user worktree changes | PASS | The three untracked repo-root docs are intact |

Bounds sanity-check (device endpoint 5,000-ID cap): assets page size is clamped to `1..1000`
(`FalconCollectorConfigurationBuilder.cs:150`) and `aidBatchSize` to `1..5000` (`:161`), so a
bounded unit always fits one request. Definition chunking is a constant 100
(`FalconPreventionPolicyClient.MaxIdsPerRequest`) with a guard that throws if a caller exceeds it.

---

## 6. What the final response to the operator must disclose

1. **Verdict:** implementation satisfies the plan's output contract, failure asymmetry, cache
   semantics, call budget, and cross-flow parity; cross-flow parity and the "no Spotlight on
   assignment failure" guarantee are asserted by real tests, not asserted by claim.
2. **Two §12 required-test bullets are not actually asserted:** (a) only one Discover page is
   materialized at a time; (b) the checkpoint does not advance when the assignment lookup fails.
   Both behaviors are structurally correct in the code; only the proofs are missing.
3. **The connection-test path does not exercise the new permissions.**
   `FalconConfigurationValidationService` was not extended, so a client missing Prevention Policies
   read (or Hosts read for `/devices/entities/devices/v2`) passes "Test Connection" and fails on the
   first real run's init.
4. **Every emitted host now carries a new top-level `device_policies` property in both flows.**
   Parser/DB/UI work is explicitly out of scope per plan §2, so downstream consumers will see this
   shape change with no coordinating change.
5. **Two narrow behavior notes:** a definition-endpoint HTTP 400/404 degrades to a permanent
   `partial`/`unavailable` rather than failing loudly; and a Discover host with duplicate top-level
   JSON keys now fails its whole page (previously it streamed through). Both are edge cases.
6. **A >60s `Retry-After` from the definition endpoint defers the whole unit** rather than publishing
   `partial`. Verified this is not an accidental laundering — the vendor-delay exception does not
   derive from `HttpRequestException`. Defensible, but it differs from a literal reading of §8.
7. **New memory characteristic:** one assets page is now fully materialized (bounded at 1,000 hosts
   plus per-host definition clones). Accepted by plan §5.1, unverified against a live tenant.
8. **Not exercised against a live tenant.** All vendor interactions are verified against probe
   captures and unit fixtures only.
9. **The final collector review has not run** (`state.json`: `run_final_collector_review: pending`),
   so the contract's success criteria are not yet fully closed.
10. **Nothing was staged, committed, or pushed;** `HEAD` remains `c7231e3` and no version was bumped.
