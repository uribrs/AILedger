# Defect Register

Standing artifact. Bug-finding is a first-class objective, so findings are recorded here
as they surface — including ones deliberately not fixed. A defect absorbed silently into
a refactor commit is a defect nobody can audit.

| # | Where | Finding | Status |
|---|---|---|---|
| D1 | `IsbExecutionSink` (monorepo, host) | Does not override `PublishStreamAsync`, so it uses the default buffering fallback — the whole page materialises into a `List` before delegating to `PublishBatchAsync`. Not a regression (that path was already buffered), but the streaming benefit is unrealised on the ISB path. | OPEN — host-side, monorepo read-only |
| D3 | `CountingSink.PublishStreamAsync` | Recorded the count pulled through the tap, not the count the inner sink reported publishing — and the two diverge (the interface's own default fallback skips empty elements; a sink may stop reading early). `ApplyCounts` binds to the published figure, so the one field callers use was wrong. Also skipped recording the page entirely on a throw, while the batch path recorded before forwarding — opposite failure semantics for no reason. | FIXED in this branch |
| D4 | `InMemorySink.TotalPublishedRecords` | Was `_records.Count`, which `Drain()` clears — so a drain rewound a property the interface documents as monotonic. The engine reads it after its page loop, and the runner shares one sink per topic across stages, so this under-reported silently. | FIXED — separate monotonic counter |
| D5 | `CountingSink.DisposeAsync` | Disposed a sink it does not own, so disposing the wrapper would double-dispose a live host egress the caller still holds. The repo's other decorator (`MergeEnrichmentSink`) had already chosen the opposite convention. | FIXED — no-op, ownership documented |
| D6 | `CountingSink.Tap` | Per-record `Encoding.UTF8.GetString` on the path whose entire purpose is not materializing large pages. `JsonNode.Parse` has a span overload, already used at `WorkflowRunner.cs:524`. | FIXED — span overload |
| D9 | `IntegrationEngine.cs:746-752, :796` | A truncated run — sink signalled stop, or `max_pages` cap — returns `Success = true` with the full vendor total, and for a sink caller `Records` is empty. Nothing distinguished a complete scroll from one that stopped at 5%. Pre-existing for sink-less callers; D7/D8 extended it to sink callers by making `TotalRecords` the vendor total everywhere. | FIXED — new `OperationResult.CollectedRecords` carries what the run actually collected, set on all six exits; compare against `TotalRecords` to detect truncation |
| D10 | `IntegrationEngine.cs:350` streaming-ingest gate | Latent behavioural change: the gate could not engage for sink-less callers before (it required a non-null sink and they had none). Now they always supply one, so it can — which nulls `LastResponseBody` and switches to `ResponseHeadersRead`. No YAML in the repo currently matches `CanStreamIngest`, so it is latent, but the gate exists for the Tenable `/vulns/export` shape. | OPEN — pin with a test in S7; decide whether the sink-less overload should opt out of the fast path |
| D11 | `CollectInMemoryAsync` | Extends the overload-funnel chain that CLAUDE.md rule 5 names as its own motivating symptom. Pre-existing shape, and the contract's out-of-scope list defers it. | OPEN — resolve when `ExecutionRequest` lands (phase-3 page-loop decomposition) |
| D8 | `IntegrationEngine.cs` six `TotalRecords` assignments | **The first D7 fix was wrong, and the register overstated it.** `TotalRecords` is assigned on six exits; only the success path was fixed. `knownTotal` was computed *after* all five early returns, so it was structurally unavailable to them. Two of those exits (transport error, `FailureAction.Fail`) read `allRecords.Count`, which is permanently empty once a sink always supplies the records — so they returned `TotalRecords = 0` next to a populated `Records`. A self-inconsistent result object on any transport failure. Caught by verifier-1, not by the 697 tests. | FIXED — two local functions `Collected()`/`TotalSoFar()` declared in scope of every return; all six exits use `TotalSoFar()`; `knownTotal` removed as redundant |
| D7 | `OperationResult.TotalRecords` (`IntegrationEngine.cs:827,840`) | **`TotalRecords` means two different things depending on whether a sink was passed, and the sink branch contradicts the property's own documentation.** Sink-less: `knownTotal` — the vendor-reported total from pagination metadata, falling back to the record count. With a sink: `sink.TotalPublishedRecords` — records actually published. These are different numbers by design (a vendor may report 10,000 while page 1 publishes 100). `OperationResult.TotalRecords` documents itself as *"Total number of records as reported by the API (from pagination metadata). Set to the count of Records when the API does not report a total"* — so the **sink** branch is the one that violates the contract. Naively making the sink-less overload sugar over `InMemorySink` (S3) would silently switch every sink-less caller from vendor-total to published-count, including the adapter's connection test. | FIXED (properly, on the second attempt) — see D8 |
| D2 | `OperationResult.TotalRecords` | Derives from `sink.TotalPublishedRecords`. A sink overriding `PublishStreamAsync` without bumping its own counter reports zero records with no error. | OPEN — pin with a test in S7 |

## Carried forward from the extraction task

Still open, out of scope here, tracked in
`ai/active/2026-07-27_1749_yaml-engine-extraction/progress_log.md`:

- `_definitions` is an unsynchronised `Dictionary` with a public `InjectDefinition`
  mutator, read on the execution path. Dies with the definition-source inversion.
- `HmacAuthenticator` computes a digest of a concatenation, not a keyed MAC; only
  authenticator of eight with zero test coverage.
- 46 pre-existing XML-doc defects hidden by `GenerateDocumentationFile` being off.
- Rule 0 has no automated guard in this repo.
