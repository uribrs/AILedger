# Frozen measurement fixtures for structured findings v1

Captured at task 1, before production implementation, from source `b72437ad3a91a8dea0503d0010582ea676533566`.

- `inputs/graph.events.jsonl`: exact 47-event history of `2026-09-23_0752-harness-graph-evaluation`.
- `inputs/axonius.events.jsonl`: exact 97-event history of `2026-09-23_2014-axonius-parser-fixes`.
- `inputs/axonius.refusals.jsonl`: exact four-row journal accompanying that Axonius task.
- `inputs/axonius.refusals-truncated.jsonl`: explicitly synthetic variant of that journal with one malformed row appended.
- `cases.json`: report-input variants, including missing refusal telemetry.
- `provider-samples.json`: values transcribed from existing `RunTelemetrySamples` / `RunCostReaderTests`, plus synthetic empty/malformed cases. These are reader fixtures, not newly observed provider runs.
- `expected/`: frozen `TaskRetrospective`, `TaskCloseoutEvidence`, and `RunCostReader` outputs from those inputs.
- `manifest.json`: immutable baseline provenance and SHA-256 hashes of all data files.

These historical snapshots are evidence for reader compatibility, not targets for mutation. Use a temporary copy for storage fault tests. The baseline harness only reads events into the pure reducer/projections; it never opens a live task, writes ledger events, repairs projections, or invokes providers. Original histories remain in the original checkout and are not needed to verify these fixtures.

See the [measurement inventory](../../../docs/structured-findings-v1/measurement-baseline.md) for reproduction commands, coverage, and limitations. Do not regenerate expected output or refresh hashes to hide a change. Preserve this baseline when making a deliberate, separately justified correction.
