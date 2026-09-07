# Orchestration Plan

## Problem Classification

- Contract sufficiency: sufficient — amended in flight, see Recon correction.
- Classes:
  - `entity-ids` — the task is entirely about which identifier keys an asset record, and which identifier may be handed to a vendor endpoint. Exact match to the existing ledger tag.
  - `asset-coverage` — new slug. The failure class is "the collector dropped inventory the vendor returned", which no existing tag names.
  - `spark` — the downstream consumer collapses equal keys with `Window.partitionBy`, which is what makes key uniqueness load-bearing rather than cosmetic.
- Additional classified prior art: none beyond the designer's seeds. The `entity-ids` delta returns only `L-c9239f9c` (already A10); the `spark` delta returns `L-9a2a1c4d` (already A12) and `L-90bc1864` (JDBC write concurrency, not relevant). `asset-coverage` has no prior rows.
- Recon correction: **classification confirmed; the SOLUTION was refuted.** The contract's named mechanism — derive a unique AID via `AidExtractor.ExtractAid` — produces a key for 0 of 254 AID-less assets (`research/lab-tenant-identity-probe.md`, full inventory, not a sample). The 32-hex suffix gate at `AidExtractor.cs:124` rejects the tenant's 56-char base64url token. `decisions.md` and `assumptions.md` are amended: the key is the Discover combined `id`, applied as a fallback AFTER `ExtractAid`, never as a replacement for it.
- New or changed artifacts:
  - Discover combined `id` as a record key (65 chars, contains `-` and `_`) → `FalconHostSpooler.cs:206` `seenAids.Add(h.Aid)` (`Ordinal` set) → deduplicates on it; a collision drops the second host silently and run-wide.
  - Same key → `FalconHostSpooler.cs:280` `[.. boundaryAids]` → serialized into `FalconPhase1Manifest.DiscoverWatermarkAids`, read back under `MaxControlArtifactBytes` = 1 MiB. At the 5,000-entry cap this roughly doubles, ~175 KB → ~340 KB. **Design-invalidating if the cap is ever reached.**
  - Same key → `Recovery/FalconCheckpointSerializer.cs:68` → serialized into the checkpoint with no collector-side cap at all.
  - Same key → `FalconSpotlightBatchPump.cs:592` accumulator map + the `aid:[…]` FQL filter → must NOT reach either; both already gate on `ExtractSensorAid`, which stays null for these hosts.
  - A host held out of the accumulators → `FalconSpotlightBatchScroller.cs:197-203` batch-end flush → if not routed there, `stats.HostsEmitted` under-reports into `totalAssetsEmitted` (`FalconFindingsFlow.cs:348`) and the checkpoint.
  - Truncated-freeze signal → `FalconPhase1Manifest.CompletedFreeze` (`:278-285`) hardcodes `units == unitsCovered, rejected: 0`, so `Finished` (`:98-115`) can only return `Completed`. There is no existing way to express a truncated freeze without either throwing before the manifest write or supplying values that select `Degraded`.

### Attention Items

| id | failure mode | causal path and impact | planned handling | source |
|---|---|---|---|---|
| R1 | Hosts that get a key from the 32-hex fallback today are re-identified | If the combined `id` REPLACES rather than falls back after `ExtractAid`, every host currently keyed by the derived suffix changes identity. `AidExtractor.cs:39-41` records 333 of 374 identifiers coming from that fallback on another live tenant, so the population is real. Downstream asset churn on tenants that work today. | `test:FalconCorrelatedFindingsTests::R1_HostWithThirtyTwoHexIdSuffix_KeepsItsDerivedAid_AndDoesNotSwitchToTheCombinedId` | recon L2 |
| R2 | Two hosts collide on one key and the second is dropped silently | `FalconHostSpooler.cs:206` `.Where(h => seenAids.Add(h.Aid))` is a silent set-add. A collision reintroduces exactly the loss this task removes, relocated somewhere harder to see. Measured behaviour, `collector-seams.md` §6. | `test:FalconTwoPhaseFindingsTests::R2_TwoDistinctHostsDerivingTheSameKey_AreDetectedAndSurfaced_NotSilentlyMerged` | recon L4 |
| R3 | A 65-char key overflows the manifest's 1 MiB control-artifact ceiling | `FalconHostSpooler.cs:280` carries up to `MaxBoundaryAids = 5000` keys into `DiscoverWatermarkAids`; the manifest is read back under `MaxControlArtifactBytes` = 1 MiB and buffered-read-ceiling under memory pressure (`FalconPhase1Manifest.cs:31-39`). An over-ceiling manifest fails the run. Same list also hits the checkpoint uncapped (`FalconCheckpointSerializer.cs:68`). | `test:FalconTwoPhaseFindingsTests::R3_ManifestWithFullBoundarySetOfLongKeys_StaysUnderTheControlArtifactCeiling` | recon L13 |
| R4 | A wholly AID-less aid-batch under-reports emitted hosts | `hosts.Chunk(...)` never yields an empty batch today and the codec refuses a keyless host, so `aid:[]` is currently unreachable. Any design holding hosts out of `accumulators` creates that path; if those hosts are not routed to the batch-end flush at `FalconSpotlightBatchScroller.cs:197-203`, `stats.HostsEmitted` → `totalAssetsEmitted` → checkpoint all under-report. | `test:FalconCorrelatedFindingsTests::R4_BatchOfOnlyKeylessHosts_IssuesNoSpotlightRequest_AndStillEmitsEveryHost` | recon L5 |
| R5 | The E2E parser proof passes with both sides wrong | `tests/test_e2e_correlated_real_output.py:56-62` builds `expected_assets` as a set keyed on `rec["aid"]`, then asserts `assets_df.count() == len(expected_assets)`. On degenerate keys both sides collapse identically and the assertion holds. SC6 would then certify the exact failure it exists to catch. | `test:tests/test_e2e_correlated_real_output.py::R5 independent distinct-host count, not derived from the aid key` | recon L8, `downstream-contract.md` §7 |

### Research Questions

| topic slug | triggered by | question | decision it can change | authority | result |
|---|---|---|---|---|---|
| falcon-fql-null-field-matching | assumption:A17 | Does Falcon FQL `last_seen_timestamp:>='<floor>'` match a host whose `last_seen_timestamp` is null? | Whether the 5 null-last-seen assets are lost by every re-anchor, which is inside "no asset may be dropped at any stage" but outside the two named defects. Decides whether this task grows a third defect or records it for a successor. | CrowdStrike FQL documentation; live probe against the lab tenant is decisive and cheap. | pending |

## Complexity Decision

- Path: **decompose**
- Axis scores: Complexity high | Separability high | Coupling low (read off recon: six disjoint sets) | Dependency order shallow (one frozen surface, then parallel) | Execution risk high | Worker clarity high (recon gives every seam a file:line)
- Rationale: three hard triggers fire at once — two repositories with separate toolchains, six disjoint file sets named by recon, and tests separable from implementation. The two test files cannot be merged into one worker because each carries its own private `CreateFactory`, so a route added in one is invisible in the other.

## Research Decisions

- External topic: `falcon-fql-null-field-matching` — triggered by: assumption:A17 — status: pending
- Internal recon: complete → `research/internal-recon.md`
- Live vendor probe: complete → `research/lab-tenant-identity-probe.md` (run from the main thread; it refuted the contract's mechanism before any worker started)

## File Ownership

- Disjoint sets found: 6
- W0 (main thread) owns the frozen surface: the `DiscoverHost` shape and the key-derivation contract.
- W1 owns: `Flows/Findings/Correlated/FalconDiscoverHostScroller.cs`, `Flows/Findings/Hosts/AidExtractor.cs`
- W2 owns: `Flows/Findings/TwoPhase/FalconHostSpooler.cs`, `FalconStagedHostPage.cs`, `FalconPhase1Manifest.cs`
- W3 owns: `Flows/Findings/Correlated/FalconSpotlightBatchPump.cs`, `FalconSpotlightBatchScroller.cs`, `FalconCorrelatedRecord.cs`, `HostFindingsAccumulator.cs`
- W4 owns: `UnitTests/.../FalconCorrelatedFindingsTests.cs`
- W5 owns: `UnitTests/.../FalconTwoPhaseFindingsTests.cs`, `InMemoryFalconStagingStore.cs`, `FalconConcurrencyHarness.cs`
- W6 owns: the parsers repo — `deprecated/crowdstrike/CrowdstrikeAssetsFindingsCorrelated.py`, `tests/test_crowdstrike_assets_findings.py`, `tests/test_e2e_correlated_real_output.py`
- Shared surface frozen in phase 0: the key-derivation contract below. Nothing else crosses a set boundary.

Not in any set, must not be touched: `Flows/Policies/*`, `Recovery/*` except read-only inspection, `FalconStagingArea.cs`, `FalconStagingPaths.cs`, `FalconFrozenKeyList.cs`, `Processing/Configuration/FalconCollectorConfiguration.cs`, and `libs/packages/build/lib/**` in the parsers repo (a stale duplicate tree — editing it changes nothing).

## Frozen Shared Surface (phase 0)

Written by the main thread before any worker starts, so no worker invents it:

- `DiscoverHost.Aid` stays a NON-NULL `string`. The type does not widen. What changes is that a key is always producible, so the null case disappears at the source rather than being propagated.
- Key derivation, in this exact order: `AidExtractor.ExtractAid(host)` first; if that is null or whitespace, the Discover combined `id` verbatim. Never a relaxed 32-hex gate, never a sentinel, never a hash.
- `DiscoverHost.SensorAid` remains nullable and remains the ONLY value any vendor endpoint or `aid:[…]` FQL filter may receive. `ExtractSensorAid` is unchanged.
- A host for which neither path yields a value is the one case that may still be excluded, and it must be counted and logged, never silently skipped.

## Worker Plan

- W1 — scope: stop the drop, fix the truncation, add the named fallback derivation and the collision detection site. owns: S1. inputs: frozen surface. output: the two defects fixed, R1's production path in place. phase: 1. continuity: fresh.
- W2 — scope: guard the dedup predicate, express a truncated freeze without changing manifest structure, keep the manifest under its ceiling. owns: S2. inputs: frozen surface. output: R2 and R3 production paths. phase: 1. continuity: fresh.
- W3 — scope: keyless hosts bypass accumulators and the FQL filter, empty batch issues no request, hosts still reach the batch-end flush. owns: S3. inputs: frozen surface. output: R4 production path. phase: 1. continuity: fresh.
- W4, W5 — scope: the tripwire tests, written against the frozen surface and the contract, not against whatever W1-W3 happened to write. owns: S4, S5. phase: 1, in parallel with W1-W3. continuity: resumed — they must react to integration failures against their own tests.
- W6 — scope: parser defensive guard plus the R5 independent count. owns: S6. phase: 1 for the guard, phase 2 for the E2E proof, which needs an artifact from the fixed collector. continuity: resumed.

## Synthesis Approach

Main thread reassembles after phase 1, builds, then runs the FULL Falcon suite once — never a filtered subset, never a shortened timeout — against the 357/1/2 baseline. Then produces a live artifact via the local runner in dry-run mode against the lab tenant and hands it to W6 for the E2E proof.

## Verification Obligations

- Every Success Criterion in `prompt_contract.md`, with SC6 requiring the R5-corrected assertion rather than the existing collapsing one.
- The amended mechanism must be reflected in the disposition of A1 (superseded), A15, A16, A17, A18.
- Recon L14 / A17 must be disposed explicitly. It is a real coverage defect inside the contract's own premise that neither named defect addresses.
