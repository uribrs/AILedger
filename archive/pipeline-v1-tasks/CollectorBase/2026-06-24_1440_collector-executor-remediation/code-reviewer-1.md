# Code Review — collector-executor remediation slice

Isolated quality review of the recently-changed code only, judged on its own merits.
Reviewer: senior C# engineer. Verdict at bottom.

## Scope reviewed
1. `JsonNav.StringAt` (Interpreter.cs)
2. Pagination strategies switched to `StringAt` (Cursor / CursorWatermark / NextUrl)
3. `Math.Max(1, size)` clamps in Offset / PageNumber / NextUrl-fallback strategies
4. `StaticPageSizeStrategy.Resolve` clamp
5. `Chunk` clamp + `Prepare`/`PreparedRun` extraction in CollectorExecutorRunner.cs
6. Removal of the `FetchSignal`/`FetchDecision`/`RunContext.Signal` write-only vocabulary

---

## Findings

### Blocking
None.

### Major
None.

### Minor

**M1 — Redundant `BuildSessionSpec` call after the Prepare extraction.**
`CollectorExecutor/Execution/CollectorExecutorRunner.cs:116` (inside `Prepare`) and `:283` (inside
`RunAsync`). `Prepare` builds a `SessionSpec` purely to null-check auth shape and discards it;
`RunAsync` then builds it again to obtain the instance it actually uses. Behavior is correct and
preserved — `BuildSessionSpec`/`BuildAuthSelection` are pure and deterministic, so the second call
yields the same result and the same validation outcome. The cost is one throwaway `SessionSpec`
(carrying secret values) per run. Carrying the built spec on `PreparedRun` would remove the rebuild
but would also place secret material on a record that is *also* produced by the validation-only
`Preflight` path — a worse tradeoff. Acceptable as-is; flagging only so the duplication is a known,
deliberate choice, not an oversight. Severity: minor (borderline nit).

### Nits

**N1 — `StringAt` does not normalize numeric formatting.**
`Interpreter.cs:46-50`. `JsonValue.ToString()` round-trips the *source* token form, so a JSON
exponent literal (`1e3`) yields `"1e3"`, not `"1000"`. Verified empirically on net8.0:
integers/longs/negatives/bools coerce cleanly (`42`, `10000000000`, `-5`, `true`), but exponent and
trailing-zero forms pass through verbatim. Irrelevant for the stated use (opaque/integer cursor and
next-id tokens — no vendor sends an exponent-form cursor), so no action needed; documenting the one
non-obvious coercion edge.

---

## Correctness confirmations (the things the brief asked to verify)

**StringAt coercion is correct, including the JSON `null` token.** `Interpreter.cs:48` —
`At(root, path) is not JsonValue v` returns `null` for object/array. For a JSON `null` *literal*,
`System.Text.Json` materializes a C# `null` node (not a `JsonValue` wrapping null), so the pattern
fails and `StringAt` returns `null` — verified empirically (`o["n"] is JsonValue` == `false`). No
NRE, no `"null"` string. The doc comment ("object/array/absent yields null") is accurate; the JSON
`null` literal also (correctly) yields `null` and could be mentioned, but the behavior is right.
The `v.TryGetValue<string>(out var s) ? s : v.ToString()` order is right: a genuine string is
returned as-is (preserving e.g. an empty string `""`, which the callers then treat as "no token"
via `IsNullOrEmpty`), and only non-string scalars fall through to `ToString()`. `ToString()` on a
`JsonValue` scalar never throws.

**Pagination call-site swap is behavior-preserving for the common case and strictly safer for the
edge.** Old `JsonNav.At(...)?.GetValue<string?>()` threw `InvalidOperationException` on a non-string
token (number/bool), which would escape the page loop's catch filter
(`JsonException`/`XmlException`/`AdapterHttpRequestFailedException`/`BrokenCircuitException` only) and
crash the run. `StringAt` returns the coerced text instead. For a string token the result is
identical. Applied consistently in CursorPaginationStrategy.cs:20, CursorWatermarkStrategy.cs:36,
NextUrlPaginationStrategy.cs:22.

**Clamp interactions are correct and the rationale in each comment is accurate.**
- `OffsetPaginationStrategy.cs:21`, `NextUrlPaginationStrategy.cs:27`: `size = Math.Max(1, spec.PageSize)`
  used for both the short-page stop (`recordsThisPage >= size`) and the offset advance
  (`ctx.Offset + size`). With an unclamped `size <= 0`, `recordsThisPage >= 0` is always true and
  the offset never moves → infinite non-advancing loop. Clamp closes it.
- `PageNumberPaginationStrategy.cs:18`: clamp only affects the stop check (page advance is `+1`,
  independent of size). With `size <= 0` the short-page stop `recordsThisPage >= size` is
  unreachable; the loop then relies solely on `recordsThisPage > 0`, i.e. it runs until an empty
  page. Clamp restores the intended "stop on a short page" semantics. Correct.
- `StaticPageSizeStrategy.cs:10`: `Math.Max(1, declared)`. This is the value the runner injects as
  `{{page_size}}`/`{{offset_end}}` AND (via `runCtx`/`recordsForPaging`) the basis a vendor uses to
  fill a page; clamping here means the offset/page strategies receive a coherent `spec.PageSize`
  too. Note the two clamps are independent (the strategies clamp `spec.PageSize` directly, not the
  resolved value) — that redundancy is fine and defensive; both must hold for the loop to terminate.
- `Chunk` (CollectorExecutorRunner.cs:1101-1103): `size = Math.Max(1, size)` before `i += size`.
  `GetRange(i, Math.Min(size, items.Count - i))` is safe for all `i < Count`. With unclamped
  `size <= 0` the `for` never advances → infinite loop emitting empty/duplicate batches. Clamp is
  correct. Consumers (`hydrate.BatchSize` at :416, `step.BatchSize` at :516) can legitimately be
  0/unset, so the clamp is load-bearing, not theoretical.

**Prepare extraction is faithful and behavior-preserving.**
- Validation ORDER is unchanged and matches the documented fail-closed sequence: payload/yaml shape
  (:73) → profile load (:79) → stream/flow resolve (:85) → steps + registry presence (:90) →
  per-step paginator existence (:93) → mapper/pagesize/failure resolve (:104) → creds/config/inputs +
  base_url (:112) → auth shape (:116). Every error CODE and MESSAGE is preserved verbatim
  (`INVALID_PAYLOAD`, `UNSUPPORTED_EVENT_TYPE`, `MISSING_STRATEGY_REGISTRY`, `UNKNOWN_STRATEGY`,
  `MISSING_STRATEGY`, and the auth `INVALID_PAYLOAD` text). The `try/catch` filters
  (`InvalidOperationException or YamlException` for load; `InvalidOperationException` for strategy
  resolve) are unchanged.
- `Preflight` (:128) returns `Prepare(...).Failure` — exactly the prior pre-bus fast-fail contract;
  the adapter call site (CollectorExecutorAdapter.cs:145) is unchanged and still short-circuits on
  non-null.
- `RunAsync` unpacking (:203-219) destructures all 15 `PreparedRun` members; each is then used
  exactly where the inline derivation previously fed it. Nothing is dropped: `resilience` flows to
  poll/session, `config` to the context builders, `inputs` to fingerprint+context, `vendorName` to
  progress/results, `fingerprint` to checkpoint guards. Nothing is duplicated except the deliberate
  `BuildSessionSpec` rebuild (M1) — and that rebuild is *required* because Prepare intentionally does
  not carry the (secret-bearing) spec on the record.
- The `prep!` null-forgiving at :205 is sound: the immediately-preceding `validationFailure is not
  null` early-return (:204) guarantees `prep` is non-null on the fall-through (the tuple is
  `(failure, null)` XOR `(null, run)` at every return site in Prepare). Idiomatic.
- `PreparedRun` as a positional `record` with a 15-arg ctor is at the edge of comfort but justified:
  it is a private, single-producer/single-consumer DTO whose whole purpose is to be the single source
  of truth, and the construction site (:121) and unpack site (:205) are adjacent in the same file.
  No builder/named-args ceremony warranted. Clean.

**Dead-vocabulary removal is complete.** `grep` across the solution (excluding bin/obj) for
`FetchSignal`, `FetchDecision`, `RunContext.Signal`, and `.Signal` returns nothing. `RunContext`
(Seams.cs:29-58) has no `Signal` member and its class doc (:26-27) now correctly states a strategy
signals a reset via the returned `Paginator.Step` (NextCursor:null, HasMore:true), which is exactly
what CursorWatermarkStrategy.cs:32/45 does. CursorWatermarkStrategy no longer references the removed
type. No orphaned usings, no compile-time dangling references.

---

## Verdict
Clean. 0 blocking, 0 major, 1 minor (deliberate redundant session-spec build), 1 nit (numeric
formatting passthrough in StringAt). All clamps close real infinite-loop paths, `StringAt` coercion
(including the JSON `null` literal) is correct and verified empirically, the `Prepare` extraction
preserves validation order/codes/messages exactly with nothing dropped or wrongly duplicated, and
the write-only signal vocabulary is fully removed. Ship it.
