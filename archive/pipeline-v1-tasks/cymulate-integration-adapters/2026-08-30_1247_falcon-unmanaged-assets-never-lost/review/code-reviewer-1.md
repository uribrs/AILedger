# Code review — Falcon unmanaged assets / correlated findings

Scope: `git diff bbb044b9` in `cymulate-integration-adapters` (7 source files + 2 test files) and
`git diff` on `master` in `cymulate-integration-parsers` (1 source file + 2 test files).

Verification performed: `dotnet build Cymulate.Integration.Adapters.sln` — succeeded, 0 warnings,
0 errors. `.venv/bin/python -m pytest tests/test_crowdstrike_assets_findings.py` — 32 passed. The
.NET suite was not run, per instruction.

Overall: the central change is sound. The key derivation does what its comments claim, the
ordering is right, only `ExtractSensorAid`-derived values reach a vendor endpoint, the new shared
structures are per-batch and single-consumer, and the 89-character key touches no slicing, hashing,
case-folding or URL that would truncate or mangle it. The findings below are one real regression of
the change's own stated goal, three data-loss paths that survive the change (one newly created, two
pre-existing but now load-bearing), one cost, and some inaccurate comments.

---

## 1. HIGH — the keyless-host counter can never log. The drop it replaced is still silent.

`Collectors/FalconCollector/Flows/Findings/Correlated/FalconDiscoverHostScroller.cs:46-50, 93-101,
108-115`
`Collectors/FalconCollector/Flows/Findings/FalconFindingsFlow.cs:141`

The scroller gained an optional logger and a `keylessHosts` counter, and the comment at
`:95-97` states the case plainly:

> Counted rather than dropped in silence — the probe found an id on 303 of 303 hosts, so a non-zero
> count here means the vendor's record shape changed.

The warning is emitted through `_logger?.LogWarning(...)`. `_logger` is only ever non-null if a
logger is passed to the constructor, and the sole production construction site is:

```csharp
var scroller = new FalconDiscoverHostScroller(_http);   // FalconFindingsFlow.cs:141
```

`FalconFindingsFlow` has `_logger` in scope on that very line and does not pass it. So in every
production run `_logger` is null, `keylessHosts` is incremented and then discarded when the method
returns, and the exclusion is exactly as invisible as the `continue` it was written to replace.

Concrete failure: CrowdStrike ships a Discover change in which a class of entity omits `id`
(or renames it — the field is read by literal name at `AidExtractor.ReadString(host, "id")`). Every
such asset is parsed, found unkeyable, and dropped. `totalAssetsCollected` falls, the run returns
`Success`, and there is no warning line, no counter in the manifest, no ledger field, and nothing in
the checkpoint. The operator sees a smaller tenant and no reason for it. This is the same failure
mode as the 254-of-303 drop, in the same method, at the same `continue`.

Secondary, and worth fixing at the same time: even with the logger wired, `keylessHosts` is
page-local and never reaches the ledger, while `SpoolKeyClaims.Collisions` is carried durably into
`FalconStageLedgerEntry.Unresolved`. Two exclusions of the same kind, two different levels of
durability. `FetchPageAsync` would need to return the count (or the scroller to own a run-total) for
the spool to fold it into the freeze the way it folds collisions.

---

## 2. MEDIUM — a truncated freeze publishes a truncated inventory and reports success.

`Collectors/FalconCollector/Flows/Findings/TwoPhase/FalconHostSpooler.cs:239-256`
`Collectors/FalconCollector/Flows/Findings/TwoPhase/FalconPhase1Manifest.cs:294-337`
`Collectors/FalconCollector/Flows/Findings/FalconFindingsFlow.cs:595-624, 705`

SC3 correctly identifies that an empty Discover page carrying a live `after` token is the vendor
saying "keep going", and correctly stops recording that as a completed freeze. But *recording* is
all it does. The spool still breaks out of the loop, the flow proceeds to Phase 2 over the partial
frozen list, and the run publishes and returns `Success`. `FalconPhase1Manifest.cs:307-309` states
the consequence itself — "nothing in the flow gates on the freeze's state" — and that is confirmed:
the only ledger gate in the flow is `manifest.Edges.IsTraversed` (`FalconFindingsFlow.cs:705`).
`Freeze` is read nowhere but `ToString()`.

Concrete failure: a 97,332-host tenant. Discover page 1 returns 1,000 hosts and `after:"c1"`; the
page behind `c1` comes back with `"resources": []` and `after:"c2"` (a transient shard/index
condition, which is precisely why the case is being handled at all). The spool stops. 96,000 hosts
behind `c2` are never fetched. The platform receives ~1,000 assets where it had 97,332, and every
missing asset reads downstream as deleted. The evidence that this happened is one `LogWarning` and
a `"state": "degraded"` string in an artifact no code reads.

Mitigation that limits the blast radius: the staging area lives "inside the run's own `storageUrl`"
(`FalconStagingPaths.cs:5-6`), so the truncated generation is not adopted by the next scheduled run
— it costs one run's output, not permanently. That is worth knowing but does not make the published
output correct.

The SC3 test cannot catch this: it accepts "a degraded freeze line" as one of three passing shapes
by construction, so the weakest of the three is what shipped. Either follow the live cursor with a
bounded empty-page allowance (the loop already has `MaxConsecutiveServerErrorReanchors` as a
precedent for bounding a retry-ish arm), or return `PartialResult`/fail so the truncation is not
published as a complete inventory.

---

## 3. MEDIUM — a host that gains a sensor AID mid-spool is now staged twice, and no guard sees it.

`Collectors/FalconCollector/Flows/Findings/TwoPhase/FalconHostSpooler.cs:377-410`
`libs/packages/parsers/deprecated/crowdstrike/CrowdstrikeAssetsFindingsCorrelated.py:237-303`

`SpoolKeyClaims` stores `key -> hash(combined id)` and detects "two distinct ids under one key". It
cannot detect the inverse — one id under two keys — because a second key is simply an unclaimed
entry. Under the old code that case was unreachable: the AID-less serving of a host was dropped at
the scroller, so only the AID-bearing serving was ever staged. This change makes it reachable.

Concrete failure: unmanaged host U, combined id `8884…_ASCeLr0LF_zz3…`, no `aid`. It is served at
page 12 and staged under record key `8884…_ASCeLr0LF_zz3…`. A sensor is installed on U during the
spool; Discover populates `aid: A` and bumps `last_seen_timestamp`. The routine depth-cap re-anchor
(`config.MaxPagesPerCursorScroll`, `FalconHostSpooler.cs:232-235, 285-295`) restarts the query from
the watermark, and U — now above the watermark — is served again. `ExtractRecordKey` now returns
`A`. `_claims` has no entry for `A`, so `TryClaim` returns true and U is staged a second time. The
run emits two correlated records, the parser partitions them into two `aid` groups, and one physical
asset becomes two asset rows.

The parser's new `_report_spine_collapse` computes exactly the two numbers that expose this —
`distinct_hosts` (by `host.id`) and `assets_count` — but only tests one direction
(`distinct_hosts > assets_count`, line 288). `assets_count > distinct_hosts` is the duplication
direction and is not checked, so the run logs *"no asset collapse"* while the duplicate is emitted.
Adding the inverse branch is one `elif` and reuses values already in hand; it also makes the check
symmetric with the guarantee the collector now claims.

Note that these two directions can also cancel: one collapse plus one duplication leaves
`distinct_hosts == assets_count` and the check reports clean. Checking both directions removes that
too.

---

## 4. MEDIUM — the null-`last_seen_timestamp` exclusion is measured, deferred, and invisible in code.

`Collectors/FalconCollector/Flows/SharedFlows/FalconHostFilters.cs:14-36`
`Collectors/FalconCollector/Flows/Findings/TwoPhase/FalconHostSpooler.cs:434-449, 285-295`

The task's own research (`research/falcon-fql-null-field-matching.md`, confirmed live) proves
`last_seen_timestamp:>='…'` returns 298 of 303 assets on the lab tenant: FQL excludes rows where the
field is null, and offers no null predicate to OR back in (both spellings 400). `ReanchorAsync`
rebuilds that gate on every re-anchor, and every re-anchor path — cursor expiry, mid-scroll 5xx,
and the routine depth cap — goes through it.

Concrete failure: a run with no base date starts unfiltered and can see all 303. At the depth cap
(a normal, expected event on any large tenant) the scroll re-anchors with
`last_seen_timestamp:>='<watermark>'`. From that point on, every asset with a null
`last_seen_timestamp` that had not yet been served is unreachable for the rest of the run — 1.6% on
the measured tenant. With a base date configured they are never returned at all.

`constraints.md` puts the fix out of bounds, and that is a defensible call. What is not defensible
is that nothing in the shipped code says so. `BuildLastSeenTimestampGateFilter` carries a
three-line comment about combining a user filter; `ReanchorAsync` carries none; the spool counts
nothing for it. Meanwhile the same change spends two paragraphs on the boundary-AID cap's byte
budget. A reader of these files cannot learn this exclusion exists. At minimum it belongs as a
comment on the filter builder and on `ReanchorAsync`, next to the decision to defer it.

---

## 5. LOW-MEDIUM — `_report_spine_collapse` adds a full extra pass over the correlated lane.

`libs/packages/parsers/deprecated/crowdstrike/CrowdstrikeAssetsFindingsCorrelated.py:151, 275-285`

`records_df` is not cached. The new `.filter(chunk == 0).agg(countDistinct(...), count(...)).head()`
is an action, so it re-reads and re-parses every `findings_*.json` shard — including the `findings`
arrays it never touches, which are the bulk of the bytes. That is a fourth full pass over the
heaviest frame in the job (`asset_spine.count()`, this agg, `_explode_record_findings`, and the
`correlated_df.cache()` materialisation).

Not a correctness defect, and the check is worth having. But both numbers it needs are available in
the pass that already builds the spine over the same `chunk == 0` subset, so the cost is avoidable:
compute `distinct_hosts`/`unidentified` alongside the spine (e.g. a single `chunk == 0` projection
of `host.id` + `aid`, cached or aggregated once) rather than as an independent scan.

---

## 6. LOW — inaccurate comments and messages.

**a. The manifest-ceiling guard still says the page list dominates.**
`Collectors/FalconCollector/Flows/Findings/TwoPhase/FalconStagingArea.cs:293-316, 381-390`

`WarnIfManifestNearsReadCeiling`'s remark asserts "`PageKeys` is the only member that grows with the
traversal", and its log message says the manifest "is dominated by the {PageCount} staged page
key(s) it names". `TryReadManifestAsync`'s failure message repeats it and tells the operator to
"increase the Discover page size so fewer pages are named". This change's own new docstrings
contradict all three: `DiscoverWatermarkAids` is now up to 465,708 bytes — 44.4% of the 1 MiB
ceiling — and `FalconPhase1Manifest.cs:191-197` calls it "the manifest's only unbounded-by-shape
field".

Concrete failure: a manifest at ~790 KB built from a full 5,000-key boundary set of 89-char ids
(~466 KB) plus ~6,700 page keys (~322 KB) trips the 75% warning, which then points the operator at
the smaller of the two terms. Halving `MaxBoundaryAids` would be as effective as changing the page
size, and the message does not mention it. Three call sites, all in an untouched file whose premise
this change invalidated.

**b. Stale line references introduced by this change.**
- `FalconHostSpooler.cs:400` cites "Phase 2's accumulator map (`FalconSpotlightBatchPump.cs:598`)";
  `:598` is a docstring line — the map is built at `:620-633`.
- `FalconTwoPhaseFindingsTests.cs` (R2 docstring) cites `FalconHostSpooler.cs:206` as
  `.Where(h => seenAids.Add(h.Aid))`; that predicate is now at `:224` and reads
  `.Where(keyClaims.TryClaim)`.
- Same file, SC3 docstring cites `FalconHostSpooler.cs:216-223` and `:280-289`; neither range holds
  the code described.

These were wrong the moment they were written, which is the argument for citing symbols rather than
lines.

**c. The 89-character figure is correct — do not "fix" it to 65.**
`AidExtractor.cs:97-105` and the `MaxBoundaryAids` docstring both say 89 characters (32-hex CID +
`_` + 56-char base64url suffix), and I measured the sample id at exactly 89. The `~65` figure that
appears in the surrounding task framing is the stale one. The docstring's byte arithmetic also
reproduces: 5,000 × 92 + ~98 page keys ≈ 465.5 KB against a stated 465,708 B, and the stated
crossing point of 11,335 keys against a computed ~11,338.

---

## 7. LOW — one uncounted exclusion remains in the same loop.

`Collectors/FalconCollector/Flows/Findings/Correlated/FalconDiscoverHostScroller.cs:81-85`

A `resources` element whose `ValueKind` is not `Object` is `continue`d with no counter and no log,
in a method whose new comments assert that exclusions are counted rather than silent. Pre-existing
and unlikely, but it is now the only such path left in the method and closing it is two lines
alongside `keylessHosts`.

---

## Verified sound — checked and found correct

- **Key derivation and ordering.** `ExtractRecordKey` runs `ExtractAid` first (stated `aid`/
  `device_id`, then the 32-hex combined-id suffix) and only then falls back to the whole `id`. The
  R1 population (333 of 374 identifiers on the other tenant) keeps the key it has today. The
  `TryExtractAidFromCombinedHostId` gate is unchanged, so no new key shape is minted.
- **Nothing but a vendor-stated AID reaches the wire.** The `aid:[…]` filter is built from
  `plan.BySensorAid.Keys` (`FalconSpotlightBatchScroller.cs:113`), which is populated only through
  `FalconStagedPolicyPage.ResolveSensorAid`. The device-entities lane goes through the same call in
  both the spool (`CollectSensorAids`, `FalconHostSpooler.cs:664-676`) and the assets flow
  (`FalconAssetsScrollRunner.BuildPolicyTargets`). I traced every use of `DiscoverHost.Aid`: staged
  page codec, boundary set, key claim, accumulator, emitted record. None reaches a URL. Staged
  object keys are index-derived, not key-derived.
- **Sensor-AID uniqueness in `BySensorAid`.** For a host with a stated AID the record key *is* the
  sensor AID, so a second host with the same sensor AID would already be refused by
  `SpoolKeyClaims`. The last-writer-wins insert at `FalconSpotlightBatchPump.cs:633` is therefore
  unreachable, and `stats.SensorlessHosts = plan.Hosts.Count - accumulators.Count`
  (`FalconSpotlightBatchScroller.cs:101`) is exact rather than approximate.
- **The cursor change cannot unbound the scroll.** Dropping `hosts.Count == 0 ? null` in
  `FetchPageAsync` returns the vendor's own token; termination is carried entirely by the
  empty-string normalisation. The spool's own `page.After is not null && page.Hosts.Count > 0` guard
  still stops on an empty page, and the only other callers (`ReanchorAsync`, the dry-run probe) make
  a single call each. The comment at `:126-132` is accurate.
- **Concurrency.** `FalconSpotlightBatchScroller` and `FalconDiscoverHostScroller` hold only
  readonly fields and keep all per-call state local, so the pump's parallel batch fetches are safe.
  `SpotlightBatchPlan`, `HostFindingsAccumulator` and `BatchEmitStats` are per-batch and
  single-consumer. `SpoolKeyClaims` runs only on the single-threaded spool loop (its internal `lock`
  would be unnecessary; there isn't one, correctly). The Discover prefetch keeps at most one task in
  flight and observes it in `finally`.
- **The 89-char key against length/charset.** No slicing, no fixed-width buffer, no case-folding, no
  URL. Every comparer on the path is `StringComparer.Ordinal` (`_claims`, `boundaryAids`,
  `seenFindingIds`, `bySensorAid`), so the `-`, the second `_`, and mixed case are safe. The one
  ceiling it does touch (`MaxControlArtifactBytes`) is measured and pinned by
  `R3_ManifestWithFullBoundarySetOfLongKeys_StaysUnderTheControlArtifactCeiling`, which reads both
  `MaxBoundaryAids` and the ceiling from production constants and does a real write/read through
  `FalconStagingArea`. On the Python side the key survives as a plain string column with no width
  assumption.
- **`FalconSpotlightBatchScroller`'s 254 changed lines carry no unrelated modification.** Line by
  line the substance is: the `SpotlightBatchPlan` parameter; wrapping the scroll in
  `if (accumulators.Count > 0)` (pure re-indentation of the existing loop body); `aid` →
  `accumulator.RecordAid` at the two `Build` sites; `aid` → `findingAid` at the orphan check; the
  batch-end flush walking `plan.Hosts` instead of the dictionary; comments. Everything else is
  whitespace. Behaviour for an all-sensor batch is identical, including chunk indices.
- **Tests drive the real path.** The .NET tests go through `ProcessAsync`/`ResumeAsync` against a
  mock HTTP factory, so the real scroller, spooler, staged NDJSON codec, frozen key list and
  Spotlight pump all participate — a diverging production path would fail them. `R2` defines
  "surfaced" as a *difference against a control run* rather than by matching message text, which is
  the right shape. `MixedDiscoverPage_...` asserts an exact count and key-set equality, not
  "more than zero". The resume test asserts the absence of Discover traffic *and* the absence of
  staging writes, which is the correct pair.
- **The one test that builds its own version of production logic** is
  `tests/test_e2e_correlated_real_output.py::_storable_asset_value`, which re-implements
  `crowdstrikeAssets.py`'s hostname/`current_local_ip` preference and the control-char rule. I
  checked it against `crowdstrikeAssets.py:52-61` and it matches, including `contains("ip")` being
  case-sensitive. It is gated on `E2E_CORRELATED_DIR` and its own header says it is deleted after
  verification, so the drift risk is bounded — but it is real if the file outlives the task.
