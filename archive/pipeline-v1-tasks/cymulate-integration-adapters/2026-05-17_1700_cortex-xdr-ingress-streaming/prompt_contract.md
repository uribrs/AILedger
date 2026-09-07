Role:
You are a senior .NET collector engineer working in the `cymulate-integration-adapters` repository. You build the `DataPipeline/Ingress/` library and apply it as the first refactor in the Cortex XDR findings flow.

Goal:
Populate `Shared/DataPipeline/Ingress/` with a minimal byte-row streaming library — two static helpers — and refactor `CortexXdrXqlClient` and `CortexXdrFindingsFlow` to consume it, removing the `List<JsonElement>` materialization of `va_cves` rows and the SHA-256 tie-breaker. Alongside the Cortex refactor, tighten `Egress` to validate every record at append time (closing the asymmetry that the new lenient byte-pass Ingress would otherwise expose). One PR, two commits. No checkpoint shape change. No new contract with upstream. Validation policy: lenient at Ingress, strict at Egress — Egress is the single authoritative JSON validator.

Context:
- `Shared/DataPipeline/Egress/` (formerly `Publishing/`) applies four-tier heap defense on the sink side. Its publisher accepts `IAsyncEnumerable<ReadOnlyMemory<byte>>` and handles record-count, byte-size, GC memory-pressure, and S3 multipart concerns.
- Today `CortexXdrXqlClient.PollAndReadResultsAsync` accumulates the entire `va_cves` row payload into `List<JsonElement>`, and `CortexXdrFindingsFlow.OrderCveRowsForResume` builds a second LINQ-ordered copy with a SHA-256 tie-breaker. These two buffers are the largest in-process allocations in the flow.
- The publisher already streams; only the source side does not. This refactor removes that gap.
- The `cve_id` uniqueness question in `va_cves` is the one open external-behavior assumption. If unique, the tie-breaker is over-defensive and the drop is free. If non-unique, the streaming refactor cannot proceed safely without a spool-to-disk path, which is out of scope.
- The forthcoming time-based adapter type (vendor alerts/events over a time window) will be the second `Ingress/` occupant. The library shape is chosen with that in mind but not over-fitted to it.

Constraints:

* One PR, two commits exactly.
  - Commit 1: Build `Shared/DataPipeline/Ingress/IngressStream` (two static helpers), `Shared/DataPipeline/Ingress/README.md`, and unit tests. No consumer changes. Builds and tests pass standalone.
  - Commit 2: Refactor Cortex XDR (XQL client + findings flow + tests). Behavior preserved at the checkpoint/recovery contract; memory profile changes (intentional).
* Library namespace: `Cymulate.Integration.Adapters.Shared.DataPipeline.Ingress`. Location: `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/DataPipeline/Ingress/`.
* `IngressStream` exposes exactly two `public static` methods. No interface, no base class, no options class.
  - `ReadNdjsonLinesAsync(Stream, CancellationToken) -> IAsyncEnumerable<ReadOnlyMemory<byte>>` — line-by-line UTF-8, skip empty/whitespace-only lines, no JSON parsing or validation, caller owns stream disposal, cancellation respected between lines.
  - `Skip(IAsyncEnumerable<ReadOnlyMemory<byte>>, long, CancellationToken = default) -> IAsyncEnumerable<ReadOnlyMemory<byte>>` — drop first N rows by count, no buffering beyond current row, cancellation respected.
* Library unit tests cover the cases enumerated in `constraints.md`.
* `CortexXdrXqlClient.ExecuteAsync` returns `IAsyncEnumerable<ReadOnlyMemory<byte>>`. Drop `List<JsonElement>` and `CloneArray`. Stream branch yields via `IngressStream.ReadNdjsonLinesAsync`. Inline `results.data` branch yields each array element via `NormalizedUtf8Json.SerializeToSingleLine`.
* `CortexXdrFindingsFlow.CollectAsync` / `PublishCveRowsAsync` consume the streaming source through `IngressStream.Skip(stream, resumeState?.NextCveIndex ?? 0)`, page by `_configuration.PageSize`, publish.
* Remove from `CortexXdrFindingsFlow`: `OrderCveRowsForResume`, `BuildCveTieBreakerHash`, `GetJsonSortValue`, `AppendHashData`, `CveSortTieBreakerFields`, `CveStageResult`, and the in-memory `EnumerateCveRows` over a list. Replace with the streaming page loop.
* Trust XQL's `| sort asc cve_id, name` for ordering. The tie-breaker hash is gone. This is contingent on `cve_id` uniqueness in `va_cves` — see Stop Conditions.
* DryRun: drain only the first element of the XQL streaming source via `GetAsyncEnumerator` + `MoveNextAsync`, then dispose. Do not iterate further.
* Checkpoint shape unchanged. `checkpointVersion = 2` remains valid. `NextCveIndex` semantics unchanged.
* Egress tightening (Commit 2 only — same commit as the Cortex refactor):
  - The UTF-8 publish path validates every record. Records without newlines get a cheap `using JsonDocument.Parse(record.Span)`-and-dispose validity check before append; on `JsonException`, throw `InvalidOperationException($"invalid JSON record: {ex.Message}", ex)` matching the existing multi-line failure shape.
  - Records with newlines continue through the existing `Utf8ResultsRecordFormatter.NormalizeToSingleLine` path unchanged.
  - The string publish path is already strict and is not modified.
  - The only Egress modification permitted is this validation tightening. Do not refactor or restructure Egress beyond that one change.
  - A new Egress unit test pins the strict contract: single-line malformed UTF-8 record throws `InvalidOperationException`. Existing tests publishing valid single-line UTF-8 records must remain green.
* All existing `CortexXdrFindingsFlowTests` assertions preserved: request sequence, checkpoint state at each boundary, resume from `NextCveIndex > 0`, cancellation between pages, legacy checkpoint rejection, empty terminal endpoint page.
* New tests added: incremental streaming (no pre-materialization), resume-skip correctness over the stream, malformed-line lenient policy (bytes reach publisher; publisher throws on bad JSON — that's where the failure surfaces).
* No new public types beyond `IngressStream`. No new abstractions in Cortex XDR.
* No spool-to-disk, no memory-pressure helper, no chunk-by-count helper. Deferred per `decisions.md`.
* Do not modify `DataPipeline/Egress/`, `DataPipeline/Json/`, `Session/`, `Recovery/`, or `Orchestration/`. The Ingress library imports from them; it does not modify them.
* Do not touch the SDK's `IAdapterDataPublisher` interface.
* Pre-commit hooks run. No `--no-verify`.
* `va_endpoints` source and `sourceType` discriminator are explicitly out of scope. Deferred to a separate task.

Success Criteria:

* `Shared/DataPipeline/Ingress/IngressStream.cs` exists with exactly the two specified static methods, namespace `Cymulate.Integration.Adapters.Shared.DataPipeline.Ingress`.
* `Shared/DataPipeline/Ingress/README.md` exists, describes the Ingress role, the universal shape, and cross-links from the parent `Shared/DataPipeline/README.md`.
* Library unit tests pass independently of Cortex XDR changes (Commit 1 standalone green).
* `CortexXdrXqlClient.ExecuteAsync` signature is `IAsyncEnumerable<ReadOnlyMemory<byte>> ExecuteAsync(string query, uint maxResults, CancellationToken cancellationToken)`.
* `grep -n "List<JsonElement>" src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/` returns zero matches.
* `grep -n "OrderCveRowsForResume\|BuildCveTieBreakerHash\|GetJsonSortValue\|AppendHashData\|CveSortTieBreakerFields\|CveStageResult" src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/` returns zero matches.
* `dotnet build` succeeds for the Cortex XDR project and the Shared project after each commit.
* `dotnet test` for the Cortex XDR test project passes 100% after Commit 2.
* `dotnet test` for the Ingress library test project passes 100% after Commit 1 and remains green after Commit 2.
* `dotnet test` for the Egress test project (wherever it lives in the new layout) passes 100% after Commit 2, including the new "single-line malformed UTF-8 record throws InvalidOperationException" test.
* No checkpoint format change. The flow can resume from a `checkpointVersion = 2` checkpoint produced by the pre-refactor code at any `NextCveIndex`.
* Each commit message follows repository convention.
* Both commits pass pre-commit hooks without `--no-verify`.

Execution Rules:

* Do not assume `cve_id` uniqueness. Resolve via the orchestrator's technical-researcher pass before writing the streaming Cortex code.
* Respect constraints strictly.
* Do not introduce new abstractions beyond the two `IngressStream` methods. If a third helper feels useful during implementation, halt and surface — it likely belongs in a follow-up task with a second consumer.
* Do not parallelize Commit 1 and Commit 2. Commit 1's library must be reviewable in isolation and must not depend on the Cortex change.
* Verify by running the build and test commands the orchestrator confirms with repo conventions. Report the exact commands used.
* If a test fixture rewrite for streaming reveals ambiguity in the expected request sequence (start → poll → stream), preserve the today's sequence — this is a memory refactor, not a protocol change.

Output Format:

* List of changed files grouped by commit.
* Verification commands run, with pass/fail.
* The new `IngressStream.cs` public surface (method signatures) and the `Shared/DataPipeline/Ingress/README.md` content as written.
* Any unresolved blockers or accepted technical risks.
* Final task directory path.

Stop Conditions:

* The `cve_id` uniqueness assumption in `va_cves` cannot be confirmed and duplicates are possible. The streaming refactor cannot proceed without a spool option, which is out of scope.
* `CortexXdrXqlClient.ExecuteAsync` has a caller other than `CortexXdrFindingsFlow` and changing the signature would force unscoped edits in another collector.
* A test failure cannot be resolved by mechanical updates to the streaming-source shape and would require checkpoint/recovery contract changes.
* The goal is achieved and verification is complete.
