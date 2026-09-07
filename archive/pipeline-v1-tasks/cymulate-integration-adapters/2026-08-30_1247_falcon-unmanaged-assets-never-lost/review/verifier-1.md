# Verifier 1

Actor: `verifier-1`. Verified against the diff from `bbb044b9557a205fa6b166402c19544622098e20`, not
against `execution_notes.md`. Both repos' changes are uncommitted working-tree changes
(`cymulate-integration-adapters` on `fix/falcon-disable-prevention-policy-enrichment`,
`cymulate-integration-parsers` on `master`).

Evidence produced by this pass, independently of the execution run:

- Full Falcon suite re-run, unfiltered, no shortened timeout →
  `/private/tmp/claude-501/-Users-user-Dev/64dcdf93-6ebd-478b-bc47-f6b5ccdcb688/scratchpad/falcon-suite-verifier.log`
- Parser suite re-run: `tests/test_crowdstrike_assets_findings.py` → **32 passed in 68.56s**.
- Inspection of the in-flight live collector run at
  `…/scratchpad/e2e-out/local-adapter-runner.log`.

---

## 1. Assumption Disposition

| id | status | citation | actor |
|----|--------|----------|-------|
| A1 | **REJECTED (superseded before any code was written)** | `research/lab-tenant-identity-probe.md`: full inventory of 303 lab-tenant assets, `AidExtractor.ExtractAid` derives a key for **0 of 254** AID-less assets. The 32-hex gate at `AidExtractor.cs:124` rejects the tenant's 56-char base64url suffix. The contract's and `decisions.md`'s named mechanism rescues nothing. **Must not be re-assumed:** that `ExtractAid` can key an AID-less Discover asset. The "21 in / 21 out" figure in `parser-proof.md` §6 used synthetic `derived-<hex>` keys, not `ExtractAid` output. | verifier-1 (probe: main thread, 2026-08-30) |
| A2 | **VALIDATED, lab tenant only** | `research/lab-tenant-identity-probe.md`: `id` present 303/303, unique 303/303. Generality is A15. The stated trade ("swapping the spooler's dedup key") never happened — `AidExtractor.cs:106-118` keeps `ExtractAid` first and falls back to `id`, so the dedup key is unchanged for any host that already had one. | verifier-1 |
| A3 | **REJECTED** | `research/lab-tenant-identity-probe.md`: "AID derivable from the combined `id` 32-hex suffix = **0**". The population split is 254 dropped at the scroller / 0 emitted-but-orphaned on this tenant. There is no orphaned sub-population to account for here. | verifier-1 |
| A4 | **NEVER-TESTED** | No measurement of staged host-page bytes at increased asset density exists in the diff or the notes. `R3_ManifestWithFullBoundarySetOfLongKeys_StaysUnderTheControlArtifactCeiling` measures the **manifest** against the 1 MiB *control-artifact* ceiling — a different artifact and a different ceiling. Partial counter-evidence, pre-existing: `FalconStagedHostPage.OpenEncodedStreamAsync` (`:61-79`) already hands the store a stream, so `MaxInMemoryObjectBytes` (8 MiB) is routed around and the `DataPipelineException` named in `decisions.md` is not the failure mode for a staged host page. Peak memory (the ~2× `MemoryStream` doubling on top of the `JsonObject` graph, documented in that same remark) remains unmeasured at 6× asset count. | verifier-1 |
| A5 | **VALIDATED** | Grep over `Flows/Findings/` returns exactly two `.Where(` predicates on hosts, both in `FalconHostSpooler.cs:225-226`; no other site filters or dereferences an AID. The design also removed the premise — `DiscoverHost.Aid` is non-null by construction and `SensorAid` is read only through the pre-existing `FalconStagedHostPage.ResolveSensorAid` (`:238-244`), which the Spotlight lane and the policy lane now share. `R4_BatchOfOnlyKeylessHosts_IssuesNoSpotlightRequest_AndStillEmitsEveryHost` drives three sensorless hosts through `ProcessAsync` without a throw. | verifier-1 |
| A6 | **VALIDATED (emission leg); coverage counters covered only indirectly** | `FalconSpotlightBatchScroller.cs` batch-end flush now walks `plan.Hosts`, incrementing `stats.HostsEmitted` once per host regardless of correlation-index membership. `R4_…` asserts 3 records from 3 sensorless hosts and `result.Success`; `MixedDiscoverPage_EmitsEveryHost_EmittedCountEqualsInputCount` asserts emitted count equals input count across a mixed page. No test asserts `totalAssetsEmitted` or the checkpoint value directly after a wholly sensorless batch; that leg rests on the flush being the single emission site plus the full suite passing. | verifier-1 |
| A7 | **NEVER-TESTED** | The assumption as written requires "a real S3 artifact from the fixed collector". No such artifact exists — see SC6 below. What exists is `tests/test_crowdstrike_assets_findings.py` **32/32** (re-run by this pass) and 303-host **synthetic** fixtures at `…/scratchpad/e2e_healthy` (hostnames `HOST-0…`, IPs `10.0.0.x`, `agent_version 7.0.0`). Synthetic fixtures at 303 hosts are not the artifact this assumption names. | verifier-1 |
| A8 | **NEVER-TESTED** | Nothing in the diff or the notes probes a consumer downstream of the parser. An 89-character combined `id` now enters `parser_output_assets` / `parser_output_findings` in a column that has only ever carried 32-character AIDs. `asset_match_key` was placed out of scope by `constraints.md`, so the one downstream join most likely to notice was deliberately not examined. This is the largest un-probed risk in the change. | verifier-1 |
| A9 | **VALIDATED** | Two independent termination belts survive the change, neither of which consults the post-filter count. (a) `FalconDiscoverHostScroller.cs:112` still normalizes an absent or empty `after` to null — untouched by the diff. (b) `FalconHostSpooler.cs:239` still breaks unconditionally on `page.Hosts.Count == 0`, and `:236` refuses to prefetch past an empty page. `SC3_…` drives a three-page fixture (hosts → empty+live-cursor → terminal) to completion. The removed clause was `hosts.Count == 0 ? null : responseAfter`, i.e. a third, redundant belt. | verifier-1 |
| A10 | **NEVER-TESTED** | Not exercised. Moot in practice: the fallback key is the combined `id` taken verbatim (`AidExtractor.cs:117`), so no ordering or canonicalisation decision arises. | verifier-1 |
| A11 | **NEVER-TESTED** | No probe of Falcon's 404-under-200 behaviour was run in this task, and the changed leg (`FetchPageAsync`'s cursor handling) was not exercised against one. | verifier-1 |
| A12 | **NEVER-TESTED as a claim; honoured as a rule** | Not re-derived. Its instruction was followed: `tests/test_e2e_correlated_real_output.py` now asserts on row counts against an independently derived ground truth, and the parser guard reports two counts rather than raising (`_report_spine_collapse`, docstring: "Spark buries the message of a driver-side exception"). | verifier-1 |
| A13 | **NEVER-TESTED** | Not exercised. `EnablePreventionPolicyEnrichment` stays `false`, so every host — sensor or not — takes the disabled/`Empty()` envelope path and no `settings_hash` is produced to compare. | verifier-1 |
| A14 | **NEVER-TESTED; moot for this change** | `/devices/entities/devices/v2` batch membership is derived from `ResolveSensorAid`, which returns null for every AID-less host, so this change adds no member to that call. Batch composition on that endpoint is byte-for-byte what it was at `baseRef`. | verifier-1 |
| A15 | **NEVER-TESTED** | Measured on one tenant (303/303 present and unique). No second tenant was probed. The assumption states its own limit and nothing moved it. | verifier-1 |
| A16 | **REJECTED as stated; two of its three legs re-validated at the corrected length** | The stated key length is wrong. Measured length is **89** characters (32-hex CID + `_` + 56-char base64url), not 65 — `FalconTwoPhaseFindingsTests.cs` `CombinedDiscoverId`/`LabTenantSampleCombinedId`, asserted against `LabTenantSampleCombinedId.Length`. Re-validated at 89: **manifest** — `R3_ManifestWithFullBoundarySetOfLongKeys_StaysUnderTheControlArtifactCeiling` measures 5,000 × 89-char keys at 465,708 B against the production `MaxControlArtifactBytes`, with a real `WriteManifestAsync` / `TryReadManifestAsync` round trip; **staged page codec** — `ResumedLeg_ReadsBackEveryStagedHostIncludingCombinedIdKeyedOnes_AndDoesNotRespoolTheFrozenGeneration` round-trips combined-id-keyed hosts through `FalconStagedHostPage`. **Checkpoint serializer: still unmeasured.** `FalconFindingsFlow.cs:362` copies `manifest.DiscoverWatermarkAids` into the checkpoint on every checkpoint write, `FalconCheckpointSerializer.cs:68` serializes it with no collector-side cap, and no test or measurement covers it. See finding 4. | verifier-1 |
| A17 | **VALIDATED — and the defect it names is real and unfixed** | `research/falcon-fql-null-field-matching.md`, live lab tenant: 303 unfiltered vs 298 under `last_seen_timestamp:>='2000-01-01T00:00:00Z'`; the same 298 at a 2020 floor, so it is null-exclusion and not a date boundary. Both `:null` and `:!null` return HTTP 400, so no widening recovers them. `FalconHostFilters.BuildLastSeenTimestampGateFilter` (`:19-23`) applies the gate whenever `baseDateUtc != DateTime.MinValue` and on every re-anchor (`FalconHostSpooler.cs:254`, `:319`). Consequence: 5 of 303 lab-tenant assets are unreachable on any base-dated run and on any run that re-anchors. Not addressed by this task. See finding 3. | verifier-1 |
| A18 | **VALIDATED (emission leg)** | `SpotlightBatchPlan.Hosts` is the batch's emission universe and the batch-end flush is the sole emission site for the terminal chunk; `stats.HostsEmitted` is incremented there and nowhere else. Asserted by `R4_…` (3 of 3) and `MixedDiscoverPage_…` (5 of 5, mixed). Same caveat as A6: `totalAssetsEmitted` and the checkpoint value are not asserted directly. | verifier-1 |

### Decision drift

| decision (`decisions.md`) | landed? |
|---|---|
| Premise fixed: a dropped asset is a defect | as decided |
| Restore the pre-July principle using the existing S3 two-phase spool | as decided — spool design untouched, see SC8 |
| Emit AID-less hosts with a unique record key, never null/`""`/sentinel | as decided |
| **AMENDED**: key is the Discover combined `id`, not `ExtractAid`'s derived value | **as decided, verified in code** — `AidExtractor.ExtractRecordKey` (`AidExtractor.cs:106-118`) returns `ExtractAid(host)` when non-blank, else `ReadString(host, "id")`. Not taken on the note's word: `FalconDiscoverHostScroller.cs:93` calls `ExtractRecordKey`, and `SC1_…` asserts the emitted `aid` equals the 89-char combined id verbatim |
| The combined `id` is a FALLBACK AFTER `ExtractAid`, never a replacement | as decided — precedence is in `ExtractRecordKey`, and `R1_HostWithThirtyTwoHexIdSuffix_KeepsItsDerivedAid_AndDoesNotSwitchToTheCombinedId` pins it through `ProcessAsync` |
| Do NOT relax the 32-hex gate in `TryExtractAidFromCombinedHostId` | as decided — that method is byte-identical to `baseRef` |
| Wire shape does not change | as decided — `FalconCorrelatedRecord.cs` is not in the diff |
| Truncation fix ships regardless of the drop fix | as decided — `FalconDiscoverHostScroller.cs` returns `responseAfter` unconditionally |
| Parser claim settled by execution against a real artifact | **NOT landed.** See SC6 and finding 1 |
| Defensive non-silent guard in the parser, no output-contract change | as decided — `_report_spine_collapse` is report-only; no column added, renamed or retyped |
| `EnablePreventionPolicyEnrichment` stays `false` | as decided — `FalconCollectorConfiguration.cs:179` unchanged, file not in the diff |
| A collision must be detected and surfaced, never absorbed | landed with a documented cost: the colliding host is **still not staged**, and `unresolved` deliberately does not downgrade the freeze state, so the only operator surface is a `LogError`. See finding 5 |

---

## 2. Attention Item Disposition

| id | final disposition | evidence |
|---|---|---|
| R1 | handled | `FalconCorrelatedFindingsTests.cs:2050` `R1_HostWithThirtyTwoHexIdSuffix_KeepsItsDerivedAid_AndDoesNotSwitchToTheCombinedId` exists and passed in the verifier's full-suite run. Production path confirmed at `AidExtractor.cs:114-118` — `ExtractAid` first, `id` only on blank. |
| R2 | handled | `FalconTwoPhaseFindingsTests.cs:4108` `R2_TwoDistinctHostsDerivingTheSameKey_AreDetectedAndSurfaced_NotSilentlyMerged` exists and passed. Production path: `FalconHostSpooler.SpoolKeyClaims.TryClaim` (`:395-425`) replaces `seenAids.Add(h.Aid)`; it distinguishes a replay (same key, same `host.id` hash → silent `false`) from a collision (same key, different hash → `Collisions++`, `LogError`, `false`) and carries the count into `SpooledFreeze`'s `unresolved`. The guard genuinely separates the two conditions the set-add conflated; it does not merely relocate the silence. Its residual cost is recorded as finding 5. |
| R3 | handled | `FalconTwoPhaseFindingsTests.cs:4231` `R3_ManifestWithFullBoundarySetOfLongKeys_StaysUnderTheControlArtifactCeiling` exists and passed. It reads `FalconHostSpooler.MaxBoundaryAids` and `guarded.Options.MaxControlArtifactBytes` from production rather than restating them, and performs a real write + read-back. Note the manifest is only one of the two artifacts that carry this list — see R-adjacent finding 4. |
| R4 | handled | `FalconCorrelatedFindingsTests.cs:1991` `R4_BatchOfOnlyKeylessHosts_IssuesNoSpotlightRequest_AndStillEmitsEveryHost` exists and passed. It routes the Spotlight endpoint deliberately and asserts on the recorded request count, plus a separate assertion that no URL contains `aid:[`. Production path: `FalconSpotlightBatchScroller.cs` `if (accumulators.Count > 0)` wraps the whole scroll. |
| R5 | handled | The named artifact exists — ground truth in `tests/test_e2e_correlated_real_output.py` is now `host_ids` counted on `host["id"]`, with a `len(record_aids) == len(host_ids)` collision assertion ahead of the row-count assertions — and **this pass ran it against both fixtures to confirm it discriminates**, which the execution notes did not do. Against `…/scratchpad/e2e_healthy` (303 distinct hosts, 303 distinct aids): **1 passed**. Against `…/scratchpad/e2e_degenerate` (303 distinct hosts, 50 distinct aids): **1 failed**, `AssertionError: 303 distinct hosts emitted only 50 distinct aids`, with the parser guard also firing `ASSET COLLAPSE — distinct input hosts=303 but surviving asset rows=50; 253 asset(s) were merged away`. The old `aid`-keyed ground truth passed on that same degenerate input. The tripwire is real. Separately: its *input* in both runs was a synthetic fixture, which is why SC6 is still unmet — that is a missing artifact, not a missing or unrun test. |

---

## 3. Success Criteria

| SC | verdict | evidence |
|---|---|---|
| SC1 | **met** | `SC1_AidLessDiscoverAsset_IsEmitted_KeyedByItsCombinedId_WithAnEmptyFindingsArray` (`:1886`), driven through `FalconCollector.ProcessAsync` — real scroller, spooler, staged codec, Spotlight pump. Asserts `aid` == the 89-char combined id verbatim, `findings` length 0, `chunk` 0, `isLastChunk` true. Fixture is the probe's real vendor shape, not a hand-built `DiscoverHost`. |
| SC2 | **met** | `SC2_DiscoverPageOfOnlyAidLessHosts_DoesNotTerminateTheScroll_TheAfterCursorIsFollowed` (`:1933`) asserts `after=cursor-1` appears in the recorded URLs and the page-2 managed host reaches the output. |
| SC3 | **met, literally** | `SC3_SpoolThatStoppedWhileTheVendorCursorWasStillLive_IsNotRecordedAsACompletedFreeze` (`:4011`). Implementation takes the degraded-freeze branch: `FalconHostSpooler.cs:242` sets `scrollTruncated = page.After is not null`, `SpooledFreeze` then passes `units = stagedPages + 1` against `unitsCovered = stagedPages`, and `FalconStageLedgerEntry.Finished` selects `Degraded`. Manifest JSON shape unchanged. Caveat in finding 2: nothing in the collector reads `Freeze.State`, so this is a durable record, not a control signal. |
| SC4 | **met** | `R4_…` — see the attention table. |
| SC5 | **met** | `R2_…` — see the attention table. |
| SC6 | **NOT MET** | See finding 1. |
| SC7 | **met** | See the suite result below. |
| SC8 | **met** | Proven by diff, four ways. (a) *Checkpoint format*: `git diff bbb044b9 --stat` touches seven production files, none under `Recovery/`; `FalconCheckpointSerializer.cs`, `FalconCheckpointState.cs`, `FalconCheckpointKeys.cs`, `FalconCheckpointDeserializer.cs` are byte-identical. (b) *Manifest structure*: a diff of every `JsonPropertyName` line in `FalconPhase1Manifest.cs` against `baseRef` differs only in line numbers — same 14 properties, same names, same order, same types. `SpooledFreeze` is a new static factory, not a field; `CompletedFreeze` is preserved and now delegates to it with `scrollTruncated: false, unaccountedHosts: 0`, so its output is identical to `baseRef`'s. (c) *Batching geometry*: `hostsPerPage`, `AidBatchSize`, `freshHosts.Chunk(hostsPerPage)` and `FalconFrozenKeyList.cs:254` `hosts.Chunk(manifest.AidBatchSize)` are unchanged; `FalconFrozenKeyList.cs` is not in the diff. (d) *`EnablePreventionPolicyEnrichment`*: `FalconCollectorConfiguration.cs:179` still `= false`; the file is not in the diff. (e) *Parser output columns*: the parser diff adds one module constant, one call site, and one static report-only method; `process()` / `post_process()` and the emitted frames are untouched. |

**Suite result (verifier's own run, full project, unfiltered, no shortened timeout):**
see the tail of this file.

---

## 4. Findings, most severe first

### 1. SC6 is unmet, and the live run in flight cannot satisfy it

No artifact from the fixed collector exists. `…/scratchpad/e2e-out/` contains a runner log and an
empty `wire-bodies/` directory — zero `findings_*.json`. The run failed three retries out of three
and its process is gone.

The cause is not the task's change, but it does mean the run was never going to help:

```
Falcon Discover freeze already complete for generation gen_08df066dd3f35b04; skipping the spool
  (gen_08df066dd3f35b04 (hosts=49, pages=1, …))
System.InvalidOperationException: Falcon generation 'gen_08df066dd3f35b04' cannot be shown to share
  this leg's Prevention policy contract. Its edge stage is [completed … items=49 …], i.e. resolved
  with enrichment ON, while this leg is configured with enrichment OFF.
```

The local staging area holds a **pre-fix** generation with `hosts=49` — the 49-managed-asset freeze
this task exists to replace. The run resumed it, skipped the spool entirely, and then died on a
stale policy-contract mismatch. Even had the exception not fired, the artifact would have been the
old 49-host inventory. **The staging generation has to be cleared before any run can produce the
303-host artifact SC6 needs.**

What stands in SC6's place today is `tests/test_crowdstrike_assets_findings.py` 32/32 (re-run by
this pass) plus 303-host **synthetic** fixtures under `…/scratchpad/e2e_healthy` and
`e2e_degenerate` — `HOST-0`, `10.0.0.0`, `agent_version 7.0.0`. Those are good tests. They are not
the criterion. `constraints.md` is explicit that the parser claim is established by running the
parser over a real collector artifact and that reasoning is not evidence; the same standard applies
to a fixture the task authored itself. The operator's "parser writes ALL of it into postgres" is
therefore **not proven**.

The distance to closing it is short, and worth stating precisely so it is not over-read. The R5
tripwire is verified working (see the attention table), the parser handles a 303-host correlated
lane with 89-character keys end to end, and the guard fires correctly on a collapse. What is missing
is one real artifact. Clearing the local staging generation and re-running the collector against the
lab tenant should produce it; SC6 then reduces to pointing `E2E_CORRELATED_DIR` at the output.

### 2. The one remaining silent drop is still silent — the scroller's logger is never wired

`FalconDiscoverHostScroller.cs:96-103` counts hosts with neither an AID nor an `id` and logs:

```csharp
_logger?.LogWarning(
    "Falcon Discover: {KeylessHosts} host(s) on this page carry neither an AID nor an id …");
```

`_logger` comes from an optional constructor parameter added by this change. The single production
construction site is `FalconFindingsFlow.cs:141`:

```csharp
var scroller = new FalconDiscoverHostScroller(_http);
```

No logger. `_logger` is null in every production run, `_logger?.LogWarning` is a no-op, and
`keylessHosts` is a local that is discarded at the end of the method. Nothing else reads it, and it
does not reach the manifest or the stats bag.

So the code's own stated guarantee — *"Counted rather than dropped in silence"* — does not hold.
The class of asset is expected to be empty (the probe found `id` on 303/303), which is why no test
caught it, but "expected to be empty" is exactly the reasoning that produced the original defect.
`FalconFindingsFlow` was in no worker's file set, so the wiring fell through a decomposition seam.
One-line fix: `new FalconDiscoverHostScroller(_http, _logger)`.

### 3. A17: 5 of 303 lab-tenant assets remain unreachable, inside the operator's own premise

Confirmed live, not speculative (`research/falcon-fql-null-field-matching.md`). Any run with a base
date, and any run that re-anchors (cursor expiry, mid-scroll 5xx, depth cap), applies
`last_seen_timestamp:>='<floor>'`, which does not match a null `last_seen_timestamp`. FQL rejects
both `:null` and `:!null` outright, so no widening recovers them.

Note this is the *normal* path, not an edge: the live runner's own log shows
`BaseDateUtc="2025-08-30T11:03:22Z"`, i.e. a gated run.

This was found by this task's research, correctly judged out of scope (fixing it needs a different
traversal, and `constraints.md` puts the spool's design off-limits), and recorded for a successor.
That is the right call. It is recorded here so it is not mistaken for coverage the work delivers:
"absent a user FQL, no asset Falcon returns may be dropped at any stage" is still violated for this
population. Roughly 1.6% of the lab tenant.

### 4. The boundary-key list reaches a second, unmeasured ceiling — the checkpoint

R3 measured the manifest and passed. The same list is also copied into the checkpoint on **every
checkpoint write**:

- `FalconFindingsFlow.cs:362` — `manifest.DiscoverWatermarkAids ?? Array.Empty<string>()`
- `FalconFindingsCheckpointWriter.cs:73` — `DiscoverWatermarkAids = discoverWatermarkAids.ToList()`
- `FalconCheckpointSerializer.cs:68` — `JsonSerializer.Serialize(state.DiscoverWatermarkAids …)`, no
  collector-side cap

At the `MaxBoundaryAids = 5000` cap and 89-character keys that is ~465 KB of JSON in a single
checkpoint value, up from ~180 KB at 32 characters — a 2.6× increase against a platform limit
nobody has measured, written far more often than the manifest is. `internal-recon.md` L13 flagged
exactly this ("where the collector imposes no cap of its own") and A16 carried it; the manifest leg
was closed and the checkpoint leg was not. Not a regression introduced by the change — the list was
always copied — but the change is what made the entries 2.8× wider.

### 5. The collision guard converts a silent loss into a logged one — correctly, but the surface is thin

R2's guard does what it claims: `TryClaim` holds a hashed `host.id` beside each claimed key and
separates a replay from a genuine collision, which a `HashSet.Add` structurally cannot. The
colliding host is then **not staged** — an asset is still lost. That is forced by the contract's own
uniqueness constraint (`aid` unique per record, because `Window.partitionBy("aid")`), and the code
argues the point well: staging it would lose the same asset one layer down, in the pump's
last-writer-wins map and again in Spark, while inflating `hostCount`.

Two things narrow the surface, both worth knowing:

- `unresolved` deliberately does not downgrade the freeze state
  (`FalconPhase1Manifest.cs:91-97`), so a colliding run still writes `Completed` and still reports
  `Success`. The only operator-visible signal is a single `LogError` line.
- Identity falls back to `host.Aid` when a host carries no `id`
  (`"Unprovable is not the same as proven"`), so two distinct hosts that both state one AID and both
  lack an `id` would read as a replay and merge silently. Documented in place; narrow, since the
  probe found `id` on 303/303.

Recorded, not objected to. The R2 test asserts the weaker of the two available bars ("counted,
logged or raised — any of the three passes"), which matches what shipped.

### 6. A degraded freeze changes nothing downstream

`grep` over the whole collector finds no consumer of `FalconPhase1Manifest.Freeze` or
`Freeze.State`; `FalconStagingArea.TryFindCompletedGenerationAsync` gates on the manifest's
*existence*. So a truncated spool now records `Degraded`, logs a warning, and then proceeds to
publish a partial inventory under a successful run exactly as before. SC3 asks that the freeze not
be *recorded* as completed, and that is met — the manifest is honest. But an operator only learns
of the truncation from logs. `internal-recon.md` L3/L6 weighed the alternative (throw before the
manifest write) and rejected it as a one-way door that ends the run permanently, citing a real
production correlation. The trade is sound and was made deliberately; it is recorded so the residual
is not read as more than it is.

### 7. W3's 254-line restructure is justified, not scope creep

Checked hunk by hunk. Roughly 190 of the 254 lines are re-indentation from wrapping the existing
scroll loop in `if (accumulators.Count > 0)` — required by R4 and not achievable without it. The
genuine semantic edits are four, all inside scope: the empty-batch guard; keying the correlation
index by `ResolveSensorAid` instead of `host.Aid`; emitting under `accumulator.RecordAid` instead
of the dictionary key; and driving the batch-end flush off `plan.Hosts` instead of the index. The
`SpotlightBatchPlan` record is the minimum shape that carries two populations where one was carried
before. No unrelated refactoring rode along.

One behavioural delta worth naming: `BuildBatchPlan` appends to `hosts` unconditionally, where the
removed code was `accumulators[host.Aid] = …` with an explicit comment that last-writer-wins made a
repeated key "a harmless no-op". A staged page that repeated a record key would now emit **two**
records with `isLastChunk: true` under one `aid` instead of one. The spooler's run-wide claim makes
that unreachable in practice, so this is a note rather than a defect — but the case the old comment
named is no longer handled.

### 8. Minor: the parser guard adds a Spark pass on every run

`_report_spine_collapse` runs a `countDistinct` aggregation over `records_df` on every invocation,
not only when a collapse is suspected. Cheap relative to the parse, and the guard cannot be derived
from a cached count without defeating its own independence from `aid`. Noted for cost, not objected
to.

---

## 5. Bottom line

The two named defects are genuinely fixed, and fixed at the right seam. The drop is gone at
`FalconDiscoverHostScroller.cs:93`, the truncation clause is gone at `:127`, both are pinned by
tests that run through `FalconCollector.ProcessAsync` against the probe's real vendor shape, and the
Spotlight lane now keeps the record key and the sensor id properly separate so no combined `id` can
reach a vendor endpoint. R1's regression trap — silently re-identifying every host that gets a key
from the 32-hex path today — was correctly anticipated and is pinned by its own test. SC8 holds
under a hunk-level check: nothing in the spool's frozen surface moved.

Two things stop this being finishable as it stands. **SC6 is unmet and its evidence path is
blocked** — the live run resumes a pre-fix 49-host generation and dies on a stale policy contract,
so the staging generation must be cleared before any artifact can be produced; until then the
operator's "parser writes ALL of it into postgres" rests on fixtures the task wrote itself.
**The scroller's keyless-host warning never reaches a logger**, so the single remaining exclusion
path is still silent — a one-line fix at `FalconFindingsFlow.cs:141`, and the exact failure mode the
task exists to remove.

Beyond that, A17 is a measured, unfixed 1.6% coverage hole inside the contract's own premise, and
A8 — 89-character keys arriving in columns that have only ever held 32-character AIDs — is the
largest thing nobody looked at.
