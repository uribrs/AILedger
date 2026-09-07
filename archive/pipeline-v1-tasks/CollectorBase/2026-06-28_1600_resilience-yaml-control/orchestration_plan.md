# Orchestration Plan

## Complexity Decision
- Path: direct (incremental, A→B1→B2→C→DOCS)
- Rationale: One cohesive feature on a shared contract surface (ResilienceSpec/SessionFactory/
  FetchStepExecutor), tightly coupled, build+test gated between items. Workers would collide on the
  same files and lose the regression safety net.

## Research Decisions
- None (external). A1 — whether the package `RetryOptions` exposes a settable header/rate-limit
  strategy — is an in-repo/package CODE-TRACE the executor performs as a VERIFY-FIRST step before
  wiring B1 (read the Options type / its construction). Not external-system behavior → no
  technical-researcher topic. `researchNeeded = false`.

## Worker Plan
Not applicable — direct path.

## Synthesis Approach
Omitted — direct path.

## Execution Sequence (build + 88/88 between each)
- A: PaginationSpec.WatermarkSeed + FetchStepExecutor fresh-run seeding (resume wins); seed falcon profile; fresh-vs-resume test.
- B1: VERIFY RetryOptions header-strategy surface FIRST; wire server_delay_strategy if settable, else document on/off truth.
- B2: ResilienceSpec.rate_limit + circuit_breaker specs; SessionFactory → SessionSpec.RateLimiter/CircuitBreaker; OPT-IN null-preserves-default; SessionFactory unit tests (present→populated, absent→null).
- C: Runner runs-root anchored to repo root (walk up to CollectorBase.slnx; fallback CWD); --out preserved.
- DOCS: capabilities.md + yaml-contract.md (new knobs + B1 truth).
- STOP rule: a no-knobs build/suite going red (regression) → halt + report.

## Verification Obligations
- Cross-check Success Criteria 1–6.
- Verifier MUST: run suite (88/88 + new); confirm NO-REGRESSION (absent knobs ⇒ SessionSpec
  RateLimiter/CircuitBreaker null, not a fabricated default — read the SessionFactory); confirm
  watermark_seed seeds fresh AND is ignored on resume (read FetchStepExecutor + test); STATE the B1
  outcome (wired vs documented-on/off).
- Code-reviewer (isolated, minimal): null-preservation correct; watermark_seed never overrides a
  resumed watermark; field mapping matches Shared option property names.
