# TenableIo Correlated Findings Redesign (Collector Side)

Rewrite the TenableIo `CollectFindings` flow (`src/Cymulate.Integration.Adapters/Collectors/TenableIoCollector`) from two dumb passthrough lanes (`assets_*.json` + `findings_*.json`, correlated downstream by the split parser) to correlated asset-driven output.

## Output contract

Self-complete NDJSON records, Falcon-grammar mirror:

```json
{ "uuid": "<asset uuid>", "chunk": N, "isLastChunk": bool, "findingsInChunk": N, "host": { ...full /assets/export record... }, "findings": [ ...raw /vulns/export records... ] }
```

- 2,000-findings-per-record cap; an asset with more findings emits multiple records (`chunk` 0..N, `isLastChunk` on the final one).
- Zero-vuln assets emit one empty-findings envelope.
- File naming stays `findings_{page:D6}.json`; separate assets lane in CollectFindings is dropped.
- Standalone `CollectAssets` flow unchanged.

## Mechanism

1. **Phase 1 — asset spool.** Create BOTH exports up front. Stream assets-export chunks into an in-RAM spool: `uuid → gzip-compressed raw UTF-8 record bytes` (never parsed DOM). Hard budget (default ~1GB compressed); overflow assets publish immediately as chunk-0 host records + uuid marker set.
2. **Phase 2 — correlated emission.** Existing progressive vuln-chunk loop; per chunk, bucket findings by `asset.uuid`; flush any bucket at 2,000 findings (non-last record); flush all at chunk end with `isLastChunk`. Spool hit → enriched record, remove-on-use. Miss → thin host from the finding's embedded `asset` sub-object, counted + logged.
3. **Phase 3 — zero-vuln sweep.** Spool residue → empty-findings envelopes.

## Resume

Keep export-uuid + processed-chunk-ids + page-counter checkpoint model. On resume: rebuild claimed-set FIRST (uuid-only re-scan of processed vuln chunks), then rebuild spool from assets export SKIPPING claimed assets. Checkpoint format version bump; major CollectorVersion bump.

## Scope boundary

Adapters repo only. Parser dual-shape support is a later phase, after the user locally compares this collector's output against the current collector via LocalAdapterRunner (large-tenant creds already in gitignored `appsettings.local.json`).
