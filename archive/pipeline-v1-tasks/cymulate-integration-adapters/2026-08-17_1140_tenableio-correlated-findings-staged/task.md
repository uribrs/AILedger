# Task

Re-implement the TenableIo correlated (asset-driven) `CollectFindings` technique on top of current
dev mechanisms.

`CollectFindings` must stop publishing two independent passthrough lanes for the parser to join in
Spark, and instead emit self-complete correlated NDJSON envelopes
`{ uuid, chunk, isLastChunk, findingsInChunk, host, findings[] }` to a single findings lane. Standalone
`CollectAssets` is unchanged.

The technique is already designed, built and run against the largest tenant on
`origin/feature/tenableio-correlated-findings`. That branch is **not** being merged: it targets the
pre-IntegrationInfra substrate, and three of the mechanisms it invented have since been replaced by
substrate capabilities — most importantly `IAdapterObjectStore` now gives collectors S3 **read and
write**, which the in-RAM asset spool was built to work around.

Scope of this task is therefore: keep the vendor logic and the record contract, rebuild the storage,
publishing and recovery planes on what the substrate provides now.

Full research carry-over — the branch's technique, the mechanical and architectural dev diff, the
validated vendor facts, and the three open design forks — is in
`research/branch-technique-and-dev-diff.md`.

## Deliverables

- Correlated `CollectFindings` on the TenableIo collector, on current Infra APIs.
- A spool backend chosen on evidence (Fork A), with the degrade machinery the choice obsoletes removed
  rather than ported.
- A resume model consistent with that choice (Fork B), with checkpoint format v2 and refusal of
  old-format checkpoints.
- Publishing through the emitter-owned batch-scoping path, one atomic object per vuln chunk.
- Tests, collector documentation, and `ai/skills` updates.
- A local end-to-end run for the operator's tenant comparison.

## Out of scope

- Changes to `IntegrationInfra`.
- The standalone `CollectAssets` lane's own pagination.
- Parser-side work (unless Fork C resolution says otherwise — see `decisions.md`).
