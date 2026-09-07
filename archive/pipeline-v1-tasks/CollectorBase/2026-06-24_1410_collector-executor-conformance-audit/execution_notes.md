# Execution Notes — synthesis

## Conformance (cross-verifier-1.md) — VALIDATED
- CollectorExecutor genuinely conforms to the native Shared-usage baseline on all 8 subsystems.
  Overstated claims: 1 (low). False claims: 0.
- Claim re-tests: interface set (TRUE), bus routing + 13 delegates -> CompletionRequest publish (TRUE),
  resume via CollectorResumeRunner (TRUE, but see overstatement), decoupled per-target page counter
  seeded+persisted (TRUE — no path uses the cursor as the page number), RUN-envelope ingress incl.
  encrypted-blob decrypt + lastRanAt floor, reachable + no-op for Runner shape (TRUE), no double-resilience
  + classified codes survive (TRUE — independently chased: the engine's internal recovery budget is NOT
  double-counted because the re-throw becomes ServerSuggestedRetryDelayException and
  ServerSuggestedRetryDelayPolicy uses UseRecoveryBudget:false; ResiliencePipeline.Empty prevents
  double-retry; DelegateAdapterFailurePolicy preserves codes), capability methods throw NotSupported
  (acceptable by design for a generic engine).
- Overstatement (LOW): resume re-restores progress that CollectorResumeSetup already restored
  (CollectorExecutorRunner.cs:283-286) — idempotent today, latent trap.
- Telemetry (LOW): success Data["pages"] reflects pagination depth, not output-file count.
- Contrast confirmed: the real divergence is YamlCollector (bypasses the substrate: own HttpClient/auth,
  raw DataBatchRequest egress, 3 events not 5, in-engine retry under the bus default retry). CollectorExecutor
  conforms on 8/8 subsystems; YamlCollector only on the bus/resume/checkpoint shells.

## Code quality (code-reviewer-1/2/3) — 0 blocking; convergent findings
Pre-existing interpreter robustness bugs (NOT introduced by the conformance work; surfaced now):
- MAJOR: non-string cursor token -> JsonValue.GetValue<string?>() throws InvalidOperationException, which
  escapes the page-loop catch filter (only Json/Xml/AdapterHttp caught) and crashes past partial-success.
  CursorPaginationStrategy.cs:20, CursorWatermarkStrategy.cs:37, NextUrlPaginationStrategy.cs:22. [R2]
- MAJOR: page_size / hydrate.batch_size of 0 (or negative) -> infinite loop / hang (Chunk never advances;
  offset/page paginators refetch the same page); no cancellation/timeout. [R1]
- MAJOR: cursor strategies gate hasMore on recordsThisPage>0 -> a sparse intermediate page with a valid
  next token silently truncates the stream. [R2]
- MAJOR: watermark advance only fires for a top-level STRING field via CompareOrdinal -> numeric / nested /
  variable-width-epoch watermarks never advance, degrading hardened deep-scroll vendors. [R2]
- MAJOR/MINOR: dead FetchSignal/FetchDecision/RunContext.Signal seam — written by CursorWatermarkStrategy,
  never read by the runner (reset works via the returned Paginator.Step); class/seam comments present this
  dead channel as the live mechanism (actively misleading). [R1 minor, R3 major]
- MAJOR(latent): request.headers parsed from YAML but never applied to any request — silent config loss
  (no shipped profile uses it yet). [R1]

Conformance-introduced maintainability debt (ours, this work):
- MAJOR: RunAsync (~190-line god-method) first ~130 lines duplicate Preflight almost verbatim — two copies
  of the validation gauntlet that will drift. [R3] (Introduced when Preflight was added for fast validation.)
- MAJOR: step methods take 13-18 positional args (RunFetchStepAsync = 18); same-typed adjacent params make
  a silent transposition possible. [R3]

Minor/nit (deferred): next_url 1-cycle guard only; soft poll_until deadline; silent ParseEnum fallback;
hydrate ID-count vs record-count in the same termination math; double-serialize already addressed.

## Net
- The conformance we asserted is real (audit confirms 8/8, 0 false). The two low items are latent, not bugs.
- The code-quality findings are mostly PRE-EXISTING interpreter robustness gaps (non-string cursor crash,
  page_size=0 hang, sparse-page truncation, watermark-string-only) — none are blockers, build is green and
  43 tests pass, but they matter for vendors with non-string tokens/watermarks. Read-only audit: reported,
  not fixed.
- Highest-value follow-ups (separate, approval-gated): (1) the non-string cursor crash + page_size=0 hang
  (robustness); (2) de-duplicate RunAsync/Preflight (our debt); (3) delete or wire the dead FetchSignal seam
  and fix the misleading comments.
