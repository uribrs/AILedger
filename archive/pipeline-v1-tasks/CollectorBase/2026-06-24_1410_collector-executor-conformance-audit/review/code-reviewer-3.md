# Code Review — CollectorExecutor (idiom / API-structure / maintainability lens)

Reviewer: independent senior C# engineer. Scope: `CollectorExecutorAdapter.cs`,
`Execution/CollectorExecutorRunner.cs`, `Execution/CollectorExecutorRequest.cs`, `Seams/Seams.cs`,
`Composition/StrategyRegistry.cs`, plus the overall `CollectorExecutor/` structure. Judged on its own
merits as written.

---

## Findings

### BLOCKING

(none)

---

### MAJOR

**M1 — Dead cross-seam "decision vocabulary" (`FetchSignal` / `FetchDecision` / `RunContext.Signal`) — write-only, never honored**
`Seams/Seams.cs:59,63-76`; written at `Strategies/Pagination/CursorWatermarkStrategy.cs:29,45,54`.
`RunContext.Signal` is assigned by `CursorWatermarkStrategy` but is **never read anywhere** in the
codebase (`grep` for any read of `.Signal` returns nothing). The reset is actually driven entirely by
the returned `Paginator.Step` (`NextCursor: null, HasMore: true`) — the signal is inert. The entire
`FetchDecision` enum (6 members) and the `FetchSignal` record exist to feed this dead channel.
*Why it matters:* this is the single most maintainability-damaging item. A future maintainer reading the
strategy will reasonably believe the `Signal` is the reset mechanism, change it, and observe no behavior
change — or worse, "fix" the runner to honor it and double-apply the reset. An abstraction that doesn't
earn its keep and actively misleads. Either wire the runner to consume `Signal` (and delete the
duplicate `Paginator.Step`-based path), or delete `FetchSignal`/`FetchDecision`/`RunContext.Signal`
outright. As written it is dead code masquerading as the system's core "decision vocabulary."

**M2 — Misleading comments assert a live mechanism that does not exist**
`Strategies/Pagination/CursorWatermarkStrategy.cs:13-15` ("emits the cross-seam signal (ResetToWatermark);
the runner extracts/persists the watermark ... proves the decision vocabulary live") and
`Seams/Seams.cs:58` ("Cross-seam coordination signal (the decision vocabulary)"). These describe the M1
dead path as the operative behavior. The class XML doc even calls itself the proof that the vocabulary is
"live" — it is not.
*Why it matters:* comments that contradict the code are worse than no comments; they cost every future
reader the time to discover the lie, and they anchor wrong mental models in review.

**M3 — `RunAsync` is a ~190-line god-method doing 6 distinct jobs**
`Execution/CollectorExecutorRunner.cs:180-366`. One method performs: payload/profile parse, flow→steps
resolution, registry strategy resolution, credential/config/input assembly, checkpoint seeding (the dense
~45-line block at 250-293), session construction, and the step-dispatch loop with two catch handlers.
The step loop (309-345) re-dispatches by `s.Kind` string compare with three near-identical call sites
each threading **13–15 positional arguments**.
*Why it matters:* the first ~130 lines (parse → seed) are a self-contained "prepare run state" phase that
is duplicated almost verbatim in `Preflight` (62-109: same payload check, same `ProfileLoader.Load` +
same catch filter, same `ResolveStream`/`ResolveSteps`, same registry resolution, same
`BuildEffectiveInputs`/`base_url` check, same `BuildSessionSpec` null check). Two copies of the same
validation gauntlet will drift. Extract a `PrepareRun(...)` returning a struct, call it from both
`Preflight` and `RunAsync`.

**M4 — Pervasive 13–18 positional-parameter method signatures**
`RunFetchStepAsync` (18 params, lines 371-376), `RunForEachStepAsync` (15), `RunPollUntilAsync` (13),
`WriteCheckpoint` (10). These are passed positionally at every call site (e.g. 316-317, 322-323,
328-329, 542-544).
*Why it matters:* a transposed argument of a compatible type (e.g. `flowName`/`fingerprint`/`vendorName`
are all `string`; `stepIndex`/`forEachIndex`/`start` are all `int`) compiles clean and fails silently at
runtime — exactly the kind of bug that surfaces in production six months out, not in review. The stable
per-run dependencies (`http`, `baseUrl`, `inputs`, `config`, `vendorName`, `fingerprint`, `registry`,
`mapper`, `pageSize`, `failure`, `runCtx`, `ct`, `isResumeRun`) are invariant across all three step
methods and the whole flow loop — they are a "run environment" object, not 13 free parameters. Bundle
them into a `StepExecutionContext`/`RunEnvironment` record; the step methods then take `(StepSpec, int
stepIndex, seed-ish, env)`. This is the highest-leverage idiomatic fix in the file.

---

### MINOR

**m1 — `RunContext.IsFindings` is set but never read**
`Seams/Seams.cs:33` (required init), assigned `CollectorExecutorRunner.cs:238`. The runner instead
re-derives `isFindings` locally per step from `emitTarget` (`RunFetchStepAsync:380`). Dead state on a
`required` property — every `RunContext` construction must supply a value nobody consumes.
*Why it matters:* minor, but it's a required field that lies about being part of the contract; remove it
or use it.

**m2 — Non-standard 2-space indentation inside the `while (true)` page loop**
`CollectorExecutorRunner.cs:392-512`. The whole loop body and its `try`/`catch` blocks are indented 2
spaces against the file's prevailing 4-space style, so the catch filters at 483/490 sit at an unusual
column.
*Why it matters:* breaks scan-ability and any auto-format will produce a large noise diff over real
changes. Reformat to 4-space.

**m3 — Two near-identical exception-classification helpers**
`CollectorExecutorAdapter.cs:291-301` (`BuildResilience`'s `DelegateAdapterFailurePolicy`) and `303-306`
(`ClassifyFlowException`) both pattern-match `CollectorExecutorFlowException` → `(Message, ErrorCode,
ErrorSeverity.Error, IsRetryable)`, differing only in the wrapper type (`AdapterFailureHandling` vs
`FlowExceptionHandling`). The mapping logic is duplicated.
*Why it matters:* low severity (two call shapes genuinely differ), but if the error-classification rule
changes, both must change in lockstep; a shared private `(string,string,ErrorSeverity,bool)?
Classify(Exception)` removes the risk.

**m4 — `vendorName` resolution duplicated across `ProcessAsync` and `ResumeAsync` with divergent code**
`CollectorExecutorAdapter.cs:148-154` vs `209-215`. Same intent (best-effort YAML→vendor, fall back to
"collector-executor", swallow the same two exception types) written two different ways (inline `is { }`
chain vs `var yaml = ...; if`). Extract a `ResolveVendorName(platformEvent)` helper.
*Why it matters:* small, but it's copy-paste with cosmetic drift — the exact pattern that rots.

**m5 — `ExtractCount` and the duplicated success-data dictionaries encode an implicit "records/total" contract by string keys**
`CollectorExecutorAdapter.cs:174-185, 233-236, 308-314`; runner `359-365`, `829-831`. The
`["records"]/["total"]/["vendor"]/["stream"]/["findings"]` dictionary is hand-built in ~5 places and
read back by `ExtractCount` via string lookup + `is int` cast. The producer/consumer coupling is
untyped and unenforced.
*Why it matters:* a typo in a key, or emitting `long` instead of `int`, silently yields count 0
(`ExtractCount` returns 0 on miss). A small `record RunOutcome(int Total, int Findings, string Vendor,
string Stream)` with one `ToData()` would centralize it. Minor because the keys are currently
consistent.

**m6 — `StepStrategy` lowercases `Kind` two different ways across the file**
`CollectorExecutorRunner.cs:614-620` uses `step.Kind?.ToLowerInvariant() switch`, while the dispatch
loop (313, 319) uses `string.Equals(s.Kind, "for_each", StringComparison.OrdinalIgnoreCase)`. Two idioms
for the same "case-insensitive kind compare."
*Why it matters:* nit-adjacent, but the duplication means the set of valid `Kind` values lives in two
places; an enum or a single `KindOf(step)` normalizer would make the step taxonomy single-sourced.

**m7 — `Seams.cs` "Phase 0 status" comment will date badly**
`Seams/Seams.cs:16-20` and `87-88` narrate migration phases ("currently lives inline in the runner and
is migrated behind these seams in subsequent Phase-0 slices"). The mapper/pageSize seams **are** already
resolved through the registry (`RunAsync:220-222`), so the comment is already partly stale.
*Why it matters:* process/roadmap commentary embedded in a contract file becomes wrong the moment the
phase advances and nobody updates it. Move roadmap notes to the docs; keep the file comment to what the
seam *is*.

---

### NIT

**n1 — `Dispose()` and `DisposeAsync()` both no-op; class implements only `IAsyncDisposable`**
`CollectorExecutorAdapter.cs:116-121`. A public synchronous `Dispose()` exists but the class declares
`IAsyncDisposable` (not `IDisposable`). Harmless, but the lone public `Dispose()` invites confusion about
the disposal contract. Drop it or implement `IDisposable` explicitly.

**n2 — Magic numbers for wait caps**
`CollectorExecutorRunner.cs:350` (`TimeSpan.FromHours(6)` appears twice in one expression), `632`
(`3600`s default max-wait), `675` (1s poll floor), `Adapter.cs:270` (`FromMinutes(5)` default delay).
Pull to named consts; the 6h cap especially is a policy value worth naming.

**n3 — `catch (Exception ex) when (...)` filter pattern repeated verbatim 4+ times**
e.g. `RunAsync:192`, `Preflight:71`, `Adapter:154,215`, and the JSON/XML filters at runner `483,653`.
Consistent and correct, but a small `static bool IsProfileParseError(Exception)` predicate would make
intent self-documenting. Pure nit.

**n4 — `BuildContext` token namespace is stringly-typed and spread across methods**
`RunContext`-derived tokens (`cursor`, `page`, `offset`, `offset_end`, `watermark`, `capture.*`) are
assembled partly in `BuildContext` (1002-1014) and partly inline in `RunFetchStepAsync` (398-402). The
token vocabulary is the YAML contract surface; having it half-here/half-there makes "what tokens exist"
hard to answer. Nit — consolidating would aid the next person writing a profile.

---

## What's solid (call-outs)

- **`StrategyRegistry`** (`Composition/StrategyRegistry.cs`) is clean, idiomatic, genuinely fail-closed,
  and its `Missing(...)` error messages list the available names — exactly the error-message quality you
  want. No notes.
- **Fail-fast preflight before any HTTP** (`Preflight`) and the explicit comment on *why* validation must
  not enter the retry pipeline (`ProcessAsync:143-144`) is the right call and well-justified.
- **Error messages throughout are above average** — they name the offending value and the legal set
  (e.g. `ResolveStream` failure lists available streams; `BuildHydrateRequest` default lists valid
  `id_style`s; auth failures point at `auth.params`). This is the strongest dimension of the code.
- **`FailureResolution` / `FailureContext` seam** (`Resilience/FailureSeam.cs`) is a well-shaped, small
  abstraction with a clear invariant (`Decision` null iff `ResetAndContinue`) enforced in the factory.
- **`CollectorExecutorFlowException` → bus translation** is a coherent, well-documented boundary; the
  comments explaining the no-op retry pipeline (`Adapter:196-200`) and the double-resilience hazard are
  the *good* kind of why-comment.
- **Checkpoint-ordering discipline** (state written before `AdvancePage`, `WriteCheckpoint` centralized)
  is consistent and the invariant is honored at every call site I checked.

---

## Overall verdict

Structurally sound, well-documented declarative interpreter with excellent error messages and a clean
registry — but undermined by a dead "decision vocabulary" the comments wrongly present as live (M1/M2)
and by god-method/parameter-count sprawl (M3/M4) that will make the runner risky to change in six months.
