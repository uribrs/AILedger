# Code review — CollectorExecutor (adapter / runner / checkpoint / seams / composition / strategies)

Reviewer lens: correctness & runtime behavior (control flow, async/cancellation, exception flow, resource/lifetime, concurrency/shared-mutable-state, null/boundary, crash/hang/double-count/data-loss). Reviewed as written, on its own merits.

## Overall verdict
Solid, carefully-reasoned interpreter with disciplined checkpoint ordering, fail-closed strategy resolution, and correct partial-success handling; a small number of unguarded YAML-driven boundary inputs can hang the runner, and one declared seam (`FetchSignal`) plus one declared config field (`request.headers`) are dead.

---

## Findings

### 1. [MAJOR] Unbounded YAML `batch_size`/`page_size` of 0 hangs the runner (infinite loop)
`CollectorExecutorRunner.cs:429` (`Chunk(ids, hydrate.BatchSize)`), `Chunk` impl at `:1114-1118`.
`HydrateSpec.BatchSize` (`Profile.cs:132`) defaults to 100 but is never validated, and `ValidateFetchStep` (`Profile.cs:218-229`) does not check it. A profile with `hydrate.batch_size: 0` (or negative) makes `Chunk` loop forever: `for (i = 0; i < items.Count; i += size)` never advances when `size <= 0`, and `GetRange(i, Math.Min(size, ...))` yields empty batches indefinitely. The loop issues no HTTP and never cancels — a hard hang (worker pinned, no progress, no timeout from the page-loop catch since nothing throws).
Same class of bug via pagination: `OffsetPaginationStrategy.cs:21-22` and `PageNumberPaginationStrategy.cs:18-19` compute `hasMore = recordsThisPage >= spec.PageSize && recordsThisPage > 0` and advance `NextOffset = ctx.Offset + spec.PageSize`. With `page_size: 0`, every non-empty page has `hasMore == true` while the offset never advances → the same page is re-fetched forever. `PaginationSpec.PageSize` (`Profile.cs:122`) is unvalidated.
Why it matters: a single mis-typed YAML value (the whole point of this design is hand-written YAML) takes the collector from "validation failure" to "silent infinite loop." Validate `page_size >= 1` and `hydrate.batch_size >= 1` at profile load, or guard `size <= 0` in `Chunk`/the offset strategies.

### 2. [MAJOR] `request.headers` is declared, parsed, and silently ignored
`RequestSpec.Headers` (`Profile.cs:114`) is deserialized from YAML, but neither `BuildUrl` (`:1053-1060`), `BuildHydrateRequest` (`:1019-1051`), nor the send path (`AdapterHttpClient.SendForStringAsync`, which has no header parameter) ever applies it. A profile author who declares `request: { headers: {...} }` gets no headers and no error. No shipped profile in `integrations/` currently uses `headers:`, so this is a latent gap rather than an active production bug — but it is silent config loss the moment someone relies on it (a vendor needing e.g. `Accept`/`X-Api-Version`). Either wire headers through a new `AdapterHttpClient` overload or reject `headers` at validation so the contract is honest.

### 3. [MINOR] `FetchSignal` / `RunContext.Signal` is dead code; the documented "cross-seam signal" is never read
`Seams.cs:58-76` defines `FetchSignal`/`FetchDecision` and `RunContext.Signal`, and `CursorWatermarkStrategy.cs:29,45,54` writes `ctx.Signal`. The runner never reads `runCtx.Signal` (confirmed by grep — only the strategy writes it). The proactive depth-cap reset still *works*, but only incidentally: the strategy returns `NextCursor: null, HasMore: true`, the runner sets `cursor = null` at `:471` and re-templates `{{watermark}}` at `:398` on the next iteration. The "decision vocabulary" the seam doc (`FailureSeam.cs:6-21`, `Seams.cs:11-15`) advertises as the coordination mechanism is therefore vestigial for pagination; only the failure-path `ResetAndContinue` (`:496`) is real. Risk: a future strategy author will reasonably assume setting `Signal` does something (e.g. `DeferUnbudgeted`, `Fail`) and it will be silently ignored. Remove the enum/field or actually consume it.

### 4. [MINOR] Two distinct cursor-reset code paths reach the watermark, only one is exercised by the loop guard
`CursorWatermarkStrategy.cs` resets via the paginator (`HasMore: true, NextCursor: null`), while `CursorExpiryFailureStrategy.cs:35` resets via `FailureResolution.ResetToWatermark` → runner `:496-508`. The failure path explicitly guards "reset requested without a watermark floor" (`:498-504`) and degrades to a transient failure. The paginator path has no such guard: if `MaxPagesPerScroll` is hit but `Watermark` is empty, the strategy's `if (...!string.IsNullOrEmpty(ctx.Watermark))` simply falls through to the normal walk — correct — but the runner trusts the strategy's `NextCursor: null` blindly. This is currently safe because `CursorWatermarkStrategy` only nulls the cursor when a watermark exists; it's a latent coupling (runner correctness depends on an invariant maintained only inside one strategy). Worth a comment or a defensive check at `:471`.

### 5. [MINOR] `next_url` non-advance guard compares only against the URL just used, not the seed
`RunFetchStepAsync` `:475-479` breaks when `pstep.NextUrl == usedUrl` (the URL fetched this iteration). Good loop-cycle guard. But on a resumed run the first iteration's `usedUrl = nextUrl` comes from the seed; if the vendor returns that same URL again, the guard fires and stops — correct. However a 2-cycle (A→B→A→B) is not detected. Low likelihood for real OData/`@odata.nextLink` feeds; noting for completeness, not asking for a visited-set.

### 6. [NIT] `poll_until` max-wait is wall-clock measured but the in-process delay isn't cancellation-bounded against it
`RunPollUntilAsync.cs:635-685`: `Task.Delay(interval, ct)` (`:675`) honors `ct` (good), and `maxWait` is checked at loop top (`:637`). If `interval` is large but still `<= MaxInProcessDelaySeconds`, a single delay can overshoot `maxWait` before the next check — the poll then runs one extra HTTP request past the deadline before failing. Bounded and harmless (one extra request), but the deadline is soft. Minor.

### 7. [NIT] `ParseEnum` underscore-stripping can mis-map values silently
`CollectorExecutorRunner.cs:993-996`: `value.Replace("_", "")` then `Enum.TryParse(ignoreCase: true)`, falling back silently on miss. A typo'd `auth.params` enum (e.g. `timestamp_format: unix_milisecond`) silently falls back to the default rather than failing validation. For auth shape this can produce a hard-to-diagnose 401 at runtime instead of a clean validation error. Consider surfacing unparseable enum params at preflight.

---

## Things that are solid (verified, not nits)

- **Checkpoint ordering invariant holds**: state is written (`WriteCheckpoint`) before `AdvancePage` at `:472-473`; defer/wait/cancel paths return terminal `StepResult` *after* having checkpointed their position (poll externalize `:680-682`, for_each item boundary `:559-571`). Matches the stated invariant.
- **Partial-success-wins is consistently applied**: `TerminalResult` (`:821-827`), `PartialSuccess` (`:829-831`), and the `ExecuteDecisionAsync` `Rethrow`/`CompletePartial` branches (`:799-813`) all preserve emitted data. The adapter boundary (`RunAndCountAsync` `:255-283`) only calls `ExtractCount` on `Success`, and `PartialSuccess` returns a `Success` status carrying `records` — `ExtractCount` (`:308-314`) falls back to `records` when `total` is absent, so no double-count and no lost count.
- **No double-count across steps**: comment at `:331-333` is correct — `StepResult.Done`'s terminal carries its own counts in `Data`; `Completed` carries `emitted/findings/pages`; accumulation happens only on the `Completed` path.
- **Fail-closed strategy resolution before any HTTP**: `:204-224` resolves every step's paginator + mapper + page-size + failure strategy and returns `ValidationFailure` on a miss, before the session is created. `StrategyRegistry` (`Composition/StrategyRegistry.cs`) throws on unknown names (no silent fallback). Preflight (`:62-110`) mirrors this pre-bus.
- **Exception filters are precise and don't over-swallow**: the page-loop catches are narrowed to `JsonException/XmlException` (`:483`) and `AdapterHttpRequestFailedException/BrokenCircuitException` (`:490`); `ServerSuggestedRetryDelayException` and `OperationCanceledException` correctly bypass them to the outer handlers (`:347-357`). `ProfileLoader` catch filters (`:71,:192,:154`) are scoped to `InvalidOperationException/YamlException` rather than blanket `Exception`.
- **Session lifetime**: `await using var handle = provider.Create(...)` (`:302`) disposes the per-run session deterministically; `AdapterHttpClient` wraps the handle's session without owning it. Adapter is stateless per run; `RunContext` is per-run, so no shared mutable state across concurrent events.
- **Auth shape validated eagerly to avoid escaping crashes**: the `api_key`/`hmac`/`basic` cases return `null` (→ clean validation failure) for shapes that `http.package`'s auth ctors would otherwise throw on outside the page-loop catch (`:910-916,:955-965,:936-937`). Good defensive reasoning, explicitly documented.
- **Resilience-on-resilience avoided**: `FlowRetryPipelineBuilder => ResiliencePipeline.Empty` (`CollectorExecutorAdapter.cs:200,:240`) prevents the bus from re-retrying the interpreter's already-resilient terminal result. The `collector-executor.classified` policy (`:291-301`) preserves real error codes through the strategy fallback.
- **Cursor-TTL ages from `WrittenAtUtc`, not `CreatedAtUtc`** (`:270`) — correct: row age vs. pause age distinction is real and the comment explains it.
- **`CheckpointState.FromJson` swallows only `JsonException` → null → fresh start** (`CheckpointState.cs:43-48`); version + fingerprint + step-shape guards (`:259-261`) start fresh rather than resuming wrong. Correct.
- **`ToBytesAsync` honors cancellation per record** (`:1089-1098`); `SliceByBytes` (`:1068-1087`) correctly never splits a single oversized record (atomic JSON) and disables on `maxBytes <= 0`.
