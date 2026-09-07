# Verifier-1 — CollectorExecutor Remediation

Independent verification of Success Criteria 1-6 against actual code. Repo is NOT a git repo, so
behavior-preservation (item 4) is judged against the prior audit's cross-verifier references plus
the present code, not a diff.

## SC1 — Build clean — PASS
`dotnet build CollectorBase.slnx` → exit 0, `0 Error(s)` (12 NU1507 package-source warnings only,
pre-existing/unrelated to this work).

## SC2 — Tests pass — PASS
`dotnet test CollectorBase.slnx` → `Passed! Failed: 0, Passed: 43, Skipped: 0, Total: 43`.
Meets the ">= current 43" bar. No new 1a/1b unit tests added; execution_notes justifies this
(strategy seam needs a host-created `required AdapterProgressContext`). The fixes are pure clamps +
coercion guarded by the existing 43-test regression suite. Acceptable per "if feasible" wording.

## SC3 — Robustness bugs fixed — PASS
1a non-string next-token:
- `JsonNav.StringAt` (Interpreter.cs:46-50) returns a string token as-is, coerces numeric/bool via
  `v.ToString()`, returns null for object/array/absent — no `GetValue<string>()` throw escapes.
- Used at the seam in all three strategies: CursorPaginationStrategy.cs:20,
  CursorWatermarkStrategy.cs:36, NextUrlPaginationStrategy.cs:22. Page-loop catch filter NOT weakened
  (constraint honored).

1b zero/negative size infinite-loop:
- StaticPageSizeStrategy.Resolve → `Math.Max(1, declared)` (StaticPageSizeStrategy.cs:10).
- Offset advance clamps: OffsetPaginationStrategy.cs:21, PageNumberPaginationStrategy.cs:18,
  NextUrlPaginationStrategy.cs:27 (offset fallback) — all `Math.Max(1, spec.PageSize)`, used for both
  the short-page stop check and the advance.
- Chunk helper clamp: CollectorExecutorRunner.cs:1103 `size = Math.Max(1, size)` (covers
  hydrate.batch_size + for_each batch_size). Effective size clamped; a valid configured size is not
  silently altered (constraint honored).

## SC4 — Single shared validation routine — PASS
- `Prepare(PlatformEvent) -> (AdapterResult? Failure, PreparedRun? Run)` (CollectorExecutorRunner.cs:71-123)
  is the sole site of every validation `ValidationFailure` (lines 76, 82, 87, 92, 97, 110, 114, 117).
- `Preflight` is now `=> Prepare(platformEvent).Failure` (line 128).
- `RunAsync` calls `Prepare` once (line 203) and unpacks `PreparedRun` (lines 205-219); the ~130-line
  duplicated parse/validate/resolve prefix is genuinely gone, not relocated. The only `ValidationFailure`
  outside Prepare is the auth re-check at session-build time (lines 285-286): this is the SECOND
  `BuildSessionSpec` call that actually constructs the session (Prepare's call at line 116 only probes
  for null). Same check/message/code as Prepare's auth probe — behavior-preserving, not residual
  validation duplication.
- Order documented at lines 69-70 (yaml → profile → flow/steps → registry/strategies →
  creds/config/inputs+base_url → auth) and matches the fail-closed sequence the prior audit's
  cross-verifier attributed to both the old Preflight and RunAsync. No order/code/message drift found.

## SC5 — Dead FetchSignal vocabulary removed — PASS (with one cosmetic note)
- Repo grep `FetchSignal|FetchDecision|.Signal|decision vocabulary` → ZERO hits in production code.
  No `FetchDecision` enum, no `FetchSignal` record, no `RunContext.Signal`.
- Seams.cs:22-27 RunContext doc now correctly states a strategy "signals a reset via its returned
  Paginator.Step (NextCursor: null, HasMore: true) ... not through any side-channel on this context."
- CursorWatermarkStrategy.cs:13-16 + inline comments (31, 39-45) describe the live mechanism (returned
  Step), no ctx.Signal writes remain.
- NOTE (not a failure of SC5 as scoped to production code): the only surviving "decision vocabulary"
  string is a TEST comment, CollectorExecutorTests.cs:597 "(decision vocabulary LIVE)". It is stale
  wording but is in a test header, names no deleted symbol, and the test it labels still passes and
  asserts the real reset behavior (post-reset watermark-filtered request, line 612). Cosmetic.

## SC6 — Conformance NOT regressed — PASS
- Full interface set intact: CollectorExecutorAdapter.cs:32-37 implements `IAssetsCollectorAdapter,
  IFindingsCollectorAdapter, ICollectorEventSink, IResumableAdapter, IAsyncDisposable`.
- ProcessAsync routes through `AdapterBusEntrypointRunner.RunAsync` (line 203); Preflight runs pre-bus
  (line 145).
- ResumeAsync routes through `new CollectorResumeRunner(...).ResumeAsync(...)` (lines 243-244).
- Per-emit-target page counter unchanged: `++runCtx.FindingsPage : ++runCtx.AssetsPage`
  (CollectorExecutorRunner.cs:444); persisted (line 734) and re-seeded on resume (lines 266-267).
- RUN-envelope ingress unchanged: `HydrateRunEnvelope` (line 141/155-175) + `DeriveFloorFromRunEnvelope`
  (line 146/180-192) still folded into BuildEffectiveInputs, called from Prepare (line 112).

## Scope discipline — PASS
Only items 1-3 touched. Deferred findings untouched:
- sparse-page truncation: not addressed (no change to the page-loop emit/stop logic beyond the size clamp).
- string-only watermark: CursorWatermarkStrategy still keys on `ctx.Watermark` string; not widened.
- request.headers-unapplied: no change.
- 18-arg method sprawl: RunForEachStepAsync/RunPollUntilAsync signatures unchanged (the Prepare
  extraction did not refactor these).
No behavior change in Prepare vs the old inline validation (order/codes/messages preserved per SC4).

## Regressions / scope violations
None found.

## Verdict
All six Success Criteria PASS. Build clean, 43/43 tests green, both robustness bugs fixed at the
correct seam with effective-size clamps, validation de-duplicated into a single behavior-preserving
`Prepare`, dead signal vocabulary removed from production code with corrected comments, conformance
intact. Sole nit: a stale "decision vocabulary LIVE" comment in the test file — cosmetic, out of the
strict SC5 production-code scope.
