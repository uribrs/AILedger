# Verifier 2

Actor: `verifier-2`, second pass, after the four repairs. Verified against the working tree and
against live artifacts, not against `execution_notes.md`. Where `verifier-1` established something
and nothing has moved since, its disposition is carried and marked as carried.

Evidence produced by THIS pass, independently:

- **The live artifact still exists.** `execution_notes.md` says it was "downloaded, analysed, then
  deleted"; only the local copy was. The S3 objects are intact at
  `s3://cybi-data/Uri-Tests/falcon-with-policies/nolost-verify-001/` (6 `findings_*.json`, 301.3 MiB,
  plus `_staging/gen_08df0686f42bb2ae/{manifest.json,hosts_000000.json}`). Everything SC6 claims was
  therefore re-derivable, and was re-derived here rather than accepted.
- **Re-derived the artifact's identity mapping** by streaming all six objects
  (`…/scratchpad/sc6_recount.py`): 338 records, 0 null/empty `aid`, **298 distinct `aid`, 298
  distinct `host.id`, 298 distinct (host.id, aid) pairs** — a clean bijection, so neither collapse
  nor duplication is present in the real output. `aid` length histogram `{32: 89, 89: 249}`;
  252 records with an empty findings array; 298 records at `chunk == 0`; 298 records with
  `isLastChunk: true`; 13 hosts chunked across more than one record.
- **Re-ran the parser over that real artifact myself** (downloaded to `…/scratchpad/e2e-real`):
  `tests/test_e2e_correlated_real_output.py` → **1 passed in 17.25s**,
  `asset spine rows (chunk==0)=298`, `host/aid mapping is one-to-one - distinct hosts=298,
  distinct aids=298, surviving asset rows=298`.
- **Re-confirmed the R5 tripwire discriminates in both directions** after W6's rewrite: against
  `…/scratchpad/e2e_degenerate` → **1 failed**, `303 distinct hosts span 303 host/aid pairs but only
  50 distinct aids`, guard logs `ASSET COLLAPSE`; against `…/scratchpad/e2e_dup` → **1 failed**,
  `304 distinct aids span 304 host/aid pairs but only 303 distinct hosts`, guard logs
  `ASSET DUPLICATION`.
- **Read the live manifest** from S3: `hostCount 298`, `hostsPerPage 1000`, `aidBatchSize 50`,
  `hostFreeze.state "completed"`, `unresolved 0`, `rejected 0`, policy stages `disabled`.
- **Read the live run's wire bodies**: `008-collectors.done.json` →
  `"totalAssetsCollected": 298, "totalFindingsCollected": 121761, "status": "success"`. The runner log
  shows `Orphans=0` and `OverlapSkipped=0` on all 6 batches and
  `Hosts=298, StagedPages=1, ServerErrorReanchors=0, Truncated=false, KeyCollisions=0`.
- **Checked the wire shape against production output**, not only against the diff: a real record's
  top-level keys are exactly `['aid', 'chunk', 'isLastChunk', 'findingsInChunk', 'host', 'findings']`,
  and an AID-less host's `host.device_policies` is
  `{"schema_version": 1, "collection_status": "disabled", "prevention": null}` — the `Empty()`
  envelope the constraint requires, emitted rather than omitted.
- Parser suite re-run: `tests/test_crowdstrike_assets_findings.py` → **34 passed in 75.40s**.
- Full Falcon suite re-run, unfiltered, no shortened timeout →
  `…/scratchpad/falcon-suite-verifier2.log`. Result in SC7.

---

## 1. Assumption Disposition

| id | status | citation | actor |
|----|--------|----------|-------|
| A1 | **REJECTED** (carried) | `research/lab-tenant-identity-probe.md`: `ExtractAid` derives a key for 0 of 254 AID-less lab assets; the 32-hex gate at `AidExtractor.cs:124` rejects the tenant's 56-char base64url suffix. Nothing moved. Re-confirmed indirectly by the live artifact's `aid` histogram — 32-char keys appear on exactly 89 records, which is the 49 sensor hosts and their chunks, and on no AID-less host. | verifier-1 (carried), verifier-2 |
| A2 | **VALIDATED, lab tenant only** (carried, strengthened) | Carried from `research/lab-tenant-identity-probe.md` (`id` present 303/303, unique 303/303). Strengthened by production output: 298 distinct `host.id` over 298 distinct assets in the real artifact, 0 null or empty. Generality is still A15. | verifier-1 (carried), verifier-2 |
| A3 | **REJECTED** (carried) | `research/lab-tenant-identity-probe.md`: AID derivable from the combined `id` 32-hex suffix = 0. No orphaned sub-population on this tenant. | verifier-1 (carried) |
| A4 | **VALIDATED at lab-tenant density** (moved from NEVER-TESTED) | A real staged page now exists and was measured: `_staging/gen_08df0686f42bb2ae/hosts_000000.json` is **628,500 bytes for 298 hosts** at `hostsPerPage = 1000`, i.e. ~2,110 bytes/host, so a full 1,000-host staged page is ~2.1 MB — about 26% of `MaxInMemoryObjectBytes` (8 MiB), and the page is streamed rather than buffered anyway (`FalconStagedHostPage.OpenEncodedStreamAsync:61-79`). The `DataPipelineException` `decisions.md` feared is not reachable at this density. Caveat: envelope size is tenant-dependent, so this bounds the lab tenant, not every tenant. | verifier-2 |
| A5 | **VALIDATED** (carried) | Exactly two host predicates in `Flows/Findings/`, both at `FalconHostSpooler.cs:256-257`; `DiscoverHost.Aid` non-null by construction; `SensorAid` read only through `ResolveSensorAid`. `R4_…` drives three sensorless hosts through `ProcessAsync` without a throw. Live run confirms: 249 sensorless assets traversed the whole Spotlight lane, `Success=True`. | verifier-1 (carried), verifier-2 |
| A6 | **VALIDATED (emission leg)** (carried, strengthened) | Batch-end flush walks `plan.Hosts` (`FalconSpotlightBatchScroller.cs:243-249`) and is the only site incrementing `stats.HostsEmitted`. Strengthened by the live run: `totalAssetsCollected = 298` in `collectors.done`, equal to the manifest's `hostCount = 298` and to the artifact's 298 distinct `aid`s — so `HostsEmitted` → `totalAssetsEmitted` → the done event agrees with the emitted objects on a real run. That closes verifier-1's stated caveat on the counter leg. | verifier-1 (carried), verifier-2 |
| A7 | **VALIDATED** (moved from NEVER-TESTED) | The artifact this assumption names now exists AND was exercised by this pass, not merely reported: real collector output from `s3://cybi-data/Uri-Tests/falcon-with-policies/nolost-verify-001/`, 301.3 MiB, 338 records over 298 hosts, run through `tests/test_e2e_correlated_real_output.py` → 1 passed, 298 distinct input hosts → 298 asset rows, asserted on counts. Bound on the claim: the collector binary that produced the artifact predates two of the four repairs — see finding 6. | verifier-2 |
| A8 | **NEVER-TESTED** (carried as a status; verifier-1's framing corrected) | No consumer downstream of the parser was probed, so the status stands. But the stated risk — "an 89-character combined id now enters `parser_output_assets` / `parser_output_findings` in a column that has only ever carried 32-character AIDs" — is **not what the code does**. The emitted asset's `aid` column is fed from the host's Discover `id`, not from the record key: `crowdstrikeAssets.py:48` maps `"aid": {"path": "id"}` and the correlated handler inherits that mapping verbatim (`crowdstrikeAssetsFindings.py:61-63`), while `_build_asset_spine` puts every host field at the top level. That column has therefore always carried the 89-char combined id, on both the standalone assets lane and the correlated lane, before and after this change. The record key reaches exactly one output: the policy projection's `asset_match_key` (`crowdstrike_policy_projection.py:70,263`), which reads the record-level `aid` — and that path filters to hosts carrying a prevention assignment (`:271`), which are sensor hosts with 32-char AIDs, and is inert entirely while `EnablePreventionPolicyEnrichment` is `false`. Residual risk is row count (298 vs 49), not key width. | verifier-1 (carried), verifier-2 |
| A9 | **VALIDATED** (carried, and the bound it asked for now exists) | Carried: `FalconDiscoverHostScroller.cs:112` still normalizes empty/absent `after` to null. New since verifier-1: the spool now follows an empty-but-live page, so the third belt matters — it is bounded by `FalconHostSpooler.MaxConsecutiveEmptyLivePages = 5` (`:115`, decision at `:271-278`) and `SC3_EmptyLivePagesPastTheBound_StopTheScroll_AndTheFreezeIsNotRecordedAsCompleted` terminates against a fixture whose every cursor answers empty and names the next one forever. An unbounded scroll is not reachable. | verifier-1 (carried), verifier-2 |
| A10 | **NEVER-TESTED** (carried) | Not exercised; moot — the fallback key is the combined `id` verbatim (`AidExtractor.cs:117`), so no ordering or canonicalisation decision arises. | verifier-1 (carried) |
| A11 | **NEVER-TESTED** (carried) | No probe of Falcon's 404-under-200 behaviour; the changed cursor leg was not exercised against one. | verifier-1 (carried) |
| A12 | **NEVER-TESTED as a claim; honoured as a rule** (carried) | Followed, and this pass took it further: SC6 was settled here by running the parser and asserting row counts, never on an exit code. The guard reports counts rather than raising. | verifier-1 (carried), verifier-2 |
| A13 | **NEVER-TESTED** (carried) | `EnablePreventionPolicyEnrichment` stays `false`; the live manifest confirms it in production — `policyFetch.state` and `policyEdges.state` are both `"disabled"`. No `settings_hash` is produced to compare. | verifier-1 (carried), verifier-2 |
| A14 | **NEVER-TESTED; moot** (carried) | `/devices/entities/devices/v2` membership derives from `ResolveSensorAid`, null for every AID-less host, so batch composition is what it was at `baseRef`. | verifier-1 (carried) |
| A15 | **NEVER-TESTED** (carried) | One tenant only. No second tenant probed. | verifier-1 (carried) |
| A16 | **REJECTED as stated; all three legs now measured at 89 characters** | Length is 89, not 65 — carried. Manifest leg: `R3_…` (5,000 × 89-char keys, 465,708 B against production `MaxControlArtifactBytes`, real write/read round trip). Staged-page codec leg: `ResumedLeg_ReadsBackEveryStagedHostIncludingCombinedIdKeyedOnes_…`. **Checkpoint leg, unmeasured at verifier-1, is now measured**: `R3b_CheckpointCarryingTheSameBoundarySet_HasNoCeilingOfItsOwn_SoItsSerializedSizeIsPinnedHere` (`FalconTwoPhaseFindingsTests.cs:4588`) builds the payload field-for-field as `FalconFindingsCheckpointWriter.OnBatchPublished` does, serializes through the real `FalconCheckpointHelper.SaveFindingsState`, measures 460,022 B for the boundary set inside a 460,397 B payload, and round-trips it back with the set intact. Measured, not capped — see finding 4. | verifier-1 (carried), verifier-2 |
| A17 | **VALIDATED — the defect is real, unfixed, and now visible in production output** | Carried: `research/falcon-fql-null-field-matching.md`, 303 unfiltered vs 298 under `last_seen_timestamp:>=`, both `:null` and `:!null` HTTP 400. New: the live run **collected 298**, not 303 — `collectors.done` `totalAssetsCollected: 298`, manifest `hostCount: 298`. The 5 missing assets are no longer an inference from a probe; they are absent from a real successful run. `FalconHostFilters.cs` is not in the diff. | verifier-1 (carried), verifier-2 |
| A18 | **VALIDATED (emission leg)** (carried, caveat closed) | Carried. The caveat verifier-1 attached (`totalAssetsEmitted` and the checkpoint not asserted directly) is closed empirically by the live run: `totalAssetsCollected = 298 = hostCount = distinct emitted aids`. | verifier-1 (carried), verifier-2 |

---

## 2. Decision drift

| decision (`decisions.md`) | disposition |
|---|---|
| Premise fixed: a dropped asset is a defect, not a design choice | **landed as decided** — 49 → 298 on the same tenant, measured, not argued |
| Restore the pre-July principle using the EXISTING S3 two-phase spool | **landed as decided** — spool design untouched, see SC8 |
| Emit AID-less hosts with a unique record key, never null / `""` / sentinel | **landed as decided** — 0 null-or-empty `aid` across 338 real records, 298 distinct keys for 298 distinct hosts |
| AMENDED: the key is the Discover combined `id`, not `ExtractAid`'s derived value | **landed as decided** — `AidExtractor.ExtractRecordKey` (`:106-118`); confirmed in the real artifact by the `{32: 89, 89: 249}` length histogram |
| The combined `id` is a FALLBACK AFTER `ExtractAid`, never a replacement | **landed as decided** — precedence in `ExtractRecordKey`, pinned by `R1_…`; the 89 32-char records in the real artifact are the proof that the sensor path still wins |
| Do NOT relax the 32-hex gate in `TryExtractAidFromCombinedHostId` | **landed as decided** — method byte-identical to `baseRef` |
| Wire shape does not change | **landed as decided** — `FalconCorrelatedRecord.cs` not in the diff; the real artifact's records carry exactly `{aid, chunk, isLastChunk, findingsInChunk, host, findings}` |
| The truncation fix ships regardless of how the drop fix lands | **landed as decided, and went further than decided** — the scroller returns `responseAfter` unconditionally, and W2's repair added a bounded follow of an empty-but-live page in the spool |
| The parser claim is settled by execution against a real artifact | **landed** — was the one open item at verifier-1; settled, and re-executed independently by this pass |
| A defensive, non-silent guard in the parser; no output-contract change | **landed, and widened during execution** — `_report_spine_collapse` became `_report_spine_identity_drift`, checking host/aid/pair cardinality in both directions instead of one. Reason: code-review finding 3 showed the single-direction check reported "clean" on a cancelling collapse+duplication run. No column added, renamed or retyped |
| `EnablePreventionPolicyEnrichment` stays `false` | **landed as decided** — file not in the diff; live manifest shows both policy stages `disabled` |
| A collision must be detected and surfaced, never absorbed | **landed with the documented cost verifier-1 recorded** — `SpoolKeyClaims.TryClaim` separates replay from collision, counts collisions into the freeze's `unresolved`, and logs an error; the colliding host is still not staged, and `unresolved` deliberately does not downgrade the freeze state. Unchanged since verifier-1. Live run: `KeyCollisions=0`, manifest `unresolved: 0` |

---

## 3. Attention Item Disposition

| id | final disposition | evidence |
|---|---|---|
| R1 | handled | `FalconCorrelatedFindingsTests.cs` `R1_HostWithThirtyTwoHexIdSuffix_KeepsItsDerivedAid_AndDoesNotSwitchToTheCombinedId`; production precedence at `AidExtractor.cs:114-118`. Ran and passed in this pass's full-suite run. Independently corroborated by the real artifact: 89 records still key on a 32-char AID. |
| R2 | handled | `FalconTwoPhaseFindingsTests.cs:4222` `R2_TwoDistinctHostsDerivingTheSameKey_AreDetectedAndSurfaced_NotSilentlyMerged`; production path `SpoolKeyClaims.TryClaim` (`FalconHostSpooler.cs:419-…`), count carried into `SpooledFreeze(…, keyClaims.Collisions)` at `:374` and into the completion log at `:383-386`. Ran and passed. Residual cost (host not staged; `unresolved` does not downgrade state) carried forward from verifier-1 finding 5, unchanged. |
| R3 | handled | `FalconTwoPhaseFindingsTests.cs:4345` `R3_ManifestWithFullBoundarySetOfLongKeys_StaysUnderTheControlArtifactCeiling` — reads `MaxBoundaryAids` and `MaxControlArtifactBytes` from production, real write + read-back. Ran and passed. The rider this R-id also names — "same list also hits the checkpoint uncapped" — is now **measured** by `R3b_…` (460,022 B) but **not capped**; that half is accepted-risk, see finding 4. |
| R4 | handled | `FalconCorrelatedFindingsTests.cs:1991` `R4_BatchOfOnlyKeylessHosts_IssuesNoSpotlightRequest_AndStillEmitsEveryHost`; production guard `if (accumulators.Count > 0)` at `FalconSpotlightBatchScroller.cs:107`. Ran and passed. Live corroboration: 249 sensorless assets, `Orphans=0` on all six batches, `Success=True`. |
| R5 | handled | `tests/test_e2e_correlated_real_output.py` — ground truth counted on `host["id"]`, with pair-cardinality assertions ahead of the row-count assertions. **This pass ran it three ways**: real artifact → passed (298/298); `e2e_degenerate` → failed with `303 … only 50 distinct aids`; `e2e_dup` → failed with `304 distinct aids … only 303 distinct hosts`. It discriminates in both directions, which is more than the original R5 asked for. |

---

## 4. Success Criteria

| SC | verdict | evidence |
|---|---|---|
| SC1 | **met** | `SC1_AidLessDiscoverAsset_IsEmitted_KeyedByItsCombinedId_WithAnEmptyFindingsArray` (`FalconCorrelatedFindingsTests.cs:1886`), driven through `FalconCollector.ProcessAsync` — real scroller, spooler, staged codec, Spotlight pump. Now also demonstrated in production: 249 of the real artifact's 298 assets carry an 89-char combined-id key and 252 records carry an empty findings array. |
| SC2 | **met** | `SC2_DiscoverPageOfOnlyAidLessHosts_DoesNotTerminateTheScroll_TheAfterCursorIsFollowed` (`:1933`). Carried from verifier-1; nothing moved. |
| SC3 | **met, and stronger than at verifier-1** | Two tests now, not one. `SC3_EmptyDiscoverPageWithALiveCursor_DoesNotEndTheScroll_AndEveryHostBehindItIsStaged` (`:4009`) asserts the OUTCOME — `after=cursor-2` requested, `manifest.HostCount == 2`, both hosts emitted, freeze `Completed`. `SC3_EmptyLivePagesPastTheBound_StopTheScroll_AndTheFreezeIsNotRecordedAsCompleted` (`:4104`) asserts exactly `Bound + 1` cursor requests, nothing past the bound, freeze `Degraded`, `IsTraversed` false, an operator-visible alert. I traced the production logic at `FalconHostSpooler.cs:271-306` line by line: an empty page with a live cursor is followed (`nextPageTask` set, no `break`), the counter resets on any page carrying a host (pre-dedup, which is the right signal), and the bound trips on the sixth consecutive empty page. **Caveat, unchanged in substance from verifier-1 finding 6: past the bound the run still publishes the partial inventory and still returns Success.** See finding 1. |
| SC4 | **met** | `R4_…` — see the attention table. |
| SC5 | **met** | `R2_…` — see the attention table. |
| SC6 | **met** | Not accepted on the notes' word. The artifact is still in S3; I re-derived its identity mapping myself (298 distinct hosts, 298 distinct aids, 298 pairs, 0 null keys) and re-ran the parser over it — 298 in, 298 out, asserted on `assets_df.count()` against a ground truth counted on `host["id"]`, never on an exit code. `findings_df.count()` also matched the independently counted set of CVE-bearing finding ids. Bounded by finding 6: the collector binary that produced the artifact predates two of the four repairs, neither of which can alter this run's output. |
| SC7 | **met** | See the suite result below. |
| SC8 | **met** | Re-proven by diff at this pass, not carried: `git diff bbb044b9 -- …/FalconCollector/Recovery/` is **empty** (checkpoint format untouched); the `JsonPropertyName` list in `FalconPhase1Manifest.cs` is **identical** to `baseRef`'s, same names and same order (manifest structure); the spooler diff contains **no** line touching `Chunk`, `hostsPerPage`, `aidBatchSize` or `MaxBoundaryAids` (batching geometry); `FalconCollectorConfiguration.cs` is not in the diff and `:179` is still `= false`; the parser diff adds one constant, one call site and one report-only static method, leaving `process()` / `post_process()` and the emitted frames untouched. The live manifest's own JSON is the production confirmation — same 14 fields, `hostsPerPage 1000`, `aidBatchSize 50`. |

**Suite result (this pass's own run, full Falcon project, unfiltered, no shortened timeout):**

```
Failed!  - Failed:     1, Passed:   368, Skipped:     2, Total:   371, Duration: 14 m 7 s
```

`…/scratchpad/falcon-suite-verifier2.log`. Exactly one `[FAIL]` line in the whole log, and it is the
known pre-existing
`FalconTwoPhaseFindingsTests.ResumedLegThatPublishesNothing_LeavesTheCoordinateUnchanged_SoTheNoProgressBudgetAccumulates`.
Against the `baseRef` baseline of 357/1/2 of 360 that is **11 new tests, all passing, and no new
failure** — which is also how every "ran and passed" claim in the attention table above is
established, since the totals leave no room for a new test to have failed. Duration 14m07s, full
project, no `/Blame:TestTimeout` override.

---

## 5. Findings, most severe first

### 1. Past the empty-page bound, a truncated inventory is still published as Success. Narrowed, not closed.

`FalconHostSpooler.cs:289-306`, `:370-374`, `FalconFindingsFlow.cs:708`

W2's repair is real and it is the right shape. An empty Discover page carrying a live `after` is now
followed (`:272-278`), the counter resets on any page that carried a host so it bounds a burst and
not the scroll, and the bound terminates — I traced all three and the two SC3 tests pin them.
The common case the code-review named (a transient shard or index gap, one page or a few) no longer
truncates anything.

What is unchanged is the tail. On the sixth consecutive empty-but-live page the spool breaks,
`scrollTruncated` is set, `SpooledFreeze` records `Degraded`, an error is logged — and then the flow
proceeds to Phase 2 over the partial frozen list, publishes it, and returns `Success`. I re-checked
the gate myself: the only `IsTraversed` read in the whole collector is
`FalconFindingsFlow.cs:708`, and it reads `manifest.Edges`, never `manifest.Freeze`. The code says so
in its own comment at `FalconHostSpooler.cs:370-372`, and the bound test says so in its docstring.

So for the operator's premise — "no assets are ever lost" — this is the one path left where the
collector knows it lost assets, says so in a log line and a ledger field, and reports success anyway.
The distance to closing it is one condition in `FalconFindingsFlow` on `manifest.Freeze.IsTraversed`;
recon L3/L6 rejected *throwing* as a one-way door, which is a sound argument against throwing but not
against returning a partial-completion result. Recording this as the residual, not objecting to the
call.

### 2. A17 is no longer a probe result — 5 assets are missing from a real successful run.

`FalconHostFilters.BuildLastSeenTimestampGateFilter:19-23`, applied at `FalconHostSpooler.cs:254`
and on every re-anchor.

Discover holds 303 assets unfiltered on the lab tenant. The fixed collector's live run reported
`totalAssetsCollected: 298` and froze `hostCount: 298`, with `Truncated=false` and
`KeyCollisions=0` — i.e. the run believed it was complete, and it was 5 assets short. FQL's
`last_seen_timestamp:>='…'` does not match a null, and both `:null` and `:!null` return 400.

This was correctly judged out of scope and is not a regression — but it means the contract's own
sentence, "absent a user FQL, no asset Falcon returns may be dropped at any stage", is still
violated for ~1.6% of this tenant, on the normal path rather than an edge. It should not be read as
covered by the work.

### 3. The duplication direction is detected, never prevented — and only downstream.

`FalconHostSpooler.SpoolKeyClaims`, `CrowdstrikeAssetsFindingsCorrelated.py:311-334`

`SpoolKeyClaims` maps key → host identity, so it catches two hosts under one key. It structurally
cannot catch one host under two keys: a second key is simply an unclaimed entry. That case is
reachable through this change — a host staged under its combined `id`, given a sensor mid-spool, then
re-served above the watermark after a depth-cap re-anchor under its new AID — and the collector will
stage it twice and emit two records.

W6's repair makes the parser *report* it (both directions, and the cancelling case, verified above
against `e2e_dup`), which is a genuine improvement over the single-direction check. But reporting is
all it does: two asset rows are still emitted, and the only signal is a log line. The collector has
no host-identity claim that would refuse the second staging.

One mitigating fact, traced but not tested: both duplicate rows would carry the same `aid` in the
emitted asset output, because that column is fed from `host.id` (`crowdstrikeAssets.py:48`), so a
downstream upsert keyed on it may absorb the duplicate. That is an inference about a consumer nobody
probed — do not bank on it.

The real artifact is clean on this axis (298 hosts, 298 aids, 298 pairs), so nothing is duplicated
today on this tenant.

### 4. The checkpoint's boundary list is measured, uncapped — and nothing reads it.

`FalconFindingsFlow.cs:365` → `FalconFindingsCheckpointWriter.cs:73` →
`FalconCheckpointSerializer.cs:68`; pinned by `R3b_…`.

W5's response was to measure rather than to cap, on the stated grounds that it does not own
`Recovery/`. Judgment on proportionality, since the team lead asked for one:

**The pin is the right response for this task, and the stated reason for it is weaker than the
conclusion.** The pin itself is good work: it builds the payload field-for-field as production does,
serializes through the real serializer, measures 460,022 B of a 460,397 B payload, round-trips it
back, and asserts the boundary set dominates so a future change that makes some other field linear
fails the test rather than the measurement silently becoming meaningless. A future doubling of the
key length or the cap fails a test instead of shipping.

But `Recovery/` ownership is not what blocks a cap. The write site is `FalconFindingsFlow.cs:365` —
a file this task already edited — and capping there needs no `Recovery/` change at all. What
*actually* argues for leaving it alone is that there is no ceiling to size a cap against: I searched
the infra package's own documentation (`Cymulate.IntegrationInfra.xml`) and neither
`AdapterCheckpoint.AdapterState` nor `CommitPage` states a size limit, and the persistence itself
lives outside both repos.

One fact neither the test nor verifier-1 records, which changes the cost/benefit for whoever picks
this up: **`FalconCheckpointState.DiscoverWatermarkAids` is write-only in this repo.** It is
serialized (`FalconCheckpointSerializer.cs:68`), deserialized back into the state
(`FalconCheckpointDeserializer.cs:220,278`) — and then read by nothing. `grep` across
`Flows/` and `Recovery/` finds no decision that consults it; the resume path takes the boundary set
from the manifest, not from the checkpoint. So ~460 KB is re-persisted at every aid-batch boundary
for a value no code consumes, which makes a cap (or dropping the field from the checkpoint entirely)
cheap and safe rather than a semantic risk. Not a data-loss path; a follow-up, not a blocker.

### 5. The keyless-host count now logs, but is still page-local and never durable.

`FalconDiscoverHostScroller.cs:44-50, 106-113`, `FalconFindingsFlow.cs:144`

Repair 1 landed exactly as described. `FalconFindingsFlow._logger` is non-nullable
(`:78`, `:101` throws on null), the scroller is constructed with it at `:144`, and
`grep -rn "new FalconDiscoverHostScroller" src/` returns that one line and no other — it is the only
construction site in the collector. The warning can now reach an operator.

The secondary half of code-review finding 1 was not addressed and should be recorded: `keylessHosts`
is still a local that dies with the method. It reaches no manifest field, no ledger counter, no
checkpoint — unlike `SpoolKeyClaims.Collisions`, which is carried into `SpooledFreeze`'s `unresolved`
and into the completion log. Two exclusions of the same kind with two different durabilities. A log
line is a real surface, so this is now a gap in evidence retention rather than a silent drop.

Also still open from the same method, and still uncounted: a `resources` element whose `ValueKind`
is not `Object` is `continue`d with no counter and no log (`:80-83`). Pre-existing, two lines to
close, and it is now the only wholly silent exclusion left in the scroller.

### 6. The SC6 artifact was produced by a collector one repair behind — bounded, but state it.

The live run's completion line reads
`Generation=…, Hosts=298, StagedPages=1, ServerErrorReanchors=0, Truncated=false, KeyCollisions=0`.
The current code logs `EmptyLivePagesFollowed` in that same line (`FalconHostSpooler.cs:383-386`).
Its absence dates the binary: the artifact predates W2's empty-page repair, and predates the logger
wiring.

Neither can change that run's output. The spool staged one page and hit no empty page at all
(`StagedPages=1`, `Truncated=false`), so the empty-page arm was never entered; the logger wiring is
diagnostic only. The two repairs that came after it are exercised by tests, and the parser repair —
the only other one that could touch this evidence — I ran in its *current* form over the artifact.
So SC6 stands. It is recorded so nobody later reads "live run, fixed collector" as covering all four
repairs end to end. A re-run against the same tenant would close it outright.

### 7. Two silent-drop paths on the parser side, both pre-existing, one wholly unlogged.

`CrowdstrikeAssetsFindingsCorrelated.py:203-206` filters `_corrupt_record` rows out of the lane with
**no count and no log**. A malformed NDJSON line is dropped, the identity guard counts from the
already-filtered frame, and nothing downstream can tell. It is unlikely on collector-written output
and it mirrors the split handler, but it is precisely the class of drop the operator asked about and
it is the only one in the chain with no counter at all.

By contrast `BaseParser._enforce_asset_output_contract` (`base_parser.py:168-205`) drops assets whose
identity `value` is null, blank, or carries a control char — a real loss path into Postgres — but it
**logs the count**, and the E2E test models it explicitly (`expected_assets = len(host_ids) -
len(unstorable_hosts)`). On the real artifact that exclusion was 0 hosts, because
`crowdstrikeAssets.py`'s `current_local_ip` fallback keeps passive-discovery hosts that carry only an
IP. Recorded so the two are not confused: one is counted, the other is not.

### 8. Carried forward unchanged from verifier-1 and code-review, not re-argued.

- The collision guard converts a silent loss into a logged one; the colliding host is still not
  staged and `unresolved` does not downgrade the freeze state (verifier-1 finding 5).
- `FalconStagingArea.cs:293-316, 381-390` still tells the operator the manifest "is dominated by the
  {PageCount} staged page key(s)" and to raise the Discover page size — which this change's own
  measurements contradict, since the boundary set is now 44.4% of the ceiling. Not repaired; that
  file is not in the diff (code-review 6a).
- `_report_spine_identity_drift` still runs its own `chunk == 0` aggregation rather than deriving the
  counts from the pass that builds the spine (code-review 5). Cost, not correctness — and the check
  now needs three distinct counts, which makes folding it into the spine pass less trivial than the
  review implied.
- Stale line-number citations in comments and test docstrings (code-review 6b).

---

## 6. Bottom line

The four repairs all landed, and I verified each in code rather than in the notes. The logger is
wired at the one construction site that exists. The empty-page follow works, is bounded, resets
correctly, and terminates. The parser's identity check is bidirectional and catches the cancelling
case — a better fix than the `elif` the review asked for, and I confirmed it discriminates in both
directions against real fixtures. The checkpoint measurement exists and is honest about being a pin
rather than a cap.

**SC6 is met, and I did not take it on trust.** The artifact was not deleted — it is still in S3 —
so I re-derived its identity mapping myself and re-ran the parser over it: 298 distinct hosts in,
298 asset rows out, 0 null keys, and a clean host↔aid bijection with no collapse and no duplication
anywhere in 338 real records. The done event, the manifest, and the emitted objects all agree on 298.

**Is anything still silently dropping or duplicating assets?** Traced end to end, the answer is:
nothing *silent* in the collector, and one thing silent in the parser.

- Every exclusion left in the collector is counted, logged, or both: keyless hosts (logged, now that
  the logger is wired), key collisions (logged plus a durable `unresolved` count), the truncated
  freeze (logged plus a `Degraded` ledger state). The one wholly silent `continue` left is the
  non-Object `resources` element.
- The loss that is *measured and unfixed* is A17's 5 assets, which the live run demonstrates rather
  than predicts.
- The loss that is *possible and not gated* is a truncated freeze past the empty-page bound, which
  publishes a partial inventory as Success.
- Duplication is possible (one host, two keys, across a mid-spool sensor installation), reported
  downstream by the parser, and not prevented by the collector.
- On the parser side, `_corrupt_record` rows are dropped with no count.

None of these are regressions from this task, and the change removes far more loss than it leaves:
249 of 303 assets on the lab tenant went from dropped-in-silence to emitted, measured on real output
rather than argued. The work is sound. What is left, in order, is the flow-level gate on
`Freeze.IsTraversed`, A17, and a host-identity claim in the spool.
