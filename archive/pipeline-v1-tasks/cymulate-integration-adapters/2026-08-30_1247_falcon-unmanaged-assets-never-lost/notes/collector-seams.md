# Falcon collector — seams that assume a non-null AID (findings lane)

Read-only map. Paths relative to
`src/Cymulate.Integration.Adapters/Collectors/FalconCollector/`.

Scope note: the `.Aid` surface in production code is **six sites**. Everything else keys off the
staged page index, so the blast radius is smaller than the two-phase machinery suggests.

```
Flows/Policies/FalconPolicyEnricher.cs:105              target.Aid is { Length: > 0 }   (already nullable — the template)
Flows/Policies/FalconPolicyEnricher.cs:154              ComposeForVendorStated(target.Aid, …)
Flows/Findings/Correlated/FalconSpotlightBatchPump.cs:598  accumulators[host.Aid] = …   ← THROWS on null
Flows/Findings/TwoPhase/FalconStagedHostPage.cs:117     [AidProperty] = host.Aid
Flows/Findings/TwoPhase/FalconHostSpooler.cs:206        seenAids.Add(h.Aid)           ← SILENTLY COLLAPSES on null
Flows/Findings/TwoPhase/FalconHostSpooler.cs:630/690    boundaryAids.Contains/Add(host.Aid)
```

---

## 0. Where the drop happens, and a caveat on *why*

`Flows/Findings/Correlated/FalconDiscoverHostScroller.cs:82-86`

```csharp
string? aid = AidExtractor.ExtractAid(hostObj);
if (string.IsNullOrWhiteSpace(aid)) { continue; }
```

The FQL is confirmed to be a `last_seen_timestamp` gate only —
`Flows/SharedFlows/FalconHostFilters.cs:14-36` composed onto
`Processing/Urls/FalconUrls.cs:10-23`. No `entity_type` clause exists anywhere in the findings
lane; the only `entity_type:'managed'` in the collector is the *policy access probe* at
`Processing/Validation/FalconAccessProber.cs:196`. So all 298 assets are fetched and 249 are
discarded client-side, as reported.

**UNCERTAIN — flagging rather than guessing.** `AidExtractor.ExtractAid` (best-effort) falls back to
the `<cid>_<suffix>` split at `Flows/Findings/Hosts/AidExtractor.cs:106-142`, and returns the suffix
whenever it is exactly 32 hex chars. That fallback is documented at `AidExtractor.cs:39-41` as having
produced derived identifiers for **333 of 374** hosts on a live tenant — i.e. on *that* tenant
unmanaged entities were NOT dropped here; they were staged under a bogus AID that simply matched no
Spotlight findings. The unit-test fixtures show why the shapes differ:
`UnitTests/…/FalconPolicyEnrichmentTests.cs:646-657` has a *managed* host whose `id` suffix is
**not** 32-hex (`…_ASBFS8ioZ_oHIZI47cmMD18Vgvw…`, and `IndexOf('_')` takes the *first* underscore)
and an *unmanaged* host whose suffix **is** 32-hex.

I cannot determine from the code which population the lab tenant's 249 fall into. Two distinct
populations may exist and they need different handling:

- **(a) suffix not 32-hex / no `id`** → `ExtractAid` returns null → dropped at line 83. This is the
  population the intended change targets.
- **(b) suffix is 32-hex** → `ExtractAid` returns a *derived, meaningless* AID → **already staged
  today**, already emitted, carrying a fake `aid` in the record's top-level `aid` key and consuming
  a slot in the Spotlight `aid:[…]` filter.

If (b) is non-empty on some tenants, the change should decide whether those hosts keep emitting a
derived AID or are reclassified as AID-less — otherwise the same customer asset emits differently
depending on the vendor's `id` formatting. Worth confirming against a live page before designing.

### 0b. Severe adjacent defect the drop causes today

`FalconDiscoverHostScroller.cs:100`

```csharp
return new DiscoverHostPage(hosts, hosts.Count == 0 ? null : responseAfter);
```

`hosts` is the **post-filter** list. A Discover page on which *every* asset is AID-less returns
`Hosts=[]` and `After=null`, and the spooler then treats that as the end of the scroll:

- `FalconHostSpooler.cs:216` — `nextPageTask = page.After is not null && page.Hosts.Count > 0 …`
- `FalconHostSpooler.cs:220-223` — `if (page.Hosts.Count == 0) { break; }`

So one fully-unmanaged page **silently truncates the entire Discover freeze**, and the manifest
records the truncated result as a `Completed` freeze. This is not hypothetical on a tenant that is
mostly unmanaged with `last_seen` clustering. The intended change removes this failure mode as a
side effect — worth calling out as a second reason to make it, and worth a regression test.

Risk of touching: **low** (the fix is the change itself). Severity if left: **high**.

---

## 1. The record contract

`Flows/Findings/Correlated/FalconCorrelatedRecord.cs:50-74`

```csharp
public static ReadOnlyMemory<byte> Build(JsonObject host, string aid, int chunkIndex, bool isLastChunk, …)
…
["aid"] = aid,
```

- `aid` is a **non-nullable `string` parameter**, positional, no guard. Schema pinned in the class
  doc at `:10`.
- It is a `JsonObject` key assignment, not a dictionary lookup, so a null would **not throw**: the
  implicit `string → JsonNode` conversion yields a null node and serializes as `"aid": null`. An
  empty string serializes as `"aid": ""`.
- Nothing downstream in the collector re-reads the emitted `aid`. Whether `null`, `""` or absent is
  correct is a **downstream parser contract question this map cannot answer** — flagging it as the
  one thing outside the collector that this change touches.

Both call sites pass the accumulator dictionary's key, never `host.Aid` directly:
`FalconSpotlightBatchScroller.cs:170-171` (chunk-cap flush) and `:199-200` (batch-end flush).

Risk: **low mechanically**, **contract decision required** (null vs. omit vs. empty).

---

## 2. Phase 1 staging

**`DiscoverHost.Aid` is `string` (non-nullable), confirmed:**
`Flows/Findings/Correlated/FalconDiscoverHostScroller.cs:26`

```csharp
internal sealed record DiscoverHost(string Aid, JsonObject Host, DateTime? LastSeenUtc, string? SensorAid = null);
```

`SensorAid` is already `string?` and already null for every unmanaged entity — the record already
carries the two-confidence-level distinction; only `Aid` is over-constrained.

`Nullable` is `enable` in the csproj (`…FalconCollector.csproj:7`) and **`TreatWarningsAsErrors` is
not set anywhere** (checked `Directory.Build.props`, `Collectors/Directory.Build.props`, the csproj).
So widening `Aid` to `string?` produces compiler *warnings* at the six sites above, not build
failures — which means a missed site fails at runtime, not at build. Do not rely on the compiler
here.

**The staged-page codec requires an aid per line, in both directions:**

- Encode — `Flows/Findings/TwoPhase/FalconStagedHostPage.cs:113-124`. `[AidProperty] = host.Aid`;
  a null writes `"aid":null`, an empty writes `"aid":""`. No throw.
- Decode — `FalconStagedHostPage.cs:156-166`:

```csharp
string? aid = record[AidProperty]?.GetValue<string>();
if (string.IsNullOrWhiteSpace(aid))
    throw new FalconStagedPlaneCorruptException(
        $"Staged host page '{relativePath}' line {lineNumber} carries no aid; the frozen key list is unusable.");
```

  **This is a hard tripwire and it fires on both null and empty.** A staged line without an aid
  aborts the whole page read, which fails the run. It must be relaxed in lockstep with the encoder,
  and the `host` guard at `:169-173` must stay — the host object is what remains load-bearing.

  Note `GetValue<string>()` on a JSON `null` is not reached, because `record[AidProperty]` is a null
  `JsonNode` and `?.` short-circuits. So the existing code path degrades to the corrupt-exception
  rather than an InvalidOperationException. Fine.

**`FalconStagingArea` is aid-agnostic.** Its whole public surface is keyed by generation id and page
index (`FalconStagingArea.cs:193, 223, 250, 276, 416, 420, 440`); nothing reads an AID.
`FalconStagingPaths` likewise. **No change needed there.**

Risk: **medium** — two symmetric edits (encode/decode) plus a decision about what an AID-less staged
line looks like on the wire. Getting them out of step corrupts a generation.

---

## 3. Phase 2 batching geometry — **this one is safe, and the manifest's warning does not apply**

`Flows/Findings/TwoPhase/FalconFrozenKeyList.cs:254`

```csharp
foreach (DiscoverHost[] batch in hosts.Chunk(manifest.AidBatchSize))
```

Batches are chunked over **staged hosts**, not over AIDs. An AID-less host therefore **does occupy a
batch slot**, exactly like any other. Answering the question directly: batches are enumerated by
walking `manifest.PageKeys` (`:207`), reading the page (`:244`), attaching policy (`:251`), then
`Chunk`. Nothing counts AIDs.

**No output-page ordinal arithmetic exists any more.** The `NO ORDINAL DERIVATION LIVES HERE ANY
MORE` block is `FalconPhase1Manifest.cs:237-272`; it records that `OutputPageFor(pageIndex,
batchIndex) = pageIndex * BatchesPerPage + batchIndex + 1`, `BatchesPerPage`,
`LastPossibleOutputPage`, and `pageHostCounts` were all **deleted** in v4. The published object's
name is now `progressContext.CurrentPage` read on the consumer at
`Flows/Findings/FalconFindingsFlow.cs:319`, which counts objects. So `hostsPerPage / aidBatchSize`
arithmetic no longer exists to be perturbed. `HostsPerPage` and `AidBatchSize` survive in the
manifest only so a config change mid-generation cannot re-chunk it (`FalconPhase1Manifest.cs:163-168`).

**Absent-page analysis (`FalconFrozenKeyList.cs:155-242`)** is a three-case test on `pageIndex` vs.
`lastCompletedStagedPage` only — page *length* deliberately never enters the comparison
(`:179-181`). Adding AID-less hosts changes page contents, never this logic.

The one geometry consequence: staged pages get **denser** (298 hosts/page instead of 49 at the same
Discover limit), so per-page memory and per-batch Spotlight scroll counts rise proportionally. The
staged-page write path is fully buffered (`FalconStagedHostPage.cs:81-103`, and read its own remarks
at `:71-79` — the object store's 8 MiB in-memory refusal is *routed around*, not removed, and peak
memory is ~2× the payload). A ~6× denser page on a large tenant is a real memory delta on a path
whose author explicitly documented it as the caller the guard was written to catch. **Flagging as a
sizing question, not a blocker.**

Risk: **low** for correctness, **medium** for memory sizing on large tenants.

---

## 4. Checkpoint and resume — **no AID assumption**

- `Recovery/FalconCheckpointState.cs:187, 194` — the resume position is
  `LastCompletedStagedPage` / `LastCompletedBatchIndex`, a coordinate into the frozen list. No AID.
- `Recovery/FalconCheckpointState.cs:201-207` — `DiscoverWatermarkUtc` / `DiscoverWatermarkAids` are
  **explicitly diagnostic only**; the doc says Phase 2 resume does not consult them.
- `Recovery/FalconCheckpointSerializer.cs:57,68` and `FalconCheckpointDeserializer.cs:211-230,
  276-278` round-trip them as an opaque `List<string>`; a deserialize failure is caught and ignored
  (`:229-230`).
- `Flows/Findings/FalconFindingsCheckpointWriter.cs:56-75` writes the position; nothing aid-derived.

**No format-version implication.** `CurrentFormatVersion = 4` is about the *checkpoint's* shape
(`FalconCheckpointState.cs:153`), and no checkpoint field changes. The relevant version is the
**manifest's** `CurrentLedgerVersion = 1` (`FalconPhase1Manifest.cs:226`) — and the question there
is subtler:

> A generation frozen by the *current* build and resumed by a build with a relaxed staged-line
> format is fine (aid is optional in both). A generation frozen by a **relaxed** build and read back
> by an **unrelaxed** one hits `FalconStagedHostPage.cs:163` and throws
> `FalconStagedPlaneCorruptException` — which is *not* the benign "treat as absent and re-spool"
> path. `TryParse` only guards the manifest (`FalconPhase1Manifest.cs:307-346`); a corrupt *page*
> propagates. During a rolling deploy that is a failed run, not a re-spool.

If mixed-version legs are possible in your deployment, a `ledgerVersion` bump is the mechanism that
already exists to refuse the old build cleanly (`FalconPhase1Manifest.cs:330-336` rejects a
*newer* ledger version and re-spools). **Recommend treating this as a real decision, not a
formality.** I have not verified whether rolling deploys can actually interleave legs of one run —
that is outside the collector.

Risk: **low within one build**, **medium across a rolling deploy** unless the ledger version moves.

---

## 5. Policy enrichment — the template already exists, verbatim

**The assets-flow pattern (the template asked for), `Flows/Assets/FalconAssetsScrollRunner.cs:460-472`:**

```csharp
private static List<PolicyEnrichmentTarget> BuildPolicyTargets(List<JsonNode?> pageRecords)
{
    var targets = new List<PolicyEnrichmentTarget>(pageRecords.Count);
    foreach (JsonNode? record in pageRecords)
        if (record is JsonObject host)
            targets.Add(new PolicyEnrichmentTarget(AidExtractor.ExtractSensorAid(host), host));
    return targets;
}
```

Precisely: it builds **one target per host row, unconditionally**, and passes
`ExtractSensorAid(...)` — the *strict* extractor — which is **null for every unmanaged entity**. The
doc comment at `:451-459` states the reason: only an explicitly-stated AID may reach the device
endpoint, which rejects a derived one and fails the whole page with it.

The null then flows through three already-nullable hops:

1. `Flows/Policies/FalconPolicyEnricher.cs:14` — `record PolicyEnrichmentTarget(string? Aid, JsonObject Host)`,
   documented at `:12` as "the Discover AID, **or null/blank for an unmanaged entity with no usable AID**".
2. `FalconPolicyEnricher.cs:101-109` — the vendor request list is filtered:
   `if (target.Aid is { Length: > 0 } aid && seen.Add(aid))`. Null and empty are both excluded, so
   no AID-less host reaches the wire. `ResolveAssignmentsAsync` returns `Empty` for a unit with no
   usable AID rather than issuing a 400 (`:170-173`).
3. `FalconPolicyEnricher.cs:150-155` → `FalconDevicePoliciesEnvelope.ComposeForVendorStated(target.Aid, …)`,
   whose first branch is `Flows/Policies/FalconDevicePoliciesEnvelope.cs:203-208`:

```csharp
// No AID is not a failure to collect: an entity with no sensor AID is not a sensor-managed host, so it
// cannot carry a Prevention assignment and "complete, none" is the true answer.
if (string.IsNullOrWhiteSpace(aid)) return Empty();
```

So **every** host gets an envelope, AID-less ones get `collection_status: "complete"` with
`prevention: null`, and no device lookup is issued. Pinned by tests at
`UnitTests/…/FalconPolicyEnrichmentTests.cs:659-676` and `:678-703`.

**The findings lane already implements the identical shape** — it needs *no change at all*:

- `FalconHostSpooler.cs:544-558` `CollectSensorAids` — skips any host whose
  `FalconStagedPolicyPage.ResolveSensorAid(host)` is null, so AID-less hosts never enter the device
  request.
- `FalconStagedHostPage.cs:238-244` `ResolveSensorAid` — `host.SensorAid ?? ExtractSensorAid(host.Host)`,
  normalized to null on blank. Its remarks at `:227-236` state that one definition is shared by the
  write and read sides deliberately.
- `FalconFrozenKeyList.cs:110-133` (`FalconStagedPolicyComposer.AttachAsync`) — iterates **every**
  host on the page, `sensorAid` may be null, and calls the same `ComposeForVendorStated`. Class doc
  at `:77-78`: "Every host gets one, always — a host is never dropped or left bare."

**Conclusion for this seam: policy enrichment needs zero changes.** It is already null-tolerant on
both flows, by construction, and the AID-less path is already the tested one. This is the strongest
evidence that the record contract is the only real blocker.

Risk: **none**.

---

## 6. Dedup safety — **the correctness trap, confirmed empirically**

`Flows/Findings/TwoPhase/FalconHostSpooler.cs:204-207`

```csharp
List<DiscoverHost> freshHosts = page.Hosts
    .Where(h => !IsBoundaryDuplicate(h, watermarkUtc, boundaryAids))
    .Where(h => seenAids.Add(h.Aid))
    .ToList();
```

I ran the actual .NET 8 semantics rather than reasoning about them
(`HashSet<string>(StringComparer.Ordinal)`, `Dictionary<string,int>(StringComparer.Ordinal)`):

| operation | result |
|---|---|
| `HashSet.Add(null)` first | `true` |
| `HashSet.Add(null)` again | **`false`** |
| `HashSet.Contains(null)` | `true` (no throw) |
| `dictionary[null] = v` | **`ArgumentNullException`** |

So:

- **If `Aid` becomes nullable and the spooler is left as-is, the run-wide `seenAids` set silently
  keeps the FIRST AID-less host and drops every subsequent one.** On the lab tenant that is 1 of 249
  staged and 248 lost — a *worse* outcome than today's honest drop, and completely silent. This is
  the single most dangerous line in the change.
- Empty string behaves the same way (`HashSet.Add("")` dedupes normally), so "use `""` instead of
  null" does not avoid the trap. **Neither sentinel is safe; the predicate itself has to change.**
- `boundaryAids` at `:630` (`Contains`) and `:690` (`Add`) tolerate null without throwing, and their
  effect is the same collapse — but that set is a re-anchor tie-break whose overflow behaviour is
  already documented as safe (`:45-52`), so the consequence is a possible duplicate emission, not a
  loss. Lower severity than `seenAids`.

**Alternative identity Discover offers.** `Flows/SharedFlows/FalconJson.cs:66-69`:

```csharp
public static string? TryReadAnyId(JsonElement obj)
    => ReadString(obj, "device_id")
       ?? ReadString(obj, "aid")
       ?? ReadString(obj, "id");
```

Exact precedence, as asked: **`device_id` → `aid` → `id`**, each whitespace-normalized to null. Note
this is the **reverse** of `AidExtractor.ExtractSensorAid`, which reads `aid` → `device_id`
(`AidExtractor.cs:49`). Both are best-effort for identity purposes; the difference only matters for
a host carrying both fields with different values, which I have no evidence occurs.

The combined `id` is the field that always exists on a Discover row and is the natural dedup key for
an AID-less host. **Correction to one premise in the brief:** the assets flow does **not** run a
run-wide dedup keyed on `TryReadAnyId`. It uses it only for the *boundary* watermark tie-break —
`Flows/Assets/FalconAssetsPageParser.cs:78, 86, 91-99`, filling `MaxLastSeenIds`
(a `HashSet<string>` with **`StringComparer.OrdinalIgnoreCase`**, `:29`), consumed at
`Flows/Assets/FalconAssetsScrollRunner.cs:405-409` as `WatermarkFloorIds` and persisted to the
checkpoint as `LastWatermarkIds`. Every use is guarded by `!string.IsNullOrWhiteSpace(id)` first, so
a row with no identity at all is simply never added — it is never used as a *membership* key that
could collapse. The assets flow has **no equivalent of `seenAids`**; it relies on the segmented
month windows plus the boundary tie-break instead. So the assets flow is a template for *tolerating*
missing identity, but not for run-wide dedup.

Whatever key is chosen, the safe shape is the guarded one the codebase already uses everywhere else
— skip the dedup for a host with no key rather than inserting a null/empty into the set:

```
.Where(h => DedupKey(h) is not { Length: > 0 } key || seenAids.Add(key))
```

…which trades a possible duplicate for a guaranteed non-loss. That is the right trade here given the
whole point of the change is not losing hosts, but note it *is* a trade: the re-anchor replay this
set exists to collapse (`FalconHostSpooler.cs:174-177`, `:29-31`) would then duplicate keyless hosts
on every in-process re-anchor. `id` being present on essentially every Discover row makes that
mostly theoretical — **but I have not verified that `id` is universally present**, and if it is,
`id` is the correct key and there is no trade at all. Worth one look at a live page.

Risk: **high**. This is the seam most likely to ship a silent regression.

---

## 7. Everything else that mis-handles a null AID

### 7a. `SeedAccumulators` — hard crash (`FalconSpotlightBatchPump.cs:590-602`)

```csharp
foreach (DiscoverHost host in batch.Hosts)
    // AIDs are unique in the frozen list (the spool dedupes them); last-writer-wins is a harmless
    // no-op if a staged page ever repeats one.
    accumulators[host.Aid] = new HostFindingsAccumulator(host.Host);
```

`Dictionary<string,_>` with `StringComparer.Ordinal` **throws `ArgumentNullException` on a null
key** (verified above). With an empty-string key it does *not* throw — it silently collapses every
AID-less host in the batch into one accumulator via the documented "last-writer-wins", emitting
**one** record for all of them. The inline comment's premise ("AIDs are unique in the frozen list")
stops holding the moment AID-less hosts are staged. Both failure modes are unacceptable; AID-less
hosts must be held in a separate list, not in this dictionary.

### 7b. The Spotlight FQL is built from the accumulator keys (`FalconSpotlightBatchScroller.cs:79-83`)

```csharp
string aidList = string.Join(',', accumulators.Keys.Select(static aid => $"'{aid}'"));
string baseFilter = "suppression_info.is_suppressed:!'true'"
                  + "+status:['open','reopen','closed']"
                  + $"+aid:[{aidList}]";
```

Two consequences:

- An empty-string key emits a literal `aid:['']` clause — a malformed/wasteful filter.
- A batch consisting **entirely** of AID-less hosts yields `aid:[]`, which is almost certainly a
  vendor 400. With 249 of 298 assets AID-less this is not an edge case: at `AidBatchSize` granularity,
  whole batches of AID-less hosts are the *normal* case. **The pump must skip the Spotlight scroll
  entirely for a batch with no AIDs** and go straight to the batch-end flush.

### 7c. The batch-end flush is the left-join, and it is keyed the same way (`FalconSpotlightBatchScroller.cs:195-203`)

```csharp
foreach ((string aid, HostFindingsAccumulator accumulator) in accumulators)
    yield return FalconCorrelatedRecord.Build(
        accumulator.Host, aid, accumulator.ChunkIndex, isLastChunk: true, accumulator.Findings);
```

This is the exact mechanism the brief wants AID-less hosts to reuse — "the same left-join treatment
the flow already gives a host Spotlight returned nothing for". It already emits one
`isLastChunk: true`, empty-findings record per zero-finding host. The AID-less path needs to reach
this loop (or an equivalent) **without** having gone through `accumulators`.

`stats.HostsEmitted` is incremented here (`:202`), so AID-less hosts will correctly flow into
`totalAssetsEmitted` at `FalconFindingsFlow.cs:348` and into the checkpoint at
`FalconFindingsCheckpointWriter.cs:74` with no counter change — **provided** they are emitted
through this loop. If they are emitted elsewhere, the counter must be incremented explicitly or
`TotalAssetsEmitted` under-reports.

### 7d. Orphan-finding accounting (`FalconSpotlightBatchScroller.cs:147-155`)

`stats.OrphanFindings` counts findings whose `aid` misses the accumulator dictionary. Unchanged by
this work, but note that today's *derived*-AID hosts (population (b) in §0) are precisely the ones
that produce a filter matching nothing — they inflate no counter, they just quietly contribute zero.

### 7e. The two `AidExtractor` overloads — which caller uses which

| Caller | Overload | Strictness |
|---|---|---|
| `FalconDiscoverHostScroller.cs:82` (findings, the drop site) | `ExtractAid(JsonElement)` | **best-effort**, composite-id fallback |
| `FalconDiscoverHostScroller.cs:91` (findings, `SensorAid` field) | `ExtractSensorAid(JsonElement)` | strict |
| `FalconAssetsScrollRunner.cs:467` (assets policy targets) | `ExtractSensorAid(JsonObject)` | strict |
| `FalconStagedPolicyPage.cs:242` (`ResolveSensorAid`, both write and read sides of the edge page) | `ExtractSensorAid(JsonObject)` | strict |
| `FalconAccessProber.cs:181` (representative-host probe) | `ExtractSensorAid(JsonElement)` | strict |

`ExtractAid(JsonObject)` (`AidExtractor.cs:24-30`) has **no production caller** — only
`FalconPolicyEnrichmentTests.cs:715`. Worth knowing before anyone assumes the assets flow uses it;
the class doc at `:20-23` claims "used by the assets flow", which is **stale** — the assets flow
uses the strict one, as its own comment at `FalconAssetsScrollRunner.cs:455-458` says.

The strict/best-effort split is load-bearing and must survive: `ExtractSensorAid` is what keeps a
derived 32-hex hash away from `/devices/entities/devices/v2`, which rejects it and fails the whole
batch (`AidExtractor.cs:36-42`, verified against a live tenant per that comment).

### 7f. Logging and counters

- `FalconHostSpooler.cs:291-293` logs `Hosts={hostCount}` — will jump ~6× on the lab tenant. Expected,
  but it is the number an operator compares across releases.
- `FalconPhase1Manifest.CompletedFreeze` (`:278-285`) records `items: hostCount`. No aid dependency.
- The ledger's freeze entry has **no AID-derived field** — every `FalconStageLedgerEntry` field is an
  O(1) count (`FalconPhase1Manifest.cs:71-79`), and the O(1)-ness is a hard constraint (`:31-39`:
  the manifest is read under a 1 MiB control-artifact ceiling). **Do not add a "hosts without an
  aid" per-host or per-page field to the manifest.** A scalar count would be legal; anything
  per-unit is not.
- `FalconFindingsCheckpointWriter.cs:106` — `WatermarkUtc = discoverWatermarkUtc ?? DateTime.MinValue`,
  with a comment explaining why wall-clock must never be substituted. AID-less hosts *do* carry
  `last_seen_timestamp` (they are Discover rows), so the watermark keeps advancing normally and
  `AdvanceWatermark` (`FalconHostSpooler.cs:638-692`) already skips hosts with no `LastSeenUtc`.
  No change.
- `FalconFindingsFlow.cs:342-345` — the dud-batch guard (`RecordCount == 0` → `RestoreBase`) is
  documented as defensive because "every aid-batch emits at least one envelope record". That stays
  true under the change, and in fact becomes true for *more* batches. No change.

---

## Summary — what actually has to move

| Seam | Verdict |
|---|---|
| §5 Policy enrichment | **No change.** Already null-tolerant end to end, already tested. |
| §4 Checkpoint/resume | **No change** within a build. Ledger-version decision for rolling deploys. |
| §3 Batching geometry | **No change.** Chunking is over hosts; no ordinal arithmetic survives. Memory sizing question only. |
| §1 Record contract | Widen `aid` param; **decide null vs. omit** with the downstream parser owner. |
| §2 Staging codec | Symmetric encode/decode relax; `DiscoverHost.Aid` → `string?`; no build-break safety net (no warnings-as-errors). |
| §0 Scroller | Remove the `continue`; fix the `hosts.Count == 0 → After=null` early-termination while there. |
| §6 `seenAids` | **Highest risk.** Guarded predicate, not a sentinel. Both `null` and `""` collapse silently. |
| §7a/7b/7c Pump + scroller | AID-less hosts must bypass `accumulators` and the `aid:[…]` filter entirely; a fully-AID-less batch must issue no Spotlight call. |

Two things I could not determine from the code and would not guess at:
1. Which of populations (a) and (b) in §0 the lab tenant's 249 assets are — it changes whether some
   unmanaged hosts are *already* being emitted under a derived AID.
2. Whether Discover's combined `id` is universally present, which decides whether §6's dedup key is
   free or a trade.
