# Execution Notes

## Contract phase (2026-06-18)
- Contract created (full tier). Phase 0 of the strategy-expansion plan: seam refactor, behavior-preserving.
- Working repo = CollectorBase (standalone net8.0 sln; not git-tracked yet).
- Regression oracle is currently REMOTE (unit tests live in the adapters repo) — decision: copy the
  test project (+ Collectors.Tests.Infrastructure if needed) into CollectorBase. Flagged as S7/A4.
- Later phases (Falcon hardened quirks) are explicitly out of scope; this pass only opens the seams.

## Execution increment 1 (2026-06-18) — foundation established, refactor surfaced for decision

DONE + VERIFIED:
- A3 RESOLVED → VALIDATED: the progress context is host-created (`context.CreateProgressContext`),
  so RunContext cannot subclass it — it must be a SIDECAR wrapping the AdapterProgressContext +
  run/position state + the decision-signal channel. (No SDK-internal read needed.)
- A4 RESOLVED → VALIDATED: copied the CollectorExecutor unit test project + Collectors.Tests.Infrastructure
  (FakeHttpClientFactory, CollectorEventCapture) into CollectorBase/Tests; repointed refs; added both to
  the sln. Local regression oracle is GREEN (3/3: assets=5, findings=3, resume no-overlap) against the
  CURRENT code. The behavior-preservation oracle is now LOCAL — the precondition for the refactor.

SURFACED TO ORCHESTRATOR (blockers/decisions before S1–S6):
1. CONTRACT CONTRADICTION — "wire always-on RecoveryBudget" vs "behavior-preserving, zero functional
   change". Today's runner has NO recovery budget; the http.package session owns resilience and the
   runner only maps caught exceptions → AdapterResult. Adding a budget IS a behavior change. Proposed
   resolution: in Phase 0, create the resilience/recovery SEAM with the DEFAULT = today's behavior
   (no budget gating); defer the real progress-anchored budget to Phase 1 (it is hardened content,
   like the Falcon quirks). Needs orchestrator confirmation.
2. SCOPE/SEQUENCING — the six-seam refactor + Strategies library + open/closed demo is a large change
   best done as its own increment now that the oracle is local. Recommend the orchestrator proceed
   with S1–S6 as increment 2 (direct path, same contract), gated by the green oracle, rather than
   bundling it under-verified with the foundation work.

STATE: S7 complete; A3/A4 VALIDATED; S1–S6 pending the decision in (1).

## Execution increment 2 (2026-06-18) — pagination seam + registry + open/closed, behavior-preserving

B1 applied: behavior-preserving wins; resilience/recovery seam default = today's behavior (no budget); real budget deferred to Phase 1.

DONE + VERIFIED (build green, tests 4/4 GREEN):
- Decision vocabulary defined (Seams/Seams.cs): RunContext (sidecar over host AdapterProgressContext) +
  FetchDecision { Continue, Stop, ResetToWatermark, DeferUnbudgeted, Retry, Fail } + FetchSignal.
- Strategy registry: IStrategyRegistry (CollectorExecutor/Seams) + StrategyRegistry concrete
  (CollectorExecutor/Composition) — fail-closed resolution.
- Pagination seam fully wired through the registry: runner resolves the paginator by
  stream.Pagination.Strategy, fails closed on unknown strategy (UNKNOWN_STRATEGY) and on missing
  registry (MISSING_STRATEGY_REGISTRY). Behavior identical (default paginator delegates to the
  interpreter's Paginator).
- Strategies library (new project, refs CollectorExecutor only — no cycle): DefaultPaginationStrategy +
  StrategyRegistryDefaults (CreateDefault/AddDefaults). Composition layer (Runner LocalRunHost + test
  harness) registers IStrategyRegistry into context.Services.
- OPEN/CLOSED proven by test: a test-local SinglePagePaginator ("single_page") registered + a profile
  selecting it → runner uses it with ZERO CollectorExecutor edits. 4th test green.
- Other seam interfaces DECLARED as extension points (IWindowPlanner, IStagePlanner, IPageSizeStrategy,
  IRecordMapper) — not yet wired into the runner (their behavior remains inline; migrating them behind
  the seams is the next Phase-0 slice).

PARTIAL vs contract: pagination is the only seam wired through the registry this slice; hydration,
mapping, window, stage remain inline (interfaces exist). Honest partial — flagged for the verifier.

Build: dotnet build green. Tests: 4/4 (assets=5, findings=3, resume no-overlap, open/closed single_page=2).

## Execution increment 3 (2026-06-18) — close the dead-abstraction finding, behavior-preserving

Addresses verifier/reviewer M1 (dead abstraction):
- Wired RecordMapper seam: runner resolves IRecordMapper "dotpath" from the registry (DotPathMapper
  delegates to the interpreter's ResponseMapper). Both map sites now go through the seam.
- Wired PageSize seam: runner resolves IPageSizeStrategy "static"; effective page size = strategy.Resolve.
- REMOVED the two speculative planner interfaces (IWindowPlanner, IStagePlanner) — no caller until the
  time-segmentation / Spotlight-lanes phases; they return then (don't ship speculative API).
- Registry extended (mapper + pagesize) with fail-closed resolution; defaults registered in
  StrategyRegistryDefaults.
- Build green; tests 4/4 unchanged (behavior preserved).

Seam status now: Paginator + RecordMapper + PageSize are registry-driven. Hydration remains a clean
inline component (already generic via declared id_style) — formalizing it as a registry seam rides
with the next segment. Decision vocabulary (RunContext/FetchSignal) retained — consumed by the
cursor_watermark hardened strategy (next segment, the payoff).

NEXT SEGMENT (its own coordinator pass): lift Falcon cursor_watermark + depth-cap as the first REAL
hardened pagination strategy — makes the decision vocabulary live (ResetToWatermark), adds the
watermark to the checkpoint, and proves the registry indirection is genuine (a distinct strategy,
not the re-switching shim flagged in M2).
