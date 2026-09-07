# Internal Recon

## Durable sources read

- `CLAUDE.md` (repo root) — settles: the four adapter kinds; `Cymulate.IntegrationInfra` is the ONLY Cymulate package any csproj may declare; the substrate/collector ownership split (substrate owns `ProcessAsync` sequencing + DONE shape, collector owns vendor request logic, pagination, checkpoint *formats*, flow exception classification); the five subsystems and the boundary heuristic; Falcon stays on `SessionRetryDefaults.CreateTransient()` and opts into the 60s in-process server-delay threshold; `Catalog/` owns vendor names, `Job/` owns topic names. Test stack xUnit+Moq+FluentAssertions, central package management, `InternalsVisibleTo` per collector. No worker needs to re-derive any of this.
- `ai/skills/collector-flow-patterns/SKILL.md` — settles the Falcon findings flow shape (two-phase, aid-batch publish boundary, no persisted cursor, batch-scoped storage rules, deterministic UUIDv5 `instanceBatchId`), and carries five rules that bind this task directly: enrichment is ONE join per bounded unit with a run-scoped, never-checkpointed cache; enrichment failure splits by *what the data means* (essential ⇒ publish nothing, supplemental ⇒ explicit partial); the envelope is always present and self-describing incl. a visible `disabled`; streaming-a-page and enriching-that-page are mutually exclusive (materialize exactly ONE page); **a vendor-stated identifier and a derived one are not interchangeable**; and any emitted payload an upstream parser consumes must be documented in two layers (contract + observed variants). Also: `DataPipelineException` is an OVERLOADED type (over-ceiling refusal, read-slot timeout, null stream) — "bound it by volume, do not treat the type as benign".
- `ai/skills/collector-recovery/SKILL.md` — settles: the recovery budget has ONE owner (`RecoveryBudgetEvaluator`); no collector-side attempt counter; never `AdvancePage(0,0)` to force a wait snapshot; cursor expiry re-anchors IN-PROCESS from the watermark and must not route through the resilience strategy; a checkpoint SHAPE change requires a format bump + `CanResumeFrom` rejection.
- `ai/skills/collector-execution-and-recovery/SKILL.md` — settles: `PublishFailure(IsRetryable=true)` is terminal in practice (prod `6a85ca2038f164746a562020`, 2026-08-20) — a fault worth retrying must produce `RequestDeferredRecovery`/`RecoverAndRetry`; Polly's pipeline ends at response headers, so mid-stream drops are unretryable below the collector; do NOT chain a transport fault as `InnerException` when wrapping it in a vendor type (position-4 policy walks 10 levels deep); supplying `transientTransportBackoff` steals Falcon's circuit-breaker arm.
- `ai/skills/collector-tests/SKILL.md` — settles: assert the enrichment CALL BUDGET, the cache (outage never cached), essential-vs-supplemental failure semantics; enabling enrichment by default changes every flow fixture and the shared fake-transport factory must route the new endpoint with a neutral response; when two flows must emit the same record, run both and compare the canonical serialized host.
- `ai/skills/collector-shape-and-layers/SKILL.md`, `ai/skills/collector-review/SKILL.md` — layer ownership and the review checklist. Nothing task-specific beyond "flows own vendor requests/pagination/enrichment/checkpoints/publishing sequence".
- `ai/active/2026-07-26_1655_falcon-prevention-policy-enrichment/decisions.md` — the 15 locked decisions this work overturns or keeps. **Overturned by the plan:** #2 ("policies are host metadata, not independent entities"), #3/#14 (enrich at each flow's existing bounded unit — Findings' unit moves out of `SpoolAsync`), #4 as applied to findings (raw definition JSON preserved *inside each host*), #11 ("one shared enricher keeps the host envelope identical by construction"). **Still standing and still binding:** #1 (Prevention Policy only), #5 (assignment required, definition degradable), #6 (explicit empty/partial/disabled), #7 (cache only resolved + confirmed not-found — never an outage), #8 (no policy-specific attempt counter; existing recovery owns deferral), #9 (findings unchanged except the enriched `host` — the chunk envelope, stripping, grouping, chunking, publication and checkpoint behaviour are protected contracts), #10 (no reintroduction of other policy families or a generic route catalog), #12 (`Flows/Policies/` is the mandated space), #15 (source docs archived).
- `ai/active/2026-07-26_1655_falcon-prevention-policy-enrichment/constraints.md` — **constraints that still bind:** one assignment lookup per bounded unit; definition lookups deduped through a run-scoped cache; assignment uncertainty prevents publication AND checkpoint for the unit; authorization + malformed contracts never masquerade as empty; existing cursor/watermark/recovery/finding-shaping/publish/checkpoint behaviour intact; reuse substrate session/auth/retry/publishing/recovery; extend `FalconUrls` for endpoints; no new HttpClient/token manager/retry loop/attempt counter; **no checkpoint format bump unless persisted state changes**; never run the full FalconCollector suite (filter to change-relevant classes); don't stage/commit/push unless asked. Two constraints the plan contradicts and that need an explicit decision, not silence: *"Enrichment is nested in each Discover host"* and *"Both `CollectAssets` and `CollectFindings` use the same host envelope"* — item 3 (plane) breaks both for findings while item (e) below shows assets can keep the embedded envelope.
- `FalconCollector/FalconDocs/CollectorDocs/06-prevention-policy-contract.md` (157 lines) + `07-prevention-policy-observed-variants.md` (391 lines) — the normative published contract. §6.1 states verbatim: *"In `assets_*.json` it sits on the record; in `findings_*.json` it sits on the record's `host`. The same source host produces a canonically identical object in both."* Both docs are output-contract artifacts the flow-patterns skill requires be updated by this work.
- Working tree (branch `fix/falcon-disable-prevention-policy-enrichment`): only two uncommitted edits — `Collectors/Directory.Build.props:28` bumps `CollectorVersion` 6.3.3 → 6.3.4, and `FalconCollectorConfiguration.cs:179` flips `EnablePreventionPolicyEnrichment` default `true` → `false`.

## Files in scope

### Phase 1 / staging (`Flows/Findings/TwoPhase/`)

- `Flows/Findings/TwoPhase/FalconPhase1Manifest.cs` (148 ln) — record + codec + usability gate + policy-contract gate. **Touched by: work item 1 (staged ledger), item 6 (coverage counters).** Contains a long "NO ORDINAL DERIVATION LIVES HERE ANY MORE" block (`:75-103`) forbidding any re-derivation of a traversal position from published-object counts; a staged ledger must not reintroduce it.
- `Flows/Findings/TwoPhase/FalconHostSpooler.cs` (380 ln) — `SpoolAsync` (`:75`), `ApplyPolicyContractAsync` (`:234`), `EnrichStagedPagesAsync` (`:257`). **Touched by: items 1, 2, 4, 6.** This is the file the restructure guts.
- `Flows/Findings/TwoPhase/FalconStagingArea.cs` (385 ln) — the only door to the object store. **Touched by: items 1, 2, 4** (needs a `pgen_` write/read/list surface, and a streaming write path if the definitions sweep is large).
- `Flows/Findings/TwoPhase/FalconStagingPaths.cs` (186 ln) — key schema. **Touched by: item 4.**
- `Flows/Findings/TwoPhase/FalconStagedHostPage.cs` (131 ln) — NDJSON codec, `Encode` (`:34`) / `DecodeAsync` (`:62`). **Touched by: item 3** if the staged line grows a policy edge instead of a nested envelope.
- `Flows/Findings/TwoPhase/FalconFrozenKeyList.cs` (141 ln) — Phase 2 traversal, reads `manifest.PageKeys` (`:86`, `:90`) and `manifest.AidBatchSize` (`:126`). **Touched by: item 1** (must read the ledger's page list, and must not regress the three-case absent-page analysis at `:92-121`).

### Findings flow + policies

- `Flows/Findings/FalconFindingsFlow.cs` (674 ln) — `CollectAsync` (`:113`), `ResolveFrozenKeyListAsync` (`:544`), `CreateStagingArea` (`:640`). **Touched by: items 1, 2, 3, 6.** Owns the manifest-compatibility gate (`:579-612`) that item 1 replaces, and the Phase-2 transport guard (`:366-390`).
- `Flows/Findings/FalconFindingsCheckpointWriter.cs` (119 ln) — `OnBatchPublished` (`:32`); reads `manifest.GenerationId`/`HostCount`/`DiscoverWatermarkUtc`/`DiscoverWatermarkAids` via arguments from the flow (`:337-342` of the flow). **Touched by: item 1** only if per-stage coverage counters are to appear in the checkpoint (they need not — see (f)).
- `Flows/Policies/FalconPolicyEnricher.cs` (275 ln) — the shared enricher, `EnrichAsync` (`:71`), `HydrateDefinitionsAsync` (`:142`), `BuildAssignedEnvelope` (`:196`, deep-clones the definition per host at `:224`), `IsTransientDefinitionOutage` (`:265`). **Touched by: items 3, 5.**
- `Flows/Policies/FalconDevicePolicyClient.cs` (153 ln) — `FetchPreventionAssignmentsAsync` (`:38`). **Touched by: item 5** (requested-vs-returned comparison lives here or just above it).
- `Flows/Policies/FalconPreventionPolicyClient.cs` (107 ln) — `FetchDefinitionsAsync` (`:38`), `MaxIdsPerRequest = 100` (`:24`). **Touched by: items 4, 5.**
- `Flows/Policies/FalconDevicePoliciesEnvelope.cs` (102 ln) — the wire vocabulary. **Touched by: item 3** (the plane's edge fields are the same vocabulary; this type must stay the single speller).
- `Flows/Policies/FalconPreventionPolicyCache.cs` (70 ln) — run-scoped, never checkpointed. **Touched by: item 4** (a definitions sweep changes what "miss" means).

### Assets flow (must keep working)

- `Flows/Assets/FalconAssetsFlow.cs` (122 ln) — constructs the enricher at `:60`, hands it to the runner at `:61`. **Touched by: item 3 only if the enricher's signature changes.**
- `Flows/Assets/FalconAssetsScrollRunner.cs` (501 ln) — `EnrichAsync` call site at `:315`, `BuildPolicyTargets` at `:460`, publish at `:317`. **Touched by: item 3 only if the enricher's signature changes.**

### Classification / validation / URLs

- `Processing/FalconFlowExceptionClassifier.cs` (132 ln) — **Touched by: item 6** (new `DataPipelineException` arm).
- `Processing/Validation/FalconAccessProber.cs` (199 ln) — `ProbePreventionPolicyAsync` (`:51`), `ProbeAsync` (`:160`). **Touched by: item 6** (404 must not fail init).
- `Processing/Validation/FalconConfigurationValidationService.cs` (115 ln) — calls `ProbePreventionPolicyAsync` at `:82`, catch-all at `:92`. **Touched by: item 6.**
- `Processing/Urls/FalconUrls.cs` (70 ln) — `BuildDeviceEntitiesUrl` (`:49`), `BuildPreventionPolicyUrl` (`:55`). **Touched by: item 4** (new `GET /policy/combined/prevention/v1` builder).
- `Flows/SharedFlows/FalconHttpFailureClassifier.cs` (145 ln) — the ONLY hook that sees a raw non-2xx body. **Touched by: item 5.**
- `Flows/Findings/Correlated/FalconDiscoverHostScroller.cs` (102 ln) — `FetchPageAsync` (`:50`); a 5xx surfaces from here as `HttpRequestException`/`AdapterHttpRequestFailedException`. **Touched by: item 6** (mid-scroll 500 re-anchor is in the *spooler's* loop, not here).

### Recovery (read-only for this work — see (f))

- `Recovery/FalconCheckpointState.cs` (221 ln) — `FalconFindingsCheckpointState` at `:140`, `CurrentFormatVersion = 4` at `:153`, position fields at `:167-194`.
- `Recovery/FalconCheckpointKeys.cs` (44 ln), `Recovery/FalconCheckpointSerializer.cs` (72), `Recovery/FalconCheckpointDeserializer.cs` (284), `Recovery/FalconCollectorRecoveryHandlers.cs` (148) — no policy field anywhere.

### Docs

- `FalconCollector/FalconDocs/CollectorDocs/06-prevention-policy-contract.md`, `07-prevention-policy-observed-variants.md` — **must be updated by items 3 and 4.**

### Tests

- `UnitTests/.../FalconCollector.Test/FalconTwoPhaseFindingsTests.cs` (2864 ln) — **breaks; see Landmines.**
- `UnitTests/.../FalconCollector.Test/InMemoryFalconStagingStore.cs` (278 ln) — the object-store double.
- `UnitTests/.../FalconCollector.Test/FalconPolicyEnrichmentTests.cs` (720 ln) — component tests for the enricher.
- `UnitTests/.../FalconCollector.Test/FalconCorrelatedFindingsTests.cs` (1704 ln) — cross-flow policy tests at `:1462-1703`.
- `UnitTests/.../FalconCollector.Test/FalconCollectorTests.cs` (2176 ln) — assets-side policy tests at `:1780-2180`.
- `UnitTests/.../FalconCollector.Test/FalconConcurrencyHarness.cs` (781 ln) — second manifest-seeding harness at `:607-633`.
- `UnitTests/.../FalconCollector.Test/FalconFlowExceptionClassifierTests.cs` (174 ln), `FalconAccessProberTests.cs` (278 ln).

## Patterns to mirror

- **Staging key schema, and why a leading `_` is mandatory** → `FalconStagingPaths.cs:9-22` + `AssertNotParserVisible` at `:97`. The upstream parser discovers input by an *undelimited string-prefix match* on `assets` / `findings` across the whole run prefix. Any new prefix must not begin with either.
- **Completion proof = one artifact written LAST** → `FalconStagingArea.WriteManifestAsync` remarks at `:197-200`; enforced at `FalconHostSpooler.cs:219-220` and `:244-246`. A staged ledger with per-stage terminal state must keep exactly this property per stage or it loses the "never redone" guarantee.
- **Positional keys ⇒ per-attempt generation folder** → `FalconStagingPaths.cs:39-64`. The plan's `pgen_<id>` prefix is only *needed* if the definitions sweep's keys are positional; the flow-patterns skill states the rule explicitly ("do not copy the generation concept without checking whether your keys are positional or identity-derived").
- **Guarded façade, never the raw contract** → `FalconFindingsFlow.CreateStagingArea:663-670`. Options resolve once per run, not per call.
- **Buffered control-artifact read with an explicit ceiling** → `FalconStagingArea.TryReadManifestAsync:264-266` uses `ReadAllBytesAsync(loc, Options.MaxControlArtifactBytes, ct)`; streamed read for a data-sized artifact → `ReadHostPageAsync:291-293` uses `OpenReadAsync` and disposes before the caller's loop body.
- **Publish and record are one step** → `FalconFindingsFlow.cs:332-344`. Nothing awaitable or cancellable between the publish and `checkpointWriter.OnBatchPublished`.
- **Wrapping a transport fault to route it past position 4, with NO InnerException** → `FalconFindingsFlow.cs:366-389` and `FalconSpotlightBatchPump.cs:447-450`.
- **Vendor-stated vs derived identifier, kept as two named paths** → `FalconHostSpooler.cs:274-277` (`host.SensorAid ?? AidExtractor.ExtractSensorAid(host.Host)`) and `FalconAssetsScrollRunner.cs:452-459`. `AidExtractor.ExtractSensorAid` (JsonObject `:45`, JsonElement `:53`) is the only extractor that may reach the device endpoint; `ExtractAid` (`:24`, `:71`) is filter-only.
- **A single place spells the wire vocabulary** → `FalconDevicePoliciesEnvelope.cs:27-33` (`PropertyName`, `SchemaVersion`, the three status constants). Add the plane's field names here, not in two flows.
- **Enrichment-status asymmetry** → `FalconPolicyEnricher.cs:23-31` (class remarks) and `IsTransientDefinitionOutage:265-274`. Only 429 / ≥500 / status-less degrade; every deterministic 4xx propagates.
- **Cache stores only run-durable outcomes** → `FalconPreventionPolicyCache.cs:10-13`, `StoreNotFound` only on a 2xx that omitted the id (`FalconPolicyEnricher.cs:182-184`).
- **Classifier arms ordered subtype-before-base, with the reason written down** → `FalconFlowExceptionClassifier.cs:96-97` and `:74`.
- **A probe that cannot answer its question swallows rather than fails the run** → `FalconAccessProber.ProbeAsync:178-194` (5xx is inconclusive; the comment records that failing there cost 7,063,015 findings).
- **Test double mirrors the real store's two sharp edges** → `InMemoryFalconStagingStore.cs:18-24` (separator-boundary listing; `ObjectNotFoundException` not `FileNotFoundException`) and `:26-32` (one lock, because the collector reads from more than one task).

## Shared surface to freeze

| Surface | Produced by | Consumed by |
|---|---|---|
| `FalconPhase1Manifest` record shape + `TryParse`/`ToUtf8Json` (`FalconPhase1Manifest.cs:56-119`) | `FalconHostSpooler.SpoolAsync:205-217`, `ApplyPolicyContractAsync:245`, tests `FalconTwoPhaseFindingsTests.cs:322-332`, `FalconConcurrencyHarness.cs:620-632` | `FalconFindingsFlow:206,211,337-342,579-617`; `FalconFrozenKeyList.EnumerateAsync:86,90,126`; `FalconStagingArea.TryReadManifestAsync:272` |
| `bool? PolicyEnrichmentEnabled` / `int PolicyEnvelopeSchemaVersion` + `HasCompatiblePolicyContract` (`:131`) + `WithPolicyContract` (`:135`) | `FalconHostSpooler.cs:216-217,245` | `FalconFindingsFlow.cs:579,591-594,603-606` — **the only two readers.** Both die with item 1. |
| `FalconStagingArea` public surface — `WriteHostPageAsync:181`, `WriteManifestAsync:201`, `TryFindCompletedGenerationAsync:220`, `TryReadManifestAsync:255`, `HostPageExistsAsync:283`, `ReadHostPageAsync:287`, `CountPublishedFindingsAsync:99`, `DeleteAbandonedGenerationsAsync:303`, `CanPrune:124`, `BaseStorageUrl:78`, `TryResolveBaseStorageUrl:137` | `FalconHostSpooler`, `FalconFindingsFlow`, `FalconFrozenKeyList` | same three + `FalconTwoPhaseFindingsTests` |
| `FalconStagedHostPage.Encode(IReadOnlyList<DiscoverHost>) → ReadOnlyMemory<byte>` (`:34`) / `DecodeAsync(Stream, string, ct) → Task<List<DiscoverHost>>` (`:62`); line shape `{"aid","sensorAid","lastSeen","host"}` | `FalconStagingArea.WriteHostPageAsync:189`; tests `FalconTwoPhaseFindingsTests.cs:318`, `FalconConcurrencyHarness.cs:608` | `FalconStagingArea.ReadHostPageAsync:293` |
| `PolicyEnrichmentTarget(string? Aid, JsonObject Host)` + `FalconPolicyEnricher.EnrichAsync(IReadOnlyList<PolicyEnrichmentTarget>, ct)` + `Enabled` (`FalconPolicyEnricher.cs:14,40,71`) | `FalconHostSpooler:271-280`; `FalconAssetsScrollRunner:315,460-469`; `FalconPolicyEnrichmentTests` throughout | **two production callers — both must be changed together or the signature must stay** |
| `device_policies` envelope shape (`FalconDevicePoliciesEnvelope.cs:88-93`, `:62-74`) and the enum→wire mapping (`:95-101`) | `FalconPolicyEnricher.BuildAssignedEnvelope:196`, `Empty:47`, `Disabled:40` | the published `assets_*.json` record and `findings_*.json` `record.host`; docs 06/07; tests `FalconCollectorTests.cs:1840,2012,2063,2122,2172`, `FalconCorrelatedFindingsTests.cs:1506,1648`, `FalconPolicyEnrichmentTests.cs:143` |
| **The pinned findings record** `{aid, chunk, isLastChunk, findingsInChunk, host, findings[]}` — `FalconCorrelatedRecord.cs:10-17`, built at `:63-71` | `FalconSpotlightBatchScroller` / `FalconSpotlightBatchPump` | byte-pinned in `FalconTwoPhaseFindingsTests.cs:765-768`. Locked decision #9. |
| `FalconFindingsCheckpointState` v4 shape + `CurrentFormatVersion = 4` (`FalconCheckpointState.cs:140-194`), keys in `FalconCheckpointKeys.cs:34-43` | `FalconFindingsCheckpointWriter.OnBatchPublished:56-75` | `FalconCheckpointDeserializer`, `FalconCheckpointResumePolicy.CanResumeFrom`, `FalconFindingsFlow:171-176,557-573`, tests `FalconTwoPhaseFindingsTests.cs:353-389` |
| `AdapterHttpClient` ctor `maxErrorSnippetChars` (default 2000; policy path sets 16000 at `FalconPolicyEnricher.cs:47,58`) and `IHttpFailureClassifier.ClassifyFailure(request, response, bodySnippet, context)` | Infra `AdapterHttpClient.cs:45-55,166` | `FalconHttpFailureClassifier.ClassifyFailure:17` — the only place a raw non-2xx body is reachable |
| `NdjsonBatchEmitter.BuildMandatoryTargetPath` — **allows `findings` and `assets` ONLY** (Infra `Emission/NdjsonBatchEmitter.cs:243-257`, `AdapterOutputDefaults.BuildPageTargetPath:16`, `AdapterGlobalDefaults.cs:19-20`) | — | every collector publish; this is the hard gate on where a policy plane can be written |

## Disjoint sets available

- **set-A `policy-clients-and-urls`**: `Processing/Urls/FalconUrls.cs`, `Flows/Policies/FalconPreventionPolicyClient.cs`, `Flows/Policies/FalconDevicePolicyClient.cs`, `Flows/SharedFlows/FalconHttpFailureClassifier.cs` — independent of: set-B, set-C, set-D. This is work items 4 (endpoint) and 5 (partial-rejection detection) at the client layer. Only outward dependency: whatever type the requested-vs-returned comparison raises, which set-C must classify.
- **set-B `init-and-classification`**: `Processing/FalconFlowExceptionClassifier.cs`, `Processing/Validation/FalconAccessProber.cs`, `Processing/Validation/FalconConfigurationValidationService.cs`, plus `UnitTests/.../FalconFlowExceptionClassifierTests.cs`, `UnitTests/.../FalconAccessProberTests.cs` — independent of: set-A, set-C, set-D (it consumes only exception *types*, not staging or manifest shapes). Work item 6's first two defenses.
- **set-C `staging-ledger`**: `Flows/Findings/TwoPhase/FalconPhase1Manifest.cs`, `FalconStagingPaths.cs`, `FalconStagingArea.cs`, `FalconFrozenKeyList.cs`, `FalconStagedHostPage.cs`, `FalconHostSpooler.cs`, `Flows/Findings/FalconFindingsFlow.cs` — **not further splittable**: items 1, 2, 3 and 6's last defense all move the same two functions (`SpoolAsync`, `ResolveFrozenKeyListAsync`) and the same record.
- **set-D `assets-untouched-proof`**: `Flows/Assets/FalconAssetsFlow.cs`, `Flows/Assets/FalconAssetsScrollRunner.cs`, `UnitTests/.../FalconCollectorTests.cs` (`:1780-2180`) — independent of set-B and set-C **only while `FalconPolicyEnricher.EnrichAsync`'s signature is unchanged**. If item 3 changes that signature, set-D collapses into set-C.

`Flows/Policies/FalconPolicyEnricher.cs`, `FalconDevicePoliciesEnvelope.cs`, `FalconPreventionPolicyCache.cs` and `UnitTests/.../FalconPolicyEnrichmentTests.cs` are the **shared spine** — sets A, C and D all reach into them. They cannot be assigned to a worker in parallel with any of the three.

## Landmines

### (c) The response body on a non-2xx — the answer the plan hinges on

**Only a truncated leading-N-character snippet is available. The stream is never handed back on a non-2xx.**

- `AdapterHttpClient.SendForStreamAsync` (Infra `src/IntegrationInfra/Conversation/AdapterHttpClient.cs:92`): on the legacy `IHttpSession` path, success returns `new AdapterStreamResponse(...)` (`:134`); failure reads `StreamedResponseBodyReader.ReadFirstCharsAsync(streamed.ContentStream, _maxErrorSnippetChars, ct)` (`:137-139`), calls `ThrowFailure` and **disposes `RawResponse` in a `finally`** (`:141-149`). The stream is gone.
- `StreamedResponseBodyReader.ReadFirstCharsAsync` (Infra `src/IntegrationInfra/Conversation/StreamedResponseBodyReader.cs:7-23`) — a single `reader.ReadAsync` into a `char[maxChars]`. **It does one read, so it can return FEWER than `maxChars` characters even when more are available.** JSON parsed from that snippet can be truncated mid-document for reasons unrelated to the cap.
- Two access points, in this order:
  1. `IHttpFailureClassifier.ClassifyFailure(request, response, bodySnippet, context)` — invoked at `AdapterHttpClient.cs:166` and **receives the RAW, unscrubbed snippet** (comment at `:174-175` says so deliberately). This is the only hook that can act on `errors[]` programmatically.
  2. `AdapterHttpRequestFailedException` (Infra `src/IntegrationInfra/Kernel/Exceptions/AdapterHttpRequestFailedException.cs:9`) carries `StatusCode`, `Url`, `Context`, **`BodySnippet` (SCRUBBED via `LogRedaction.Scrub`, `AdapterHttpClient.cs:176,196`)**, `Method`, `RetryAfter`. Consuming `BodySnippet` downstream means parsing redacted text.
- `maxErrorSnippetChars` is a constructor parameter (default 2000). The policy enricher already raises it to **16000** (`FalconPolicyEnricher.cs:47,58`) precisely because "a single device entity is already ~2 KB".

**Consequences the design must absorb:**

1. The **"400 with populated `resources` + `errors[]`"** case is the dangerous one. A 400's `resources` array is only visible inside the snippet, and CrowdStrike puts `errors` *after* `resources` (per the enricher's own comment at `FalconPolicyEnricher.cs:44-46`). At ~2 KB per device entity, a 16000-char snippet holds roughly 8 entities before `errors[]` is cut off entirely — and a requested-vs-returned comparison built on a truncated `resources` array will report returned ids as missing. **A requested-vs-returned comparison cannot be driven from a non-2xx snippet at device-entity scale.** Either raise the snippet budget to cover a full bounded unit (5000 AIDs × ~2 KB ≈ 10 MB — not viable), or treat a 400 as "this whole unit is unresolved" rather than as partial data.
2. The **"200 whose body carries a 404 under `errors[]`"** case is *not* reachable today at all: on a 2xx both policy clients read the stream with `new TopLevelJsonArrayStreamReader(streamed.ContentStream, arrayPropertyName: "resources")` (`FalconDevicePolicyClient.cs:62`, `FalconPreventionPolicyClient.cs:67`), which projects out the `resources` array only. **A sibling `errors[]` on a 200 is silently discarded.** Reaching it needs either a reader change or a second pass — and the reader is Infra-owned (`Cymulate.IntegrationInfra.Kernel.Json`). Worth verifying whether `TopLevelJsonArrayStreamReader` exposes sibling properties (it exposes `Total` and `AfterToken`, per `FalconAssetsScrollRunner.cs:311,333` and `FalconDiscoverHostScroller.cs:97`, so the mechanism exists — but `errors` specifically is not surfaced today).
3. `FalconHttpFailureClassifier.TryFormatCrowdStrikeStyleError` **bails out when `bodySnippet.Length > 4096`** (`FalconHttpFailureClassifier.cs:91`). With the policy path's 16000-char budget, a large error body is never parsed by the existing formatter — the exact case the 16000 was raised for. This is already latent today.
4. `FalconHttpFailureClassifier` raises `FalconCursorExpiredException` **only when the URL contains `after=`** (`:22,59-60`). Neither policy endpoint ever carries an `after` cursor, so a 404 there becomes a plain `AdapterHttpRequestFailedException` with status 404.

### (d) Streaming write + `Encode`

- **`ObjectWriteRequest.FromStream` exists and is reachable.** Infra `src/Cymulate.Integration.Client/Contracts/ObjectStoreRequests.cs:257` (`FromBytes` at `:243`). The stream is read **once, forward-only, length unknown, caller owns disposal** (`:222-230`, and `IAdapterObjectStore.cs:93-98` obliges implementations to multipart-upload rather than demand a length).
- `GuardedObjectStore.WriteAsync` (Infra `src/IntegrationInfra/Ingestion/GuardedObjectStore.cs:274-289`) passes both forms straight through, but **throws `DataPipelineException` for a buffered payload over `Options.MaxInMemoryObjectBytes` (default 8 MB**, `IngestionOptions.cs:29,51`) with the message "Supply ContentStream instead".
- `FalconStagingArea` currently reaches only `FromBytes` — `WriteHostPageAsync:188-190` and `WriteManifestAsync:205-209`. Nothing blocks adding a `FromStream` path; `_objects` is the `GuardedObjectStore` (`:33`).
- `FalconStagedHostPage.Encode` returns **`ReadOnlyMemory<byte>`** (`FalconStagedHostPage.cs:34`) — the whole page materialized in an `ArrayBufferWriter<byte>` (`:38-54`). So a staged host page is a buffered write today and is subject to the 8 MB in-memory cap.
- **Read-side ceiling is a separate, much tighter number.** `TryReadManifestAsync` passes `Options.MaxControlArtifactBytes` — default **1 MB** (`IngestionOptions.cs:38,92`), tightened further under memory pressure (`GuardedObjectStore.cs:307`). Turning the manifest into a staged ledger grows it: `PageKeys` already dominates it, and `_staging/gen_xxxxxxxxxxxxxxxx/hosts_NNNNNN.json` is ~48 chars, so **~20k pages ≈ 1 MB and the ledger becomes unreadable** — and an unreadable manifest is *deliberately indistinguishable from an absent one* (`FalconStagingArea.cs:250-253`), so the run silently re-spools instead of failing. Per-stage coverage counters must be O(1) per stage, not O(pages).
- `GuardedObjectStore.OpenReadAsync` holds one of `MaxConcurrentReads` slots (default 4, **1 under memory pressure**) until the stream is disposed; `ListAsync` and `ReadNdjsonLinesAsync` hold none (`GuardedObjectStore.cs:30-42,110-127`). A nested `OpenReadAsync` inside a page-read loop deadlocks into a `DataPipelineException` read-slot timeout (`:361-379`).

### (b) `FalconStagingPaths` prefixes, and whether `_staging/policies/pgen_*` fits

Prefix construction is entirely string concatenation off two constants (`StagingFolder = "_staging"`, `GenerationPrefix = "gen_"`): `StagingAreaPrefix` = `"_staging/"` (`:114`), `GenerationAreaPrefix(id)` = `"_staging/{id}/"` (`:117`), `ManifestKey(id)` = `"_staging/{id}/manifest.json"` (`:121`), `HostPageKey(id, i)` = `"_staging/{id}/hosts_{i:D6}.json"` (`:128-136`). `RequireGeneration` (`:166-184`) **hard-requires the `gen_` prefix** and rejects `/` and `..`. Generation ids are fixed-width hex ticks so lexical order is chronological (`NewGenerationId:110`).

A sibling `_staging/policies/pgen_<id>/` prefix **fits without fighting anything, but silently loses garbage collection**:

- ✅ It is under `_staging/`, so `AssertNotParserVisible` still holds and `CountPublishedFindingsAsync` skips it (`FalconStagingArea.cs:106-109`).
- ✅ `WarnIfNotRelativeToBase` accepts anything under `_staging/` (`:155-158`).
- ✅ `TryGetGenerationId("_staging/policies/pgen_x/...")` returns **null** — the first segment is `policies`, which does not start with `gen_` (`FalconStagingPaths.cs:157-158`). So `IsManifestKey` is false and `TryFindCompletedGenerationAsync` will never mistake a policy artifact for a host generation (`:234-245`).
- ⚠️ **But `DeleteAbandonedGenerationsAsync` `continue`s on exactly that null** (`FalconStagingArea.cs:327-331`), so **policy artifacts are never collected.** They accumulate under every run's prefix until the host's run-scoped lifecycle reclaims the whole prefix. If that is acceptable, say so explicitly; if not, the pruner needs its own `pgen_` arm.
- ⚠️ `RequireGeneration` means **none of `GenerationAreaPrefix` / `ManifestKey` / `HostPageKey` can be reused for a `pgen_` id** — a parallel set of builders is required, and the two must not diverge on the traversal-safety checks (`:176-182`).
- ⚠️ The generation-folder rationale (`:39-64`) is *positional keys*. If the definitions sweep keys by policy id (identity-derived), a `pgen_` generation folder is unnecessary complexity — the flow-patterns skill flags this exact trap by name.

### Where a policy PLANE can legally be published — hardest constraint on item 3

`NdjsonBatchEmitter.BuildMandatoryTargetPath` **throws `InvalidOperationException` for any output name other than `findings` or `assets`** (Infra `src/IntegrationInfra/Emission/NdjsonBatchEmitter.cs:243-257`; names at `AdapterGlobalDefaults.cs:19-20`; format `{name}_{page:D6}.json` at `AdapterOutputDefaults.cs:16-30`). There is no third lane. So the plane has exactly three possible homes:

1. **Named `findings_NNNNNN.json`** — parser-visible, goes through Emission's guards, and lands in the same dense page counter the findings objects use. But it then also lands in `CountPublishedFindingsAsync` (`FalconStagingArea.cs:113-114`), which is the orphaned-leg guard's evidence (`FalconFindingsFlow.cs:238-255`) and the policy-mismatch gate's evidence (`:584-598`). **A definitions object published as `findings_*` would make a fresh leg believe a previous leg published.** That guard would need to distinguish plane objects from findings objects.
2. **Named `assets_NNNNNN.json`** from the findings flow — parser-visible, but mixes lanes and the assets flow's own numbering.
3. **Under `IAdapterObjectStore`** — *not* parser-visible (`FalconStagingPaths.cs:9-15`) and explicitly documented as the control-artifact lane, where "publishing a data lane through this interface bypasses every guard Emission enforces and is a defect" (`IAdapterObjectStore.cs:8-15`).

None of the three is free. This needs an explicit decision before any code.

### (a) Every staged-host-page and manifest-field call site

**Staged host page — writes (3):**
- `FalconStagingArea.WriteHostPageAsync:181-195` (the only writer).
- `FalconHostSpooler.SpoolAsync:163-165` — the raw spool write.
- `FalconHostSpooler.EnrichStagedPagesAsync:281-283` — the **second, replacing** write. Item 2 deletes this one; item 2's "immutable after freeze" rule makes this line the whole point of the restructure.
- Tests: `FalconTwoPhaseFindingsTests.cs:318` and `FalconConcurrencyHarness.cs:608` seed pages via `FalconStagedHostPage.Encode`.

**Staged host page — reads (3):**
- `FalconStagingArea.ReadHostPageAsync:287-294` (the only reader) and `HostPageExistsAsync:283`.
- `FalconHostSpooler.EnrichStagedPagesAsync:267-269` — read-modify-write. Dies with item 2.
- `FalconFrozenKeyList.EnumerateAsync:101` (exists) and `:123` (read). This is Phase 2's only source of hosts.

**`FalconPhase1Manifest` field readers:**
- `PageKeys` → `FalconFrozenKeyList.cs:86,90`; `FalconHostSpooler.ApplyPolicyContractAsync:242`; `ToString():143`.
- `AidBatchSize` → `FalconFindingsFlow.cs:211,217`; `FalconFrozenKeyList.cs:126`; `IsUsable():128`.
- `HostsPerPage` → `IsUsable():127` only (the spool uses its own `hostsPerPage` local).
- `GenerationId` → `FalconFindingsFlow.cs:248,337,401,602,616`; `FalconHostSpooler.cs:242,250`; `FalconStagingArea.WriteManifestAsync:207`.
- `HostCount` → `FalconFindingsFlow.cs:337,401`.
- `DiscoverWatermarkUtc` / `DiscoverWatermarkAids` → `FalconFindingsFlow.cs:341-342` → `FalconFindingsCheckpointWriter.OnBatchPublished:40-41,72-73,106`.
- `PolicyEnvelopeSchemaVersion` / `PolicyEnrichmentEnabled` / `HasCompatiblePolicyContract` / `WithPolicyContract` → **`FalconFindingsFlow.cs:579,591-594,603-606` and `FalconHostSpooler.cs:216-217,245` ONLY.** Nothing else in production reads them; see (f).

**What breaks when the manifest gains stages and pages stop carrying policy envelopes:** the compile-time surface is small (the readers above), but four *behavioural* contracts break at once — (i) the `Writes.Should().HaveCount(3)` / `Writes[1] == Writes[0]` write-order assertion, (ii) the "legacy manifest gets upgraded in place" path, (iii) the "mismatch with published work fails" path, (iv) the staged-page-contains-`device_policies` assertion. All four are in `FalconTwoPhaseFindingsTests.cs`; see below.

### Tests that break

- `FalconTwoPhaseFindingsTests.cs:438 Phase1_StagesDiscoverHostsUnderStorageUrl_AndWritesTheManifestLast` — asserts **exactly 3 writes**, `Writes[1] == Writes[0]` ("policy enrichment atomically replaces the frozen page", `:470`), manifest last (`:471`), and that the staged bytes `Contain("\"device_policies\"")` (`:482`). **Items 1, 2 and 3 all break this test.** With the manifest written straight after the scroll and pages immutable, the expected sequence becomes 2 writes with the manifest second.
- `:486 LegacyManifest_WithNoPublishedWork_IsPolicyEnrichedAndUpgradedBeforePhase2` — asserts `deviceCalls == 1` during an upgrade and `HasCompatiblePolicyContract(true)` on the rewritten manifest (`:527-532`). **Item 1 deletes the mechanism this test describes** (`ApplyPolicyContractAsync` + the compatibility gate).
- `:536 LegacyManifest_WithPublishedWork_FailsInsteadOfMixingPolicyContracts` — asserts the error message contains "mix host policy contracts inside one run" (`:560`), i.e. `FalconFindingsFlow.cs:589-597`. **Dies with item 1.**
- `:565 ManifestModeMismatch_WithNoPublishedWork_RewritesPagesForTheRequestedMode` — asserts `deviceCalls == 0` and a published `"collection_status":"disabled"` (`:599-601`). **Dies with item 1**; the disabled-envelope half of the assertion needs a new home under item 3.
- `:718 PublishedEnvelope_IsByteCompatibleWithTheCurrentParserContract` — pins the findings record **byte for byte** at `:765-768`. Any plane field added to the record breaks it; note the pinned string has NO `device_policies` because `SeedCompletedPhase1` stages a raw host, so this test does not currently prove the envelope's presence in findings output.
- `FalconCorrelatedFindingsTests.cs:1462 Policy_EnrichedHost_AppearsInEveryChunkOfAMultiChunkHost`, `:1514 Policy_OneAssignmentLookupPerStagedPage_CarryingThatPagesAids`, `:1580 Policy_AssignmentFailure_SkipsSpotlightAndPublishesNothing`, `:1620 Policy_ZeroFindingHost_StillCarriesTheEnvelope` — all assert `record.host.device_policies` (`:1506,1648`). **Item 3 breaks all four.**
- `FalconCorrelatedFindingsTests.cs:1653 Policy_SameSourceHost_IsCanonicallyEqualAcrossAssetsAndFindings` — runs BOTH flows and asserts the serialized asset record equals the serialized findings `host` (`:1699-1702`). **Item 3 makes this assertion false by design** (assets keeps the embedded envelope; findings moves to edges). This test *is* locked decision #11 in executable form — deleting or weakening it is an explicit reversal, not a test fix.
- `FalconConcurrencyHarness.cs:620-632` — a **second** manifest-seeding helper, used by `FalconSpotlightConcurrencyTests.cs` (1455 ln). Item 1 must update both harnesses or the concurrency suite fails to compile.
- `FalconTwoPhaseFindingsTests.cs:227-265 CreateFactory` — the shared fake transport routes `/devices/entities/devices/v2` and `/policy/entities/prevention/v1` to `{"resources":[]}` as a fallback (`:252-259`). **Item 4's new `/policy/combined/prevention/v1` will fall through to the `404 Unexpected url` default at `:261-264`** unless the factory is extended. Per the collector-tests skill, that is a harness gap, never a reason to weaken production.
- `FalconAccessProberTests.cs:191,232` and `FalconFlowExceptionClassifierTests.cs` — item 6's prober and classifier changes land here.

### Other traps

- **`SpoolAsync` skips writing a page when every host is a duplicate.** `FalconHostSpooler.cs:157` guards the write with `if (freshHosts.Count > 0)`, so such a Discover page produces **no staged object and no `pageKeys` entry** — exactly the "write every traversal unit's object even when empty" gap in item 6. Note also that this same guard skips `AdvanceWatermark` and the heartbeat.
- **The spool loop catches `FalconCursorExpiredException` only** (`FalconHostSpooler.cs:121`). A mid-scroll 500 surfaces as `HttpRequestException`/`AdapterHttpRequestFailedException` with `StatusCode >= 500` from `FalconDiscoverHostScroller.FetchPageAsync:65-67`, escapes the loop, and is classified by `FalconFlowExceptionClassifier.cs:67-72` as retryable `FALCON_SERVER_ERROR` → a host-scheduled deferral. Adding an in-process re-anchor arm **removes that deferral for 5xx** and makes the spool absorb a persistent outage in-process instead. That is a change of failure semantics, and it needs a bound (the depth-cap comment at `:141-142` records that CrowdStrike's scroll "eventually hard-500s", so 500s are expected at depth and an unbounded re-anchor loop is a real risk).
- **`DataPipelineException` is `sealed` and derives from `InvalidOperationException`** (Infra `src/IntegrationInfra/Kernel/Exceptions/DataPipelineException.cs:6`). `FalconFlowExceptionClassifier` has no `InvalidOperationException` arm today, so a new `DataPipelineException` arm can go anywhere before `_ => null`. But the type covers **three unrelated faults** — over-ceiling buffered read/write, read-slot timeout, and a null stream from the store (`GuardedObjectStore.cs:139,260,283,330,374`) — and the flow-patterns skill records that treating it as one thing "turns a systemic fault into mass silent degradation". Marking it non-retryable also makes the **1 MB manifest ceiling** a hard run failure rather than a re-spool; weigh that against the current silent-re-spool behaviour at `FalconStagingArea.cs:250-253`.
- **`FalconFindingsFlow.cs:238-255` (orphaned-leg guard) and `:584-598` (mismatch gate) both use `CountPublishedFindingsAsync` as their evidence.** Anything new published under the run prefix whose name starts `findings_` and ends `.json` changes both.
- **The findings flow constructs a `FalconPolicyEnricher` unconditionally at `:169`**, before `ResolveFrozenKeyListAsync`, and it is currently used only by Phase 1 / the upgrade path. Moving policy work to a post-freeze stage keeps that construction site valid but changes who consumes it.
- **`FalconPolicyEnricher.BuildAssignedEnvelope:198,224` deep-clones both the assignment and the cached definition per host** because "a `JsonNode` can have only one parent". This is precisely the cost item 3 removes for findings — and the constraint that makes it unavoidable for assets, where the envelope must sit on the record.
- **`ProbePreventionPolicyAsync` fails init on a 404 today.** `FalconAccessProber.ProbePreventionPolicyAsync:51` calls `FetchPreventionAssignmentsAsync` / `FetchDefinitionsAsync` **directly, not through `ProbeAsync`** — so the 5xx-swallow at `:178-194` does not apply, and a 404 propagates. It surfaces at `FalconConfigurationValidationService.cs:82`, is caught by the blanket `catch (Exception)` at `:92`, and becomes `ConfigurationValidationResult.AuthenticationFailed` (`:111`) — i.e. the **connection test fails**, not just init. The hint at `:103-108` only fires for 401/403.
- **Config default is currently `false` in production config but `true` in both run-config DTOs.** `FalconCollectorConfiguration.cs:179` (uncommitted, `false`) is mapped into the DTOs by `FalconFlowRunPreparer.cs:36,76`, but `FindingsFlowRunConfig.cs:58` and `AssetsFlowRunConfig.cs:29` both default to `true`. Any test that constructs a run config directly gets enrichment ON; any run through the collector gets it OFF on this branch.
- **`FalconStagingPaths.HostPageKey` is derived from the spool page index, never a running counter** (`:124-127`). The staged ledger must preserve that or a re-spool stops overwriting its own objects.
- **`FalconPhase1Manifest.cs:75-103`** is a written-down prohibition on deriving a traversal position from a count of published objects (three separate production defects are named). A "staged ledger of per-stage terminal state + coverage counters" is close enough to that shape that the prohibition should be re-read before designing it.

### (e) Where the assets flow's per-page enrichment publishes

`FalconAssetsScrollRunner.ProcessPageAsync` materializes exactly one page (`:307-313`, `MaterializePageAsync`), enriches it in place (`:315`), then publishes with **`NdjsonBatchEmitter.Create(_context.Services).PublishAssetsUtf8PageAsync(...)`** at `:317-324` → `assets_{page:D6}.json` under the run's storage URL (or `batch_NNNNNN/` when batch-scoped). Serialization goes through `ToUtf8Records` (`:495+`), which the comment at `:501-505` says deliberately uses the same `JsonNode.ToJsonString` path as the findings record so a given source host serializes byte-identically in both flows.

**So: assets can keep the embedded envelope.** Nothing in the assets path reads the manifest, the staging area, or `FalconStagedHostPage`; its only coupling to this work is the shared `FalconPolicyEnricher.EnrichAsync` / `PolicyEnrichmentTarget` signature and the shared `FalconDevicePoliciesEnvelope` vocabulary. The plane change forces an assets change **only if** the enricher's signature changes — keep an `EnrichAsync(targets, ct)` overload that attaches envelopes and assets needs no edit at all. The cost is that doc 06 §6.1's promise ("the same source host produces a canonically identical object in both") and its executable form (`FalconCorrelatedFindingsTests.cs:1653`) both become false.

### (f) Does anything persist the policy fields into the checkpoint?

**No.** A repo-wide grep for `PolicyEnvelopeSchemaVersion` / `PolicyEnrichmentEnabled` / `EnablePreventionPolicyEnrichment` returns nothing under `Recovery/`. Specifically:

- `Recovery/FalconCheckpointKeys.cs` (all 44 lines) has no policy key.
- `Recovery/FalconCheckpointState.cs` — `FalconFindingsCheckpointState` (`:140-194`) carries `FormatVersion`, `StagingGenerationId`, `StagedHostCount`, `LastCompletedStagedPage`, `LastCompletedBatchIndex`, `AidBatchSize`, `DiscoverWatermarkUtc`, `DiscoverWatermarkAids`, `TotalAssetsEmitted` — no policy field.
- `Recovery/FalconCheckpointSerializer.cs` / `FalconCheckpointDeserializer.cs` / `FalconCollectorRecoveryHandlers.cs` / `FalconResumeRunner.cs` / `FalconCheckpointResumePolicy.cs` — none mention policy.
- `FalconFindingsCheckpointWriter.OnBatchPublished:56-75` builds the state from config + manifest + counters; no policy field.

The two fields live **only** on the manifest, and the enrichment mode is re-read from configuration on every leg (`FalconFindingsFlow.cs:169`). Consequence for item 1: **replacing them is a staging-artifact format change, not a checkpoint format change** — so the standing constraint "no checkpoint format bump unless persisted state changes" is satisfied without bumping `CurrentFormatVersion = 4`, provided the staged ledger keeps `StagingGenerationId` + the two position fields meaningful. The compatibility story for an in-flight old-format *manifest* must be handled by the manifest's own `TryParse`/`IsUsable` gate (`FalconPhase1Manifest.cs:108-129`), where an unusable manifest reads as absent and the run re-spools.
