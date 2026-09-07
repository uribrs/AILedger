# Collector field availability audit

**Date:** 2026-09-03
**Scope:** read-only investigation of `/Users/user/Dev/cymulate-integration-adapters` and `/Users/user/Dev/IntegrationInfra`
**Method:** source and git history only. No collector was executed. TenableIoCollector, FalconCollector, IsbLoadTestCollector and DummyCollector are operator-prohibited from running; nothing here required running them.
**Purpose:** establish, with citations, which fields each collector actually publishes, so a per-vendor mapping is not written against a field the collector never emits.

Path convention in this document:

- `ADAPTERS` = `/Users/user/Dev/cymulate-integration-adapters/src/Cymulate.Integration.Adapters`
- `COLLECTORS` = `ADAPTERS/Collectors`
- `INFRA` = `/Users/user/Dev/IntegrationInfra/src/IntegrationInfra`

Official vendor documentation is vendored in the repo at `COLLECTORS/FalconCollector/FalconDocs/OfficialDocs/` (`spotlight.pdf`, `discover.pdf`, `crowdstrike-auth.pdf`). Line numbers cited against those PDFs refer to `pdftotext -layout` output and are given as orientation, not as stable references.

---

## Q1. `apps` on the Falcon findings lane

### Direct answer

**The current `FalconCollector` never publishes `apps`. It is explicitly removed from every finding before emission.**

`COLLECTORS/FalconCollector/Flows/Findings/Correlated/FalconCorrelatedRecord.cs:21`

```csharp
private static readonly string[] StrippedFindingFields = { "apps", "suppression_info", "host_info" };
```

Applied in `ShapeFinding` at `FalconCorrelatedRecord.cs:34-38`. That method is the only path a Spotlight finding takes into a published record — its single call site is `COLLECTORS/FalconCollector/Flows/Findings/Correlated/FalconSpotlightBatchScroller.cs:202`. There is no configuration flag that restores `apps`; the prune list is a `static readonly` array with no override.

### It was published once, then removed. This is the actual lesson.

**The prototype's `apps[0].product_name_version` mapping was not invented. It was stale — correct against the collector generation it was written for, and superseded by a later one.**

Two commits bound the change:

| Commit | Date | What it did |
|---|---|---|
| `11d0deaa` "Falcon refactor and events wiring" | earlier generation | The findings flow emitted **one enriched Discover asset per line**: `assetObj["vulnerabilities"] = vulnsForAsset` plus `vulnerabilities_count`, where each element of `vulnerabilities` was the **verbatim** Spotlight vulnerability object. See `git show 11d0deaa:.../Flows/Findings/FalconFindingsFlow.cs`, lines 205-300 (assignment at 273-278, one-asset-per-line NDJSON write at 279-300). |
| `f68e0113` "Falcon findings flow: correlated asset-driven collection (collector v5.0.0)" | 2026-07-05 | Created the correlated `{aid, chunk, isLastChunk, findingsInChunk, host, findings[]}` grammar **and introduced the `apps` strip in the same commit**. `git show f68e0113` places the prune list at what is now `FalconCorrelatedRecord.cs:21`, with the rationale in the commit's own design notes ("`apps`/`suppression_info` are not consumed downstream and inflate the payload", ~70% payload reduction). |

`apps` is a **default top-level member of a Spotlight vulnerability resource** — not a facet, so it arrives whether or not you ask for it. Evidence from the vendored official doc: the `GET /spotlight/combined/vulnerabilities/v1` example at `spotlight.pdf` requests only `facet=host_info&facet=cve&facet=remediation`, yet the example response body still contains a full `apps` array with `product_name_version` inside it (`spotlight.txt:1736-1751` in the extracted text). Confirmed independently by the repo's own captured vendor fixture, `ADAPTERS/UnitTests/Collectors/JsonTests/CollectorResponseSchemas/CrowdstrikeResulteSchema.json` (added in `90f8aefb`), which carries `apps[].product_name_version` at line 1269-1272 of that commit's diff.

**Therefore `apps[0].product_name_version` was a correct read against the `11d0deaa` grammar**, where each element of `vulnerabilities[]` was the untouched vendor resource. It stopped being correct on 2026-07-05. Note also that the older envelope's array key was `vulnerabilities`, not `findings` — so the prototype mapping was written against a different array name as well, which is a second, independent signal that it targeted the superseded contract.

The failure mode to record is **treating a superseded collector contract as current**, not fabrication. Any mapping inherited from a prototype must be dated against the collector generation it was authored for.

### Where `apps` still appears (none of it is output)

- `ADAPTERS/UnitTests/.../FalconCorrelatedFindingsTests.cs:529` — input fixture
- `ADAPTERS/UnitTests/.../FalconCorrelatedFindingsTests.cs:560` — `finding.TryGetProperty("apps", out _).Should().BeFalse()`; the strip is pinned by test
- `ADAPTERS/UnitTests/.../FalconTwoPhaseFindingsTests.cs:929` — input fixture
- `ADAPTERS/UnitTests/Collectors/JsonTests/CollectorResponseSchemas/CrowdstrikeResulteSchema.json` — captured raw vendor body for JSON-reader tests

`git log -S'"apps"'` over `COLLECTORS/FalconCollector/` returns exactly one commit, `f68e0113`. No Falcon collector code ever referenced the name before or after — consistent with "the old flow passed it through without naming it".

---

## Q2. The exact field set `FalconCollector` publishes, per lane

### The correlated findings envelope

Six properties, in this order, built at `COLLECTORS/FalconCollector/Flows/Findings/Correlated/FalconCorrelatedRecord.cs:63-70`:

| Property | Type | Notes |
|---|---|---|
| `aid` | string | the record key; `HostFindingsAccumulator.RecordAid`, which is `DiscoverHost.Aid` and **not** necessarily the vendor sensor AID (`HostFindingsAccumulator.cs:22-36`) |
| `chunk` | int | 0-based |
| `isLastChunk` | bool | |
| `findingsInChunk` | int | `findingsArray.Count` |
| `host` | object | **always present, on every chunk** — `host.DeepClone()` at `:68` |
| `findings` | array | may be empty; a zero-finding host still emits one chunk-0 record |

Falcon differs from TenableIo here: **Falcon repeats the full `host` on every chunk**; TenableIo omits it after the first. Chunk cap is `ChunkFindingsCap` (~2000, `Processing/Configuration/FalconCollectorConfiguration.cs:144`), enforced at `FalconSpotlightBatchScroller.cs:206-212`. The batch-end flush walks the batch's whole host list — not the correlation index — so every host emits exactly one `isLastChunk: true` record including zero-finding hosts (`FalconSpotlightBatchScroller.cs:232-249`).

### There is NO positive projection and NO field allow-list

**This is the load-bearing finding for the mapping design.**

Nothing in the Falcon collector declares which fields a finding or a host contains. The published field set is defined *subtractively*, by exactly two pieces of code:

1. **Which facets are requested** — `COLLECTORS/FalconCollector/Processing/Urls/FalconUrls.cs:35-41`:
   - always `facet=cve`, `facet=remediation`
   - `facet=evaluation_logic` unless `skipEvaluationLogicFacet` is set (`FalconCollectorConfiguration.cs:131-132`)
   - `host_info` deliberately **not** requested (`FalconUrls.cs:29`), because the Discover host is the envelope
2. **The three-name prune list** — `FalconCorrelatedRecord.cs:21`: `apps`, `suppression_info`, `host_info`

Everything else the vendor returns passes through untouched. There is no DTO, no `[JsonPropertyName]` surface, no query `fields=` list, no writer that enumerates finding members. `ShapeFinding` reparses the raw vendor text into a mutable `JsonObject`, removes three keys, sorts one array, and returns it (`FalconCorrelatedRecord.cs:28-41`).

**Consequence for the normalizer:** a Falcon mapping can only ever be validated against an **observed** field manifest, never against a declared source list. `fieldSource: 'declared'` is unreachable for Falcon. The nearest thing to a declared contract is "the vendor's Spotlight vulnerability schema, for the facets we request, minus three names" — which is a statement about CrowdStrike's schema, not about our code.

### What the `host` block carries

The verbatim `discover/combined/hosts` record, plus **exactly one** added property.

- **Facets requested** — `FalconUrls.cs:12-15`: `facet=risk_factors`, `facet=third_party`, `facet=system_insights`; sorted `last_seen_timestamp.asc`.
- **Verbatim guarantee** — `Flows/Findings/Correlated/HostFindingsAccumulator.cs:19-20` ("The verbatim Discover host record"). The staged round-trip is also verbatim: `Flows/Findings/TwoPhase/FalconStagedHostPage.cs:15-18` documents the staging line as `{"aid":…,"sensorAid":…,"lastSeen":…,"host":{ verbatim Discover record }}` and states the host object is round-tripped unchanged. Those four staging keys are **internal to `_staging/`** and never appear in a published record.
- **The one addition — `device_policies`** — attached at `Flows/Policies/FalconDevicePoliciesEnvelope.cs:226-230` (`host[PropertyName] = envelope`, `PropertyName = "device_policies"` at `:32`). Shape, from `:235-237` and `:88-97`:

```
device_policies: {
  schema_version: 1,                                  // FalconDevicePoliciesEnvelope.cs:35
  collection_status: "complete" | "partial" | "disabled" | "unavailable",
  prevention: null | {
    assignment:        { verbatim vendor assignment },
    definition_status: "resolved" | "not_found" | "unavailable",
    definition:        { verbatim vendor definition } | null
  }
}
```

  `Attach` replaces any pre-existing `device_policies` on the host (`:222-225`). **Every host receives one, unconditionally** — including hosts with no policy, hosts with no usable AID (`FalconPolicyEnricher.cs:424`), and runs with enrichment switched off, which emits a visible `disabled` (`FalconFrozenKeyList.cs:92`). Status semantics: `collection_status: "unavailable"` always pairs with `prevention: null`; a host whose *definition* was unavailable is `partial` with a populated `prevention` (`FalconDevicePoliciesEnvelope.cs:41-52`).

- **Both lanes agree by construction.** `FalconPolicyEnricher.cs:20-33` states the assets flow and the staged findings flow compose the same envelope through the same `FalconDevicePoliciesEnvelope` entry point, so one source host yields a canonically identical envelope in either lane. Pinned by `FalconCollectorTests.R5_BothFlowsEmitCanonicallyIdenticalDevicePolicies`.

### The assets lane

`COLLECTORS/FalconCollector/Flows/Assets/FalconAssetsScrollRunner.cs:317` publishes one Discover host per line — same record as the envelope's `host` block, same `device_policies`, **no envelope wrapper**, no `aid`/`chunk`/`findings` keys.

### Byte-level pin

`ADAPTERS/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.FalconCollector.Test/FalconTwoPhaseFindingsTests.cs:952-968` pins the whole record byte-for-byte. The expected string is the authoritative statement of the shape:

```json
{"aid":"h1","chunk":0,"isLastChunk":true,"findingsInChunk":1,"host":{"aid":"h1","hostname":"H-1","last_seen_timestamp":"2025-01-01T00:00:00Z","device_policies":{"schema_version":1,"collection_status":"disabled","prevention":null}},"findings":[{"aid":"h1","id":"v1","updated_timestamp":"2025-02-01T00:00:00Z","cve":{"id":"CVE-2025-0001"},"remediation":{"entities":[{"id":"a"},{"id":"z"}]}}]}
```

Compare against the test's input at `:922-935`. Three facts fall straight out of the diff, and they matter for Q3:

1. **Member order is the vendor's own order, minus the pruned names.** Input finding order `aid, id, updated_timestamp, cve, apps, suppression_info, host_info, remediation` → output `aid, id, updated_timestamp, cve, remediation`.
2. **Replacing an existing key's value preserves that key's position.** `remediation` keeps its slot even though `remediation["entities"]` was reassigned to a new sorted array.
3. **`device_policies` is appended last** when the vendor host does not already carry one. Input host `aid, hostname, last_seen_timestamp` → output `aid, hostname, last_seen_timestamp, device_policies`.

---

## Q3. Array ordering — the content-hash exposure

Context this answers: the downstream content hash composes `additional_fields`, which carries the whole raw record, so **array order inside vendor objects is inside the derived ids**. Measured multi-element array frequency in 52,000 real findings: `cve.vendor_advisory` 43,913 · `remediation.entities` 18,285 · `cve.references` 12,879.

### Summary table — Falcon findings lane

| Array | Canonically ordered at source? | Hash exposure | Evidence |
|---|---|---|---|
| `remediation.entities` | **YES** — Ordinal sort on each element's serialized form | none | `FalconCorrelatedRecord.cs:76-99` |
| `cve.vendor_advisory` | **NO** | **exposed — highest volume** | no code references it anywhere; see below |
| `cve.references` | **NO** | **exposed** | ditto; also **not deduplicated** — see below |
| `remediation.ids` | **NO** | **exposed — and not on your list** | see below |
| `cve.types` | NO | exposed when multi-element | vendor fixture `spotlight.txt:1784-1786` |
| `cve.cwes` | NO | exposed when multi-element | vendor fixture `spotlight.txt:1787-1789` |
| `data_providers` | NO | exposed when multi-element | vendor sample `spotlight.txt:1729-1734` |
| `evaluation_logic.*` arrays | NO | exposed (only when the facet is requested) | no code touches them |
| `apps[*]` | n/a — stripped | none | `FalconCorrelatedRecord.cs:21` |
| `host_info.*` arrays | n/a — stripped, and facet not requested | none | `FalconCorrelatedRecord.cs:21`, `FalconUrls.cs:29` |

### Summary table — Falcon `host` block (Discover record)

Every array in the host block is vendor-ordered. **Nothing in the host block is sorted by us.** The only code that touches the host object is `FalconDevicePoliciesEnvelope.Attach`, which adds one property and sorts nothing.

Arrays observed in the official `discover/combined/hosts` sample response (`discover.txt:270-420`, our exact endpoint including `facet=risk_factors&facet=system_insights`):

| Array | Location | Hash exposure |
|---|---|---|
| `tags` | host root, and again under a nested block | exposed |
| `groups` | host root | exposed |
| `data_providers` | host root, and nested under `network_interfaces` | exposed |
| `discovering_by` | host root | exposed |
| `network_interfaces` | host root | exposed |
| `mac_addresses` | inside `network_interfaces[*]` | exposed |
| `risk_factors`, `system_insights`, `third_party` faceted blocks | host root | exposed where they contain arrays |

### `cve.vendor_advisory` — not sorted anywhere

Not sorted, not read, not referenced. `grep -rn "vendor_advisory"` across the entire repository (all file types, excluding `obj/`/`bin/`) returns **one** hit: a captured vendor fixture, `ADAPTERS/UnitTests/Collectors/JsonTests/CollectorResponseSchemas/CrowdstrikeResulteSchema.json:336`. Zero hits in any collector source file.

### `cve.references` — not sorted, and **not deduplicated**

Same: zero collector-source references. The only `references` matches under `COLLECTORS/` belong to InsightVmCloud's own projected string field (`COLLECTORS/InsightVmCloudCollector/Flows/Findings/InsightVmCloudVulnerabilityDetailsEnricher.cs:272`), which is unrelated.

An additional hazard visible in the captured vendor fixture: `cve.references` **contains duplicate entries**. `CrowdstrikeResulteSchema.json:340-380` shows the same URL repeated two and three times consecutively (`.../JSA10759` ×2, `.../2016-09/msg00022.html` ×3, and so on). So this array is exposed to both **reordering** and **duplicate multiplicity** — a dedupe-then-sort canonicalisation is needed, not a sort alone, if you want the hash to be stable against that.

### `remediation.ids` — the exposure you did not ask about

`remediation` carries **two** multi-element arrays: `entities` (sorted by us) and `ids` (not sorted, not mentioned in any code). The official sample shows both, in *different* orders relative to each other — `remediation.ids` is `["ec4848…", "91b4ee…"]` while the corresponding `entities` array begins with `ec4848…` (`spotlight.txt:1839-1850`). After `SortRemediationEntities` runs, `entities` is Ordinal-sorted and `ids` is untouched, so within one record the two arrays can no longer be assumed parallel.

**`remediation.ids` should be added to your exposure list.** It is inside `remediation`, which the 18,285-record measurement already tells you is commonly multi-element, and it is entirely unprotected.

### Is the SET of facets requested stable? Can facet order change member order?

Three separate questions; the answers differ.

**a) Facet order in the URL is a fixed literal — it cannot vary at runtime.**
`FalconUrls.cs:35-41` builds the query string from string literals in fixed order:

```csharp
string facets = skipEvaluationLogicFacet
    ? "&facet=cve&facet=remediation"
    : "&facet=cve&facet=remediation&facet=evaluation_logic";
```

There is no collection iteration, no dictionary enumeration, no configuration-driven ordering. Given the flag, the URL is byte-identical every time.

**b) But the facet SET is NOT stable across configurations — and that is a real schema fork.**
`skipEvaluationLogicFacet` is operator-settable (`Processing/Configuration/FalconIdentification.cs:31`, `FalconCollectorConfiguration.cs:131-132`, `Dtos/FindingsDtos/FindingsFlowRunConfig.cs:13`). With it on, `evaluation_logic` is **absent from every finding**, not null. Two tenants — or one tenant before and after a config change — therefore publish findings with **different member sets**, and any hash over the whole raw record changes accordingly. This is a config-dependent contract fork, and it is invisible in the record itself: nothing stamps which facet set produced it.

A related, already-handled case worth knowing: `spotlightSortDescending` is accepted as config but **ignored** by the correlated flow, which pins `updated_timestamp.asc` as its cursor re-anchor axis. The flow logs a warning rather than honouring it (`Flows/Findings/FalconFindingsFlow.cs:129-135`). So that knob cannot change record content or order.

**c) Facet request order does not appear to drive response member order.**
Evidence, labelled as a single official example rather than a stated guarantee: the vendored doc's own request uses `facet=host_info&facet=cve&facet=remediation`, but the example response emits `cve` first, then `host_info`, then `remediation` (`spotlight.txt:1764`, `:1813`, `:1839`). Facet blocks appear at server-chosen positions, not in request order. Our own byte-pinned test is consistent with member order being purely the vendor document's order (see Q2, "Byte-level pin"), because `ShapeFinding` preserves insertion order from the parsed vendor text and only removes keys.

**Confidence:** this is Evidence Hierarchy level 1 for "our URL is deterministic" (source code), level 1-adjacent for "response member order is server-chosen" (one official example, not a documented guarantee), and it is **not** proof that CrowdStrike will never change member order. A hash over whole-record bytes is exposed to vendor member reordering as well as array reordering. If the hash must be stable, canonicalise member order too — do not rely on the observed ordering.

### Does the Spotlight API document any ordering guarantee for these arrays?

**No. Searched the vendored official documentation and found no ordering guarantee for any nested array.**

What the documentation actually says:

- **`vendor_advisory`** — "Link to the vendor page where the CVE was disclosed." (`spotlight.txt:466`). No ordering statement.
- **`references`** — "Array of URLs providing additional information about the CVE." (`spotlight.txt:468`). No ordering statement.
- **`remediation.entities`** — "Array of remediation objects." (`spotlight.txt:576`). No ordering statement. Our sort is therefore a locally invented canonical order, not the vendor's.
- **`sort` applies only to the RESULT SET, not to array contents.** "The `sort` parameter defines criteria for ordering the result set in GET requests." (`spotlight.txt:1486`). The enumerated sort parameters are all record-level timestamps: `created_timestamp`, `closed_timestamp`, `updated_timestamp`, ascending or descending (`spotlight.txt:1490-1510`). No sort parameter addresses a nested array.
- **The only in-record ordering guarantee documented anywhere applies to endpoints we do not use.** For `GET /spotlight/entities/remediations/v2` and `GET /spotlight/entities/vulnerabilities/v2`, "Successful requests return an HTTP 200 code and an array of … objects for the specified IDs, **listed in the order they were provided in the request**" (`spotlight.txt:2261` and `:2901`). That is about the top-level `resources` array of an ID-keyed lookup. The correlated findings flow uses `GET /spotlight/combined/vulnerabilities/v1` (`FalconUrls.cs:38`), for which no such statement exists.
- **Discover** — `discover.pdf` documents `sort` the same way, as a result-set property (`discover.txt:245-246`, `:1087-1088`). No array-content ordering statement anywhere.

The repo's own reasoning agrees that array order is observed-unstable rather than vendor-guaranteed: `FalconCorrelatedRecord.cs:85-86` — "array order is the only observed cross-run difference, so a canonical order makes the emitted finding stable and fixes downstream `entities[0]` nondeterminism". Cross-referenced in `/Users/user/Dev/cymulate-integration-adapters/docs/two-phase-correlation-and-the-object-store-capability.md:330-331` ("removing it regresses the parser *silently*"), `COLLECTORS/FalconCollector/FalconDocs/CollectorDocs/01-collection-strategy.md:113`, and `.../02-decision-making.md:83`.

A neighbouring precedent in the same collector, for the same class of mistake: `COLLECTORS/FalconCollector/FalconDocs/CollectorDocs/06-prevention-policy-contract.md:131` — "**Do not treat the sweep's array order as `precedence`.** The sweep endpoint accepts no confirmed sort", repeated at `.../07-prevention-policy-observed-variants.md:403`. The house position is already that an unsorted vendor array carries no meaning.

### Are ANY other arrays canonically ordered, anywhere in the codebase?

**No. Exactly one payload array is sorted in the entire collector tree.**

`grep -rn "OrderBy\|OrderByDescending\|\.Sort(\|Order(StringComparer"` over all of `COLLECTORS/` (excluding tests and `obj/`) yields one payload-array sort — `FalconCorrelatedRecord.cs:88` — and nothing else. Every other hit is control-plane or diagnostics:

| Site | What it orders | Payload? |
|---|---|---|
| `COLLECTORS/TenableIoCollector/Flows/Assets/TenableIoAssetsFlow.cs:704` | chunk-ID list | no |
| `COLLECTORS/TenableIoCollector/Flows/Findings/Correlated/TenableIoExportPollHelpers.cs:22` | chunk-ID list | no |
| `COLLECTORS/FalconCollector/Flows/Policies/FalconPolicyEnricher.cs:348` | key names in a log line | no |
| `COLLECTORS/IsbLoadTestCollector/...` (5 sites) | synthetic dataset generation | no (not vendor data) |

One order-stability guarantee exists that is not a sort, and it is about **record** order rather than array order: `COLLECTORS/QualysCollector/Flows/Findings/QualysFindingsFlow.cs:261-262` iterates the requested host IDs so line order within a published object is stable across runs and pods.

### Bottom line for the content hash

`remediation.entities` on the Falcon findings lane is **the only array you may assume is order-stable at the source**. Everything else — `cve.vendor_advisory`, `cve.references`, `remediation.ids`, `cve.types`, `cve.cwes`, `data_providers`, every array in the Discover host block, and every array in every other collector — is vendor-ordered, unsorted, and can differ between two fetches of the same logical record. `cve.references` additionally carries duplicates. If `additional_fields` feeds a derived id, the same finding can hash to two different ids, and the highest-volume path to that is `cve.vendor_advisory` at 43,913 of 52,000 findings.

---

## Q4. `TenableIoCollector` — the correlated findings envelope

### Field set

`COLLECTORS/TenableIoCollector/Flows/Findings/Correlated/TenableIoCorrelatedRecordWriter.cs:46-65`:

| Property | Type | Notes |
|---|---|---|
| `uuid` | string | asset key |
| `chunk` | int | |
| `isLastChunk` | bool | |
| `findingsInChunk` | int | `findingsRawUtf8.Count` |
| `host` | object | **conditional — omitted, never null** |
| `findings` | array | |

### `host` is OMITTED, not null, after the first chunk

`TenableIoCorrelatedRecordWriter.cs:52-56` writes `host` only when `hostRawUtf8` is non-null. Rationale at `:14-20`: an asset whose findings exceed the per-envelope cap spans several envelopes and **carries its host on the first one only**; the parser aggregates per `uuid`. An explicit `"host": null` would be a different value downstream from "this envelope does not restate the host", so the property is left out entirely.

**This is a direct behavioural difference from Falcon**, which deep-clones and restates `host` on every chunk. A mapping written against Falcon's envelope and reused for Tenable will read a missing `host` on every chunk after the first.

`CorrelatedRecord.IsHostBearing` (`.../CorrelatedRecord.cs:9-12`) is how the flow counts distinct assets: exactly one envelope per asset carries a host.

### What decides the field set — nothing on our side

No projection, no prune list, no DTO, no field allow-list. `TenableIoCorrelatedRecordWriter.cs:11-13`:

> "The vendor's bytes pass through untouched. `host` is written verbatim from pre-serialized raw UTF-8 — the full `/assets/export` record, or the thin host synthesized from a finding's embedded `asset` — and every finding likewise. Nothing here parses, filters or remaps a field."

Both `host` and every finding are emitted with `WriteRawValue(..., skipInputValidation: true)` (`:54`, `:62`). The asset spine is byte-transparent too: `.../TenableIoAssetSpine.cs:32-33` — "Bytes in, bytes out. Records are stored and returned verbatim."

The **request** is the only field-set authority, and it names no fields:

- **vulns** — `COLLECTORS/TenableIoCollector/Flows/Findings/TenableIoVulnsExportClient.cs:34-44`. Body is `{"filters": {"since": <unix>, "state": ["OPEN","REOPENED"]}}` plus optional `num_assets`. Comment at `:37`: `state=[OPEN,REOPENED]` skips FIXED (closed) vulns; **all severities including info are emitted**.
- **assets** — `COLLECTORS/TenableIoCollector/Flows/Assets/TenableIoAssetsExportClient.cs:44-48`. Body is `{"chunk_size": …, "filters": {"last_assessed": <unix>}}`, the same date anchor as the findings lane (`:20`).

So, as with Falcon, `fieldSource: 'declared'` is unreachable: the field set is whatever Tenable's export returns.

### Pruned or sorted? No. One rewrite exists.

Nothing is pruned. Nothing is sorted. The single shaping rule is the **thin-host rewrite**, `COLLECTORS/TenableIoCollector/Flows/Findings/Correlated/TenableIoThinHostShaper.cs`.

It applies **only** to a host synthesized from a finding's embedded `asset` sub-object, when the spine did not hold the asset (`.../TenableIoVulnPhase.cs:417`; the miss lane is counted via `CorrelatedRecord.IsMiss`).

| Rule | Detail | Line |
|---|---|---|
| Rename | `uuid` → `id` | `:40-42`, `:104-110` |
| Rename | `operating_system` → `operating_systems` (already an array) | `:44-46`, `:112-117` |
| Pluralise-and-wrap | `ipv4`→`ipv4s`, `fqdn`→`fqdns`, `hostname`→`hostnames`, `netbios_name`→`netbios_names`, each as a one-element array | `:33-38`, `:126-152` |
| Null/blank handling | a null or blank singular becomes an **empty** array, never `[null]` | `:121-125` |
| Absent fields stay absent | `first_seen`, `last_seen`, `tags`, `acr_score`, `ratings`, `agent_names` are **not** synthesized as null | `:21-27` |
| Unknown members | copied through unchanged | `:88-99` |
| Non-object / unparseable input | returned unchanged rather than discarded | `:56-82` |

Why it exists (`:12-20`): the two Tenable exports describe the same host in different grammars, and emitting the embedded object verbatim put two host shapes in one lane. Measured on STG `batch_001424` (2026-08-18), the downstream parser dropped the thin host for a null name and cascade-dropped its findings — silently — in 911 of that run's first 1,458 batches.

### Envelope bounding — by count AND bytes

`.../TenableIoVulnPhase.cs:85` — `FindingsPerEnvelopeCap = 2000`; `:535-568` — an envelope closes on whichever of the count cap or `MaxFindingBytesPerEnvelope` is reached first. The byte budget arrived in commit `69ce436f` "fix(tenableio): bound a correlated envelope by finding BYTES, not only count", and `:438-439` notes that envelope count stopped being `ceil(findings.Count / cap)` the moment it existed.

Zero-vuln assets are swept separately and emit `isLastChunk: true` (`.../TenableIoZeroVulnSweep.cs:157`), so the asset spine is complete downstream — the same left-join intent Falcon has.

---

## Q5. Which collectors emit a `sourceType` discriminator

**Two collectors emit a property literally named `sourceType`. A third emits a functionally identical discriminator under a different name. A fourth wraps records in a typed envelope instead.**

| Collector | Property | Values | Citation |
|---|---|---|---|
| **CortexXdr** | `sourceType`, first JSON property, fail-fast on collision | `va_cves`, `endpoint` | `COLLECTORS/CortexXdrCollector/Processing/CortexXdrRecordFormatter.cs:18-19`; collision guard `:54-58`; write `:63`; call sites `Flows/Findings/CortexXdrFindingsFlow.cs:126` and `:453-455` |
| **DefenderVm** | `sourceType`, first JSON property; drops any vendor-supplied one, OrdinalIgnoreCase | `inventory`, `delta`, `recommendationCatalog`, `recommendationScopedVulnerability` | `COLLECTORS/DefenderVmCollector/Processing/DefenderVmRecordFormatter.cs:11-14`; write `:60`; dedupe `:68-72`. Emits an optional sibling `recommendationReference` (`:12`, `:62-65`) |
| **MicrosoftEntraId** | `"Asset Type"` — note the space in the key | `User`, `Device` — **and nothing at all for service principals or applications** | `COLLECTORS/MicrosoftEntraIdCollector/Flows/Graph/MicrosoftEntraEntityEnricher.cs:50` and `:546` |
| **DefenderVm**, machines/software stages only | `type`, with the vendor record nested under `data` | `machine`, `software` | `DefenderVmRecordFormatter.cs:16-20`, `:34-44` |

### Two inconsistencies to plan around

1. **CortexXdr stamps inconsistently across its own two lanes.** The endpoint stream inside the *findings* run is stamped `endpoint` (`CortexXdrFindingsFlow.cs:453-455`), but the **standalone assets flow emits the same endpoint objects unstamped** — `COLLECTORS/CortexXdrCollector/Flows/Assets/CortexXdrAssetsFlow.cs:186` is a bare `NormalizedUtf8Json.SerializeToSingleLine(endpoint)`. A mapping that requires `sourceType` on Cortex assets fails on the assets lane and succeeds on the findings lane.
2. **MicrosoftEntraId's discriminator covers only half its records.** Users and devices carry `"Asset Type"`. Service principals and applications are the verbatim Graph entity with four added keys (`appRoleAssignments`, `oauth2PermissionGrants`, `Group Memberships`, `Applicable Policies`) and **no `"Asset Type"`** (`MicrosoftEntraEntityEnricher.cs:164-208`).

### Not a discriminator

- `COLLECTORS/TenableScCollector/Flows/Findings/Clients/TenableScVulnDetailsClient.cs:51`, `:63`, `:176` use `sourceType` as a **vendor request parameter** (`cumulative` / `patched` on the analysis API). It is never written to output.
- `COLLECTORS/DefenderForCloudCollector/Processing/DefenderForCloudQueryCatalog.cs` hits on `resourceType` are KQL column names.

---

## Q6. Does any collector emit a schema manifest?

### **NO. No collector emits a manifest describing the fields of what it wrote. Your finding stands and the design assumption is safe.**

Stated loudly as requested — there is nothing here that changes the design. Every manifest in either repository is a checkpoint/resume control artifact carrying generation ids, watermarks and counts. None names a field, a type, a cardinality, or a presence rate.

Where I looked:

- `grep -rn -il "manifest"` across all of `ADAPTERS/` and all of `INFRA/`.
- Read the shared primitive and both concrete manifests in full.

What is actually there:

- **The infra primitive is deliberately content-agnostic.** `INFRA/Ingestion/Staging/StagingManifest.cs:7-9` — "a small JSON control artifact describing a staging area — how many lanes it has, what was frozen, which phase produced it." `:11-16` — "The shape is the caller's: this helper is generic on purpose, because what a manifest needs to say is collector-specific and a shared schema here would grow a union of every collector's fields. Infra owns only the read/write discipline."
- **It is size-capped against exactly this misuse.** `StagingManifest.cs:37-44` throws when a serialized manifest exceeds `Ingestion__MaxControlArtifactBytes`, with the message: *"A manifest that large is carrying data, not a description of it."*
- **It is explicitly not authoritative.** `StagingManifest.cs:18-22` — "A manifest is a **description**, never a cursor. Correctness of a resumable flow must not depend on it being present or current … Derive progress from the staged objects themselves."
- **`TenableIoAssetSpineManifest`** carries five fields, all counters and watermarks: `AssetsExportUuid`, `StagedAssetCount`, `SkippedChunkCount`, `BaseDateUtc`, `CompletedUtc` (`COLLECTORS/TenableIoCollector/Flows/Findings/Correlated/TenableIoAssetSpineManifest.cs:29-48`).
- **`FalconPhase1Manifest`** is the same class of artifact — generation id, staged page count, frozen ordered key list, edge-stage state (`COLLECTORS/FalconCollector/Flows/Findings/TwoPhase/FalconPhase1Manifest.cs`). Its role is completion proof for Phase 1, described at `Flows/Findings/FalconFindingsFlow.cs:41-49`.

**The nearest thing to field-shape reporting anywhere is a log line, not an artifact.** `COLLECTORS/FalconCollector/Flows/Policies/FalconPolicyEnricher.cs:337-352` prints the verbatim key set of the first swept Prevention definition, once per run. Its own remarks explain why: nothing in the repository records whether that endpoint carries `precedence` or `is_default`, and "guessing a field name would put an invented contract into the output". That is a direct, in-repo precedent for the position that **observed field shape is the only field shape anyone here has** — and it reinforces the Q2 conclusion that `fieldSource: 'declared'` is unreachable.

---

## Q7. `NdjsonBatchEmitter` guarantees

`INFRA/Emission/NdjsonBatchEmitter.cs` (546 lines).

### Enforced for every collector, no opt-out

1. **One minified JSON string per record — confirmed, and egress is the sole validator.**
   - String path (`:344-353`): every record goes through `ResultsRecordFormatter.NormalizeToSingleLine`.
   - UTF-8 path (`:427-462`): a record containing `\n` or `\r` is reparsed and reserialized minified via `Utf8ResultsRecordFormatter.NormalizeToSingleLine`; a record without one still gets a `JsonDocument.Parse` validity check and is then byte-passed.
   - Either path throws `DataPipelineException("invalid JSON record: …")` on invalid JSON. Comment at `:427-429`: "Egress is the single authoritative JSON validator. Validate every record at append time."
2. **Empty and whitespace-only records are silently skipped** (`:337-341`, `:420-423`) — they never become blank lines.
3. **File naming is owned by the emitter, not the collector.** `BuildMandatoryTargetPath` (`:246-263`) rejects any output name other than `findings` or `assets` with `InvalidOperationException`, then delegates to `AdapterOutputDefaults.BuildPageTargetPath` (`INFRA/Emission/AdapterOutputDefaults.cs:16-29`), which is `{name}_{page:D6}.json`. Constants at `INFRA/Emission/AdapterGlobalDefaults.cs:17,19,20` (`.json`, `findings`, `assets`). A collector chooses *which* of the two names and *which* page number; it cannot choose the pattern.
4. **Atomicity.** One publish call = one logical object. A multipart upload that started but never committed throws rather than reporting success (`:369-376`, `:474-481`), so no checkpoint advances on a partial object.
5. **Minimum batch sizing.** `MaxBytesPerBatch` and the resolved `MaxBufferedBytes` must both be ≥ 5 MiB (`MultipartPartPlanner.MinPartSizeBytes`), validated in the constructor (`:73-87`).
6. **A zero-record publish returns `PublishResult.Ok()` with no location** (`:378-383`, `:483-488`).

### Per-collector choice

1. **`batch_NNNNNN/` scoping is a per-collector opt-in flag — but the emitter owns its whole lifecycle.**
   The flag is a construction parameter (`:38-59`; `NdjsonBatchEmitter.Create(services, batchScopedStorage)` at `:127-137`). Once set, `BeginBatchScope`/`ReleaseBatchScope` (`:267-320`) run inside every page publish, and `:47-49` states a collector "needs nothing beyond this flag".
   Path is `{storageUrl}/batch_{page:D6}` (`INFRA/Envelopes/Common/BatchScopedStorage.cs:11`, `:174`), announced alongside a deterministic `instanceBatchId` — an RFC 4122 v5 name-based UUID of `{baseUrl}/batch_NNNNNN` (`BatchScopedStorage.cs:73-79`).
   Key invariant (`:290-300`): **a batch scope survives only a page that actually produced records.** A 0-record page and a page whose publish threw both release the scope from a `finally`, so the progress event announces the run root rather than an empty batch folder. Releasing is idempotent and does not impede a retry, because the batch path is re-derived from the preserved base URL plus the page number.
   Falcon exposes it as `BatchScopedStorage` config, **default off** (commit `4dc98c8f`); Qualys takes it as a constructor argument (`COLLECTORS/QualysCollector/Flows/Findings/QualysFindingsBatchPublisher.cs:24`).
2. **Which lane name (`findings` vs `assets`)** — the collector's call.
3. **The page number, and page sequencing** — the collector's, and **not enforced**. `:50-58` and `:112-124`: page N+1 must not be published before page N's `AdvancePage`, or page N's progress event announces the wrong folder; and scoping rewrites `Metadata["storageUrl"]` on a shared, unsynchronized `Dictionary`, so concurrent scoped page publishes on one progress context can upload to each other's folder or corrupt the dictionary. The emitter itself holds no mutable state, so publishes against *distinct* progress contexts are safe in parallel.
4. **Batch-level publishes bypass mandatory naming entirely** and are never batch-scoped (`:44-46`): `PublishAsync` / `PublishUtf8Async` take an explicit `targetPath`. InsightVmCloud reaches these through `PublishUtf8PageAsync` with an explicit `outputName`, though it passes the standard constants (`COLLECTORS/InsightVmCloudCollector/Flows/Findings/InsightVmCloudFindingsFlow.cs:60`).
5. **Record byte encoding** — a collector picks the string overload or the UTF-8 overload.

### Caveat that matters for the content hash

The emitter reserializes **only** records containing a newline. A newline-free record is byte-passed after a parse check, so **property order and number formatting are whatever the collector produced** — the emitter does not canonicalise them. Falcon's records are minified by `JsonObject.ToJsonString()` before reaching egress (`FalconCorrelatedRecord.cs:71`), and TenableIo's are written minified by `Utf8JsonWriter` with `Indented = false` (`TenableIoCorrelatedRecordWriter.cs:23`), so neither is reserialized at egress. Egress adds no ordering guarantee of any kind.

---

## Q8. Per-line shape, every collector

### Two corrections to the README's inferred claim

**1. There are 17 collectors, not 18.** `COLLECTORS/YamlCollector/` contains **zero tracked files** — `git ls-files src/Cymulate.Integration.Adapters/Collectors/YamlCollector` returns nothing, and the local directory holds only an empty `Cymulate.Integration.Yaml.Engine` subdirectory plus `bin`/`obj`. It is a stale local artifact. The YAML lane lives at `ADAPTERS/YamlAdapter/`.

`git ls-files` over `COLLECTORS/` lists exactly: CloudGuard, CortexXdr, DefenderForCloud, DefenderVm, Dummy, Falcon, Guardicore, InsightVmCloud, InsightVm, IsbLoadTest, MicrosoftEntraId, Qualys, SentinelOne, ServiceNowCmdb, Taegis, TenableIo, TenableSc.

**2. "Only Falcon and TenableIo emit the envelope; the rest are flat" is wrong. Do not ship it.**

`findingsInChunk` is not the envelope marker — it is Falcon's and TenableIo's *specific grammar*. **Four collectors emit a correlated host-plus-findings envelope, and two more nest findings inside an asset**, in four different grammars, and only two of them use `findingsInChunk`:

| Grammar | Collectors | Uses `findingsInChunk`? |
|---|---|---|
| `{aid, chunk, isLastChunk, findingsInChunk, host, findings[]}` | Falcon | yes |
| `{uuid, chunk, isLastChunk, findingsInChunk, host?, findings[]}` | TenableIo | yes |
| `{HOST_DETAILS, DETECTION_LIST[]}` | Qualys | **no** |
| `{asset:{6 projected fields}, vulnerabilities[]}` | TenableSc | **no** |
| verbatim asset + appended `vulnerabilityDetails[]` | InsightVmCloud (findings) | **no** |
| `{type, data}` wrapper | DefenderVm (machines/software) | **no** |

Inferring "flat" from the absence of `findingsInChunk` is wrong for **Qualys, TenableSc and InsightVmCloud**, all three of which correlate.

### The table

| Collector | Lane | Per-line shape | Evidence (paths relative to `COLLECTORS/`) |
|---|---|---|---|
| **CloudGuard** | Findings | Flat vendor finding **+ added `assets[]`** joined from an asset lookup | `CloudGuardCollector/Flows/Findings/CloudGuardFindingsFlow.cs:165-173`, `:179-183` |
| | Assets | Flat vendor asset, verbatim | `CloudGuardCollector/Flows/Assets/CloudGuardAssetsFlow.cs:91-95` |
| **CortexXdr** | Findings (CVE) | Flat XQL row **+ `sourceType:"va_cves"`** as first property | `CortexXdrCollector/Flows/Findings/CortexXdrFindingsFlow.cs:121-127` |
| | Findings (endpoint lane) | Flat endpoint **+ `sourceType:"endpoint"`** as first property | same file, `:453-455` |
| | Assets (standalone) | Flat endpoint, **unstamped** — inconsistent with the lane above | `CortexXdrCollector/Flows/Assets/CortexXdrAssetsFlow.cs:186` |
| **DefenderForCloud** | Findings | Flat ARG row, verbatim; **field set fixed by the KQL `\| project` clauses** | `DefenderForCloudCollector/Flows/Findings/DefenderForCloudFindingsFlow.cs:101`; projections at `DefenderForCloudCollector/Processing/DefenderForCloudQueryCatalog.cs:92, 148, 195, 296, 372` |
| | Assets | Flat ARG row, verbatim; same projection mechanism | `DefenderForCloudCollector/Flows/Assets/DefenderForCloudAssetsFlow.cs:65` |
| **DefenderVm** | Machines, Software | **Wrapped**: `{type:"machine"\|"software", data:{vendor record}}` | `DefenderVmCollector/Processing/DefenderVmRecordFormatter.cs:16-20, 34-44`; dispatch `Flows/Findings/DefenderVmFindingsFlow.cs:394-397`, `Flows/Assets/DefenderVmAssetsFlow.cs:126-127` |
| | Vulns, Recommendations | Flat vendor record **+ `sourceType`** (+ optional `recommendationReference`) prepended | `DefenderVmRecordFormatter.cs:22-31, 46-80`; dispatch `Flows/Findings/DefenderVmFindingsFlow.cs:389-407` |
| **Dummy** | Findings | Flat synthetic record, 8 fixed fields (`id`, `asset_id`, `vulnerability_id`, `severity`, `title`, `first_seen`, `last_seen`, `state`) | `DummyCollector/Flows/Findings/DummyRecordGenerator.cs:30-42` |
| **Falcon** | Findings | **Envelope** `{aid, chunk, isLastChunk, findingsInChunk, host, findings[]}`; `host` restated on **every** chunk | `FalconCollector/Flows/Findings/Correlated/FalconCorrelatedRecord.cs:63-70` |
| | Assets | Flat Discover host **+ `device_policies`** | `FalconCollector/Flows/Assets/FalconAssetsScrollRunner.cs:317`; `FalconCollector/Flows/Policies/FalconPolicyEnricher.cs:150-155` |
| **Guardicore** | Assets | Flat vendor asset **+ added `policies`** | `GuardicoreCollector/Flows/Assets/GuardicoreAssetsFlow.cs:134, 157` |
| **InsightVmCloud** | Findings | **Asset with nested findings**: verbatim asset + appended `vulnerabilityDetails[]` of *projected* findings | `InsightVmCloudCollector/Flows/Findings/InsightVmCloudVulnerabilityDetailsEnricher.cs:149-177`; per-finding projection `:200-275`; flow `Flows/Findings/InsightVmCloudFindingsFlow.cs:142-166` |
| | Assets | Flat verbatim asset | `InsightVmCloudCollector/Flows/Assets/InsightVmCloudAssetsPagePublisher.cs:50` |
| **InsightVm** | Findings | Flat vendor vulnerability, verbatim | `InsightVmCollector/Flows/Findings/InsightVmFindingsFlow.cs:115` |
| | Assets | Flat vendor asset, verbatim | `InsightVmCollector/Flows/Assets/InsightVmAssetsFlow.cs:101` |
| **IsbLoadTest** | Findings | **Projected DTO**: `{FindingId, ScopeId, Severity, ObservedAt, Asset{Uuid,FirstSeen,LastSeen,Tags}, Plugin{Id}}` | `IsbLoadTestCollector/.../Mapping/IsbDtoMapper.cs:60-81`; emitted at `IsbLoadTestCollector/.../Flows/Findings/Pipeline/Stages/IsbChunkProcessStage.cs:148` |
| **MicrosoftEntraId** | Assets (users, devices) | **Fully projected**, human-readable space-separated keys, `"Asset Type"` discriminator | `MicrosoftEntraIdCollector/Flows/Graph/MicrosoftEntraEntityEnricher.cs:48-65`, `:546` |
| | Assets (SPs, applications) | Verbatim Graph entity **+ 4 added keys**, **no `"Asset Type"`** | same file, `:164-208` |
| **Qualys** | Findings | **Envelope, own grammar**: one line per host, `{HOST_DETAILS:{…}, DETECTION_LIST:[…]}`; record order stable across runs | `QualysCollector/Flows/Findings/QualysFindingsFlow.cs:288-292`, order note `:261-262`; emitter `QualysCollector/Flows/Findings/QualysFindingsBatchPublisher.cs:87-97` |
| **SentinelOne** | Assets | Flat vendor asset **+ added `policy`** | `SentinelOneCollector/Flows/Assets/SentinelOneAssetsFlow.cs:90, 131` |
| **ServiceNowCmdb** | Assets | **Fully projected typed DTO**, ~37 named fields, Newtonsoft-serialized | `ServiceNowCmdbCollector/Flows/Assets/ServiceNowCmdbAsset.cs:5-41`; emitted `ServiceNowCmdbCollector/Flows/Assets/ServiceNowCmdbAssetsFlow.cs:217` |
| **Taegis** | Assets | Flat vendor asset **+ added scalar `hostname`** derived from `hostnames[0].hostname` | `TaegisCollector/Flows/Assets/TaegisAssetsFlow.cs:226-241` |
| **TenableIo** | Findings | **Envelope** `{uuid, chunk, isLastChunk, findingsInChunk, host?, findings[]}`; `host` **omitted** after chunk 0 | `TenableIoCollector/Flows/Findings/Correlated/TenableIoCorrelatedRecordWriter.cs:46-65` |
| | Assets | Flat verbatim export record | `TenableIoCollector/Flows/Assets/TenableIoAssetsFlow.cs:485` |
| **TenableSc** | Findings | **Envelope, own grammar**: `{asset:{ip,uuid,name,os,firstSeen,lastSeen}, vulnerabilities:[verbatim]}` — the asset is a **6-field projection**, the only correlated lane with a declared asset field set | `TenableScCollector/Flows/Findings/Output/TenableScAssetFindingRecordBuilder.cs:15-42`; call site `TenableScCollector/Flows/Findings/TenableScFindingsFlow.cs:122` |
| *(YamlCollector)* | Findings, Assets | **Not a collector — zero tracked source.** The YAML lane emits `Dictionary<string,object?>` rows shaped by the per-vendor YAML spec, so its field set is data-driven, not code-fixed | `ADAPTERS/YamlAdapter/Cymulate.Integration.Adapters.YamlAdapter/Sinks/IsbExecutionSink.cs:57-97`; also `Sinks/WorkflowPublishSink.cs:48-51` |

### Lane coverage, for completeness

Assets **and** findings: CloudGuard, CortexXdr, DefenderForCloud, DefenderVm, Falcon, InsightVmCloud, InsightVm, TenableIo.
Findings only: Dummy, IsbLoadTest, Qualys, TenableSc.
Assets only: Guardicore, MicrosoftEntraId, SentinelOne, ServiceNowCmdb, Taegis.

### Two traps

**Dead code with a misleading shape.** `COLLECTORS/InsightVmCloudCollector/Flows/Findings/InsightVmCloudFindingsRecordBuilder.cs` builds `{state, vulnerabilityId, asset, vulnerability}` — a finding-per-line grammar with the asset embedded. **It has zero call sites**: `grep -rn "InsightVmCloudFindingsRecordBuilder"` matches only its own declaration at `:10`. The live shape is the asset-with-`vulnerabilityDetails` one. Do not map against that file.

**A fifth envelope grammar exists outside the collector tree.** `ADAPTERS/YamlAdapter/Cymulate.Integration.Adapters.YamlAdapter/SiemRules/SiemRulesChunkEnvelope.cs:40` and `SiemRules/SiemRulesChunkFramer.cs:11` emit `{siemRules:{rules}, pageNumber, totalPages, chunkSize, isLastChunk}`. That is the SIEM-rules lane, not a findings/assets NDJSON lane. Flagged only because it uses `isLastChunk` and will match a naive grep for envelope markers.

---

## Consolidated implications for the normalizer

1. **`fieldSource: 'declared'` is unreachable for Falcon and for TenableIo.** Neither collector declares a field set; both define output subtractively (facets/request minus a prune list, or pure byte pass-through). Validation must be against an observed manifest.
2. **Three collectors DO have a declared field set**, and are the only ones where a declared source is honest: **TenableSc** (`asset`, 6 fields, `TenableScAssetFindingRecordBuilder.cs:19-24`), **ServiceNowCmdb** (`ServiceNowCmdbAsset.cs`, ~37 fields), **IsbLoadTest** (`IsbPublishFindingDto`). **DefenderForCloud** is a near-fourth: its field set is fixed by KQL `| project` clauses, which are declared but live in query text rather than a type.
3. **Only one array is order-stable at any source**: `remediation.entities` on the Falcon findings lane. If `additional_fields` feeds a derived id, canonicalise arrays yourself — and dedupe `cve.references`, which carries duplicates. Add `remediation.ids` to the exposure list.
4. **Member order is also exposed**, not just array order. Neither the collectors nor egress canonicalise JSON member order; both preserve whatever the vendor sent.
5. **`skipEvaluationLogicFacet` forks the Falcon findings schema** — `evaluation_logic` is absent, not null, when set — and nothing in the record says which facet set produced it.
6. **The envelope/flat split is not a two-collector question.** Four correlated grammars exist; two do not carry `findingsInChunk`. Correct the README rather than shipping the inferred claim.
