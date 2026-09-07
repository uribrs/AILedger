# Cortex XDR Ingress streaming

Build the `Shared/DataPipeline/Ingress/` library (its first occupant) and use it to remove the in-memory CVE buffer from the Cortex XDR findings flow.

## Library (Commit 1)

Two static helpers under `Cymulate.Integration.Adapters.Shared.DataPipeline.Ingress.IngressStream`:

- `ReadNdjsonLinesAsync(Stream, CancellationToken)` — yield UTF-8 NDJSON lines from a stream as `ReadOnlyMemory<byte>`. Skip empty/whitespace-only lines. Lenient on malformed JSON: pass bytes through and let the downstream publisher's normalization surface them (matches today's Cortex XDR behavior of swallowing parse errors with a debug log).
- `Skip(IAsyncEnumerable<ReadOnlyMemory<byte>>, long, CancellationToken)` — drop the first N rows of a streaming source. Caller owns upstream sort determinism.

Plus a short `Shared/DataPipeline/Ingress/README.md` describing the data-plane source-side role, the universal shape, and a cross-link from the existing `Shared/DataPipeline/README.md`.

Unit tests for both helpers.

## Cortex XDR refactor + Egress tightening (Commit 2)

- `CortexXdrXqlClient.ExecuteAsync` returns `IAsyncEnumerable<ReadOnlyMemory<byte>>` instead of `Task<List<JsonElement>>`. The poll loop still drives `start_xql_query` and `get_query_results` (single in-memory document each — small control responses, not the row payload). The stream branch (`results.stream_id`) yields rows via `IngressStream.ReadNdjsonLinesAsync`. The inline branch (`results.data` array) yields each element as bytes. No `List<JsonElement>`, no `CloneArray`.
- `CortexXdrFindingsFlow.CollectAsync` / `PublishCveRowsAsync` consume the streaming source. Resume = `IngressStream.Skip(stream, nextCveIndex)`, then page by `_configuration.PageSize` rows.
- Remove `OrderCveRowsForResume`, `BuildCveTieBreakerHash`, `GetJsonSortValue`, `AppendHashData`, `CveSortTieBreakerFields`, `CveStageResult`.
- DryRun: drain just the first element of the streaming source.
- **Egress tightening**: `NdjsonUtf8BatchSession` (UTF-8 publish path) validates every record at append time, not only newline-containing ones. Single-line records get a cheap `JsonDocument.Parse`-and-dispose check; on failure, throw `InvalidOperationException` matching the existing multi-line failure shape. Closes the asymmetry that Ingress's new lenient byte-pass-through would otherwise expose.
- Tests updated to the streaming shape; existing assertions preserved. New Egress test: single-line malformed UTF-8 record throws `InvalidOperationException`.

## Out of scope

- `va_endpoints` source + `sourceType` discriminator (next task).
- Spool-to-disk wrapper, memory-pressure helper, chunk-by-row-count helper. Deferred until a second consumer justifies.
- Any change to `Egress/` or `Json/`.
- Any change to `Session/`, `Recovery/`, `Orchestration/`.
- Changes to `IAdapterDataPublisher` SDK contract or anything outside this repo.
