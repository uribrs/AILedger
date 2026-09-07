# Code Review — Sinks: InMemorySink, CountingSink, PublishedPage

**Calibration.** Shared library code on a public NuGet surface; `CountingSink` sits on the
streaming publish path (large vendor chunks). Risk: **Medium-High** — decorator over a
production egress, per-record hot path, disposal semantics. Depth applied accordingly.

`dotnet build` clean (0 warnings). `dotnet test`: 697 passed / 0 failed. No test exercises
the three new types.

---

## 1. Major — `PublishStreamAsync` records the page only on success, and records the wrong number

`Sinks/Logic/CountingSink.cs:61-68`

Two defects in one block:

**(a) The count is `counted` (records *pulled* through the tap), not `published` (what the
wrapped sink reports).** Line 68 returns `published` and then throws it away; line 67 stores
`counted`. These diverge in practice, not in theory: if `inner` does not override
`PublishStreamAsync`, the interface's own fallback skips `utf8.IsEmpty` elements and returns
`buffer.Count` (`Sinks/Contracts/Interfaces/IExecutionSink.cs:60-68`), and a sink that stops
reading early (byte cap, memory pressure — precisely what production sinks override this
method for) publishes fewer than it was offered. `Pages.Sum(p => p.RecordCount)` then
disagrees with `TotalPublishedRecords`. The existing consumer that this shape is built for,
`Workflow/Logic/WorkflowRunner.cs:214`, takes `int published` as its count parameter — so the
one field callers will bind to is the one that is wrong.

**(b) On any throw, the page vanishes from `Pages`.** Source failure mid-stream, cancellation,
or an inner-sink error all bypass line 67, so records the inner sink *did* publish are
unaccounted for. `PublishBatchAsync` (line 48) records the page *before* forwarding, so the two
paths have opposite failure semantics for no stated reason.

**Impact.** A type whose sole purpose is producing an accurate count produces an inaccurate one,
and silently loses a page on the failure path where reconciliation matters most.

**Fix (local patch).** Store `published`, and record the page in a `finally` so a partial page
is still reported:

```csharp
try { published = await inner.PublishStreamAsync(observed, pageNumber, ct); }
finally { _pages.Add(new PublishedPage(pageNumber, published, nodes)); }
```

For `PublishBatchAsync`, `records.Count` is *offered*, not published; take the delta of
`inner.TotalPublishedRecords` around the forward call if the two paths must mean the same
thing. Either way, define `RecordCount` in the XML doc as offered-or-published and make both
paths obey it.

## 2. Major — `Drain()` silently resets `TotalPublishedRecords`

`Sinks/Logic/InMemorySink.cs:26, 61-66`

`TotalPublishedRecords` is `_records.Count`, and `Drain()` clears `_records`. The interface
documents that property as "Total records published so far"
(`IExecutionSink.cs:89-91`) — monotonic. The engine reads it *after* the pagination loop to
build `CompleteAsync(recordCount)` and `OperationResult.TotalRecords`
(`Execution/Logic/IntegrationEngine.cs:806, 840`), and `WorkflowRunner` shares one sink per
topic across stages (`WorkflowRunner.cs:79, 536-539`). Any drain between two engine calls on a
shared sink makes the second call's totals restart from zero — under-reported record counts in
the result and in the sink's completion signal, with no error anywhere.

**Fix (local patch).** A separate `private int _totalPublished` incremented in
`PublishBatchAsync` and never touched by `Drain()`. Two lines, and it makes the ownership
contract honest: `Drain` transfers the *records*, not the *history*.

## 3. Major — `CountingSink.DisposeAsync` disposes a sink it does not own

`Sinks/Logic/CountingSink.cs:92`

The intended usage is "the caller wraps its real sink, hands the wrapper to the engine, and
reads `Pages` afterwards" — so the caller retains `inner` and will dispose it. Whoever disposes
the wrapper also disposes `inner`, giving a double dispose of a production egress (for an ISB
sink that can mean a second completion or a throw on a torn-down context). The sibling
decorator in this repo made the opposite call explicitly:
`Workflow/Logic/Merging/MergeEnrichmentSink.cs:339` is a no-op with a comment stating the
wrapped sink is owned by the runner (`:22-23`).

**Fix.** Make it a no-op with the same comment, or take an explicit `bool ownsInner`. Do not
leave it implicit — this is the kind of thing that only shows up as a duplicate publish in
production.

## 4. Major (hot path) — per-record string allocation in `Tap`

`Sinks/Logic/CountingSink.cs:103`

`JsonNode.Parse(Encoding.UTF8.GetString(utf8.Span))` allocates a full intermediate `string` for
every record, on the path whose stated reason for existing is that a large page "never resides
fully in memory". `JsonNode.Parse` accepts a span directly, and this repo already does exactly
that in the tap this type is replacing: `WorkflowRunner.cs:524` —
`nodes.Add(JsonNode.Parse(record.Span))`. Verified both span overloads compile on net8.0.

**Fix.** `nodes?.Add(JsonNode.Parse(utf8.Span));` — one line, removes one allocation and one
UTF-8 transcode per record, and matches the existing idiom.

## 5. Minor — `captureNodes` on the streaming path defeats the guarantee the comment claims

`Sinks/Logic/CountingSink.cs:58-61`

The comment says materialising would "silently undo the streaming it exists to provide" — but
with `captureNodes: true`, `nodes` accumulates a `JsonNode` per record for the whole page,
which is a larger footprint than the buffered list would have been. The bytes stream; the
observations do not. The tradeoff is defensible (it is opt-in, and `collect` genuinely needs
the records), but the comment overstates the guarantee. Say plainly that `captureNodes: true`
costs one live `JsonNode` per record for the page's duration, so callers know what they are
enabling on a Tenable-sized chunk.

## 6. Minor — `Records` hands out the live list, and `Drain()` empties it underneath the caller

`Sinks/Logic/InMemorySink.cs:20-23, 61-66`

`Records => _records` returns the backing list itself, not a snapshot. On a **public** type
that means: a caller who holds the reference sees it emptied by an unrelated `Drain()`, a
caller enumerating it while the engine publishes gets `InvalidOperationException`, and a
caller can cast back to `List<>` and mutate the sink's state. Also, `Drain()` copies and then
clears — two O(n) passes where the copy exists only to survive the clear. Document the type as
single-threaded and not safe to read during a run (no lock needed — the engine's pagination
loop and `WorkflowRunner` are both strictly sequential; I found no `Task.WhenAll`/`Parallel`
around any publish call, so this is a documentation gap, not a live race).

## 7. Minor — the tap's hard paths will ship with no test

`CountingSink` and `PublishedPage` are `internal` and there is no `InternalsVisibleTo`
(CLAUDE.md, "On testing internals"). The behaviours most likely to be wrong here — mid-stream
throw, consumer abandoning enumeration early, inner publishing fewer than offered — are not
reachable end-to-end until the wiring exists, and findings 1 and 3 are exactly those paths.
Not a rule violation (the repo accepts this deliberately), but worth naming: the decorator's
risk is concentrated in the paths current visibility makes untestable.

## 8. Observation — visibility split is right, with one coupling to note

`InMemorySink` public matches CLAUDE.md's intended surface (the sink abstraction plus what
standalone/UI callers must construct). `CountingSink` and `PublishedPage` internal is correct
*if* the observing caller lives in this assembly. If the intended consumer is a host adapter
outside it, both must go public together — a public `Pages` property cannot expose an internal
`PublishedPage`. Better to settle that before the package ships than in a patch release.

## 9. Observation — `ARCHITECTURE.md` tree is now stale

`ARCHITECTURE.md:186-187` still shows `Sinks/` containing only
`Contracts/Interfaces/ IExecutionSink`. The commit adds `Sinks/Logic/` and
`Sinks/Contracts/Models/` without updating the tree.

---

## Verified correct

Checked because these are the easy things to get wrong here, and they are right:

- **All ten `IExecutionSink` members are forwarded** by `CountingSink`, including the two
  default-implemented ones (`SetCursorRecoveryState`, `SetControlState`). Nothing is silently
  left on the default no-op while the inner sink expects it.
- **No double counting when `inner` uses the default `PublishStreamAsync`.** The fallback is a
  static helper taking the sink explicitly (`IExecutionSink.cs:51, 70`), so it re-enters
  `inner.PublishBatchAsync`, not the decorator's — one `_pages` entry per page, as intended.
- **The tap enumerates once, propagates cancellation correctly, and disposes cleanly.**
  `[EnumeratorCancellation]` plus `WithCancellation` links the creation-time and
  enumeration-time tokens; abandoning the iterator disposes the source enumerator through the
  generated finally. `ConfigureAwait(false)` throughout.
- **Records forwarded unmodified.** The stream path yields the same `ReadOnlyMemory<byte>` it
  received; the batch path serialises to a *separate* node graph, so `Nodes` cannot alias what
  the inner sink was handed. The "byte-for-byte" claim holds.
- **Nullable `Nodes` is the right shape**, and nullable *elements* match the existing consumer
  signature exactly (`WorkflowRunner.ApplyCounts(..., IReadOnlyList<JsonNode?>? nodes, ...)`).
  Null distinguishes "not requested" from "requested, empty page" — a bool flag would be worse.
- **Not overriding `PublishStreamAsync` in `InMemorySink` is the right call.** The fallback
  buffers a page, which is what an in-memory sink does anyway; overriding would duplicate it.
  One narrow caveat, inherited from the fallback rather than introduced here: for non-object
  records the streaming path lands `$raw` as a `JsonElement`, the buffered mapper as a `string`
  (`Mapping/Logic/ResponseMapper.cs:70`). Object records agree on both paths.
- **Layout and rules comply**: `Logic/` vs `Contracts/Models/` split per CLAUDE.md rule 1, one
  type per file, namespace-by-concept, all methods and classes far under the limits.
