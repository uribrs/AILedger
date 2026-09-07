# Code Review — CollectorExecutor (data handling, algorithms & edge cases)

Reviewer lens: data-structure/allocation choices, algorithmic complexity, serialization/byte
accounting, pagination/loop-termination math, off-by-one / counter-seed errors, empty/oversized/malformed
input, checkpoint round-trip correctness, resume/idempotency. Reviewed files: `Execution/CollectorExecutorRunner.cs`,
`Checkpointing/CheckpointState.cs`, `Seams/Seams.cs`, `Resilience/FailureSeam.cs`, `Composition/StrategyRegistry.cs`,
`Interpreter/*`, `Profile/Profile.cs`, `Strategies/{Pagination,Resilience,Mapping,PageSize}/*`.

---

## Findings

### 1. Cursor token extracted with `GetValue<string?>()` — throws on a numeric/bool cursor — MAJOR
`Strategies/Pagination/CursorPaginationStrategy.cs:20`, `CursorWatermarkStrategy.cs:37`,
`NextUrlPaginationStrategy.cs:22`.
`JsonNav.At(response, spec.NextTokenAt)?.GetValue<string?>()` calls `JsonValue.GetValue<string>()`. For a
JSON node whose underlying value is a **number** (e.g. a numeric `next_id` / page token) or boolean,
`System.Text.Json` throws `InvalidOperationException`, not a JSON/Xml exception. The page-loop catch
filter (`RunFetchStepAsync` line 483) only catches `JsonException`/`XmlException` and (line 490)
`AdapterHttpRequestFailedException`/`BrokenCircuitException`. An `InvalidOperationException` from the
paginator escapes the loop entirely, unwinds past the partial-success handling, and surfaces as an
unhandled crash — losing the partial-success guarantee for any data already emitted this run.
Why it matters: many vendors return integer continuation tokens; the contract claims `ExtractIds`
tolerates non-string scalars (it uses `JsonValue.ToString()`) but the cursor path does not, so the two
code paths disagree on the same data shape. Use `.ToString()` (as `ExtractIds`/`Matches` do) or
`GetValueKind`-guard.

### 2. `cursor` / `cursor_watermark` stop on a full page when the vendor omits an empty terminal page — MAJOR (data loss)
`CursorPaginationStrategy.cs:21`, `CursorWatermarkStrategy.cs:52`.
`hasMore = !string.IsNullOrEmpty(token) && recordsThisPage > 0`. Correct for the empty-terminal-page
convention. But the **inverse** edge is silent truncation: if a vendor returns a full page of records
**and a valid next token** but the runner's `recordsForPaging` is 0 for that page (e.g. a hydrate flow
where `idsPath` resolved to 0 ids on a transiently-degraded query response, yet a cursor token is still
present), pagination terminates and the remaining pages are dropped. The cursor strategies tie "has more"
to records-on-this-page rather than to the presence of the continuation token. For a pure cursor walk the
token is the authoritative signal; gating it additionally on `recordsThisPage > 0` means one empty-but-not-final
page truncates the stream. This is a deliberate-looking guard but it conflates "empty page" with "no more
pages," which is not the same for token-based APIs that can return a sparse intermediate page.

### 3. `recordsForPaging` for hydrate flows counts IDs, not mapped records — pagination math mismatch — MAJOR
`RunFetchStepAsync` lines 424–440, 468.
In the hydrate branch `recordsForPaging = ids.Count` (count of IDs from the *query* response), while the
non-hydrate branch uses `records.Count` (mapped records). The paginator's termination math
(`recordsThisPage >= spec.PageSize`) is then evaluated against the ID count for hydrate flows but the
record count otherwise. For `offset`/`page_number`/`next_url` fallback, `PageSize` is compared to the ID
count — which is correct only if the query page size equals the hydrate input cardinality. If the query
endpoint paginates by a different unit than `PageSize` describes (common: query returns N ids, page size
describes records), the "incomplete final page" detection misfires — either early stop or an extra empty
request. The two counters being semantically different per-branch but fed into the same comparison is an
edge-case trap.

### 4. Watermark advance assumes the watermark field is a top-level string — silently no-ops otherwise — MAJOR
`RunFetchStepAsync` lines 443–447.
`rec[step.Pagination.WatermarkField!] is JsonValue wv && wv.TryGetValue<string>(out var ws)` only advances
the watermark when the mapped record has the field **at the top level** and **as a string**. Two failure modes:
(a) `WatermarkField` names a path the *mapper* renamed/nested — the lookup returns null, the watermark never
advances; (b) the timestamp is emitted as a JSON **number** (epoch) — `TryGetValue<string>` fails, watermark
never advances. A non-advancing watermark is not loud: `cursor_watermark` reset and `cursor_expiry` reset
both fall back to "treat as transient failure / stop" when the watermark is empty (lines 498–504), so a
mistyped/numeric watermark field degrades a hardened deep-scroll vendor into repeated resets-to-nothing or
premature termination. String-ordinal comparison of timestamps (`CompareOrdinal`) is also only correct for
fixed-width ISO-8601/lexicographically-sortable formats; epoch-seconds-as-string of differing widths sort
wrong ("9" > "10").

### 5. Resume resets `ScrollDepth` to 0 — defeats the depth cap across re-dispatch — MINOR
`RunFetchStepAsync` line 388; checkpoint state has no `ScrollDepth` field (`CheckpointState.cs`).
`runCtx.ScrollDepth = 0` at every step entry, and `ScrollDepth` is never persisted. A `cursor_watermark`
flow that defers/resumes mid-scroll restarts its depth counter from 0 each dispatch, so the
`MaxPagesPerScroll` cap is per-dispatch, not per-scroll. If re-dispatches happen often enough the proactive
cursor-aging protection never fires. Functionally tolerable (the reactive `cursor_expiry` path still
catches an actually-expired cursor), but the proactive cap silently doesn't do what it says under resume.

### 6. `SliceByBytes` byte budget is computed on a re-serialization that differs from the published bytes — MINOR
`SliceByBytes` lines 1068–1087, vs publish path `ToBytesAsync`/`r.ToJsonString()` at line 459–460 reuses
the *same* `b` — actually consistent. **However**: `Encoding.UTF8.GetBytes(r.ToJsonString())` is computed
in `SliceByBytes`, then the slice (the `ReadOnlyMemory<byte>` list) is what's published — so the bytes are
reused, good. The remaining issue is allocation: every record is fully serialized to a `byte[]` and held
in memory for the whole slice before publish; for a large page this is the entire page materialized twice
(once as `List<JsonObject>`, once as `List<ReadOnlyMemory<byte>>`). The doc claims "bounded-memory,
page-by-page" — it is bounded *per page* but a single page can be large (no record-count cap, only the
byte-budget *slice* boundary, and slicing happens after full serialization). Not a correctness bug; a
memory-spike risk on fat pages. (Verified the byte reuse is correct — flagging only the double-materialization.)

### 7. Single oversized record silently forms its own over-budget slice — MINOR
`SliceByBytes` lines 1076–1083. The guard `slice.Count > 0 && ... bytes + withNewline > maxBytes` means a
record larger than `maxBytes` is never split (correct — JSON records are atomic) but is also never
rejected here; it's emitted as a lone slice that exceeds the egress budget and is handed to the publisher,
which "enforces its own hard cap" (per the comment). If the publisher's hard cap rejects it, that single
record fails the page and — depending on emitted-count — can flip the whole step to a hard failure. The
behavior is documented but the failure mode (one giant record poisoning a page) is worth an explicit
size-pre-check + skip-with-log rather than relying on the downstream cap. Edge case, low frequency.

### 8. `offset_end = offset + size` uses the *resolved* size but offset advance uses `spec.PageSize` — counter divergence — MINOR
`RunFetchStepAsync` line 400 (`ctx["offset_end"] = offset + size`, where `size = pageSize.Resolve(...)`) vs
`OffsetPaginationStrategy.cs:22` / `NextUrlPaginationStrategy.cs:28` (`NextOffset = ctx.Offset + spec.PageSize`).
The template window emitted to the request uses the *resolved* page size, while the offset advance uses the
*declared* `spec.PageSize`. With the current `StaticPageSizeStrategy` these are equal so it's latent — but
the moment an adaptive page-size strategy is registered (explicitly anticipated in `Seams.cs` and the
PageSize folder), the request window and the offset cursor will desync (overlapping or gapped windows →
duplicate or dropped records). The two should both read the same resolved size. Latent, but a real
counter-seed trap given the codebase's own roadmap.

### 9. `for_each` `MaxFailureRatio` uses strict `>` — ratio exactly at the bound passes — NIT
`RunForEachStepAsync` line 583: `(double)failedItems / units.Count > step.MaxFailureRatio`. With the default
`0.5`, 1 of 2 items failing yields ratio `0.5`, which is **not** `> 0.5`, so it's tolerated. Whether the
bound is inclusive is a policy choice, but the field name "max ratio" reads inclusive ("at most"). Minor
semantic ambiguity; document or use `>=`.

### 10. `Matches` collapses any non-`JsonValue` node to "no match" and uses `JsonValue.ToString()` — NIT
`Matches` line 689. `JsonNav.At(node, cond.Path) is JsonValue v ? v.ToString() : null`. A boolean `true`
stringifies to `"true"` (matchable) but an object/array at the path yields `null` → never matches `until`,
which for a `poll_until` whose completion field is e.g. an array length or nested object means the poll can
**never** satisfy `until` and will run to `max_wait` then hard-fail. The poll condition vocabulary only
supports scalar equality / set membership; if a profile points `until.path` at a non-scalar the failure is
a timeout, not a validation error. Profile validation (`Profile.cs:190`) checks `until.path` is non-empty
but not that it's scalar-resolvable (unknowable at load). Acceptable given the narrow vocabulary; flagging
the silent-timeout failure mode.

### 11. Fingerprint truncated to 16 hex chars (64 bits) — NIT
`Fingerprint` line 1127: `Convert.ToHexString(SHA256...)[..16]`. 64 bits of a SHA-256 as the resume guard.
Collision probability is negligible for the cardinality here (per-tenant per-vendor inputs), and a
collision only causes a wrong-resume which the step-shape/strategy guard (line 259–261) partially catches.
Fine as written; noting the deliberate truncation for the record.

### 12. `ReadStringMap` flattens nested JSON to raw text; `inputs`/`config` are `string→string` only — NIT
`ReadStringMap` lines 1100–1112. A nested object/array under `config`/`inputs` is stored as its raw JSON
text (`GetRawText()`), then folded into the template context as an opaque string. Templating (`{{config.x}}`)
can only substitute it verbatim. This is consistent and documented-by-behavior, but a profile author
expecting `{{config.nested.key}}` gets the whole JSON blob. The template token regex `[\w\.]+` would *parse*
`config.nested.key` but no such key exists in the flat map → renders empty. Expected given the flat model;
noted so the limitation is explicit.

---

## Things that are solid

- **Checkpoint ordering invariant** (state written *before* `AdvancePage`, lines 470–473) is correctly
  implemented, and defer/poll snapshots do not advance the page — matches the stated contract.
- **Checkpoint round-trip**: `CheckpointState` is a flat POCO with explicit version + fingerprint;
  `FromJson` swallows only `JsonException` and returns null (clean fresh-start), version/shape/strategy all
  re-validated on resume (lines 259–261). Output page sequence (`AssetsPage`/`FindingsPage`) is persisted
  and restored so resumed numbering stays contiguous — correct and well-reasoned.
- **`next_url` non-advance guard** (lines 475–479) correctly breaks a pagination cycle when the vendor
  returns the same next URL, and `cursor_watermark` has an analogous repeated-token guard (lines 41–50).
- **`Chunk`** (lines 1114–1118) is correct, including the final short chunk (`Min(size, remaining)`); no
  off-by-one. `for_each` batch vs per-item unit construction (lines 528–530) is correct.
- **XML→JSON normalization** is XXE-safe (DTD prohibited, no resolver) and handles the single-vs-array
  ambiguity coherently with the mapper's `ListAt` tolerance.
- **Registry** is fail-closed with case-insensitive keys and a useful "available:" error; preflight resolves
  every step's strategy before any HTTP (lines 84–99) — good fail-fast.
- **Partial-success preservation** is threaded consistently through `TerminalResult`/`ExecuteDecisionAsync`
  (emitted > 0 ⇒ partial success, never a hard fail).

---

## Verdict

Solid, well-structured declarative interpreter with correct checkpoint round-tripping and partial-success
handling, but it carries a small cluster of data-shape assumptions in the pagination/watermark math — most
notably an uncaught `GetValue<string?>()` on non-string cursor tokens (crash past the partial-success net)
and watermark advance that silently no-ops on numeric/nested timestamp fields — that need fixing before it
can be trusted against vendors whose continuation tokens or watermarks aren't top-level strings.
