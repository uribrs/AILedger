# Assumptions

## A1 — `topic` is known before a stage runs
**Status: VALIDATED**
`GetOrCreateSinkAsync(topic, …)` keys off `stage.Topic`, which is YAML config, not a
runtime value. So a sink can be resolved and handed to the engine *before* execution.
This is the fact the whole task depends on; verified by inspection of
`WorkflowRunner.cs` routing and `StageConfig.Topic`.

## A2 — Not every stage has a downstream consumer
**Status: VALIDATED — changes the design**
The brief assumed "the runner passes the host's sink". Only partly true. Routing in
`RunAsync` sends to `RunTargetStageWithMergesAsync` (sink-routed) only merge targets and
stages that are topic'd **and** have no `ForEach` **and** no `Poll`. Everything else —
control stages, poll stages, fan-out stages, and `MergeInto` lookup sources — goes
through `RunStageInstanceAsync` and has **no consumer by design**: their records exist
to be captured into `{{stages.*}}` scope or joined in-process.

So the inversion needs two answers:
- **sink-routed stages** → host sink from `SinkProvider`, wrapped in `CountingSink`
- **control / poll / fan-out / merge-source stages** → `InMemorySink`, read back by the
  runner

That is not a workaround; it is the honest shape. "Every caller supplies a sink" holds,
and `InMemorySink` is what a caller with no downstream legitimately supplies.

## A3 — The sink-less overload survives as sugar
**Status: VALIDATED as a decision (see decisions.md)**
It internally supplies an `InMemorySink` and returns its records. One internal path in
the engine, no host break.

## A4 — `CountingSink` can observe everything the runner needs
**Status: VALIDATED (S2 gate passed)**
`ApplyCounts` and `CollectFromRecords` consume exactly two inputs: a `published` count and
`IReadOnlyList<JsonNode?>? nodes`. Neither touches `OperationResult`, a file, or anything
wider than one page. A decorator in the record path sees every record and can supply both —
`published` from its own tally, `nodes` by parsing each record when asked. No count or
collect spec needs anything outside the decorator's vantage point, so the stop condition did
not fire and no fallback to `OperationResult.Records` is required.

*Original wording:*
The runner needs per-record JSON only when `NeedsRecordNodes(stage)` is true (array-path
counts, `collect` specs). `CountingSink` must expose the same nodes
`PublishStageRecordsAsync` builds today via `EnumerateAndCollect`. If some count or
collect spec cannot be satisfied from the decorator's vantage point, stop and report
rather than reaching back into `OperationResult.Records`.

## A5 — `InMemorySink` may make test doubles redundant
**Status: OPEN — assess in S7, do not force**
Several test files define their own accumulating sinks. Consolidating is desirable but
must not weaken an assertion. `TestSupport/CapturingSink` stays regardless: it
deliberately overrides `PublishStreamAsync` so the default buffering fallback cannot
mask whether the engine truly streamed.

## A6 — Memory profile of workflow stages changes
**Status: VALIDATED, accepted**
Sink-routed stages stream instead of accumulating. Intended. Control/poll/lookup stages
still accumulate — via `InMemorySink` — bounded as before by `ChunkConfig.Max` for
lookups.

## A7 — S4 cannot be delivered in slices
**Status: VALIDATED by experiment**
`OperationResult.Records` is populated only on the sink-less path, so handing a stage a sink
empties it. `WorkflowRunner` reads `Records` at `:493`, `:691` and `:715`. Supplying sinks
without moving those reads in the same edit gave **58 failures**; reverted. S4 and S5 are one
commit. An earlier claim that the two were separable was wrong.
