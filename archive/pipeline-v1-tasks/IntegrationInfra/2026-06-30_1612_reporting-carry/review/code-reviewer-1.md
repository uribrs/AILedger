# Code Review — Reporting Concern (IntegrationInfra)

**Reviewer role:** Independent senior-engineer code review, isolated from any requirements/contract.
**Stack:** C# / .NET 8, System.Text.Json, in-proc events.
**Change type:** Shared library code (public contract / SDK-level), relocated largely verbatim from a battle-tested source.
**Risk level:** Medium overall. The serialization converter and envelope builder are on the publishing path (Medium); the in-proc hub/raiser and diagnostics are best-effort, off the critical path (Low–Medium).

Scope reviewed: `Reporting/**` plus `Envelopes/Common/{AdapterStatus, AdapterStatusJsonConverter, AdapterEventMetadata}.cs` and the two test files. Build + 15 existing tests pass.

Bounding context honored: pre-existing verbatim style not flagged; best-effort in-proc forwarding swallowing errors is not flagged as a defect (risks noted only); `AdapterError`/shared shapes living in `Envelopes.Common` is accepted as deliberate.

---

## Summary by severity

| Severity | Count | Items |
|----------|-------|-------|
| Critical | 0 | — |
| High     | 0 | — |
| Medium   | 3 | Hub/raiser + diagnostics entirely untested (M1); event-handler snapshot races in best-effort raiser (M2); `AdapterError` mutable `set` vs init-only envelope style (M3) |
| Low      | 4 | `firstSignal` double-equality comparison redundancy (L1); diagnostics has no "first wins"/ordering guarantee under concurrency edge (L2); `Read` accepts non-`success/failed/partial` numeric token only by type-check, fine but error message loses original (L3); regex redaction scope (L4) |
| Nit      | 3 | `var s` naming in converter; `Errors` default allocation; XML-doc duplication |

No blockers. The serialization core (the highest-traffic, hardest-to-change surface) is correct and well-tested. The concerns are concentrated in untested, currently-unwired components.

---

## Correctness — Serialization (AdapterStatusJsonConverter)

The converter is **correct and the most important thing here is solid.**

- `Read`: rejects non-string tokens, rejects empty/whitespace, trims + `ToLowerInvariant()` before matching, throws `JsonException` on unknown — all idiomatic STJ converter behavior. Culture is handled correctly: `ToLowerInvariant()` (not `ToLower()`) avoids the Turkish-I class of bug. **Good.**
- `Write`: exhaustive switch, throws on undefined enum values (defends against a future enum member or a cast garbage value). **Good.**
- Round-trip is verified by test; case-insensitivity and trimming are verified; invalid `""`/`"unknown"`/`123` all verified to throw.

One observation, not a defect: `Read`'s unknown-value branch interpolates the **original** `s` (`'{s}'`) while matching on the normalized form — that's actually the better choice for diagnostics (shows what the caller sent). Fine.

Verdict: no action.

---

## Medium

### M1 — Hub, raiser, diagnostics, enricher, and event-args are entirely untested (and currently unreferenced)

**Problem.** A repo-wide search shows nothing outside `Reporting/**` references `AdapterInProcEventHub`, `AdapterInProcEventRaiser`, `IAdapterEventSink`, `BatchProducedEventArgs`, `CheckpointAdvancedEventArgs`, `AdapterRunDiagnostics`, or `AdapterResultDiagnosticsEnricher`. The only tests cover the envelope builder and the status converter. So roughly half the concern — including the only stateful, concurrency-touching code (`AdapterRunDiagnostics`) and the only redaction logic (`AdapterResultDiagnosticsEnricher`) — has zero coverage.

**Impact.** Medium. For verbatim-relocated code this is acceptable as a floor, but the diagnostics enricher contains real logic that can regress silently:
- The secret-redaction regex (`client_secret`, `token`, etc.).
- The 2048-char truncation + `"..."` suffix.
- The `ShouldIncludeFailure` precedence (`exception is not null || !result.Success && !string.IsNullOrWhiteSpace(...)`) — operator precedence here is correct (`&&` binds tighter than `||`), but it is exactly the kind of expression that wants a regression test pinning the intent.
- `AdapterRunDiagnostics` first/last/count snapshot semantics.

**Recommended fix.** Add focused unit tests (local patch, not refactor):
1. `AdapterRunDiagnostics`: first-signal-wins, last-signal-updates, count increments, `Snapshot()` returns `Empty` before any signal, `ShutdownRequested` flips. A small concurrency test (N threads recording, assert count == N) is cheap and worthwhile given the lock.
2. `AdapterResultDiagnosticsEnricher`: redaction of a `token=abc` in a message; truncation past 2048; failure block included on exception vs omitted on clean success; `FlowLifecycle` shape when no shutdown vs shutdown.
3. Raiser: `handler is null` is a no-op (no throw); handler receives the mapped args. This directly closes the "forwarding untested" gap called out.

Defer-able but recommended before this concern is wired into a collector.

### M2 — Best-effort raiser reads the handler via a delegate each call; multicast + cross-thread visibility

**Problem.** `AdapterInProcEventHub` captures `Func<EventHandler<...>?>` accessors and calls `_getProgress()` etc. at raise time, passing the returned delegate to the static raiser, which does `handler?.Invoke(...)`. This is the standard "snapshot the delegate then null-check" pattern and is correct against the classic unsubscribe-between-check-and-invoke race **for a single delegate read**. Two real-world notes:

1. The `Func` accessors presumably return a backing field (`() => Progress` on the owner). If the owner's event field is read on a different thread than where subscribers mutate it, there is no memory barrier guaranteeing the latest delegate is observed. In practice .NET field-like events compile to `Interlocked.CompareExchange` on add/remove, and the read is a plain field read; visibility is "eventually" but not guaranteed without a barrier. For best-effort telemetry this is **acceptable** — worst case is a missed or one-stale forward — but it should be understood, not assumed race-free.
2. Multicast exception isolation: if any subscriber throws, `Delegate.Invoke` stops invoking the **remaining** subscribers and propagates. Per the stated bounding context, swallowing forwarding errors is intentional and not a defect — but note the current code does **not** swallow at the raiser level; it propagates to the caller of `Progress/Completed/Error`. If "must never change publishing semantics" is the contract, the swallow must live in the **calling collector**, because nothing here protects it. Flagging so the boundary is explicit: as written, a throwing subscriber **will** surface into whoever calls the hub.

**Impact.** Medium if a caller assumes the hub itself is exception-isolated (it is not). Low if callers already wrap.

**Recommended fix.** Either (a) document on `AdapterInProcEventHub`/raiser that exception isolation is the caller's responsibility, or (b) if these are meant to be the best-effort boundary, wrap each `handler?.Invoke` in try/catch and iterate `GetInvocationList()` so one bad subscriber doesn't starve the rest. Local patch. Decide deliberately — do not leave it ambiguous.

### M3 — `AdapterError` uses mutable `set` while every sibling DTO is `init`-only

**Problem.** `AdapterError` properties are `{ get; set; }`. Every other envelope record in `Envelopes.Common` and `Reporting` is `{ get; init; }` (immutable-after-construction). `AdapterError` is carried inside `AdapterProgressPayload.Errors` (an `IReadOnlyList`) and `AdapterPartialCompletionMetadata.Error`.

**Impact.** Medium-low. The `IReadOnlyList` only protects the list, not the elements — a holder of the payload can mutate an error's `Message`/`Code` in place after the envelope is built, defeating the immutability the rest of the design relies on. For a serialize-and-publish DTO this is mostly theoretical, but it is an inconsistency that invites accidental post-build mutation.

**Recommended fix.** Change to `init` for consistency with the rest of the envelope shapes. Local patch. (If a deserializer or vendor code relies on settable `AdapterError`, keep as-is and note it — but given the verbatim-relocation context, verify before changing.)

---

## Low

### L1 — Redundant double equality in `BuildFlowLifecycleData`
`AdapterResultDiagnosticsEnricher.BuildFlowLifecycleData` guards the `firstSignal` block with both `!ReferenceEquals(first, last)` **and** `first != last`. For a `record`, `!=` is value equality, which already returns false when references are equal; and when first IS the same reference (the common single-signal case, since `_firstShutdownSignal ??= signal; _lastShutdownSignal = signal;` makes them the same object on the first signal) the `ReferenceEquals` short-circuits. The value comparison only matters if two *distinct* signal objects are *value-equal* (same source/reason/message/timestamp), which is plausible but harmless either way. The combined check is defensible (skip the redundant `firstSignal` when it would be identical content) but the `ReferenceEquals` is then strictly redundant with `!=`. Minor clarity cost. Optional simplification: keep only `first != last`.

### L2 — `AdapterRunDiagnostics` ordering under concurrency is "first observed wins," not "earliest timestamp wins"
`RecordShutdownSignal` sets `_firstShutdownSignal ??= signal` and `_lastShutdownSignal = signal` under the lock, in arrival order. If two threads record near-simultaneously with out-of-order `observedAtUtc`, "first" reflects lock-acquisition order, not the smaller timestamp; "last" reflects the last to acquire, not the largest timestamp. For shutdown-signal diagnostics this is almost certainly fine (and arguably the desired "what did we see first" semantics), but it is worth a one-line doc note so a future reader doesn't assume timestamp ordering. No code change required.

### L3 — Converter loses original token in the non-string path is fine; no fix
Non-string tokens throw with `reader.TokenType` (e.g. `Number`) rather than the value. Adequate for a converter. No action — listed only to confirm it was considered.

### L4 — Redaction regex only covers `key=value` query-style pairs
`SensitiveQueryValueRegex` redacts `token=...`, `client_secret=...`, etc. It will **not** catch secrets embedded as JSON (`"token":"abc"`), bearer headers (`Authorization: Bearer abc`), or basic-auth in URLs (`https://user:pass@host`). Given this runs on exception/result messages that may include serialized payloads or `HttpRequestException` text containing a full URL, the coverage is partial. Not a regression (verbatim), but a real residual leak risk in diagnostics output. Recommend noting as a known limitation; expanding patterns is a deferred enhancement, not merge-blocking.

---

## Nits

- **N1.** `var s = reader.GetString();` — single-letter name in the converter; `raw`/`value` reads better. Pre-existing style; leave unless touching the file.
- **N2.** `AdapterProgressPayload.Errors` defaults to `Array.Empty<AdapterError>()` and the builder also coalesces `errors ?? Array.Empty<...>()` — both correct and zero-alloc (`Array.Empty` is cached). No change.
- **N3.** `AdapterDoneEnvelope` and `AdapterProgressEnvelope` duplicate the `topic/vendor/correlationId/timestamp` header verbatim. Extracting a base record would couple two independently-evolving wire shapes; the duplication is the cheaper choice. No change — explicitly not recommending a refactor here.

---

## Things verified and found correct (no action)

- Status converter round-trip, culture-invariant casing, error handling — solid and tested.
- `AdapterEnvelopeBuilder`: `Topic` constant, `Timestamp` default via `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()` (UTC, ms — matches the documented contract), null-coalescing of `errors`, guard clauses (`ThrowIfNullOrWhiteSpace`/`ThrowIfNull`) on all public entry points, and the `ToEventMetadata` field mapping (note: `InstanceOid` is intentionally set to `correlationId`, with `InstanceId` carried separately from run metadata — consistent across both Build paths). Field mapping is 1:1 and correct.
- `AdapterDonePayload.PartialCompletion` uses `[JsonIgnore(WhenWritingNull)]` so a non-partial done omits the field — correct.
- `AdapterPartialCompletionMetadata.AdditionalData` uses `OrdinalIgnoreCase` extension-data dict — sensible for vendor keys.
- Diagnostics lock discipline: `Snapshot()` reads all three fields under the same `_gate` it writes under — consistent, no torn reads. `AdapterRunDiagnosticsSnapshot.Empty` is a cached static singleton — fine since the record is immutable.
- No `IDisposable`/event-handler subscription is *owned* by these types — the hub holds `Func` accessors, not subscriptions, so there is **no event-handler memory leak originating here**. (Leak risk, if any, lives in whoever wires the owner's events; out of scope.)

---

## Bottom line

Merge-able. Zero blockers, zero high. The load-bearing, hard-to-change surface (status converter + envelope builder) is correct and tested. The actionable items are: add tests for the stateful/redaction/forwarding components (M1), make the exception-isolation boundary of the best-effort hub explicit one way or the other (M2), and align `AdapterError` mutability with its siblings (M3). All are local patches; none require a refactor.
