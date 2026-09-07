# Internal Recon

## Durable sources read

- `CLAUDE.md` — settles: nullable + implicit usings on everywhere (`:103`); test output redirected to `artifacts/bin|obj/ut/` (`:101`); `InternalsVisibleTo` is how tests see `internal` members (`:102`); single-project test command (`:34`); central package management (`:42`).
- `ai/skills/collector-shape-and-layers/SKILL.md` — settles layer ownership: flows own vendor requests, pagination, checkpoints, publishing sequence; the collector shell owns none of it. A scroller/spooler/pump change is a flow-layer change and stays there.
- `ai/skills/collector-flow-patterns/SKILL.md` — settles: the publish boundary is the aid-batch; the checkpoint is the traversal position, not a cursor; `BatchScopedStorage` is opt-in and its subfolder names are deterministic; **"an identifier a vendor *states* and one you *derive* are not interchangeable … keep the two extraction paths separate and named, and let each call site pick the confidence level it needs"** — this is the rule the whole task turns on, and it already exists.
- `ai/skills/collector-tests/SKILL.md` — settles test priority order (contract invariants → resume → publishing → deterministic flow logic) and the hard rule "add a regression test for every bug fix that changes collector control flow"; also "an unrouted URL in a flow fixture is a harness gap, never a reason to weaken production behaviour".
- `ai/skills/collector-execution-and-recovery/SKILL.md` — settles: **`PublishFailure(IsRetryable = true)` is terminal in practice — the host does not redeliver it** (production disproof, correlation `6a85ca2038f164746a562020`, 2026-08-20); a fault worth retrying must produce `RequestDeferredRecovery` or `RecoverAndRetry`. Also: Polly's pipeline ends at the response headers, which is why `FalconSpotlightBatchPump` carries its own in-flow retry; add vendor classification on top of the substrate's transport handling, never instead of it; do not wrap a transport fault with the original as `InnerException`. Bears directly on Landmine L3.
- `notes/HANDOFF.md` — the defect, the three branches, the two live measurements, the "do not fold in" list.
- `.../collector-seams.md` — the `.Aid` surface, the staged codec's symmetry, the `seenAids` collapse (measured, §6), policy enrichment needs zero change (§5), batching geometry needs zero change (§3), checkpoint needs zero change (§4). **Cited, not re-derived.**
- `.../downstream-contract.md` — which parser consumes the lane, the four uses of `aid` in it, `aid` is not an output column, the assets lane already carries AID-less assets.
- `.../parser-proof.md` — executed: null/`""`/absent ⇒ 249 in → 1 out, silent; unique derived AID ⇒ 21 in → 21 out. **Cited, not re-derived.**
- `research/lab-tenant-identity-probe.md` (operator, live, 2026-08-30) — **supersedes the handoff's mechanism.** 303 assets, 49 with an explicit `aid`, **0** derivable via `TryExtractAidFromCombinedHostId` (the suffix is a 56-char base64url token, not 32 hex). `id` present 303/303 and unique 303/303. `last_seen_timestamp` null on 5/303. `hostname` on only 227/303, so hostname is not an identity fallback. Every design statement below is aimed at the combined-`id` key, not a derived AID.
- `prompt_contract.md` / `constraints.md` / `decisions.md` / `assumptions.md` — the bounds. Their central mechanism is overtaken by the probe (L1); a second collision is L3.

Repo state at recon time: adapters on `fix/falcon-disable-prevention-policy-enrichment`, `git status --porcelain` shows only `?? ai/notes/` — the working tree is clean and `FalconCollectorConfiguration.cs:179` reads `= false` as the constraint requires. Parsers repo on `master`, clean.

---

## Files in scope

Paths under `src/Cymulate.Integration.Adapters/Collectors/FalconCollector/` unless stated.

### (Q1) The AID type surface

- `Flows/Findings/Correlated/FalconDiscoverHostScroller.cs:26` — the declaration:
  ```csharp
  internal sealed record DiscoverHost(string Aid, JsonObject Host, DateTime? LastSeenUtc, string? SensorAid = null);
  ```
  `Aid` is **non-nullable `string`**; `SensorAid` is already `string?`. The doc at `:17-25` already states the two confidence levels.

- **Every read of `DiscoverHost.Aid` in the findings flow — five sites, all of them:**
  | file:line | expression | behaviour on a null key |
  |---|---|---|
  | `Flows/Findings/Correlated/FalconSpotlightBatchPump.cs:598` | `accumulators[host.Aid] = …` | `ArgumentNullException` |
  | `Flows/Findings/TwoPhase/FalconStagedHostPage.cs:117` | `[AidProperty] = host.Aid` | writes `"aid":null`, no throw |
  | `Flows/Findings/TwoPhase/FalconHostSpooler.cs:206` | `.Where(h => seenAids.Add(h.Aid))` | silent collapse |
  | `Flows/Findings/TwoPhase/FalconHostSpooler.cs:630` | `boundaryAids.Contains(host.Aid)` | silent, tolerated |
  | `Flows/Findings/TwoPhase/FalconHostSpooler.cs:690` | `boundaryAids.Add(host.Aid)` | silent collapse |

  **Correction to `collector-seams.md`'s "six sites":** the other two hits (`Flows/Policies/FalconPolicyEnricher.cs:105` and `:154`) are `PolicyEnrichmentTarget.Aid`, a *different* record declared `record PolicyEnrichmentTarget(string? Aid, JsonObject Host)` at `FalconPolicyEnricher.cs:14` — already nullable, already the template. They are not `DiscoverHost.Aid` and a widening does not touch them.

- Construction sites of `DiscoverHost`: `FalconDiscoverHostScroller.cs:91` (live), `FalconStagedHostPage.cs:188` (decode), plus test helpers `FalconTwoPhaseFindingsTests.cs:320` and `FalconConcurrencyHarness.cs:591`.

- **Which shape the code favours: "guarantee a non-null derived value at construction", decisively.** Five reasons, all structural:
  1. The staged codec's decode tripwire (`FalconStagedHostPage.cs:160-165`) throws `FalconStagedPlaneCorruptException` on a blank aid. Under a guaranteed-value design that tripwire is the *enforcement* of the new invariant and needs no edit; under a widening it must be relaxed in lockstep with the encoder, and a mismatch corrupts a whole generation (`collector-seams.md` §2).
  2. All three AID-keyed collections keep working unchanged (`seenAids`, `boundaryAids`, `accumulators`). A widening requires editing all three, and two of the three fail *silently* if missed.
  3. `TreatWarningsAsErrors` is not set anywhere (`collector-seams.md` §2, verified there against `Directory.Build.props`, `Collectors/Directory.Build.props`, the csproj) — so a widening produces warnings, not build breaks, and a missed site fails at runtime. The compiler is not a safety net here.
  4. `DiscoverHost` already expresses "no vendor-stated AID" through `SensorAid` (`string?`). The distinction the task needs is already representable without touching `Aid`.
  5. `decisions.md` fixes it: "Emit AID-less hosts with a UNIQUE derived AID … Not a null AID, not an empty string, not a shared sentinel."

  Under that shape the change collapses to: **the derivation at `FalconDiscoverHostScroller.cs:82-86`, plus a collision guard, plus the `After` fix at `:100`.** Nothing else in the `.Aid` surface moves.

### (Q2 — superseded) The AID premise, and what replaces it

Dropped as originally worded: the operator has read `AidExtractor.cs` in full and
`research/lab-tenant-identity-probe.md` settles it. What that probe establishes, and this recon
confirms against the code:

`AidExtractor.ExtractAid` (`Flows/Findings/Hosts/AidExtractor.cs:71-83`) gates its only fallback on
the combined `id` suffix being **exactly 32 characters and all hex** (`:124`, `:129-139`). The lab
tenant's suffix is a 56-character base64url token, so the gate rejects it and `ExtractAid` returns
null for **all 254** lost assets — 0 derivations, not "some". The code agrees with the probe from the
other direction: `FalconDiscoverHostScroller.cs:82-86` drops a host **iff** `ExtractAid` returned
null, it is the only filter in the lane (`collector-seams.md` §0), and the live run staged exactly 49
(`logs/20260830-110801/local-adapter-runner.log`: `Hosts=49, StagedPages=1`).

The replacement key is the Discover combined `id`: present 303/303 and unique 303/303 (probe). It
needs no derivation, no suffix parsing, and no length or charset gate a future tenant can fail.

Three structural consequences the rest of this map is now aimed at:

- The type already carries the split this needs. `DiscoverHost(string Aid, JsonObject Host,
  DateTime? LastSeenUtc, string? SensorAid)` (`FalconDiscoverHostScroller.cs:26`), constructed at
  `:91` as `new DiscoverHost(aid, host, lastSeenUtc, AidExtractor.ExtractSensorAid(hostObj))`.
  `Aid` is the record key; `SensorAid` is the vendor-stated id. **No type change is required** —
  see Q1's five reasons, all of which hold a fortiori now that the key is always producible.
- `ExtractSensorAid` stays null for these 254 and therefore keeps them out of
  `/devices/entities/devices/v2`. The policy lane needs no change (`collector-seams.md` §5), and
  `FalconHostSpooler.CollectSensorAids` (`:544-558`) reads `ResolveSensorAid`, never `.Aid`.
- What *is* required is a decision about the one place `.Aid` is still handed to a vendor. See
  Q2a immediately below.

### (Q2a) Every consumer that treats `DiscoverHost.Aid` as a real sensor AID

The five `.Aid` reads from Q1, classified by what the value is used *for*. **Exactly one of them
reaches a vendor.**

| site | what the value is used for | verdict under a Discover-`id` key |
|---|---|---|
| `FalconSpotlightBatchPump.cs:598` → `FalconSpotlightBatchScroller.cs:79-83` | **sent to a vendor endpoint** — interpolated into the Spotlight FQL as `aid:['…','…']` and put on the query string of `/spotlight/combined/vulnerabilities/v1` (`FalconUrls.cs:25-30` + `FalconSpotlightBatchScroller.cs:98`) | **THE ONE THAT BREAKS.** See below. |
| `FalconSpotlightBatchScroller.cs:151` | matched against a **vendor response keyed by sensor AID** (`FalconJson.ReadString(finding, "aid")` looked up in the accumulator map) | Safe. A Discover `id` never matches a finding's `aid`, so the host falls through to the batch-end empty-findings flush at `:197-203` — which is the intended left-join outcome. It does **not** inflate `stats.OrphanFindings` either: that counter (`:153`) counts findings whose aid misses the map, and these hosts produce no findings at all. |
| `FalconStagedHostPage.cs:117` / `:160-165` | staged-object transport only | Safe. JSON string, no interpretation. |
| `FalconHostSpooler.cs:206` | in-process run-wide dedup set | Safe, and strictly better: a unique key is exactly what this set needs. |
| `FalconHostSpooler.cs:630` / `:690` | in-process boundary tie-break set | Safe. See Q2c for the interaction with a null `last_seen`, and L13 for its size. |

Nothing anywhere matches `.Aid` against a policy or device response: the edge pages are written and
read through one shared key definition, `FalconStagedPolicyPage.ResolveSensorAid`
(`FalconStagedHostPage.cs:238-244`), which reads `host.SensorAid ?? ExtractSensorAid(host.Host)` and
is the only key the composer (`FalconFrozenKeyList.cs:110-133`) uses. `.Aid` does not appear in
`FalconFrozenKeyList.cs` at all (grep). `FalconAccessProber.cs:181` uses `ExtractSensorAid`.

**So the whole vendor-facing exposure is one expression:**

```csharp
// FalconSpotlightBatchScroller.cs:79-83
string aidList = string.Join(',', accumulators.Keys.Select(static aid => $"'{aid}'"));
… + $"+aid:[{aidList}]";
```

The codebase's own claim about a derived key here — "fine for a Spotlight filter — a wrong guess
simply matches no findings" (`AidExtractor.cs:11-12`) — was made about a **32-hex** value that is
shape-identical to a real AID. A 65-character `<32-hex-cid>_<56-char base64url>` value containing
`-` and `_` is a different proposition: whether Spotlight's FQL accepts it as a value token and
returns an empty result, or rejects the request and fails the whole batch, is **unverified and not
answerable from this repo**. It is the same failure class the device endpoint already demonstrated
(`AidExtractor.cs:36-42`: one rejected id, `{"code":400,"message":"invalid device id […]"}`, whole
batch lost). Two ways out, and the choice should be explicit:

- **Exclude non-sensor keys from the FQL.** Build the filter from the hosts that have a
  `SensorAid`, hold the rest out of `accumulators`, and route them to the batch-end flush. This is
  what the probe's own consequence list prescribes, it makes the vendor question moot, and it
  removes ~31 of 38 pointless Spotlight scrolls on the lab tenant (L9). Cost: it creates the
  wholly-non-sensor batch that SC4 already requires a guard for.
- **Include them and rely on "matches nothing".** One line of code, and a live probe of one
  `aid:['<combined id>']` request against the lab tenant to prove it 200s. Cheap to check; do not
  ship it unchecked.

### (Q2b) What constrains the FORMAT of `DiscoverHost.Aid`

Swept for length checks, padding, regex, hex validation, fixed-width serialization and field
widths across `Flows/`, `Recovery/`, `Processing/`. **Nothing constrains the format of a value once
it is in `DiscoverHost.Aid`.** Cited individually:

- **No length check.** The only `Length` comparison on an identifier in the collector is
  `AidExtractor.cs:124` (`suffix.Length != 32`), and it sits *inside* the derivation, before a
  `DiscoverHost` exists. No consumer re-checks.
- **No hex/charset validation.** `AidExtractor.cs:129-139` is the only hex loop; same story — it is
  a producer gate, not a consumer contract.
- **No regex anywhere on an aid.** The only `Regex`/`Substring` hits in the collector are in
  `Processing/Fql/FQLParser.cs` (`:100`, `:107`, `:395`), which parses the **user's** FQL filter and
  never sees a host aid.
- **No fixed-width or positional serialization.** The staged page is NDJSON with a JSON string
  value: encode `FalconStagedHostPage.cs:113-124` (`[AidProperty] = host.Aid`, then
  `line.ToJsonString()` `:123`), decode `:160` (`record[AidProperty]?.GetValue<string>()`). `-`,
  `_` and any length round-trip verbatim; `System.Text.Json` escaping handles the rest.
- **No checkpoint field width.** `Recovery/FalconCheckpointState.cs:207` is a plain
  `List<string>`, serialized whole as one JSON string into the state dictionary
  (`Recovery/FalconCheckpointSerializer.cs:68`) and read back at
  `Recovery/FalconCheckpointDeserializer.cs:219-230`, where a deserialize failure is caught and
  ignored (`:229-230`).
- **The emitted record** is `["aid"] = aid` on a `JsonObject` (`FalconCorrelatedRecord.cs:65`),
  serialized at `:73`. No constraint.
- **The one place format matters is the FQL string** — `$"'{aid}'"` at
  `FalconSpotlightBatchScroller.cs:79`, single-quoted and then `Uri.EscapeDataString`d as part of
  the whole filter at `:98`. A value containing `'` would break the quoting; base64url (`A–Z a–z
  0–9 - _`) does not. This is a syntax observation, not a licence — the vendor-acceptance question
  in Q2a is separate and still open.

**What a 65-character key does change is not format but volume.** See L13.

### (Q2c) A null `last_seen_timestamp` (5 of 303 assets)

Traced end to end. **A null-`LastSeenUtc` host is kept. It is not dropped, not treated as epoch, and
nothing throws.**

- `FalconHostSpooler.cs:626-630` `IsBoundaryDuplicate`:
  ```csharp
  => watermarkUtc.HasValue
     && host.LastSeenUtc.HasValue
     && TruncateToSecond(host.LastSeenUtc.Value) == watermarkUtc.Value
     && boundaryAids.Contains(host.Aid);
  ```
  A null short-circuits the second conjunct → returns **false** → the host is **not** filtered out
  at `:205`. It then passes the `seenAids` predicate at `:206` and is staged normally.
- `AdvanceWatermark` `:638-692`: null hosts are skipped when computing `pageMax` (`:645-651`) and
  skipped again when populating `boundaryAids` (`:675-680`). So the watermark is unaffected by them
  and **they never enter the boundary set**.
- `FalconFindingsCheckpointWriter.cs:106` — `WatermarkUtc = discoverWatermarkUtc ?? DateTime.MinValue`,
  with a comment on why wall-clock must never be substituted. Unaffected: with 298 of 303 carrying a
  timestamp, the watermark still advances.

The real exposure is not in these three functions but in the **re-anchor filter**, and it is a loss
path nobody has named. See L14.

### (Q3) The dedup seam

- `Flows/Findings/TwoPhase/FalconHostSpooler.cs:115` — `var seenAids = new HashSet<string>(StringComparer.Ordinal);`. **Run-wide**, allocated once per `SpoolAsync`, deliberately not reset across re-anchors (`:174-177`).
- Consumed at `:204-207`:
  ```csharp
  List<DiscoverHost> freshHosts = page.Hosts
      .Where(h => !IsBoundaryDuplicate(h, watermarkUtc, boundaryAids))
      .Where(h => seenAids.Add(h.Aid))
      .ToList();
  ```
- Other AID-keyed structures in the spool: `boundaryAids` (`HashSet<string>` Ordinal, `:114`; read `:630`, written `:690`, capped at `MaxBoundaryAids = 5000` `:52`); `CollectSensorAids`'s local `seen` (`:547`, keyed on `FalconStagedPolicyPage.ResolveSensorAid`, already null-guarded at `:551`). Phase 2: `accumulators` (`Dictionary<string,_>` Ordinal, `FalconSpotlightBatchPump.cs:592`).
- **Would a derived AID break any of them?** Under a guaranteed non-null unique value, no — every one of them simply sees more keys. Under a nullable value, `seenAids` keeps the first AID-less host and silently drops the rest (249 → 1 staged), which `collector-seams.md` §6 measured against real .NET 8 semantics. `""` behaves identically. That section's conclusion stands: neither sentinel is safe, only a guarded predicate is — and the guaranteed-value design removes the need for one.
- **The ASSETS-flow precedent, precisely.** `Flows/SharedFlows/FalconJson.cs:66-69`:
  ```csharp
  public static string? TryReadAnyId(JsonElement obj)
      => ReadString(obj, "device_id") ?? ReadString(obj, "aid") ?? ReadString(obj, "id");
  ```
  Precedence `device_id` → `aid` → `id` — the **reverse** of `ExtractSensorAid`'s `aid` → `device_id`. **What it does differently:** it is not a run-wide dedup at all. `collector-seams.md` §6 corrects the brief on this and I confirm the citation set: it fills the boundary watermark tie-break only (`Flows/Assets/FalconAssetsPageParser.cs:78, 86, 91-99` → `MaxLastSeenIds`, a `HashSet<string>` with **`OrdinalIgnoreCase`** at `:29`), consumed as `WatermarkFloorIds` at `Flows/Assets/FalconAssetsScrollRunner.cs:405-409`. Every insertion is guarded by `!string.IsNullOrWhiteSpace(id)`, so a row with no identity is never a membership key. The precedent is **"guard before inserting"**, not "swap the key".
- Comparer asymmetry worth knowing: the findings sets are `Ordinal`, the assets set is `OrdinalIgnoreCase`. A hex-shaped derived AID whose case the vendor varies would yield two keys under `Ordinal`.

### (Q4) The Spotlight side

- `Flows/Findings/Correlated/FalconSpotlightBatchPump.cs:590-602` `SeedAccumulators` — `Dictionary<string, HostFindingsAccumulator>(StringComparer.Ordinal)` at `:592`; `accumulators[host.Aid] = new HostFindingsAccumulator(host.Host);` at `:598`. The comment at `:596-597` ("AIDs are unique in the frozen list (the spool dedupes them); last-writer-wins is a harmless no-op") is the invariant the change must preserve — under a shared or colliding key it becomes silent per-batch asset loss.
- **`.Aid` appears exactly once in the whole 609-line pump** (grep). Every other keying is `batch.PageIndex` / `batch.BatchIndex` (`:406`, `:445`, `:448`). That is the evidence for A5 within this file; the cross-file surface is the five sites in Q1, which is the complete `.Aid` surface in the repo.
- **The `aid:[…]` FQL is built at `Flows/Findings/Correlated/FalconSpotlightBatchScroller.cs:79-83`:**
  ```csharp
  string aidList = string.Join(',', accumulators.Keys.Select(static aid => $"'{aid}'"));
  string baseFilter = "suppression_info.is_suppressed:!'true'" + "+status:['open','reopen','closed']" + $"+aid:[{aidList}]";
  ```
  Two other reads of the same dictionary: `:151` (orphan lookup — a finding whose `aid` misses the map increments `stats.OrphanFindings`) and `:197-203` (the batch-end left-join flush, which increments `stats.HostsEmitted` at `:202` — the counter that reaches `totalAssetsEmitted` at `Flows/Findings/FalconFindingsFlow.cs:348`).
- **What happens today to a batch whose host list is empty after filtering: it cannot happen.** Batches are `hosts.Chunk(manifest.AidBatchSize)` (`Flows/Findings/TwoPhase/FalconFrozenKeyList.cs:254`) and `Chunk` never yields an empty array; every staged host carries a non-blank aid because the codec tripwire (`FalconStagedHostPage.cs:160-165`) refuses to decode one that does not. So `aid:[]` is **unreachable in the current build** and SC4's guard is guarding a path that only the change can create. It becomes reachable the moment AID-less (or Spotlight-ineligible) hosts are held out of `accumulators`.
- **Sizing consequence of the chosen design.** With every host carrying an AID, every host enters `accumulators` and the FQL. `AidBatchSize` defaults to **8** (`Processing/Configuration/FalconCollectorConfiguration.cs:128`), so the lab tenant goes from ⌈49/8⌉ = 7 to ⌈298/8⌉ = 38 aid-scoped Spotlight scrolls — ~5.4×, 31 of which are certain to return nothing. Request budget is not the constraint (`FalconCollectorConfiguration.cs:112-118`: measured 8 req/min average, 59 peak, against a 6,000/min per-CID pool), and 8 quoted keys keeps the URL short even at 65-char ids, so there is no URL-length risk. The cost is wall time. Whether Spotlight-ineligible hosts are excluded from the FQL (cheaper, needs the empty-batch guard) or included (no new path) is a real decision — but SC4 forces the empty-batch guard to exist regardless, so excluding them costs little extra. **Q2a is where that decision actually gets made:** with the key now a 65-char combined `id` rather than a 32-hex lookalike, "included" is no longer obviously harmless.

### (Q5) The staged plane

- `Flows/Findings/TwoPhase/FalconStagedHostPage.cs` — line shape `{"aid","sensorAid","lastSeen","host"}` (`:14-15`, `:38-41`).
  - **Encode:** `EncodeLine` `:113-124`; two entry points — `Encode` `:47-59` (buffered, "retained for callers that genuinely want the bytes", used by the test seeders) and `OpenEncodedStreamAsync` `:81-103` (production; its remarks at `:65-79` explain that it routes *around* `GuardedObjectStore`'s 8 MiB in-memory refusal without removing the ~2× buffering — read them before sizing A4).
  - **Decode:** `DecodeAsync` `:131-154` → `DecodeLine` `:156-189`.
  - **Symmetric, with two hard tripwires:** blank aid `:160-165` and missing host `:169-173`, both `FalconStagedPlaneCorruptException`. Under the guaranteed-value design **neither needs to change**, and the aid tripwire becomes the cheapest available regression detector for a derivation bug — a staged plane that cannot represent a keyless host is a feature here, not an obstacle.
- **Coverage counters / staged ledger:** `FalconStageLedgerEntry` at `Flows/Findings/TwoPhase/FalconPhase1Manifest.cs:71-79` — `State, Units, UnitsCovered, UnitsEmpty, Items, Rejected, Unresolved, CompletedUtc`, **every field O(1) by hard constraint** (`:31-39`: the manifest is read under `MaxControlArtifactBytes`, 1 MiB, and `PageKeys` already spends a linear term). `Finished(...)` `:98-115` derives `Completed` vs `Degraded` from `unitsCovered >= units && rejected == 0`. The freeze line is `CompletedFreeze(stagedPages, hostCount)` `:278-285`, which passes `units == unitsCovered == stagedPages`, `rejected: 0` — **so the freeze entry is unconditionally `Completed`**.
- **Does emitting more hosts per page break a terminal-state check?** No. `items` (hostCount) and `units` (staged page count) both grow, and `units`/`unitsCovered` move together by construction. Phase 2's skip analysis is on page index alone and page length "never enters the comparison" (`FalconFrozenKeyList.cs:179-181`, `:213-242`). Nothing in the flow gates on the freeze state — grep for `FalconStageState.` across `Flows/`, `Recovery/`, `Processing/` returns exactly two consumers, both on the **edge** stage (`FalconFrozenKeyList.cs:86`, `FalconFindingsFlow.cs:689`).
- One real geometry effect: `freshHosts.Chunk(hostsPerPage)` at `FalconHostSpooler.cs:229` means a denser vendor page can now split into more staged pages, and `hostsPerPage` is the Discover limit (1000 in the live run's log). At 298 hosts nothing splits; on a large mostly-unmanaged tenant staged pages get ~6× denser in bytes — the A4 question, unmeasured.

### (Q6) The truncation seam

Current lines, quoted verbatim.

`Flows/Findings/Correlated/FalconDiscoverHostScroller.cs:95-100`:
```csharp
            // The final page carries after:"" (empty string). Treating that as a live cursor restarts the scroll
            // from page 1 — an infinite loop at the live edge — so normalize empty to null (terminal).
            responseAfter = string.IsNullOrEmpty(reader.AfterToken) ? null : reader.AfterToken;
        }

        return new DiscoverHostPage(hosts, hosts.Count == 0 ? null : responseAfter);
```

`Flows/Findings/TwoPhase/FalconHostSpooler.cs:212-223`:
```csharp
                bool reanchorAfterPage = page.After is not null
                                         && pagesSinceAnchor >= config.MaxPagesPerCursorScroll
                                         && freshHosts.Any(static h => h.LastSeenUtc.HasValue);

                nextPageTask = page.After is not null && page.Hosts.Count > 0 && !reanchorAfterPage
                    ? _scroller.FetchPageAsync(scrollFilter, page.After, discoverLimit, cancellationToken)
                    : null;

                if (page.Hosts.Count == 0)
                {
                    break;
                }
```

- **Exact termination condition today:** `page.After is null` **OR** `page.Hosts.Count == 0`, where `Hosts` is the **post-filter** list and `After` is itself forced to null by the post-filter count at `:100`. Two independent guards keyed on the same corrupted quantity.
- **The vendor's own signal** is `reader.AfterToken`: CrowdStrike sends `after: ""` at the live edge, normalised to null at `:97`. Following it unconditionally terminates on that.
- **Is there an existing guard against an unbounded scroll? Yes, but it is a re-anchor, not a stop.** `config.MaxPagesPerCursorScroll`, default **500** (`Processing/Configuration/FalconCollectorConfiguration.cs:149-157`, justified as ≈⅓ of an observed ~page-1542 hard-500), read at `FalconHostSpooler.cs:213` and acted on at `:251-260` — it drops the cursor and restarts from the watermark. **There is no absolute page cap, no max-host cap, and no watermark-stall detector.** The only stop is the vendor's `after`. A9's risk is therefore real; what bounds it in practice is that `AdvanceWatermark` (`:638-692`) is monotonic and the depth-cap re-anchor is conditioned on `freshHosts.Any(h => h.LastSeenUtc.HasValue)` (`:214`) precisely so a re-anchored `>=` query cannot stall.
- **Minimal correct fix:** return `responseAfter` verbatim at `:100` and let the spooler's own `page.Hosts.Count == 0` guard remain. After the drop is removed, `Hosts.Count` **is** the vendor's raw resource count, so the two guards stop being the same quantity and the existing `== 0` break becomes a legitimate vendor-side terminal. The `hosts.Count == 0 → null` clause is the entire defect; nothing else at `:100` needs to move.

### (Q7) Test topology

`src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.FalconCollector.Test/` — **26 `.cs` files** plus the csproj. The ones this task touches:

| file | size | role |
|---|---|---|
| `FalconCorrelatedFindingsTests.cs` | 88 KB, 27 facts | the correlated flow end-to-end through `collector.ProcessAsync` — **where SC1/SC2/SC4 belong** |
| `FalconTwoPhaseFindingsTests.cs` | 214 KB | staging, manifest, ledger, resume — **where SC3 belongs**; also holds the one known pre-existing failure |
| `FalconSpotlightConcurrencyTests.cs` + `FalconConcurrencyHarness.cs` | 72 KB + 36 KB | pump concurrency; its own `Host(...)` helper at `FalconConcurrencyHarness.cs:591` |
| `FalconPolicyEnrichmentTests.cs` | 82 KB | the only file referencing `AidExtractor` (`:669`, `:715`) |
| `InMemoryFalconStagingStore.cs` | 11 KB | the staging fake both two-phase files use |

**How a fake Discover response is built through the production path: a URL-routed fake HTTP handler, not a fixture file and not a builder.**
- `CreateFactory(Func<string,string,HttpResponseMessage?>)` — **two independent copies**, `FalconCorrelatedFindingsTests.cs:232` and `FalconTwoPhaseFindingsTests.cs:229`. Both pre-route `/oauth2/token` and the `limit=1` access probes; the two-phase copy additionally routes all three policy endpoints neutrally, and its comment at `:268-276` explains why an unrouted endpoint silently exercises the failure path — read it before adding a route.
- The vendor page is a raw JSON string literal in the test body; pagination is faked by `url.Contains("after=cursor-1")`.
- Assertions read `capture.StreamBatches` (a `PublishCapture`, `FalconCorrelatedFindingsTests.cs:66` / `FalconTwoPhaseFindingsTests.cs:76`) through `ParseRecordsByAid` (`FalconCorrelatedFindingsTests.cs:289-296`) or `Ndjson`/`RecordsByAid` (`FalconTwoPhaseFindingsTests.cs:308-318`).

**Exemplars a new test must mirror (cite these to the test worker):**
- **SC2 (after is followed) → `FalconCorrelatedFindingsTests.cs:301-358` `Discover_TraversesMultiplePagesInOrder_ViaAfterCursor`.** Exactly the shape needed: page 1 carries `"after": "cursor-1"`, the route returns page 2 for `after=cursor-1`, and the assertion is on two published batches. An all-AID-less page 1 is a one-line change to that fixture.
- **SC1/SC4 (zero-finding / no-Spotlight host) → `FalconCorrelatedFindingsTests.cs:476` `Host_WithNoFindings_EmitsOneEmptyChunkRecord`**; for "no request was issued", the negative-assertion idiom is `factory.RequestedUrls.Should().NotContain(...)` (`FalconTwoPhaseFindingsTests.cs:890`, `:1098`).
- **SC3 (manifest/freeze) → `FalconTwoPhaseFindingsTests.cs:589-658` `Phase1_StagesDiscoverHostsUnderStorageUrl_AndWritesTheManifestBeforeAnyPolicyWork`** — `InMemoryFalconStagingStore`, `store.Writes` as an ordered sequence, `store.WrittenContent[key]` for the staged bytes.
- Seeding a frozen generation: `SeedFrozenGeneration` (`FalconTwoPhaseFindingsTests.cs:384`) + `Host(aid, json, lastSeenUtc)` (`:320`) + `SeededPlane` (`:341-383`) — **`SeededPlane` is required with no default, deliberately** (`:373-378`); do not add one.

**Ownership:** test files are cleanly separable from `src/` (different project, `InternalsVisibleTo`). Between themselves they are separable **per file, not per concern** — `FalconCorrelatedFindingsTests.cs` and `FalconTwoPhaseFindingsTests.cs` each carry a private copy of `CreateFactory`/`CreateMockContext`/`PublishCapture`, so a routing change must be made twice and two workers in one file will collide. One worker per test file.

### (Q8) The parser side — against the live chain

**Chain confirmed exactly as you state it, with citations.** `deprecated/` is packaging, not death:

1. `libs/packages/parsers/yaml_engine/specs/crowdstrike-assets-findings.yaml:22-38` — `parser_key: crowdstrike-assets-findings`, `dual_mode: true`, `run: false` with `run_skip_reason: "split/correlated shaping; covered by dedicated test tests/test_crowdstrike_assets_findings.py"` (`:28-29`), `process_hook.module: parsers.yaml_engine.delegate_hook`, `function: delegate_to_original`, `skip_post_shaping: true`, delegating to `parsers.deprecated.crowdstrike.crowdstrikeAssetsFindings.CrowdstrikeAssetsFindingsParser` (`:32-38`).
2. `crowdstrikeAssetsFindings.py:50` `class CrowdstrikeAssetsFindingsParser(CrowdstrikeAssetsParser)` — the façade; asset field mappings inherited verbatim from `CrowdstrikeAssetsParser` (`:24-27`, `:61-64`), so the split and correlated lanes cannot drift.
3. `crowdstrikeAssetsFindings.py:143-152` — `if detect_correlated_shape(self): … CrowdstrikeAssetsFindingsCorrelated(self).pre_process()` at **`:148`**, then `self._project_policies()` at `:149`. Otherwise `input_mode` must be `"split"` or `ValueError` (`:154-159`).
4. `CrowdstrikeAssetsFindingsCorrelated.py:91-120` `detect_correlated_shape` — peeks the first non-empty line of the **first findings shard only** and tests `{"isLastChunk","host"}.issubset(record.keys())` (`:79`, `:120`). Any failure returns `False` and falls through to split (`:115-119`). A Discover-`id`-keyed record still carries both keys, so detection is unaffected.

The spec's comment at `:4-5` also records the deploy order: **the parser deploys ahead of the collector**, which is why both shapes must keep working.

Duplicate-tree warning stands: `libs/packages/build/lib/parsers/...` holds a second copy of every one of these files. Build artifact. Never edit; ignore in greps.

**The two windows are different stages and dedupe different things:**

| | `Window.partitionBy("aid")` — `:215`, in `_build_asset_spine` (`:192-221`) | `Window.partitionBy("id")` — `:305`, in `_explode_record_findings` (`:224-315`) |
|---|---|---|
| operates on | the **asset spine**: one row per `chunk == 0` record (`:212`) | **exploded findings**: one row per element of every record's `findings[]`, across all chunks (`:294-299`) |
| the key | the **record-level** `aid` — `host.aid` is dropped at `:208` and the record-level one appended at `:209` "so the join key is single and unambiguous" (`:198-200`) | the **Spotlight vulnerability `id`** — the vendor's own unique finding id, not an aid |
| what it removes | the collector's watermark re-anchor / resume **replaying a whole host** — the same host's `chunk == 0` record emitted twice (`:202-206`) | the **same finding** arriving twice because its host was replayed, possibly with a changed status between the two reads (`:234-245`) |
| winner rule | last in lane order: `orderBy(_record_order.desc())`, `_record_order = monotonically_increasing_id()` (`:213`) — "the freshest replay carries the newest host state" | freshest-wins: `to_timestamp(updated_timestamp).desc_nulls_last()`, then `created_timestamp`, then `_record_order.desc()` (`:305-309`); timestamps are **cast**, not string-compared (`:247-253`) |
| determinism | **not stable across runs** — the module's own docstring says `monotonically_increasing_id` "encodes the partition index, which moves with file listing order, split sizing and AQE" (`:255-265`) | deterministic whenever the timestamps differ; only a both-timestamps tie falls back to the unstable `_record_order` (`:255-265`) |
| the hazard for this task | **this is the collapse.** Distinct hosts sharing a degenerate key are indistinguishable from one replayed host, and N become 1 | none — a Discover-`id` record key never appears here; findings carry the vendor's `id` |

**Output columns.**

*Assets, three stages:*
- **Spine** (`:207-209`, `:133`): every `host.*` field flattened to top level **except `host.aid`**, plus the record-level `aid`, plus `_row_asset_id` (a generated UUID, one per surviving spine row). Then `+ vulnerabilities` after `correlate(...)` / `fill_missing_array` (`:144-145`).
- **`process()`** (`crowdstrikeAssetsFindings.py:191-219`): `asset_mandatory_columns` + `additional_fields` struct + `agent_metadata` struct + `device_metadata` struct + `_row_asset_id AS id`. The mandatory set is inherited from `crowdstrikeAssets.py`; note **`"aid": {"path": "id", …}` at `crowdstrikeAssets.py:48`** — the asset field *named* `aid` reads the Discover combined **`id`**, not the sensor aid, in both lanes and both shapes. `value` is `hostname` → `current_local_ip` with the documented no-drop rationale (`:49-63`).
- **Persisted** (`base_parser.py` `_ASSET_OUTPUT_SCHEMA`): `id, client_id, client_integration_id, instance_id, integration_setting_id, integration_setting_flow_id, connector_flow_name, created_at, first_seen, last_seen, ip_address, tags, type, value, os_type, os_version, os_build, fqdn, group_names, site_names, additional_fields, agent_metadata, device_metadata, risk_score`. **`aid` is not among them** — established by execution in `parser-proof.md` §4, not re-derived here.

*Findings:* `_explode_record_findings` emits the finding's own top-level fields plus `cve`/`remediation` structs; `_explode_vulnerabilities` (`crowdstrikeAssetsFindings.py:127-131`) carries `_row_asset_id` through; `process()` (`:222-238`) selects `finding_mandatory_columns` + `additional_fields` struct + `_row_asset_id AS asset_id`, then stamps a fresh UUID `id` (`:238`). Mandatory finding fields (`:67-124`): `client_id, instance_id, name, display_name, type, severity, mitigation, status, first_seen, last_seen, cve_ids, description`. **`id`, `cid` and `aid` are explicitly excluded from `additional_fields`** (`:259-262`) — "the finding feed's own identity/correlation keys … avoids leaking redundant identifiers into every finding row". So no aid, derived or otherwise, reaches a persisted finding row either.

**Does anything raise, warn or log on two records sharing an `aid`? No. The collapse is entirely silent.** There is no collision check in the module. The one log line is `:135-137` — `f"CrowdStrike correlated parser: asset spine rows (chunk==0)={assets_count}"` — and `assets_count` is computed at `:134` **after** the window, so the log corroborates the wrong number. The only loud failure in the whole path is the absent-`aid`-**column** case at `:209` (`F.col("aid")` → `AnalysisException`), which is why an all-AID-less batch with the key *omitted* fails the run instead of publishing a wrong inventory (`parser-proof.md` §3). This is also the reason a **defensive guard here is worth its cost** — `decisions.md` allows one, and the cheapest honest one is a count of distinct spine keys before vs after the window, logged or raised. It changes no output column.

**`crowdstrike_policy_projection.py` — does it hold an AID assumption of its own? Yes, one, and it is already safe.**
- `:70` `ASSET_MATCH_KEY = "aid"`, read at `:263` as `F.col(ASSET_MATCH_KEY).alias("_asset_match_key")`. It is called from `crowdstrikeAssetsFindings.py:180` with **`assets_source_df`** — the correlated frame — so in the correlated lane the value it reads is the **record-level `aid`**, i.e. the very key this task is changing.
- It cannot leak a Discover `id` into an edge. `:271` `.filter(F.col("_external_id").isNotNull())` drops every row with no `prevention.assignment.policy_id`, and an AID-less host's envelope is the `Empty()` one (`prevention: null`) by construction on the collector side (`FalconDevicePoliciesEnvelope.cs:203-208`). `:369` filters `_asset_match_key.isNotNull()` again before emitting edges.
- `:240-252` short-circuits to `_empty_policies` when `device_policies` is absent, or when `prevention` is not a `StructType` — the comment at `:247-250` names this "the normal shape for an all-unmanaged page, not an error". **That path becomes much more common** under this change (whole pages of non-sensor hosts) and it is already correct.
- One incidental use: `:308-313`, a `Window.partitionBy("_external_id")` whose last tiebreak is `_asset_match_key.asc_nulls_last()`, picking a deterministic winner among rows sharing a policy id. Only rows with an assignment reach it, i.e. sensor hosts only. Unaffected.
- The `asset_match_key` ↔ asset-output mismatch (`:70` vs an asset output with no `aid` column) is real and reproduced, and is **explicitly out of scope** (`L-f3058a37`, `L-cc7eaeb4`).

**How to RUN a parser locally over a JSON artifact.** Real local Spark, not a mock — `tests/conftest.py:87` builds a session-scoped `spark_session`; `is_glue_env=False` routes through `sparkSession`. Verified working during this recon:

```
cd /Users/user/Dev/cymulate-integration-parsers && \
  JAVA_HOME=/opt/homebrew/opt/openjdk@11 PYTHONPATH=libs/packages \
  .venv/bin/python -m pytest tests/test_crowdstrike_assets_findings.py -q
```
I ran one test of that file: **`1 passed in 16.59s`**. `uv` is not installed on this machine; `/usr/bin/java` is not JDK 11, so the `JAVA_HOME` prefix is mandatory; `timeout(1)` does not exist here — do not put it in a command. The committed `.venv` is Python 3.9.6 / pyspark 3.3.0.

The **pattern** for running the parser over an arbitrary NDJSON artifact is three calls, taken from `tests/test_crowdstrike_live_edge_replay.py:99-109`: write the records to `tmp_path/findings*.json` → `build_default_parser_options(base_file_path=…, env_vars=_env(…), spark_session=…, glue_context=None, is_glue_env=False)` with `options["input_mode"] = "hydrated"` → `CrowdstrikeAssetsFindingsParser(options, connection_manager=None).run()`, which returns `(assets_df, findings_df)` — or a 3-tuple with `policies_df` when the payload carries `device_policies`. A variant at `:203-240` (`test_live_edge_replay_across_shards_real_path`) goes through the **production** `prepare_parser_options("crowdstrike-assets-findings", …)` multi-shard resolver instead of hand-setting `input_mode`; that is the closer analogue of a real run.

**Correction on `test_crowdstrike_live_edge_replay.py`: it does not replay a real collector artifact, so it is not SC6's harness.** All nine of its tests (`:113, :139, :176, :203, :242, :280, :308, :336, :374`) build records in-process with `_host`/`_finding`/`_record` (`:30-84`) and write them to `tmp_path`; there is no env gate and no external input. What it *is* is the best worked example of the "run the real parser over NDJSON and assert on counts" pattern, and its assertions are exactly the right kind — `assert n_assets == 1`, `assert n_findings == 5`, plus a last-wins check on `last_seen` (`:165-173`) with a comment noting the test would otherwise pass while keeping the stale copy.

**SC6's actual harness is `tests/test_e2e_correlated_real_output.py`.** It is gated on `E2E_CORRELATED_DIR` (`:20-22`), copies every `findings_*.json` from that directory into `tmp_path` (`:26-27`), runs the same production entry point (`:38-46`), and asserts: `assets_df.count() == len(expected_assets)`, `findings_df.count() == expected_findings` (`:65-66`), `finding_asset_ids ⊆ asset_ids` (`:68-71`), and a spot-check of mandatory finding fields (`:73-76`). Run it as:
```
E2E_CORRELATED_DIR=<dir of real findings_*.json> JAVA_HOME=/opt/homebrew/opt/openjdk@11 \
  PYTHONPATH=libs/packages .venv/bin/python -m pytest tests/test_e2e_correlated_real_output.py -q
```
**Its ground truth is `expected_assets.add(rec["aid"])` for `chunk == 0` (`:56-62`) — a Python set keyed on the same `aid` the parser partitions by, so both sides collapse together.** Under a unique-key emission the assertion is sound; as a *collision detector* it is worthless. SC6 needs a second, independent count over distinct `rec["host"]["id"]`.

Fixtures: `test_files/crowdstrike/assets/assets.json` (50 rows, 38 with no `aid`) + `assets.expected_assets.json` (50 rows out), and `test_files/crowdstrike/assets_and_findings/findings.json`. Correlated record builder for new tests: `_correlated_record` at `tests/test_crowdstrike_assets_findings.py:445-458`; the unmanaged-row builder `_unmanaged_asset_row` (`:360-372`) is used by the **split** lane only — **no correlated test covers a degenerate-aid record today**, which is why the collapse went unnoticed (`downstream-contract.md` §7).

---

## Patterns to mirror

- **Deriving vs. stating an identifier** → `Flows/Findings/Hosts/AidExtractor.cs:32-44` and `Flows/Findings/TwoPhase/FalconStagedHostPage.cs:227-236` — two named extraction paths, each call site picking its confidence level; a derived id must never reach `/devices/entities/devices/v2`. A third derivation must be added as a **third named method**, not by loosening `ExtractAid`, or the strict/best-effort split stops being legible.
- **Tolerating a missing identity** → `Flows/Assets/FalconAssetsScrollRunner.cs:460-472` — one target per host **unconditionally**, `ExtractSensorAid` (null for every unmanaged entity), null flowing through `FalconPolicyEnricher.cs:14 / :101-109 / :150-155` to `FalconDevicePoliciesEnvelope.cs:203-208`. Every host ships. This is the template `constraints.md` names and it needs **zero change** (`collector-seams.md` §5).
- **Guarded set insertion** → `Flows/Findings/TwoPhase/FalconHostSpooler.cs:551` (`if (ResolveSensorAid(host) is { } sensorAid && seen.Add(sensorAid))`) and `Flows/Assets/FalconAssetsPageParser.cs:91-99`. Never insert a possibly-blank key.
- **A tripwire that refuses an unrepresentable record** → `FalconStagedHostPage.cs:160-173`. Mirror it for the collision guard SC5 asks for: throw a typed exception, do not log-and-continue. `Exceptions/` already holds `FalconStagedPlaneCorruptException`.
- **Manifest write ordering** → `FalconHostSpooler.cs:286-289` ("nothing that can throw belongs between the scroll's last page and this write"). Any truncation check must sit **before** that write, not inside the manifest.
- **Counter placement** → `FalconSpotlightBatchScroller.cs:197-203` increments `stats.HostsEmitted` in the batch-end flush; that is the only path into `totalAssetsEmitted` (`FalconFindingsFlow.cs:348`). A host emitted outside that loop under-reports (`collector-seams.md` §7c).
- **Flow test through the production path** → `FalconCorrelatedFindingsTests.cs:301-358`; **staging assertions** → `FalconTwoPhaseFindingsTests.cs:589-658`.
- **Parser fixture** → `tests/test_crowdstrike_assets_findings.py:445-458` `_correlated_record` + `:510` `test_correlated_zero_finding_host_emits_asset_without_findings`.

---

## Shared surface to freeze

| surface | declared at | produced by | consumed by |
|---|---|---|---|
| `DiscoverHost(string Aid, JsonObject Host, DateTime? LastSeenUtc, string? SensorAid)` | `FalconDiscoverHostScroller.cs:26` | scroller `:91`, staged decode `FalconStagedHostPage.cs:188` | spooler `:206/630/690`, codec `:117`, pump `:598`, `FalconFrozenKeyList.cs:244-264`, both test harnesses' `Host(...)` helpers | **Freeze the signature.** If it changes, five prod sites and two test helpers change with it.
| `AidExtractor.ExtractAid` / `ExtractSensorAid` signatures | `AidExtractor.cs:24, 45, 53, 71` | — | scroller `:82`, `:91`; `FalconAssetsScrollRunner.cs:467`; `FalconStagedPolicyPage.cs:242`; `FalconAccessProber.cs:181` | **Do not change existing behaviour.** Five callers, four of them outside this task. A new derivation is additive.
| Staged host line `{"aid","sensorAid","lastSeen","host"}` | `FalconStagedHostPage.cs:38-41` | `EncodeLine :113` | `DecodeLine :156` | Encode/decode must move together or a generation corrupts mid-flight (`collector-seams.md` §2).
| Emitted record `{aid, chunk, isLastChunk, findingsInChunk, host, findings[]}` | `FalconCorrelatedRecord.cs:50-74` | `FalconSpotlightBatchScroller.cs:170-171`, `:199-200` | `CrowdstrikeAssetsFindingsCorrelated.py:207-221` | **Wire shape frozen by contract.** `aid` non-nullable `string`, positional, no guard.
| `FalconStageLedgerEntry` field set | `FalconPhase1Manifest.cs:71-79` | `CompletedFreeze :278`, `PolicyStageCounters.ToLedgerEntry` `FalconHostSpooler.cs:602` | `manifest.Freeze :419`, `Edges` gates at `FalconFrozenKeyList.cs:86`, `FalconFindingsFlow.cs:689` | O(1) fields only, hard constraint `:31-39`; structure frozen by `constraints.md`.
| `BatchEmitStats.HostsEmitted` | `FalconSpotlightBatchScroller.cs:18` | `:202` | `FalconFindingsFlow.cs:348` → checkpoint `FalconFindingsCheckpointWriter.cs:74` | Any new emission path must increment it or `TotalAssetsEmitted` under-reports.
| Parser asset/finding output columns | `base_parser.py` `_ASSET_OUTPUT_SCHEMA` | — | Exposure Analytics | Frozen by `constraints.md`. A parser-side guard may raise/log; it may not add, rename or retype a column.
| `E2E_CORRELATED_DIR` contract | `tests/test_e2e_correlated_real_output.py:20` | a real collector run's `findings_*.json` | SC6 | Directory of NDJSON files named `findings_*.json`.

---

## Disjoint sets available

Six sets. S1–S3 are the only ones that can conflict, and only through `DiscoverHost` (frozen above) — if the type does not change, they are fully independent.

- **S1 — scroller + derivation**: `Flows/Findings/Correlated/FalconDiscoverHostScroller.cs`, `Flows/Findings/Hosts/AidExtractor.cs`. The drop (`:82-86`), the truncation (`:100`), the new named derivation, the collision guard's detection site. Independent of: S2, S3, S4, S5, S6. **Owns the `DiscoverHost` construction call, so it owns any signature decision.**
- **S2 — spool + staged plane**: `Flows/Findings/TwoPhase/FalconHostSpooler.cs`, `Flows/Findings/TwoPhase/FalconStagedHostPage.cs`, `Flows/Findings/TwoPhase/FalconPhase1Manifest.cs`. The `seenAids` predicate if it moves, the truncated-freeze signal, the codec tripwires. Independent of: S1 (given a frozen `DiscoverHost`), S3, S4, S5, S6.
- **S3 — Spotlight batch path**: `Flows/Findings/Correlated/FalconSpotlightBatchPump.cs`, `Flows/Findings/Correlated/FalconSpotlightBatchScroller.cs`, `Flows/Findings/Correlated/FalconCorrelatedRecord.cs`, `Flows/Findings/Correlated/HostFindingsAccumulator.cs`. Empty-batch guard, accumulator seeding, FQL construction. Independent of: S1, S2, S4, S5, S6.
- **S4 — adapters tests A**: `UnitTests/.../FalconCorrelatedFindingsTests.cs`. SC1, SC2, SC4. Independent of everything except that it reads S1/S3's behaviour.
- **S5 — adapters tests B**: `UnitTests/.../FalconTwoPhaseFindingsTests.cs` (+ `InMemoryFalconStagingStore.cs`, `FalconConcurrencyHarness.cs` if a `Host(...)` overload is needed). SC3, SC5. **Must not be merged with S4** — separate 214 KB and 88 KB files, but each has its own private `CreateFactory`, so a route added in one is not visible in the other.
- **S6 — parsers repo**: `/Users/user/Dev/cymulate-integration-parsers/libs/packages/parsers/deprecated/crowdstrike/CrowdstrikeAssetsFindingsCorrelated.py` (the defensive guard `decisions.md` allows) and `tests/test_crowdstrike_assets_findings.py` / `tests/test_e2e_correlated_real_output.py`. Different repo, different toolchain, no shared build. Fully independent of S1–S5 **in editing**; strictly downstream of them **in verification** (SC6 needs an artifact produced by the fixed collector).

Not in any set, do not touch: `Flows/Policies/*` (zero change, `collector-seams.md` §5), `Recovery/*` (zero change, §4), `Flows/Findings/TwoPhase/FalconStagingArea.cs` + `FalconStagingPaths.cs` (aid-agnostic, §2), `Flows/Findings/TwoPhase/FalconFrozenKeyList.cs` (geometry, §3), `Processing/Configuration/FalconCollectorConfiguration.cs`, `libs/packages/build/lib/**` in the parsers repo.

---

## Landmines

- **L1 — the contract's central mechanism is dead; the probe replaces it.** `prompt_contract.md` and `decisions.md` both instruct "emit a unique derived AID **via the existing `AidExtractor.ExtractAid`**". `research/lab-tenant-identity-probe.md` measures **0 derivations from 254 AID-less assets** — the suffix gate at `AidExtractor.cs:124` (`Length != 32`) rejects the tenant's 56-char base64url token. The code agrees: the drop at `FalconDiscoverHostScroller.cs:82-86` is a null test on `ExtractAid`'s result, so every dropped asset is by definition one it failed on. `parser-proof.md` §6's "21 in, 21 out" used synthetic `derived-<hex>` keys, not `ExtractAid`'s output. **The record key must be the Discover combined `id` (present 303/303, unique 303/303).** The contract, decisions and assumptions files should be amended to say so before implementation — a worker following them literally will write code that rescues nothing.
- **L2 — do not change the `aid` of a host that already gets one.** `AidExtractor.cs:39-41` records 333 of 374 identifiers on a *different* live tenant coming from the 32-hex fallback, so population (b) is real even though it is empty here (probe: 0/303). Those hosts are emitted **today** under that derived key. A change that replaces it with the combined `id` silently re-identifies every one of them downstream — asset churn on tenants that currently work. The new key must be a **fallback after** `ExtractAid`, never a replacement for it: `ExtractAid(host) ?? <combined id>`, in that order.
- **L3 — SC3 has no expression in the frozen manifest structure.** `CompletedFreeze` (`FalconPhase1Manifest.cs:278-285`) hardcodes `units == unitsCovered`, `rejected: 0`, so `Finished` (`:98-115`) can only return `Completed`; there is no vendor-supplied denominator to compare against. `constraints.md` forbids changing manifest structure. The only shapes that satisfy both: **(a)** detect the truncation in `SpoolAsync` and **throw before `_staging.WriteManifestAsync` at `:289`** — no manifest means the next leg re-spools, which is the existing "the manifest's existence is the completion proof" semantics (`FalconFindingsFlow.cs:44`, `:595` `TryFindCompletedGenerationAsync`); or **(b)** pass values that make `Finished` choose `Degraded`. (a) touches no structure but is a one-way door unless the exception is classified as deferrable — see L6. Decide explicitly.
- **L4 — the `seenAids` line is still the highest-risk line in the file even under the non-null design.** `FalconHostSpooler.cs:206`. If the derivation ever returns a shared value for two hosts, this line drops the second one **silently and run-wide**, which is precisely the failure this task exists to remove, relocated somewhere harder to see (`collector-seams.md` §6, measured). SC5's collision guard is not optional decoration; it is what makes this line safe.
- **L5 — `aid:[]` is currently unreachable and SC4 creates the path it guards.** `hosts.Chunk(...)` never yields an empty batch (`FalconFrozenKeyList.cs:254`) and the staged codec refuses a keyless host (`FalconStagedHostPage.cs:160-165`), so no batch can have an empty accumulator map today. Any design that holds hosts out of `accumulators` must add the guard **and** route those hosts into the batch-end flush at `FalconSpotlightBatchScroller.cs:197-203` — emitting them elsewhere silently under-reports `stats.HostsEmitted` → `totalAssetsEmitted` (`FalconFindingsFlow.cs:348`) → the checkpoint (`collector-seams.md` §7c).
- **L6 — a thrown truncation is a one-way door.** If L3's option (a) is taken, the exception must reach a classifier that produces `RequestDeferredRecovery`, not a `PublishFailure`: `collector-execution-and-recovery/SKILL.md` records that `PublishFailure(IsRetryable = true)` is **terminal in practice — the host does not redeliver it** (production correlation `6a85ca2038f164746a562020`, 2026-08-20: `failed / 0 assets` over an intact checkpoint, then silence). A truncated freeze that throws an unclassified exception therefore ends the run permanently rather than re-spooling on the next leg, which is not obviously better than the truncation it replaces. Check `Processing/FalconFlowExceptionClassifier.cs` and `Processing/Resilience/` before choosing the exception type; the existing typed exceptions are `Exceptions/{FalconCursorExpiredException, FalconResumeNotPossibleException, FalconStagedPlaneCorruptException, FalconTransportFailureException}.cs`.
- **L7 — the parser path in the brief is wrong.** `libs/packages/parsers/crowdstrike/CrowdstrikeAssetsFindingsCorrelated.py` does not exist; the file is under `deprecated/`. A stale second copy of the whole tree lives at `libs/packages/build/lib/parsers/...` — a worker that greps and edits there changes nothing and tests will not notice.
- **L8 — the E2E test's ground truth collapses the same way the parser does.** `tests/test_e2e_correlated_real_output.py:56-62` builds `expected_assets` as a set keyed on `rec["aid"]`, so `assert assets_df.count() == len(expected_assets)` passes with both sides wrong on degenerate keys (`downstream-contract.md` §7). SC6 must add an independent count of distinct hosts.
- **L9 — ~5.4× more Spotlight scrolls on a mostly-unmanaged tenant.** `AidBatchSize` = 8 (`FalconCollectorConfiguration.cs:128`); the lab tenant goes 7 → 38 aid-batches, 31 of them certain to match nothing. Request budget is not the constraint (`:112-118`, 8 req/min measured against 6,000/min) but wall time is, and the run has an execution window. Staged pages also get ~6× denser in bytes on a large tenant, against a write path whose author documents it as "exactly the caller that guard was written to catch" (`FalconStagedHostPage.cs:71-79`) — that is A4, still unmeasured.
- **L10 — `Ordinal` vs `OrdinalIgnoreCase`.** The findings-lane sets are `Ordinal` (`FalconHostSpooler.cs:114-115`, `FalconSpotlightBatchPump.cs:592`); the assets-lane identity set is `OrdinalIgnoreCase` (`FalconAssetsPageParser.cs:29`). A hex-shaped derived key whose case the vendor varies produces two distinct keys in the findings lane and one in the assets lane.
- **L11 — the pre-existing failure lives in the file a test worker will edit.** `FalconTwoPhaseFindingsTests.ResumedLegThatPublishesNothing_LeavesTheCoordinateUnchanged_SoTheNoProgressBudgetAccumulates` in the 214 KB `FalconTwoPhaseFindingsTests.cs`. Baseline is 357/1/2 of 360 (`constraints.md`); anything else is a regression. Full suite only, ~14 min, never a filtered subset, never a shortened `/Blame:TestTimeout`.
- **L12 — resolved by the probe, kept as the shape of record.** The `id` on this tenant is `<32-hex cid>_<56-char base64url>` (e.g. `8884df8d8f704f43b23dad2e90572984_ASCeLr0LF_zz3dbEFdK-uwHZ4SmMawFCqU54WNcp8aZhuyQL6kxcBfPN`). Note it contains a **second underscore inside the suffix**, so `IndexOf('_')` at `AidExtractor.cs:115` splits at the cid boundary correctly, and the suffix carries `-` and `_`. A `TryExtractAidFromCombinedHostId` relaxation is **not** the fix — widening the 32-hex gate would start minting keys from suffixes on tenants where that suffix means something else. Use the whole `id`.
- **L13 — a 65-character key doubles the manifest's boundary-AID payload, against a 1 MiB ceiling.** `FalconHostSpooler.cs:280` carries `[.. boundaryAids]` into the manifest as `DiscoverWatermarkAids` (`FalconPhase1Manifest.cs:217`), the set is capped at `MaxBoundaryAids = 5000` (`FalconHostSpooler.cs:52`), and the manifest is read back under `IngestionOptions.MaxControlArtifactBytes` — **1 MiB, and the buffered-read ceiling outright under memory pressure** (`FalconPhase1Manifest.cs:31-39`). At 32-char AIDs a full set is ~175 KB of JSON; at 65-char combined ids it is ~340 KB, on top of `PageKeys` at ~48 bytes per staged page (a 97,332-host tenant at 1,000/page is ~98 keys, negligible — but the same doubling applies to any future growth). **An over-ceiling manifest fails the run.** The same list is also serialized into the checkpoint (`Recovery/FalconCheckpointSerializer.cs:68`, `FalconCheckpointState.cs:207`) where the collector imposes no cap of its own. This is the one place the key's *length* has a consequence, and it is worth a number before shipping — the cap may want lowering, or the boundary set may want to stay keyed on something shorter.
- **L14 — the 5 null-`last_seen` assets are lost by any re-anchor, and nobody has named this.** They survive the spooler's own logic (Q2c: kept, not epoch, no throw), but every re-anchor rebuilds the filter as `last_seen_timestamp:>='<floor>'` (`FalconHostSpooler.cs:254`, `:319` → `FalconHostFilters.BuildLastSeenTimestampGateFilter:22`), and a host whose `last_seen_timestamp` is null almost certainly does not match a `>=` predicate. So a cursor expiry, a mid-scroll 5xx, or hitting the depth cap **before** those hosts have been served drops them for that whole run — silently, and the fix in this task does not address it. They are only reachable at all on an unfiltered run: with a base date, `BuildLastSeenTimestampGateFilter` applies the same gate from the first request (`:19-23`), so the vendor never returns them. **Unverified** (whether Falcon FQL `>=` matches a null field is a vendor question), **high consequence**, and squarely inside "no asset Falcon returns may be dropped at any stage". Surface it; do not let it be discovered by a verifier.
