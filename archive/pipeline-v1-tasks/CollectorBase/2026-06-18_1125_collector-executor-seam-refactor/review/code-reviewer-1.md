# Code Review — CollectorExecutor seam refactor

Scope: 6 files (Seams, StrategyRegistry, CollectorExecutorRunner, DefaultPaginationStrategy,
StrategyRegistryDefaults, LocalRunHost). Reviewed in isolation against the .NET 8 YAML-collector
context. Change type: shared library / orchestration core. Risk: High (pagination state, checkpoint
resume, network-bound loop, shared singleton).

Severity legend: Blocker (fix before merge) / Major (fix unless consciously accepted) / Minor / Nit.

---

## Blocker

**B1 — Offset pagination cannot advance; the engine silently re-fetches page 1.**
`CollectorExecutorRunner.cs:136,174,178` + `BuildContext` `:271-283`.
The runner never threads an offset. `BuildContext` only puts `cursor`, `page`, `page_size` into the
template context — there is no `offset` key. `runCtx.Offset` is hardcoded to `0` every iteration
(`:174`), and the paginator's computed `step.NextOffset` is discarded (`:178` reads only `NextCursor`
and `NextPage`). For `spec.Strategy == "offset"` (`Interpreter.cs:57-62`), the request URL therefore
has no advancing offset token to render, and the paginator is fed a constant `currentOffset = 0`.
Result: an offset-paginated vendor either loops re-fetching the first page (duplicate emits, wrong
`hasMore` based on a repeated page) or, if the YAML templates `{{offset}}`, it renders empty
(`TemplateRenderer.Render` returns "" for missing keys, `Interpreter.cs:17`). `cursor`, `link_header`,
and `page_number` work because their tokens (`cursor`, `page`) ARE in the context and advance.
Impact: data correctness — incomplete or duplicated collection for any offset-based vendor.
Fix: carry an `offset` local in the loop, add `["offset"] = offset` to `BuildContext`, set
`runCtx.Offset = offset` (not 0), and assign `offset = step.NextOffset` alongside `page` at `:178`;
persist/restore it in `CheckpointState` (currently also hardcoded `Offset = 0` at `:183`).
Requires: local patch (no architectural change).

---

## Major

**M1 — Dead abstractions: 4 of 5 declared seam interfaces are unused, plus RunContext fields.**
`Seams.cs:71-96` (`IWindowPlanner`, `IStagePlanner`, `IPageSizeStrategy`, `IRecordMapper`) have zero
implementers and zero references anywhere in the tree (grep-confirmed). The registry (`:105-110`) only
knows about paginators. Likewise the cross-seam "decision vocabulary" — `FetchSignal`, `FetchDecision`,
`RunContext.Signal`, `RunContext.Watermark`, `RunContext.Offset` — is never read or written by any
consumer (`runCtx.Signal`/`.Watermark` never assigned; `IPaginationStrategy.Next` returns a
`Paginator.Step`, not a `FetchSignal`). The header comment frames these as "declared extension points…
migrated in subsequent slices," so this is intentional scaffolding — but as written it is speculative
API surface (a 6-value enum + record + 4 interfaces) ahead of any caller, which the operator's
"don't over-engineer / no abstraction without a demonstrated consumer" bar argues against.
Impact: maintenance/cognitive load; an unproven contract (e.g. `FetchSignal` vs `Paginator.Step`
return shape) that will likely change once a real second strategy lands, making it churn not leverage.
Fix: keep only `IPaginationStrategy` + `IStrategyRegistry` + the parts of `RunContext` actually used
(`Progress`, `Vendor`, `Stream`, `IsFindings`, `Cursor`, `Page`); delete the rest until the slice that
consumes them exists. If retention is a conscious roadmap decision, accept explicitly — but note the
return-type mismatch (`Step` vs `FetchSignal`) means the current vocabulary is not yet load-bearing.
Requires: deletion (smallest change) or a documented accept.

**M2 — DefaultPaginationStrategy is a thin name-tag wrapper; the "named strategy" indirection is
not yet real.** `DefaultPaginationStrategy.cs:14-22` + `StrategyRegistryDefaults.cs:14,26-27`.
All five names (`cursor`/`offset`/`page_number`/`link_header`/`none`) register the *same* class, which
ignores its own `Name` and dispatches purely on `spec.Strategy` inside `Paginator.Next`. So the
registry maps 5 keys → 5 instances of one behavior that re-switches on the same string the key came
from. The registry currently adds a lookup layer over a switch that still exists underneath
(`Interpreter.cs:48`). This is acceptable as a transitional seam (the header calls it the open/closed
proof), but right now it is indirection without a payoff and double-sources the strategy list (the
`PaginationStrategyNames` array must stay in sync with `Paginator.Next`'s cases — drift risk).
Impact: maintainability; a silent gap if the array and the switch diverge (a name registered with no
matching case falls into `default` → `HasMore=false`, terminating after one page with no error).
Fix: either (a) accept as scaffolding and add a test asserting the name set matches the switch cases,
or (b) when a real per-strategy class lands, drop the inner switch. Local patch / test.

---

## Minor

**m1 — `StrategyRegistry` uses a non-concurrent `Dictionary` behind an interface that exposes a public
mutator on a DI singleton.** `StrategyRegistry.cs:12,14-18` + `LocalRunHost.cs:30`
(registered `AddSingleton`). Today it is safe: registration happens at composition time before the
singleton is published, and per-call resolves are reads only — concurrent reads of a frozen
`Dictionary` are fine. But `RegisterPaginator` is public on a shared singleton, and a test already
mutates a shared instance post-construction (`CollectorExecutorTests.cs:315`). If anyone registers
after startup under load, this is an unsynchronized write racing reads → torn state / `InvalidOperation`.
Impact: latent threading trap, not a current defect.
Fix: cheapest — document "register at composition only, immutable after publish" and have
`CreateDefault`/`AddDefaults` be the only callers; or freeze after build; or use a
`FrozenDictionary` post-registration. A `ConcurrentDictionary` is overkill for a populate-once map.
Local patch.

**m2 — `ResolvePaginator` re-walks the dictionary after `HasPaginator` already did.**
`CollectorExecutorRunner.cs:78-80`. Two lookups (`HasPaginator` then `ResolvePaginator`) for the same
key, purely to map the missing case to a typed `AdapterResult` vs the registry's
`InvalidOperationException`. Cold path (once per run), so the double lookup is immaterial — the real
note is the duplicated fail-closed logic living in two places (registry throws; runner pre-checks and
returns a ValidationFailure, so the registry's throw is effectively dead on this path).
Impact: minor redundancy / two sources of the same guard.
Fix: optional — `TryResolve` pattern, or just let the single `ResolvePaginator` throw and catch it.
Local patch, not blocking.

**m3 — `none` strategy registered as a real name but means "no pagination."**
`StrategyRegistryDefaults.cs:14` + `Interpreter.cs:68-69`. `none` falls into the paginator `default`
(`HasMore=false`), so a stream declaring `strategy: none` runs exactly one page. That is probably the
intent, but it is implicit — `none` and an unknown/typo'd strategy take the same `default` branch, so a
misspelled real strategy that happens not to be in the registry is caught fail-closed (good), while one
that *is* registered but unhandled in the switch silently degrades to one page (see M2 drift risk).
Impact: minor correctness ambiguity.
Fix: make `none` explicit in the switch (`case "none": return …HasMore:false`) so `default` can stay a
genuine guard. Local patch.

---

## Nit

**n1 — `ToNdjson` calls `await Task.Yield()` per record.** `CollectorExecutorRunner.cs:338`.
Forces a continuation hop for every single record in a synchronous CPU-bound serialization loop —
pure overhead with no IO to interleave. Harmless at small page sizes; wasteful at large ones.
Consider dropping it (the method can stay `async IAsyncEnumerable` without yielding) unless it exists
specifically to keep a tight loop cancellable between items (the `ThrowIfCancellationRequested` at
`:336` already covers cancellation).

**n2 — `RunContext` is created (`:101`) but only `Cursor`/`Offset`/`Page` are ever set on it, all
immediately before the single paginator call** (`:174`), then never read back. The sidecar context is
currently a pass-through for three ints/strings already held as locals. Folds into M1 — once the unused
fields are gone, evaluate whether `RunContext` earns its existence over passing the three values
directly to `paginator.Next`.

**n3 — `LocalRunHost.PublishAsync` always returns `PublishResult.Ok()` (`:39-41`) ignoring the
request.** Fine for a local lab host, but it silently no-ops; a one-line comment that this is
intentional (data goes through `IAdapterDataPublisher`, not this path) would prevent confusion.

---

## Observations (no action)

- Fail-closed resolution (registry throws / runner returns typed ValidationFailure on unknown name) is
  correct and well-placed — before any HTTP (`:74-80`). Good.
- `OrdinalIgnoreCase` key comparer on the registry matches the case-insensitive stream/strategy lookups
  elsewhere — consistent.
- Exception mapping in the page loop (`:191-215`) is sound: partial-success preservation, non-retry of
  401/403, transient on 408/429/5xx, 6h cap on server-suggested delay. No notes.
- Resolving the registry per-call via `_context.Services.GetService<IStrategyRegistry>()` is acceptable
  here: it is a singleton, the lookup is cheap, and it keeps the runner free of a constructor dep on the
  Strategies library (the stated open/closed goal). Not a concern.
