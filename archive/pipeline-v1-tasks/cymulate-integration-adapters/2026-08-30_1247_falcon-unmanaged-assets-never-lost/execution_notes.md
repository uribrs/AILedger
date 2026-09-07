# Execution Notes

Path: decompose, six workers over six disjoint file sets, shared surface frozen first.

## Planning-phase evidence produced before any worker started

Two live probes run from the main thread against the lab tenant with the operator's own
credentials, read-only. Both changed the plan.

1. `research/lab-tenant-identity-probe.md` — **refuted the contract's own mechanism.** The
   contract and `decisions.md` both named `AidExtractor.ExtractAid` as the way to give AID-less
   assets a unique key. Full inventory of 303 assets: 49 state an AID, and the derivation
   produces a key for **0** of the remaining 254, because `TryExtractAidFromCombinedHostId` gates
   on a 32-hex suffix and this tenant's suffix is a 56-char base64url token. A worker following
   the contract literally would have written code that rescued nothing. The combined `id` is
   present 303/303 and unique 303/303, so it became the key instead. `decisions.md` and
   `assumptions.md` were amended before fan-out.
2. `research/falcon-fql-null-field-matching.md` — **found a third defect, measured, not fixed.**
   303 unfiltered vs 298 under `last_seen_timestamp:>=`. FQL rejects both `:null` and `:!null`,
   so the 5 null-last-seen assets cannot be recovered by widening the filter. Out of scope: the
   fix needs a different traversal and the spool's design is off-limits under `constraints.md`.
   Recorded as A17 for a successor.

`research/internal-recon.md` mapped six disjoint file sets and 14 landmines. L2 in particular
prevented a regression that no success criterion would have caught: the combined `id` had to be a
fallback AFTER `ExtractAid`, never a replacement, because on another live tenant 333 of 374
identifiers come from the existing 32-hex path and replacing it would silently re-identify every
one of those hosts.

## W1 — scroller and derivation

`FalconDiscoverHostScroller.cs`, `AidExtractor.cs`.

- Added `AidExtractor.ExtractRecordKey`, a NEW named method rather than an overload of
  `ExtractAid`, preserving the file's existing stated/derived confidence split. `ExtractAid` runs
  first; the combined `id` is the fallback. Tagged `R1` at the fallback site.
- The 32-hex gate in `TryExtractAidFromCombinedHostId` was deliberately NOT relaxed (recon L12).
- The drop at `:82-86` is gone. A host with neither an AID nor an `id` is still excluded, but is
  now counted and logged rather than skipped in silence.
- Truncation fixed: `hosts.Count == 0 ? null : responseAfter` → `responseAfter`. Termination is
  already carried by the existing empty-`after` normalisation, so removing the count clause
  cannot unbound the scroll; the reasoning is recorded in place.

## W2 — spool, freeze, manifest

`FalconHostSpooler.cs`, `FalconPhase1Manifest.cs`.

- `R2`: the silent `seenAids.Add(h.Aid)` predicate was replaced with a claim that distinguishes
  the two conditions the set-add conflated, so a key collision is surfaced rather than absorbed.
- `SC3`: a truncated scroll now lands as the freeze the spool actually achieved, without changing
  the manifest's structure.
- `R3`: the boundary-AID cap is now documented as a byte budget against the 1 MiB
  control-artifact ceiling, since a 65-char key roughly doubles that payload.

## W3 — Spotlight path

`FalconSpotlightBatchPump.cs`, `FalconSpotlightBatchScroller.cs`, `HostFindingsAccumulator.cs`.

- Hosts with no `SensorAid` bypass the accumulators and never appear in an `aid:[…]` FQL filter.
- `R4`: a wholly sensorless batch issues no Spotlight request at all, tagged at
  `FalconSpotlightBatchScroller.cs:103`.
- 254 lines changed in the batch scroller — a larger restructure than the other two workers, and
  flagged to code review on that basis.

## W4, W5 — tripwire tests, written in isolation from the implementation

`FalconCorrelatedFindingsTests.cs` (W4): SC1, SC2, R4, R1, and a mixed-page test asserting
emitted count equals input count. All use the real 56-char-suffix vendor shape from the probe.
`FalconTwoPhaseFindingsTests.cs` (W5): SC3, R2, R3, and a resume test.

## W6 — parser, separate repo

`CrowdstrikeAssetsFindingsCorrelated.py`, `test_crowdstrike_assets_findings.py`,
`test_e2e_correlated_real_output.py`.

- The `Window.partitionBy("aid")` collapse is no longer silent: it reports both counts.
- `R5`: the E2E test's ground truth was keyed on `rec["aid"]`, so on degenerate keys both sides
  collapsed identically and the assertion passed while everything was broken. Replaced with an
  independent distinct-host count.
- **Result: 32/32 pass**, including new tests proving the 65-char combined-id key survives the
  whole parser path and reaches the policy projection intact. No parser output-contract change.

Environment note: PySpark needs a JRE that is installed via brew but not linked on this machine.
Tests run with `JAVA_HOME=/opt/homebrew/opt/openjdk@17/libexec/openjdk.jdk/Contents/Home`. No
machine configuration was changed.

## Post-verification: the live end-to-end proof (SC6)

`verifier-1.md` marked SC6 unmet because no artifact from the fixed collector existed when it ran.
One now does.

**Live collection, lab tenant, fixed collector**, correlation `0a1e66ec-6613-4370-aa90-02af3bae0228`:

```
Falcon collector completed. Flow=CollectFindings Hosts=298 Findings=121761 Success=True
```

At `baseRef` the same tenant produced **49** hosts. The gap between 298 and the 303 Discover
reports unfiltered is exactly A17 — the 5 null-`last_seen_timestamp` assets the gate excludes —
which is measured, recorded, and deliberately not fixed.

Getting a clean generation took three attempts, and the two failures are themselves evidence:

1. `--dry-run` returns after a Discover probe for this flow and publishes nothing, so it cannot
   produce an artifact at all.
2. Two real runs were refused by the prior task's own guard: *"generation `gen_08df066dd3f35b04`
   cannot be shown to share this leg's Prevention policy contract … its edge stage is
   [completed 1/1 units (empty=0, items=49, …)] resolved with enrichment ON, while this leg is
   configured with enrichment OFF."* That guard worked exactly as designed, and the `items=49` in
   its own message is the pre-fix host count. An environment-variable override could not redirect
   the run because `LocalAdapterRunnerConfiguration.cs:79-81` adds the project-local JSON file
   **after** `AddEnvironmentVariables()`, so the file always wins. The run was pointed at a fresh
   generation by editing `storageUrl` in the gitignored local config; **the original value was
   backed up and restored immediately afterwards**, verified by re-reading the file.

**Artifact contents** (6 `findings_*.json`, 301 MiB, downloaded, analysed, then deleted):

| measure | value |
|---|---|
| records | 338 (hosts are chunked) |
| distinct `aid` | **298** |
| `aid` length 32 (sensor-managed) | 89 records |
| `aid` length 89 (combined id) | 249 records |
| null or empty `aid` | **0** |
| records carrying findings | 86 |
| records with an empty findings array | 252 |

Note the key is **89** characters, not the 65 the plan assumed — `<32-char CID>_<56-char
base64url>`. This confirms the verifier's rejection of A16 and is why the manifest and checkpoint
measurements had to be redone at 89.

**Parser over that real artifact** — `tests/test_e2e_correlated_real_output.py`, the R5-corrected
version whose ground truth is counted on `host["id"]` rather than on the `aid` key:

```
CrowdStrike correlated parser: asset spine rows (chunk==0)=298
CrowdStrike correlated parser: no asset collapse - distinct input hosts=298, surviving asset rows=298
1 passed
```

**298 in, 298 out.** SC6 met.

One environment caveat, and it is not a code defect: the first attempt died with
`java.lang.OutOfMemoryError: Java heap space` on the local single-JVM Spark while correlating
121,761 findings. Re-running with `PYSPARK_SUBMIT_ARGS="--driver-memory 8g --executor-memory 8g"`
passed in 15s. The asset-preservation result was already logged before the OOM, and was identical
in both runs.

## Repairs from review

- **Code review finding 1 (HIGH), fixed in this thread.** `FalconFindingsFlow.cs:141` constructed
  the scroller as `new FalconDiscoverHostScroller(_http)` while holding `_logger` in scope on that
  very line. The scroller's logger is optional, so the keyless-host counter incremented and was
  then discarded — the exclusion was as invisible as the `continue` it replaced. Now wired, with
  the reason recorded at the call site. It is the only construction site in the collector.
- Code review finding 2 and verifier finding 6 (a degraded freeze is recorded but nothing gates on
  it, so a truncated inventory still publishes as Success) — dispatched to W2.
- Code review finding 3 (the parser's collapse guard tests only one direction, so a host that
  gains a sensor AID mid-spool duplicates silently; and the two directions can cancel) —
  dispatched to W6.
- Verifier finding 4 (the boundary-key list reaches a second, unmeasured ceiling in the checkpoint,
  which has no collector-side cap) — dispatched to W5.
