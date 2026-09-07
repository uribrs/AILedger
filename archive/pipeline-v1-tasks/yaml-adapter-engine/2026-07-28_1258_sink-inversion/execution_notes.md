# Execution Notes

Appended during execution. Owner: `contract-driven-execution`.

## Baseline

697 passed / 0 failed / 0 skipped at `3103a30` on `feat/sink-inversion`.

## Progress

_Pending._

## S2 gate — assumption A4: PASSED

`ApplyCounts` and `CollectFromRecords` were read directly. Both consume exactly two inputs:
a `published` count and `IReadOnlyList<JsonNode?>? nodes`. Nothing reaches into
`OperationResult`, a file, or beyond one page. `NeedsRecordNodes(stage)` already gates when
nodes are needed at all (array-path counts and `collect` specs only).

A decorator therefore has a sufficient vantage point: it tallies records itself and parses
them when asked. The contract's stop condition did not fire, and no fallback to
`OperationResult.Records` was needed — which matters, because that fallback would have
reinstated the removed fork under a different name.

## S1 — InMemorySink (done)

`Sinks/Logic/InMemorySink.cs`, public. Accumulates records for a caller with no downstream.
Deliberately does **not** override `PublishStreamAsync`: the interface default buffers a page
and re-delegates to `PublishBatchAsync`, which for an in-memory sink is precisely right.
`Drain()` is internal, for the sink-less overload populating `OperationResult.Records`.

## S2 — CountingSink (done)

`Sinks/Logic/CountingSink.cs`, internal. Decorator recording a `PublishedPage` per page and
forwarding everything unchanged. New contract type `Sinks/Contracts/Models/PublishedPage.cs`
(internal) carries page number, record count and optional nodes.

One design point worth stating: `PublishStreamAsync` observes by **tapping the sequence as it
flows**, not by materialising it first. Materialising would have silently undone the
streaming the wrapped sink exists to provide — a decorator that defeats the thing it wraps.
Node capture is opt-in for the same reason: it costs a parse per record and only array-path
counts and `collect` specs need it.

`Sinks` now has a `Logic/` layer. Tests: **697 passed / 0 failed / 0 skipped** — the new
types compile without disturbing existing behaviour.

## Remaining — S3 to S8 not started

S3 (sink-less overload becomes sugar over `InMemorySink`), S4 (runner supplies sinks),
S5 (delete the fork), S6 (split `PublishStageRecordsAsync`), S7 (pinning tests),
S8 (determinism over ≥12 trx runs). S3–S5 must land together: the sink cannot become
non-nullable until every caller supplies one, and the fork cannot go until it is.

## Code review on the increment (review/code-reviewer-1.md)

Run deliberately *before* S3–S5, so the wiring would not be built on a wrong shape. Four
Majors, no blockers — all four in code written minutes earlier, all four now fixed:

- **D3** `CountingSink` recorded records pulled through the tap rather than records the inner
  sink reported publishing. Those genuinely diverge, and `ApplyCounts` binds to the published
  figure — so the single field this type exists to provide was the wrong one. Page recording
  also vanished on a throw, while the batch path recorded before forwarding. Both paths now
  record in a `finally` with one documented meaning: records the sink accepted.
- **D4** `InMemorySink.TotalPublishedRecords` was `_records.Count`, which `Drain()` clears —
  rewinding a property the interface documents as monotonic, read by the engine after its page
  loop and by a sink shared per topic across stages. Now a separate counter `Drain` never touches.
- **D5** `CountingSink.DisposeAsync` disposed a sink it does not own. `MergeEnrichmentSink` had
  already settled the opposite convention in this repo; now matched and documented.
- **D6** Per-record `Encoding.UTF8.GetString` in the tap — on the path that exists precisely to
  avoid materializing pages. Span overload, as `WorkflowRunner.cs:524` already does.

Confirmed sound by the review and left alone: the tap mechanism itself (single enumeration,
cancellation, disposal on early abandon, bytes forwarded byte-identical); no double-counting
when the inner sink uses the default `PublishStreamAsync`; `InMemorySink` not overriding it;
`PublishedPage`'s nullable `Nodes` as opt-in; all ten interface members forwarded.

Also fixed: `ARCHITECTURE.md` still showed `Sinks/` holding only the interface.

**Open question for the operator, not settled here:** the visibility split assumes the observing
caller is in-assembly. If a host adapter is ever meant to wrap its own sink, `CountingSink` and
`PublishedPage` must go public together — a public `Pages` cannot expose an internal type.
Decide before the package ships; it is not a patch-level change afterwards.

Tests after fixes: **697 passed / 0 failed / 0 skipped**, build 0 warnings.

## S3 — blocked on a semantic that must be decided first (D7)

Started S3 and stopped before writing code. `TotalRecords` is set two different ways:

```
IntegrationEngine.cs:827   knownTotal = pageState.TotalRecords >= 0 ? pageState.TotalRecords : recordCount
IntegrationEngine.cs:840   TotalRecords = sink is not null ? sink.TotalPublishedRecords : knownTotal
```

Sink-less callers get the **vendor-reported total** from pagination metadata. Sink callers get
**records actually published**. Those diverge whenever a vendor reports a total larger than one
page — which is the normal case.

`OperationResult.TotalRecords` documents itself as the API-reported total, so the *sink* branch
is the one violating the documented contract, not the sink-less one.

This makes the naive S3 wrong: supplying an `InMemorySink` inside the sink-less overload would
silently move every existing sink-less caller — including the adapter's connection test — from
vendor-total to published-count. Two overloads feed the core with `sink: null`
(`IntegrationEngine.cs:105` and `:133`), so both would change.

**S3 must therefore decide the semantic before wiring anything**, and the decision belongs with
S5 (deleting the fork) because that is where the two branches merge. Options, for whoever
continues:
1. Honour the documented contract — `TotalRecords` is always the vendor total when known,
   published count otherwise — and give the published figure its own field. Consistent, but a
   visible change for current sink callers.
2. Keep both meanings but name them, so nothing is implicit.

Recorded as **D7**. This is exactly the class of defect the per-step review gate exists to
catch: no diff would have shown it, because nothing was wrong until the two paths were about
to merge.

## D7 resolved, then S3 (done)

**D7 fix was one line.** `knownTotal` was already computed correctly at
`IntegrationEngine.cs:827`; the only problem was line 840 overriding it with
`sink.TotalPublishedRecords` when a sink was present. `TotalRecords = knownTotal`
unconditionally now — the vendor-reported total when pagination metadata gives one, the
collected count otherwise, exactly as the property documents itself.

Decided rather than escalated, per the standing authorization. The conservative direction:
of the two meanings that were in play, one already contradicted shipped documentation, so
that is the one that moved. **No test depended on the old sink-path meaning** — 697/697
unchanged, which is itself evidence the sink-path number was never load-bearing.

**S3.** Both sink-less overloads now route through a private `CollectInMemoryAsync` that
supplies an `InMemorySink` and hands its records over in `OperationResult.Records`. Draining
happens regardless of outcome, matching what the old in-memory accumulation did for a failed
run. `sink: null` no longer appears anywhere in `IntegrationEngine`.

The `sink is null` fork inside `ExecuteOperationCoreAsync` is now reachable **only** through
`ExecuteStageAsync`, i.e. `WorkflowRunner` passing null — which is precisely S4. Once S4 lands
the branch is unreachable and S5 deletes it.

Tests: **697 passed / 0 failed / 0 skipped**.

## Next: S4

`WorkflowRunner` supplies sinks. Sink-routed stages (merge targets, and topic'd stages with no
`ForEach` and no `Poll`) get the `SinkProvider` sink wrapped in `CountingSink` with
`captureNodes: NeedsRecordNodes(stage)`. Everything else — control, poll, fan-out,
`MergeInto` sources — gets an `InMemorySink`. Then remove the `IExecutionSink? sink = null`
default from the private `ExecuteStageAsync`. S4 and S5 land together: the fork cannot be
deleted until nothing passes null.

## Per-step review on 0c2251d — verifier FAILED it, correctly

Ran the verifier and code reviewer on the D7+S3 commit after initially skipping both, which was
a lapse against the directive.

**The verifier failed the D7 claim and it was right.** `TotalRecords` is assigned on **six**
exits of `ExecuteOperationCoreAsync`. The "one line fix" touched only the success path.
`knownTotal` was computed at :853 — *after* five other returns — so it was structurally
unavailable to them.

Two of those exits read `allRecords.Count`. That list is only appended to in the `else` of
`if (sink is not null)`, and S3 made every sink-less caller supply a sink, so `allRecords` is
now permanently empty on those paths. Result: `TotalRecords = 0` returned alongside a populated
`Records` — a self-inconsistent result object, reachable on any HTTP transport failure or
declared-failure status.

So S3 did not just leave D7 partly fixed; **S3 actively created a new inconsistency** on paths
the original code handled correctly. The 697 tests did not catch it. Logged as **D8**.

Fix: two local functions declared where `allRecords` is, in scope of every return —
`Collected()` (sink total, else the in-memory list) and `TotalSoFar()` (vendor total when
pagination reported one, else `Collected()`). All six exits now call `TotalSoFar()`; the
redundant `knownTotal` is gone. 697/697.

**Lesson worth keeping:** a claim of "one line" should have prompted a check of how many places
implement the behaviour. Six assignments, one fixed, and the register recorded it as done. Grep
the property, not the path you happened to be reading.

## Code review round 2 — remaining findings

The full report carried more than the blocker. Resolved now:

**D9 (MAJOR) — truncation was undetectable.** A sink-signalled stop or a `max_pages` cap returns
`Success = true` with the full vendor total, and a sink caller's `Records` is empty — so nothing
told a caller whether the scroll completed or stopped at 5%. Pre-existing for sink-less callers;
D7/D8 spread it to sink callers by making `TotalRecords` mean the vendor total everywhere.

This is the half of D7 I proposed to the operator and then did not do: give the collected figure
its own field. Now `OperationResult.CollectedRecords`, set on all six exits alongside
`TotalRecords`. Compare them to detect truncation. Additive, so no caller breaks.

**Minors.** `Drain()` now hands the list over instead of copying it (a second N-reference array
per call for nothing). The "drain regardless of outcome" comment was overstated — it is skipped
on cancellation, since the core rethrows; comment corrected rather than the behaviour, because
the old local list was lost in exactly the same way.

Recorded, not fixed: **D10** — the streaming-ingest gate can now engage for sink-less callers,
which nulls `LastResponseBody` and switches to `ResponseHeadersRead`. Latent (no YAML in the repo
matches `CanStreamIngest` today) but the gate exists for the Tenable chunk shape. Needs a pinning
test in S7 and a decision on whether the sink-less overload should opt out. **D11** —
`CollectInMemoryAsync` extends the overload-funnel chain rule 5 cites as its own motivating
symptom; deferred to the `ExecutionRequest` work by the contract's out-of-scope list.
