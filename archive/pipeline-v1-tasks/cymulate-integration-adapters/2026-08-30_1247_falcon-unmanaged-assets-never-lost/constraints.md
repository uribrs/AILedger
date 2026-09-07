# Constraints

## Product

- Absent a user-supplied FQL filter, every asset Falcon returns for the Discover scroll must appear in the emitted findings output. No silent drop, at any stage.
- An asset with no Spotlight correlation is emitted with an empty findings array, never omitted.
- The emitted record's wire shape stays `{aid, chunk, isLastChunk, findingsInChunk, host, findings[]}`. No new required field, no removed field, no renamed field.
- `aid` must be unique per asset in every emitted record. Never null, never `""`, never a shared sentinel — `Window.partitionBy("aid")` in the parser collapses all equal keys into one surviving asset row.
- `device_policies` envelope semantics are unchanged. AID-less hosts take the existing `Empty()` path (`FalconDevicePoliciesEnvelope.ComposeForVendorStated` already returns `Empty()` for a blank AID).

## Do not touch

- The S3 two-phase spool's design (`850de27b` / PR #314): batching geometry, checkpoint format, resume semantics, manifest structure, generation folders.
- `FalconCollectorConfiguration.cs:179` `EnablePreventionPolicyEnrichment` — stays `false` as committed.
- Policy enrichment code paths. `FalconAssetsScrollRunner.cs:455-467` already ships hosts with a null sensor AID; that is the template, not a thing to change.
- The parser's output contract: column set, column names, column types, row semantics of `parser_output_assets` and `parser_output_findings`.

## Out of scope — separate recorded defects, do not fold in

- `asset_match_key` mismatch in the parsers repo (ledger `L-f3058a37`, `L-cc7eaeb4`).
- `rules[].value` JSON encoding (ledger `L-5de41efb`, `L-85e358c4`).
- Opening a PR for `fix/falcon-disable-prevention-policy-enrichment`.

## Testing

- Run the FULL Falcon test suite. Never a change-filtered subset — filtering to change-relevant classes previously hid 5 real failures that code review had to catch.
- Never shorten `/Blame:TestTimeout`. The suite legitimately takes ~14 minutes; a shortened timeout has twice produced false "hang"/"deadlock" reports in this workstream.
- Baseline at `baseRef` `bbb044b9`: 357 passed / 1 failed / 2 skipped of 360. The single pre-existing failure is `FalconTwoPhaseFindingsTests.ResumedLegThatPublishesNothing_LeavesTheCoordinateUnchanged_SoTheNoProgressBudgetAccumulates`. Any other failure is a regression introduced by this task.
- The parser claim must be established by RUNNING the parser over a real collector artifact. Reasoning about `Window.partitionBy` is not evidence; the prior investigation established this by execution and so must this.

## Process

- Two repos, two working trees, no shared build. Workers own disjoint file sets; a worker that needs a change in another worker's file requests it rather than making it.
- Cite `notes/collector-seams.md` for seam facts rather than re-deriving them.
