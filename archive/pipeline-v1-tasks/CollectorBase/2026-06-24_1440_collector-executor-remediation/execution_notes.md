# Execution Notes

## Changes (exactly items 1-3; direct path)
- 1a tolerant token extraction: added JsonNav.StringAt (Interpreter.cs) — returns a string token as-is,
  coerces a numeric/bool JSON value to its text form, else null. CursorPaginationStrategy /
  CursorWatermarkStrategy / NextUrlPaginationStrategy now use StringAt instead of GetValue<string?>(), so a
  non-string next-token no longer throws InvalidOperationException out of the page loop.
- 1b size clamps (>= 1): StaticPageSizeStrategy.Resolve -> Math.Max(1, declared); OffsetPaginationStrategy,
  PageNumberPaginationStrategy, NextUrlPaginationStrategy offset-fallback clamp Math.Max(1, spec.PageSize)
  for both the short-page stop check and the offset advance; runner Chunk(items, size) clamps size >= 1
  (covers hydrate.batch_size + for_each batch_size). A 0/negative size can no longer infinite-loop.
- 2 de-dup: extracted Prepare(platformEvent) -> (AdapterResult? Failure, PreparedRun? Run) as the single
  source of truth for parse+validate+resolve; Preflight is now `=> Prepare(...).Failure`; RunAsync unpacks
  Prepare's PreparedRun (the ~130-line duplicated prefix is gone). Behavior-preserving.
- 3 dead seam removed: deleted FetchDecision enum, FetchSignal record, RunContext.Signal, and the three
  CursorWatermarkStrategy ctx.Signal writes; corrected the Seams.cs header + RunContext comment that
  presented the dead channel as the live mechanism (the real reset is the returned Paginator.Step with
  NextCursor:null/HasMore:true). Confirmed by search that nothing read Signal before deleting.

## Out of scope (untouched, per contract): sparse-page truncation, string-only watermark advance,
## request.headers-unapplied, 18-arg method signatures.

## Verification
- dotnet build CollectorBase.slnx -> clean (0 errors).
- dotnet test CollectorBase.slnx -> 43/43 pass (no regression).
- Conformance intact: adapter interfaces, ProcessAsync via AdapterBusEntrypointRunner, ResumeAsync via
  CollectorResumeRunner, per-emit-target page counter, RUN-envelope ingress — all unchanged by these edits.
- Tests for 1a/1b not added: a strategy-level unit test needs a RunContext whose Progress is a `required`
  host-created AdapterProgressContext (not cleanly constructable without the full harness). The 43-test
  suite is the regression gate; the fixes are simple clamps + tolerant coercion. (Noted as a follow-up.)
