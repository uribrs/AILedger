# Falcon: unmanaged assets are lost by the correlated findings flow

Read this first. The three sibling files are the evidence: `collector-seams.md` (what in the
collector assumes an AID), `downstream-contract.md` (what the parser expects), `parser-proof.md`
(what the parser actually does when fed an AID-less record, run rather than reasoned).

## The defect

`CollectFindings` silently discards every Discover asset with no derivable AID. Measured live on
the lab tenant (`api.us-2.crowdstrike.com`, CID `8884df8d8f704f43b23dad2e90572984`) on 2026-08-30:

| query | vendor `total` |
|---|---|
| Discover, no filter, `last_seen >= 2020` | 298 |
| Discover, `entity_type:'managed'` | 49 |
| Host Management `/devices/queries/devices/v1` | 49 |

The run staged exactly 49. The 249 unmanaged assets were fetched, parsed, and thrown away in the
client. There is no `entity_type` filter in the findings lane — `BuildLastSeenTimestampGateFilter`
composes only the user FQL (absent here) and a `last_seen_timestamp` gate — so the narrowing is
purely the AID requirement, not the query.

Drop site: `Flows/Findings/Correlated/FalconDiscoverHostScroller.cs:82-86`.

Every unmanaged asset carries an `id`, and a hostname or a `current_local_ip`. Verified against the
live tenant. Nothing about the vendor response makes this hard.

## Second, worse defect — silent truncation

`FalconDiscoverHostScroller.cs:100` returns `new DiscoverHostPage(hosts, hosts.Count == 0 ? null :
responseAfter)` where `hosts` is the POST-FILTER list. A Discover page whose assets are all
AID-less therefore returns `Hosts=[]` and `After=null`, and the spooler reads that as end-of-scroll
(`FalconHostSpooler.cs:216,220-223`) — then writes a manifest marked as a completed freeze.

A mostly-unmanaged tenant with `last_seen` clustering gets a truncated inventory reported as a
successful complete run. The lab tenant did not hit it only because all 298 fit one page of 1000.

This is independent of the asset-loss fix and is arguably more urgent. Fix it regardless.

## The three branches that matter

1. **The collector shape change — where the loss came from.**
   `f68e0113`, 2026-07-05, UriB, branch `feature/falcon-correlated-findings`, PR #280 (merge
   `68e1468b`). "Falcon findings flow: correlated asset-driven collection (collector v5.0.0)."
   Inverted the traversal so Discover hosts drive the run into aid batches, and made the emitted
   record `{aid, chunk, isLastChunk, findingsInChunk, host, findings[]}` — with `aid` as the record
   KEY. Once the key was mandatory, an AID-less host was unrepresentable and the `continue` followed
   mechanically. The drop is in the file's first commit; it is not a later regression.

2. **The parser accommodation — the same assumption, same day.**
   `97bad4c`, 2026-07-05, UriB, branch `feature/crowdstrike-correlated-findings`, PR #197 (merge
   `2085e11`), repo `cymulate-integration-parsers`. "CrowdStrike parser: auto-detect and ingest
   correlated collector records." This is the commit that introduced
   `Window.partitionBy("aid")` (now `CrowdstrikeAssetsFindingsCorrelated.py:215`), unchanged since.
   Both halves of "every record has an aid" landed the same day in coordinated PRs, which is why
   neither side ever questioned it.

3. **The S3 spool — NOT the problem. Do not change it.**
   `850de27b`, 2026-08-06, UriB, branch `feat/s3-capability-falcon-two-phase`, PR #314 (merge
   `fa4f65d1`). "two-phase Discover/Spotlight collection — spool, freeze, then correlate." Arrived a
   MONTH after the drop and simply freezes whatever the scroller hands it. Per `collector-seams.md`:
   batching geometry needs no change (chunking is over hosts, no ordinal arithmetic survives),
   checkpoint/resume needs no change, policy enrichment is already null-tolerant end to end.
   The truncation defect above is a seam between (1) and (3), not a fault in either alone.

## Before July, nothing was lost

The preceding flow (`11d0deaa`, "Falcon refactor and events wiring") emitted by iterating the raw
vendor page, not the AID map:

```csharp
foreach (JsonNode? hostNode in resourcesArray)   // every host Falcon returned
    await pageWriter.WriteLineAsync(hostObj.ToJsonString(...));
```

The AID map was used ONLY to attach vulnerabilities. Its comment states the principle outright:
*"Ensure schema stability for upstream consumers: every asset always has these fields, even if we
can't extract an AID or no vulnerabilities are returned."* AID-less hosts were emitted with a null
`aid`, `vulnerabilities: []`, `vulnerabilities_count: 0`.

The v5.0.0 byte-parity validation (`334==334 assets`) passed because that tenant had an AID for
every asset. The two designs agree exactly until an AID-less asset appears, and none did.

The goal is to restore that principle — no assets lost, ever, absent a user FQL — using the
existing spool.

## The safe path, proven by execution

DO NOT emit `aid` as null, `""`, or an absent key. `Window.partitionBy("aid")` puts every null key
in one Spark partition, so all AID-less records collapse to ONE surviving asset row. Reproduced at
lab scale: **249 in → 1 out, 248 silently dropped, exit code 0, no error, no warning.** That turns a
visible collector-side drop into an invisible parser-side one. Strictly worse than today.

DO emit a unique per-asset AID. `AidExtractor.ExtractAid` (`Flows/Findings/Hosts/AidExtractor.cs:71-83`)
already derives one from the Discover combined `id` for exactly these entities. Proven: 1 managed +
20 unenriched with unique derived AIDs → 21 assets out, 21 expected, findings and policy edges
correct. **Collector-only change, no parser change required.**

## Seams to touch (from `collector-seams.md`)

- `FalconDiscoverHostScroller.cs:82-86` — stop dropping; also fix the `hosts.Count == 0 → After=null`
  early termination while in there.
- `FalconHostSpooler.cs:204-207` — **highest risk.** `.Where(h => seenAids.Add(h.Aid))` collapses
  both `null` and `""` silently (confirmed empirically). If AID becomes nullable this must change to
  a guarded predicate, not a sentinel. The ASSETS flow deduplicates on `FalconJson.TryReadAnyId`
  instead — that is the precedent.
- `FalconSpotlightBatchPump.cs:590-602` (`SeedAccumulators`) — hard crash on a null AID. AID-less
  hosts must bypass the accumulators and the `aid:[…]` Spotlight filter entirely, and a wholly
  AID-less batch must issue no Spotlight call at all.
- `FalconStagedHostPage` / `DiscoverHost.Aid` — symmetric encode/decode relax if the type widens.
- Policy enrichment — no change. `FalconAssetsScrollRunner.cs:455-467` already passes null for
  hosts with no sensor AID and ships them anyway. That is the template.

## Open questions nobody should guess at

- Which population the 249 fall into: some may ALREADY be emitted under a derived AID via the
  composite-`id` fallback, in which case they are in output today joined to nothing.
- Whether Discover's combined `id` is universally present, which decides whether swapping the dedup
  key is free or a trade.

## Do NOT fold these in

- **`asset_match_key` mismatch** — a real, reproduced defect in the parsers repo, unrelated to this.
  Policy edges are keyed on `aid` while assets are keyed on `value` (hostname → `current_local_ip`);
  a real-artifact replay produced 47 AID edge keys against 43 asset values with ZERO matches. See
  ledger `L-f3058a37` and `L-cc7eaeb4`. Note `aid` is not even an output column of
  `parser_output_assets` — Exposure Analytics never sees it.
- **`rules[].value` JSON encoding** — ledger `L-5de41efb` / `L-85e358c4`, still unverified. Needs
  `select distinct jsonb_typeof(value) from cybi.security_policy_rule` against a real ingested run.

## State of the prior work

Branch `fix/falcon-disable-prevention-policy-enrichment` (policy plane restructure) is committed,
pushed, merged with `origin/dev`, 357/360 tests, archived at
`ai/done/2026-08-27_0817_falcon-policy-plane-restructure`. Validated live end to end. Not yet
PR'd to dev. A working-tree edit flipping `EnablePreventionPolicyEnrichment` back to `true`
(`FalconCollectorConfiguration.cs:179`) exists for live testing and is NOT on the branch — start
from a clean tree.
