# Cross-artifact consistency review — Qualys assets-first (Collector A anchored)

Anchor: **Collector A** = `cymulate-integration-adapters` (System.Text.Json).
Compared against: **prototype fixtures** (canonical target shape) and **Collector B** = `AgentService` (Newtonsoft).

## TL;DR

A's emitted record shape matches the prototype `combined.ndjson` exactly, and matches B field-for-field.
The only behavioral divergence that matters is the **truncation strategy** (A: follow `<WARNING>/<URL>` cursor + merge; B: `truncation_limit=0` single page). For normal tenants they produce the same complete `DETECTION_LIST`; they diverge only at an extreme batch size, and A is the safer of the two. No blocker found for SPARK consumability.

---

## 1. Record shape parity (A vs prototype) — MATCH

Prototype `combined.ndjson` (newest: `QualysAssets_20260609_145715_071Z/combined.ndjson`, 7 records) is the collector emit shape:

```
{"HOST_DETAILS":{...host-list fields...},"DETECTION_LIST":[...]}
```

A emits identically:
- `QualysFindingsBatchPublisher.cs` `EnumerateBatchRecords` → `host.ToJsonString()` per line (NDJSON), where each `host` is the `JsonObject{ ["HOST_DETAILS"], ["DETECTION_LIST"] }` built in `QualysFindingsFlow.BuildHostRecords` (`QualysFindingsFlow.cs:228-258`).
- HOST_DETAILS is the **host-list/asset spine** (`details=All`), built in `QualysFindingsApiClient.GetHostDetailsAsync` → `ParseHostDetailsPageAsync` → `ParseHostDetailsAsync` (`QualysFindingsXmlParser.cs:317-343`). This matches the prototype: HOST_DETAILS field set **varies per host** (only present fields emitted; e.g. prototype host 0 has 20 fields incl. `QG_HOSTID`/`SERIAL_NUMBER`, host 3 has 9). A reproduces this because it only writes properties present in the XML.
- `DNS_DATA` nested object: prototype emits `{"HOSTNAME","DOMAIN","FQDN"}`. A's `ConvertElementValue` (`QualysFindingsXmlParser.cs`) returns a `JsonObject` for any element with child elements, so `<DNS_DATA><HOSTNAME>…` → nested object. MATCH.

**Findings-less host → `DETECTION_LIST: []`** — MATCH. `BuildHostRecords` left-joins detections onto the spine; a host absent from `detectionsByHostId` gets `new JsonArray()` (`QualysFindingsFlow.cs:247`). A's test `ProcessAsync_Findings_PublishesFindingslessHostWithEmptyDetectionList` asserts exactly this (host 202: `DETECTION_LIST` is an empty array, full HOST_DETAILS with NETBIOS+IP).

Caveat on `exposureless_asset.json`: it is **NOT** the collector emit shape — it is the downstream SPARK-transformed asset (`{asset:{…,additional_fields,finding_ids},exposures:[]}`), and the probe note says it is *synthesized* ("no exposureless host on this tenant … exposures stripped"). So it validates the SPARK *output* contract, not A's emit. The relevant emit-shape anchor for empty detections is the test above + the prototype's Stage-4 note ("0 with empty DETECTION_LIST" on this tenant, but shape documented as `{HOST_DETAILS,DETECTION_LIST:[]}`).

### VULNERABILITY_INFO — expected difference, NOT a divergence
Prototype detections are flat (no `VULNERABILITY_INFO`). A injects `VULNERABILITY_INFO` as a sibling key inside each detection (`QualysFindingsXmlParser.EnrichDetections`). This is **expected**: the prototype `results.txt` Stage-4 explicitly states *"DETECTION_LIST is raw (knowledge-base VULNERABILITY_INFO enrichment is omitted in this probe)."* So A is a superset of the prototype detection row by design. Severity: **none** (documented intentional).

---

## 2. A vs B parity — MATCH on shape, one parsing-depth difference

Same field set, nesting, empty-array representation, and detection field names:
- HOST_DETAILS spine: both fetch `/asset/host/?action=list&details=All&show_asset_id=1` and key by `int` host ID. A `ParseHostDetailsAsync`; B `parseHostDetailsBatchAsync`. Both use the **same** `ConvertElementValue`/`convertElementValue` algorithm (identical VALUE+attributes handling, identical repeated-child→array promotion). Nested `DNS_DATA`, `METADATA`, etc. produce the same object shape.
- `BuildHostRecords` is line-for-line equivalent between A (`QualysFindingsFlow.cs:228`) and B (`buildHostRecords`), incl. the `HasAssetIdentity`/`hasAssetIdentity` NETBIOS||IP check, the iterate-requested-IDs-in-order rule (stable output order), and `(JArray)detections.DeepClone()` vs `new JArray()`.
- Empty-array representation: both `new JsonArray()` / `new JArray()` → `[]`. MATCH.

### STJ vs Newtonsoft — cosmetic only
- **Key ordering:** both `System.Text.Json.JsonObject` and Newtonsoft `JObject` preserve **insertion order**, and both insert in XML document order. Same key order out. No divergence.
- **Number/bool typing:** neither side coerces. A: every leaf is `JsonValue.Create(string)` (`GetCleanElementValue` returns string). B: every leaf is a string (`GetCleanElementValue` or `xmlReader.Value`). So `"SEVERITY":"2"`, `"SSL":"0"` are emitted as **strings** on both sides — matching the prototype (all detection values are quoted strings). No int/bool typing drift. MATCH.
- **String escaping:** STJ escapes `<`/`>`/`&` to `<` etc. by default; Newtonsoft does not. The prototype fixtures were written by the probe's own serializer (`asset_with_exposures.json` shows `<CLIENT_ID>`), so escaping is serializer-local and does not affect the parsed value SPARK sees. **Severity: informational** — only matters for byte-level fixture diffing, not for the decoded record.

### DETECTION child parsing depth — LOW divergence
A reads each `<DETECTION>` child via `ReadElementValueAsync`→`ConvertElementValue` (`QualysFindingsXmlParser.cs:415-422`), so a detection child that itself has sub-elements/attributes (e.g. `<QDS severity="…">`) becomes a **nested object**. B's `processDetectionListAsync` does a flat `ReadAsync(); propertyValue = xmlReader.Value` — every detection child becomes a **string**, and for a child with nested elements B would capture only the first text node (likely empty/wrong).
- For the prototype data (all detection fields flat: QID, RESULTS, SEVERITY, PORT, PROTOCOL, …) the two are **identical**.
- Divergence only surfaces if Qualys returns a structured child under `<DETECTION>` (QDS/QDS_FACTORS appear when `show_qds`/`show_qds_factors` are set — neither collector sets them). With the current query params this path is unreachable. **Severity: LOW** (latent; A is more correct).

---

## 3. Truncation-fix divergence (A cursor-merge vs B truncation_limit=0)

| | Collector A | Collector B |
|---|---|---|
| Detection fetch | `truncation_limit` omitted → server paginates; A follows `<WARNING>/<URL>` cursor and **merges pages by host ID** (`GetDetectionsByHostIdAsync` + `MergeDetectionsByHostId`) | `truncation_limit=0` → single page, no cursor (`QualysCollector.cs:569`) |
| Spine fetch | follows cursor too (`GetHostDetailsAsync` while-loop on `NextPageUrl`) | `truncation_limit=0`, single page (`:624`) |
| Same-host-on-two-pages | merged (A test `FollowsDetectionTruncationCursor_AndCapturesAllPages`: host 101 → QIDs 9001+9002) | N/A (single page) |

**Behaviorally equivalent for the emitted result on a normal tenant: YES.** Both produce a complete `DETECTION_LIST` per host and emit findings-less hosts with `[]`. Their unit tests assert the same end-state.

**Where they would differ — MEDIUM (B-side risk, not A):**
- Qualys's `truncation_limit=0` means "no truncation," but the detection API still enforces a **hard server cap (~1M records / output-size limits)**. If a single batch's detections exceed that cap, Qualys truncates anyway and returns a `<WARNING>` cursor on a 200. B does not parse `<WARNING>/<URL>` in `parseVulnerabilityDetectionsBatchAsync`, so **B would silently drop the overflow** and emit affected hosts as falsely findings-less. A follows the cursor and is immune. B mitigates this by capping the batch at `cMAX_BATCH_SIZE=200` host IDs, which makes the overflow unlikely but not impossible for hosts with very large detection counts.
- Same applies to B's spine call (`truncation_limit=0`, no cursor): a 200-host `details=All` page is well under limits, so practically safe.
- B's `parseDetectionsByHostIdAsync` does `detectionsByHostId[id] = detectionList` (**overwrite, not merge**). Harmless under `truncation_limit=0` (a host appears once per response), but it is the exact assumption that breaks if the server ever paginates — reinforcing the above.

Net: **A is the more robust implementation.** For the realistic operating range both emit identical complete output. Severity of the divergence as a correctness risk: **MEDIUM on B**, **none on A**.

---

## 4. SPARK consumability of A's output — NO BLOCKER

- A emits NDJSON, one `{HOST_DETAILS,DETECTION_LIST}` object per line via `host.ToJsonString()` — same line shape SPARK reads from the prototype `combined.ndjson`.
- All values are strings / nested objects / arrays — no STJ-specific tokens (no `$type`, no numbers where prototype has strings).
- STJ `<` escaping decodes to the same characters; any JSON parser (incl. Spark's) round-trips it. Not a blocker.
- `VULNERABILITY_INFO` is an additive nested object on detections; the prototype's downstream `asset_with_exposures.json` derives `exposures[].qid/cve_ids` — consistent with a SPARK stage that reads `DETECTION_LIST[].QID` (+ optional VULNERABILITY_INFO). A's superset does not break a parser that ignores unknown keys.

One thing to confirm out-of-band (not visible in these artifacts): SPARK must tolerate the **per-host varying HOST_DETAILS field set** (some hosts lack NETBIOS/DNS/QG_HOSTID). The prototype proves the real data already varies this way (`results.txt`: `NETBIOS 6/7`, `QG_HOSTID 1/7`), so the SPARK contract already had to handle absence — A reproduces exactly that. No new blocker introduced by A.

---

## Divergence summary

| # | Divergence | Direction | Severity |
|---|---|---|---|
| 1 | `VULNERABILITY_INFO` present in A, absent in prototype detections | A vs prototype | None (probe omits enrichment by design) |
| 2 | STJ `<` escaping vs Newtonsoft raw `<` | A vs B | Informational (decoded value identical) |
| 3 | A parses nested `<DETECTION>` children to objects; B flattens to strings | A vs B | Low (unreachable with current query params; A more correct) |
| 4 | A follows truncation cursor + merges; B uses `truncation_limit=0`, no cursor, overwrite-not-merge | A vs B | Medium **on B** (silent drop if batch exceeds hard server cap); none on A |
| 5 | `exposureless_asset.json` is SPARK output shape, not emit shape (and synthesized) | fixture interpretation | None (don't treat as emit-shape anchor) |

**Certainty:** High on shape parity (1, 2, 5) and SPARK consumability — read directly from diffs, tests, and fixtures. High that A and B emit identical records on normal tenants. The truncation-cap claim (#4) rests on Qualys's documented hard output limits applying even with `truncation_limit=0`; the probe data never hit it, so that specific failure mode is reasoned, not observed.
