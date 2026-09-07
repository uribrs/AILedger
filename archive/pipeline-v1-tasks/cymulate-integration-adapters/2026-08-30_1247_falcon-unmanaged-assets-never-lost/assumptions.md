# Assumptions

Status vocabulary: OPEN / VALIDATED / REJECTED / NEVER-TESTED. Only the verifier writes a terminal
status, and only with an actor and a citation.

**All statuses below are TERMINAL.** Written by `verifier-1` on 2026-08-30 and re-disposed by
`verifier-2` on the same day after the four post-review repairs. Full reasoning is in
`review/verifier-1.md` §1 and `review/verifier-2.md` §1; where verifier-2 moved a status the entry
says so and names its own evidence. NEVER-TESTED is the default: it means no evidence moved the
assumption, not that it is false.

Moved by verifier-2: **A4** NEVER-TESTED -> VALIDATED (a real staged page was measured), **A7**
NEVER-TESTED -> VALIDATED (the artifact exists and was re-run), **A16** checkpoint leg now measured.
Framing corrected on **A8**. Strengthened with production evidence: A2, A5, A6, A9, A13, A17, A18.

## Task assumptions

- **A1** — **REJECTED** (superseded before any code was written) — `AidExtractor.ExtractAid` does
  NOT derive a unique identifier for AID-less Discover assets.
  Citation: `research/lab-tenant-identity-probe.md` — full inventory of 303 lab-tenant assets;
  `ExtractAid` derives a key for **0 of 254** AID-less assets, because the 32-hex suffix gate at
  `AidExtractor.cs:124` rejects the tenant's 56-char base64url token.
  **Must not be re-assumed without new evidence:** that `ExtractAid` can key an AID-less Discover
  asset. The prior "1 managed + 20 unenriched → 21 unique AIDs" figure (`parser-proof.md` §6) used
  synthetic `derived-<hex>` keys, not `ExtractAid` output, and cannot be cited for this claim.
  actor: verifier-1

- **A2** — **VALIDATED, lab tenant only** — the Discover combined `id` is present on every asset the
  vendor returned. Citation: `research/lab-tenant-identity-probe.md` — `id` present 303/303, unique
  303/303. Generality beyond this tenant is A15. The stated trade never materialised: the spooler's
  dedup key was NOT swapped — `AidExtractor.ExtractRecordKey` (`AidExtractor.cs:106-118`) keeps
  `ExtractAid` first and falls back to `id` only when it is blank. actor: verifier-1

- **A3** — **REJECTED** — none of the lost assets were already being emitted under a derived AID.
  Citation: `research/lab-tenant-identity-probe.md`, "AID derivable from the combined `id` 32-hex
  suffix = **0**". The split is 254 dropped at the scroller / 0 emitted-but-orphaned on this tenant;
  there is no orphaned sub-population to reconcile. actor: verifier-1

- **A4** — **VALIDATED at lab-tenant density** (moved from NEVER-TESTED by verifier-2) — a real
  staged page now exists and was measured:
  `s3://cybi-data/Uri-Tests/falcon-with-policies/nolost-verify-001/_staging/gen_08df0686f42bb2ae/hosts_000000.json`
  is **628,500 bytes for 298 hosts** at `hostsPerPage = 1000`, i.e. ~2,110 bytes/host, so a full
  1,000-host page is ~2.1 MB — about 26% of `MaxInMemoryObjectBytes` (8 MiB), and the page is
  streamed rather than buffered in any case. The `DataPipelineException` `decisions.md` feared is not
  reachable at this density. Caveat: envelope size is tenant-dependent, so this bounds the lab tenant
  and not every tenant. actor: verifier-2

  verifier-1's original disposition, retained for the reasoning: NEVER-TESTED — staged page byte size
  under the increased asset count is unmeasured.
  `R3_ManifestWithFullBoundarySetOfLongKeys_StaysUnderTheControlArtifactCeiling` measures the
  MANIFEST against the 1 MiB control-artifact ceiling — a different artifact and a different
  ceiling. Partial pre-existing counter-evidence: `FalconStagedHostPage.OpenEncodedStreamAsync`
  (`:61-79`) hands the store a stream, so `MaxInMemoryObjectBytes` (8 MiB) is routed around and the
  `DataPipelineException` named in `decisions.md` is not the failure mode for a staged host page.
  Peak memory (the ~2x `MemoryStream` doubling that remark itself documents) remains unmeasured at
  6x asset count. actor: verifier-1

- **A5** — **VALIDATED** — no Spotlight-side site other than the former `SeedAccumulators` assumed a
  non-null AID. Citation: grep over `Flows/Findings/` returns exactly two host predicates, both at
  `FalconHostSpooler.cs:225-226`; `DiscoverHost.Aid` is now non-null by construction and `SensorAid`
  is read only through the pre-existing `FalconStagedHostPage.ResolveSensorAid` (`:238-244`), shared
  by the Spotlight and policy lanes. `R4_BatchOfOnlyKeylessHosts_IssuesNoSpotlightRequest_AndStillEmitsEveryHost`
  drives three sensorless hosts through `ProcessAsync` without a throw. actor: verifier-1

- **A6** — **VALIDATED (emission leg)** — a wholly AID-less aid-batch skips its Spotlight request
  without losing hosts. Citation: the batch-end flush in `FalconSpotlightBatchScroller.cs` walks
  `plan.Hosts` and increments `stats.HostsEmitted` once per host regardless of correlation-index
  membership; `R4_…` asserts 3 records from 3 sensorless hosts,
  `MixedDiscoverPage_EmitsEveryHost_EmittedCountEqualsInputCount` asserts emitted == input on a
  mixed page. Caveat: no test asserts `totalAssetsEmitted` or the checkpoint value directly after a
  wholly sensorless batch; that leg rests on the flush being the single emission site plus the full
  suite passing. **verifier-2 closes that caveat empirically:** on the live run
  `collectors.done.totalAssetsCollected = 298`, the manifest's `hostCount = 298`, and the emitted
  objects hold 298 distinct `aid`s — the counter, the ledger and the output agree on a real tenant
  that is 84% sensorless. actor: verifier-1, verifier-2

- **A7** — **VALIDATED** (moved from NEVER-TESTED by verifier-2) — the parser IS proven against a
  real S3 artifact from the fixed collector. The artifact was never deleted from S3
  (`s3://cybi-data/Uri-Tests/falcon-with-policies/nolost-verify-001/`, 6 objects, 301.3 MiB);
  verifier-2 re-derived its identity mapping (338 records, 0 null/empty `aid`, 298 distinct `aid`,
  298 distinct `host.id`, 298 distinct pairs) and re-ran
  `tests/test_e2e_correlated_real_output.py` over it — **1 passed**, 298 distinct input hosts to 298
  asset rows, asserted on counts. Bound: the collector binary that produced the artifact predates
  W2's empty-page repair and the logger wiring, neither of which can alter that run's output (the
  spool staged one page and entered no empty-page arm). See `review/verifier-2.md` finding 6.
  actor: verifier-2

- **A8** — **NEVER-TESTED** — no consumer downstream of the parser was probed, so the status stands.
  **verifier-1's framing is corrected by verifier-2:** the stated risk (an 89-character id entering a
  column that has only ever carried 32-character AIDs) is not what the code does. The emitted asset's
  `aid` column is fed from the host's Discover `id` — `crowdstrikeAssets.py:48` maps
  `"aid": {"path": "id"}` and the correlated handler inherits that mapping verbatim — so that column
  has always carried the 89-char combined id, on both lanes, before and after this change. The record
  key reaches exactly one output: the policy projection's `asset_match_key`
  (`crowdstrike_policy_projection.py:70,263`), which filters to hosts carrying a prevention
  assignment — sensor hosts, 32-char AIDs — and is inert entirely while
  `EnablePreventionPolicyEnrichment` is `false`. Residual risk is row count (298 vs 49), not key
  width. actor: verifier-1, corrected by verifier-2

- **A9** — **VALIDATED** — returning the vendor's `after` unconditionally cannot unbound the scroll.
  Citation: two independent termination belts survive and neither consults the post-filter count —
  `FalconDiscoverHostScroller.cs:112` still normalizes an absent or empty `after` to null (untouched
  by the diff), and `FalconHostSpooler.cs:239` still breaks unconditionally on
  `page.Hosts.Count == 0` while `:236` refuses to prefetch past an empty page.
  `SC3_SpoolThatStoppedWhileTheVendorCursorWasStillLive_IsNotRecordedAsACompletedFreeze` drives a
  three-page fixture to termination. The removed clause was a third, redundant belt.
  **verifier-2 addition:** the spool now follows an empty-but-live page, so the belt this assumption
  names is load-bearing — it is bounded by `FalconHostSpooler.MaxConsecutiveEmptyLivePages = 5`
  (`:115`, decision at `:271-278`) and
  `SC3_EmptyLivePagesPastTheBound_StopTheScroll_AndTheFreezeIsNotRecordedAsCompleted` terminates
  against a fixture whose every cursor answers empty and names the next one forever. An unbounded
  scroll is not reachable. actor: verifier-1, verifier-2

## Amended after the live probe

- **A15** — **NEVER-TESTED** — measured on one tenant only (303/303 present and unique,
  `research/lab-tenant-identity-probe.md`). No second tenant was probed; nothing moved the
  generality claim. actor: verifier-1

- **A16** — **REJECTED as stated; two of three legs re-validated at the corrected length.** The
  stated 65-character key is wrong: the measured key is **89** characters (32-hex CID + `_` +
  56-char base64url), pinned in test by `LabTenantSampleCombinedId.Length`. Re-validated at 89:
  **manifest** — `R3_ManifestWithFullBoundarySetOfLongKeys_StaysUnderTheControlArtifactCeiling`
  measures 5,000 x 89-char keys at 465,708 B against the production `MaxControlArtifactBytes`, with
  a real `WriteManifestAsync` / `TryReadManifestAsync` round trip; **staged page codec** —
  `ResumedLeg_ReadsBackEveryStagedHostIncludingCombinedIdKeyedOnes_AndDoesNotRespoolTheFrozenGeneration`
  round-trips combined-id-keyed hosts through `FalconStagedHostPage`. **Checkpoint serializer leg:
  still unmeasured.** `FalconFindingsFlow.cs:362` -> `FalconFindingsCheckpointWriter.cs:73` ->
  `FalconCheckpointSerializer.cs:68` copies the same list into the checkpoint on every checkpoint
  write with no collector-side cap — ~465 KB at the cap, up from ~180 KB, against an unmeasured
  platform limit. See `review/verifier-1.md` finding 4.
  **verifier-2: the checkpoint leg is now MEASURED, still not capped.**
  `R3b_CheckpointCarryingTheSameBoundarySet_HasNoCeilingOfItsOwn_SoItsSerializedSizeIsPinnedHere`
  (`FalconTwoPhaseFindingsTests.cs:4588`) builds the payload field-for-field as
  `FalconFindingsCheckpointWriter.OnBatchPublished` does, serializes through the real
  `FalconCheckpointHelper.SaveFindingsState`, measures **460,022 bytes** for the boundary set inside
  a 460,397-byte payload, and round-trips it back intact. No production cap was added. New fact for
  whoever picks it up: `FalconCheckpointState.DiscoverWatermarkAids` is **write-only in this repo** —
  serialized, deserialized, and read by no decision — so a cap or removal is cheap and safe. See
  `review/verifier-2.md` finding 4. actor: verifier-1, verifier-2

- **A17** — **VALIDATED — and the defect it names is real and unfixed.** Citation:
  `research/falcon-fql-null-field-matching.md`, live lab tenant — 303 unfiltered vs 298 under
  `last_seen_timestamp:>='2000-01-01T00:00:00Z'`, the same 298 at a 2020 floor (null-exclusion, not
  a date boundary); both `:null` and `:!null` return HTTP 400, so no widening recovers them.
  `FalconHostFilters.BuildLastSeenTimestampGateFilter` (`:19-23`) applies the gate whenever
  `baseDateUtc != DateTime.MinValue` and on every re-anchor (`FalconHostSpooler.cs:254`, `:319`).
  Consequence: 5 of 303 lab-tenant assets are unreachable on any base-dated run and on any run that
  re-anchors — the normal path, not an edge (the live runner log shows
  `BaseDateUtc="2025-08-30T11:03:22Z"`). Correctly judged out of scope; carried forward for a
  successor. **verifier-2: this is no longer an inference from a probe.** The fixed collector's live
  run reported `totalAssetsCollected: 298` and froze `hostCount: 298` with `Truncated=false` and
  `KeyCollisions=0` — a run that believed it was complete and was 5 assets short.
  actor: verifier-1, verifier-2

- **A18** — **VALIDATED (emission leg)** — holding AID-less hosts out of the accumulators and routing
  them to the batch-end flush preserves `stats.HostsEmitted`. Citation: `SpotlightBatchPlan.Hosts`
  is the emission universe and the batch-end flush is the sole site that increments `HostsEmitted`;
  asserted by `R4_…` (3 of 3) and `MixedDiscoverPage_…` (5 of 5, mixed). Same caveat as A6:
  `totalAssetsEmitted` and the checkpoint are not asserted directly — closed empirically by
  verifier-2's live-run reconciliation (298 = 298 = 298). actor: verifier-1, verifier-2

## Prior Art

Tags searched: `falcon`, `entity-ids`, `vendor-field-presence`, `spark`, `asset-loss`, `unmanaged`,
`correlated`, `two-phase`. Matched 33 surviving rows of 64 across the broad tag set; `L-57acd253` was
head-filtered as superseded. Triaged the newest and the tag-specific matches, seeded the relevant
ones below, and followed one pointer
(`tasks/cymulate-integration-adapters/2026-07-02_1705_falcon-correlated-findings-redesign/`).
Rows dropped as not bearing on this task: the 2026-08-19 Tenable/Postgres load batch, the
IntegrationServiceBus package-drift batch, the rate-limit and memory-bound rows.

- **A10** — **NEVER-TESTED** — Falcon remediation entity ID stability was not exercised. Moot in
  practice: the fallback key is the combined `id` taken verbatim (`AidExtractor.cs:117`), so no
  ordering or canonicalisation decision arises. source: lessons.md#L-c9239f9c. actor: verifier-1
- **A11** — **NEVER-TESTED** — no probe of Falcon's 404-under-a-200-header behaviour was run, and the
  changed leg (`FetchPageAsync`'s cursor handling) was not exercised against one.
  source: lessons.md#L-33ce251b, #L-882455c1. actor: verifier-1
- **A12** — **NEVER-TESTED as a claim; honoured as a rule.** Not re-derived. Its instruction was
  followed: `tests/test_e2e_correlated_real_output.py` now asserts on row counts against an
  independently derived ground truth, and the parser guard `_report_spine_collapse` reports two
  counts rather than raising, on the stated grounds that Spark buries a driver-side exception.
  source: lessons.md#L-9a2a1c4d. actor: verifier-1
- **A13** — **NEVER-TESTED** — not exercised. `EnablePreventionPolicyEnrichment` stays `false`, so
  every host takes the disabled/`Empty()` envelope path and no `settings_hash` is produced to
  compare. Confirmed in production by verifier-2: the live manifest records `policyFetch.state` and
  `policyEdges.state` both `"disabled"`. source: lessons.md#L-00f62476. actor: verifier-1, verifier-2
- **A14** — **NEVER-TESTED; moot for this change.** `/devices/entities/devices/v2` batch membership
  is derived from `ResolveSensorAid`, which returns null for every AID-less host, so this change
  adds no member to that call — batch composition on that endpoint is what it was at `baseRef`.
  source: lessons.md#L-16fed2de, #L-49fe697c. actor: verifier-1

## Recorded context, not an assumption

The July 2026 redesign deliberately dropped the separate `assets_*.json` lane from `CollectFindings`
("envelopes are the asset spine", user-confirmed) and set the record schema to
`{aid, chunk, isLastChunk, findingsInChunk, host, findings[]}` with `aid` as the key. That is why an
AID-less host became unrepresentable and the drop followed mechanically.
source: `~/Dev/AILedger/tasks/cymulate-integration-adapters/2026-07-02_1705_falcon-correlated-findings-redesign/decisions.md`
