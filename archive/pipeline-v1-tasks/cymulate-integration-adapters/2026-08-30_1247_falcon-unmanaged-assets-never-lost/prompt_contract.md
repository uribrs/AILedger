# Prompt Contract

## Role

You are a senior .NET collector engineer working on Cymulate's CrowdStrike Falcon integration, with a
PySpark parser as the downstream consumer you must not break.

## Goal

Every asset Falcon returns for an unfiltered Discover scroll appears in `CollectFindings` output —
carrying an empty findings array when it has no Spotlight correlation — and a Discover page that
yields no correlatable hosts no longer terminates the scroll.

## Context

`CollectFindings` is asset-driven: Discover hosts drive the run into aid batches, and the emitted
record `{aid, chunk, isLastChunk, findingsInChunk, host, findings[]}` is BOTH the finding and the
asset spine, because the July 2026 redesign dropped the separate `assets_*.json` lane from this flow.
`aid` is the record key. `FalconDiscoverHostScroller.cs:82-86` `continue`s when no AID can be
derived, so an AID-less asset is fetched, parsed, and dropped in the client.

Live measurement, lab tenant `api.us-2.crowdstrike.com` / CID `8884df8d8f704f43b23dad2e90572984`,
2026-08-30: Discover holds 298 assets unfiltered, 49 with `entity_type:'managed'`. The run staged 49.
249 were lost. The standalone `CollectAssets` lane keeps all of them.

Separately, `FalconDiscoverHostScroller.cs:100` returns `After = null` when the POST-FILTER host list
is empty; `FalconHostSpooler.cs:216,220-223` reads that as end-of-scroll and writes a manifest marked
as a completed freeze. A tenant whose unmanaged assets cluster onto whole pages gets a truncated
inventory reported as a successful complete run.

The evidence base is already produced. Read, do not re-derive:
`notes/HANDOFF.md`, `collector-seams.md`,
`downstream-contract.md`, `parser-proof.md`.

Two facts settled by execution in that investigation:

- Emitting a null or empty `aid` collapses every AID-less record into ONE surviving asset row, because
  the parser does `Window.partitionBy("aid")` (`CrowdstrikeAssetsFindingsCorrelated.py:215`).
  Reproduced: 249 in → 1 out, 248 gone, exit code 0, no error, no warning. Strictly worse than today.
- Emitting a unique derived AID via `AidExtractor.ExtractAid`
  (`Flows/Findings/Hosts/AidExtractor.cs:71-83`) works: 1 managed + 20 unenriched → 21 in, 21 out,
  findings and policy edges correct, collector-only, no parser change.

## Constraints

- Absent a user FQL, no asset Falcon returns may be dropped at any stage.
- `aid` is unique per asset in every emitted record. Never null, never `""`, never a shared sentinel.
- An AID collision between two distinct assets must be detected and surfaced, never silently absorbed — a collision reintroduces the exact loss this task removes, in a harder place to see.
- Wire shape unchanged: no field added, removed, or renamed on the emitted record.
- `device_policies` semantics unchanged. AID-less hosts take the existing `Empty()` path.
- The S3 two-phase spool's design is untouchable: batching geometry, checkpoint format, resume semantics, manifest structure, generation folders.
- `FalconCollectorConfiguration.cs:179` `EnablePreventionPolicyEnrichment` stays `false`.
- The parser's output contract is untouchable: column set, names, types, row semantics.
- Out of scope, do not fold in: `asset_match_key` mismatch (`L-f3058a37`, `L-cc7eaeb4`), `rules[].value` JSON encoding (`L-5de41efb`, `L-85e358c4`), the prior branch's PR.
- Full Falcon suite only, never a change-filtered subset. Never a shortened `/Blame:TestTimeout` — the suite legitimately takes ~14 minutes.
- Baseline at `baseRef` `bbb044b9`: 357 passed / 1 failed / 2 skipped of 360, the one failure being `FalconTwoPhaseFindingsTests.ResumedLegThatPublishesNothing_LeavesTheCoordinateUnchanged_SoTheNoProgressBudgetAccumulates`. Any other failure is a regression from this task.
- The parser non-regression claim is established by RUNNING the parser over a real collector artifact. Spark masks originating errors (`L-9a2a1c4d`), so an exit code is not evidence — assert on row counts.

## Known seams

Cite `collector-seams.md`; do not re-derive.

- `FalconDiscoverHostScroller.cs:82-86` — the drop. `:100` — the truncation.
- `FalconHostSpooler.cs:204-207` — HIGHEST RISK. `.Where(h => seenAids.Add(h.Aid))` collapses null and `""` silently, confirmed empirically. If AID widens this becomes a guarded predicate, never a sentinel. The assets flow dedups on `FalconJson.TryReadAnyId` — that is the precedent.
- `FalconSpotlightBatchPump.cs:590-602` (`SeedAccumulators`) — hard crash on a null AID. AID-less hosts must bypass the accumulators and the `aid:[…]` Spotlight filter, and a wholly AID-less batch must issue NO Spotlight call.
- `FalconStagedHostPage` / `DiscoverHost.Aid` — symmetric encode/decode if the type widens.
- Policy enrichment — no change. `FalconAssetsScrollRunner.cs:455-467` already ships hosts with a null sensor AID; that is the template.

## Success Criteria

1. A test proves an AID-less Discover asset reaches emitted output with a unique `aid` and an empty `findings` array, constructed through the production scroller/spooler path, not a hand-built stand-in.
2. A test proves a Discover page whose hosts are all AID-less does NOT terminate the scroll: the vendor's `after` is followed and the next page is fetched.
3. A test proves a spool whose freeze was truncated is not recorded as a completed freeze.
4. A test proves a wholly AID-less aid-batch issues no Spotlight request and does not throw.
5. A test proves two distinct AID-less assets that would derive the same AID are detected and surfaced rather than silently merged.
6. Running the parser over a real collector artifact from the fixed collector yields output asset row count equal to input asset count, asserted on counts and not on exit code.
7. Full Falcon suite result is 358/360 or better with the one known pre-existing failure and no new failure.
8. `git diff` from `bbb044b9` shows no change to the spool's checkpoint format, manifest structure, batching geometry, `EnablePreventionPolicyEnrichment`, or the parser's output columns.

## Execution Rules

- Do not assume missing data. Where a vendor behavior decides a design choice, resolve it or record it as an OPEN assumption for the verifier.
- Respect constraints strictly. A constraint that appears to block the goal is a stop condition, not something to work around.
- Diagnose before changing. If a fix causes more failures than it resolves, revert it and report.
- Do not expand scope mid-execution. The two defects and the parser proof are the whole task.
- Cross-repo: workers own disjoint file sets and never edit another worker's files.

## Output Format

- Code changes in `/Users/user/Dev/cymulate-integration-adapters` on branch `fix/falcon-disable-prevention-policy-enrichment`, plus any parser-side guard in `/Users/user/Dev/cymulate-integration-parsers`.
- `execution_notes.md` recording per-step what changed, what was run, and what the run returned — counts, not adjectives.
- `review/verifier-N.md` with the assumption disposition table and the attention-item disposition table.
- `review/code-reviewer-N.md`.

## Stop Conditions

- An AID cannot be derived uniquely for some class of asset, so the unique-AID decision cannot hold for that class.
- Staged page size crosses the 8 MiB in-memory write ceiling under the increased asset count.
- The truncation fix cannot be made to terminate.
- The parser proof shows loss the collector change cannot fix without touching the parser's output contract.
- Any success criterion cannot be met without violating a constraint.
