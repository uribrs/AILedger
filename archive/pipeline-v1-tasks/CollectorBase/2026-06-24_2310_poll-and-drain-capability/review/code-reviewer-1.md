# Code review — `poll_and_drain` capability

Reviewed in isolation against the changed regions. Build + the 8 `PollAndDrain_*`/`BestEffort_*`
tests pass. Findings ranked by severity. Line numbers are against the files as reviewed.

## Summary

The feature is correct on the happy path and the tested failure paths, and it reuses the existing
substrate (item tolerance, failure strategy, checkpointing) rather than reinventing it. The
termination logic is sound — no infinite spin, no busy-loop at `poll_interval = 0`, no early exit
with undrained items. The two material concerns are both about the processed-set mechanism: a
**checkpoint-write storm** (O(items) full-state serializations per poll cycle, each re-serializing
the whole growing set) and a **reserved-key collision** that is silent and unguarded. Everything
else is minor/nit. No blockers.

---

## Major

### M1. Checkpoint write storm + O(n²) serialization in the drain loop
`CollectorExecutorRunner.cs:841-846` — `WriteCheckpoint` is called **once per drained item**, inside
the `foreach (var item in available)` loop. Each call (`WriteCheckpoint`, lines 924-942) serializes
the *entire* `RunContext` state to JSON via `CheckpointState.ToJson()`, including
`CaptureLists.ToDictionary(...)` — which now contains the full `__drained_` set rebuilt by
`processed.ToList()` on line 842 immediately before. For an export of N chunks this is N checkpoint
writes, each copying and serializing an O(N) set → **O(N²) total work and O(N²) bytes churned**.
Tenable exports routinely have thousands of chunks; `max_wait_seconds: 10800` in the shipped profile
implies large jobs are expected.

Also note line 842 (`processed.ToList()`) and line 936-937 (`ToDictionary` deep-copies the lists)
each rebuild the list — two allocations of the whole set per item.

Suggested fix: checkpoint at **cycle granularity**, not item granularity. Drain all newly-available
items in the inner `foreach`, then write the checkpoint once after the loop (and on the
externalize/propagate paths, which already do). Resume re-granularity stays at the poll cycle, which
is acceptable because the inner item fetch is idempotent (the same dedup rule for_each relies on).
If per-item durability is genuinely wanted, persist only the delta — but that needs checkpoint-format
support and is almost certainly over-engineering here.

### M2. Reserved processed-set key can silently collide with a user capture_list, and is unbounded
`CollectorExecutorRunner.cs:757` — `var processedKey = "__drained_" + stepIndex;` is stored in the
same `runCtx.CaptureLists` namespace that user `capture_list` / `accumulate_list` keys land in
(`ApplyCaptures`, lines 904-917). Nothing reserves or rejects `__drained_<n>` at profile-load time
(`Profile.cs` `Validate`, lines 206-217 validate `poll_and_drain` but never the capture key space).
A profile that declares `capture_list: { __drained_1: "..." }` on step 1 would have its captured list
overwritten by the drain bookkeeping (or vice versa), corrupting either the user's data or the
processed set — and it would do so silently.

Two sub-issues:
- **Collision**: low probability but unguarded and silent. Fix: validate in `ProfileLoader.Validate`
  that no `capture`/`capture_list`/`accumulate_list` key starts with `__` (reserve the prefix), OR
  move the processed set out of `CaptureLists` entirely into a dedicated `CheckpointState` field
  (cleaner — see below).
- **Unbounded growth**: the set only ever grows; it is never trimmed. Combined with M1 this is the
  memory cost. A dedicated typed field on `CheckpointState` would at least make the cost explicit
  and stop it from masquerading as user capture data.

Recommendation: a first-class `CheckpointState.DrainedItems` (or `Dictionary<int, List<string>>`
keyed by step) is the honest model. It removes the collision risk and the string-key hack in one
move. If that is too much churn, the prefix-reservation validation is the minimum acceptable guard.

---

## Minor

### m1. Large duplicated block between `RunPollUntilAsync` and `RunPollAndDrainStepAsync`
`CollectorExecutorRunner.cs:636-685` vs `763-871`. The max-wait check, the poll-request construction
(`BuildContext` + capture injection + `BuildUrl` + method/body), the JSON/XML parse catch, the
`AdapterHttpRequestFailedException`/`BrokenCircuitException` catch + `ExecuteDecisionAsync` fallback,
the `inProcess` `Task.Delay(interval > Zero ? interval : 1s)` floor, and the externalize-via-
`WriteCheckpoint`+`PartialResult` tail are essentially copy-pasted. That is ~40 lines duplicated.
This is a maintenance hazard: a fix to the poll-request or externalize logic must be made in two
places. Suggested fix: extract a private `PollOnceAsync(...)` returning the parsed node (or a
terminal `StepResult`), and a `ExternalizeOrDelayAsync(...)` helper, and have both step kinds call
them. Not a blocker — the duplication is faithful, not divergent — but it will drift.

### m2. `statusVal` extraction uses raw `JsonValue.ToString()` instead of `StringAt`
`CollectorExecutorRunner.cs:804` — `JsonNav.At(node, u.Path) is JsonValue sv ? sv.ToString() : null`.
`JsonNav.StringAt` (Interpreter.cs:46-50) exists precisely to coerce non-string tokens safely and is
used elsewhere. Here a JSON object/array status would yield `null` (fine) and a numeric status would
`ToString()` (fine), so this is currently harmless, but it duplicates logic that has a named helper.
Use `JsonNav.StringAt(node, u.Path)` for consistency.

### m3. `attempted` is passed as both the log index and the checkpoint `forEachIndex`, but is meaningless there
`CollectorExecutorRunner.cs:822` passes `attempted` as the `itemIndex` argument to
`RunToleratedItemAsync`, which threads it into the inner fetch's `WriteCheckpoint` as `forEachIndex`
(via `RunFetchStepAsync(... forEachIndex: itemIndex)`, line 705 / 482). That inner checkpoint is then
immediately overwritten by the drain-level checkpoint on line 845 with `forEachIndex` defaulted to 0.
So the `forEachIndex = attempted` write is dead — it never survives. Harmless, but it muddies the
"what does forEachIndex mean for poll_and_drain" question (answer: nothing; resume keys off the
processed set, not the index). Consider passing `0` and using a separate local only for the log
message, with a one-line comment that poll_and_drain resume is set-based, not index-based.

### m4. Items at `drain_path` that are not scalars are silently dropped
`CollectorExecutorRunner.cs:815` — `JsonNav.ListAt(node, drainPath).OfType<JsonValue>()`. If a vendor
returns the drain list as an array of objects (e.g. `[{ "id": "c1" }, ...]` rather than `["c1", ...]`),
every element is filtered out by `OfType<JsonValue>()`, the drain silently does nothing, and the step
either loops to `max_wait` (if `until` never holds) or exits "successfully" with zero records once
`finished`. Tenable returns scalar chunk ids so the shipped profiles are fine, but the failure mode is
invisible. Suggested fix: if `until` holds (`finished`) and `available` parsed to empty while the raw
node at `drain_path` was a non-empty array, log a warning (mirrors the empty-list warning the for_each
path already emits at lines 544-546).

---

## Nit

### n1. `PadTolProfile` keeps `continue_on_item_failure: true` but the comment path differs
`CollectorExecutorTests.cs:1693` derives `PadTolProfile` from `PadProfile` (which has
`continue_on_item_failure: true`, `max_failure_ratio: 0.5`). The tolerance test asserts 1-of-3 (`bad2`)
is tolerated. Correct, but the assertion `Assert.Equal(2, records)` is the only proof the bad item was
*skipped* rather than *never attempted*; it is adequate but would be stronger if it also asserted the
`d1`/`d3` chunk URLs were requested and `bad2` returned 404 (i.e. attempted-then-skipped, not skipped-
because-unreached). Tautology risk is low here, just slightly under-specified.

### n2. Real `Task.Delay` backoff runs in the per-item-retry test
`CollectorExecutorRunner.cs:716` — `Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(attempt,4))), ct)`.
`PollAndDrain_PerItemRetryBudget_...` exercises this with a single transient failure: attempt 0 fails →
**real 1-second sleep** → attempt 1 succeeds. So that test (and `ForEach_YieldAfter...`, which is by
design) carries a genuine ~1s wall-clock cost. Not flaky (deterministic, single retry), but the suite
pays real seconds for retry coverage. If the budget retry count ever grows in a test, the backoff is
1→2→4→8→16s and will dominate runtime. No timing-dependent *assertions* exist, so no flakiness — just a
cost note. Consider making the backoff base injectable/clampable for tests if retry depth increases.

### n3. The interleave assertion is a genuine proof — confirmed
`CollectorExecutorTests.cs:1287-1290` — `firstChunk < lastStatus` against `/pad/status` returning
`PROCESSING+[d1]` on call 1 and `FINISHED+[d1,d2]` on call 2. URL order is status1, chunk-d1, status2,
chunk-d2, so `firstChunk` (d1) precedes `lastStatus` (status2). This *does* prove d1 was drained while
the job was still PROCESSING (not after FINISHED). Real proof, not tautological. No change needed —
flagged only because the prompt asked to scrutinize it.

### n4. `MaxWaitSeconds` default of 3600 is duplicated as a magic literal
`CollectorExecutorRunner.cs:633` (poll_until) and `751` (poll_and_drain) both hardcode
`step.MaxWaitSeconds > 0 ? ... : 3600`. Minor; a shared const would prevent the two defaults drifting.

---

## Things checked and found correct (no action)

- **No infinite spin / early exit**: exit gate `finished && !available.Any(undrained)` (line 856) is
  correct; the unbounded case is capped by `max_wait` (lines 765-767). At `poll_interval = 0` the
  in-process delay is floored to 1s (line 861) — no busy-loop.
- **Exception-filter ordering**: the `NotFound`-specific catch (line 786) precedes the general
  `AdapterHttpRequestFailedException` catch (line 793). C# evaluates filters top-down, so 404 →
  `EXPORT_GONE` wins as intended.
- **TransientFailure vs Failure** in `RunToleratedItemAsync` (lines 707-723): transient with budget →
  retry; tolerated permanent → skip; everything else → propagate. Matches the documented contract and
  the partial-success invariant (no data loss on propagate; resume re-runs the item).
- **Checkpoint-before-advance**: `poll_and_drain` never calls `AdvancePage`; it persists position via
  `WriteCheckpoint` before returning a terminal/partial. The inner fetch obeys the
  state-before-`AdvancePage` ordering (lines 480-483).
- **async/await**: `ConfigureAwait(false)` is applied consistently on the new awaits (lines 779, 821,
  823, 716, 433, 442). `CancellationToken` is propagated into every HTTP call and `Task.Delay`. No
  sync-over-async.
- **`best_effort` block** (lines 425-441): catch filter is scoped to transport/parse exceptions
  (`AdapterHttpRequestFailedException`/`BrokenCircuitException`/`JsonException`/`XmlException`); it does
  not swallow `OperationCanceledException`. Correct — a cancel still propagates.
- **Profile validation** (`Profile.cs:206-217`): `poll_and_drain` requires `request.path`, `until.path`,
  `drain_path`, and a fetch `step`; rejects a non-fetch sub-step. Adequate.
