# Verifier-1 — Collector Executor Seam Refactor (Phase 0)

Date: 2026-06-18
Scope verified: both increments (1 = local oracle/A3/A4; 2 = pagination seam + registry + open/closed).
Method: read all artifacts + planning files; `dotnet build` (0 errors, 12 NU1507 warnings only); `dotnet test Tests/CollectorExecutor.Test` → **Passed! Failed: 0, Passed: 4**.

## Per Success Criterion

1. **Behavior-preserving (assets=5, findings=3, resume no-overlap)** — **MET.**
   All 3 oracle tests + the 4th open/closed test pass green. The happy-path loop in `CollectorExecutorRunner.RunAsync` is unchanged except the pagination call now goes through `paginator.Next(...)`; the default paginator (`DefaultPaginationStrategy`) delegates verbatim to `Paginator.Next` (Interpreter.cs). Checkpoint ordering (SetCursor + SetState BEFORE AdvancePage), partial-success, retry-after externalization, sourceType + correlation key all intact.

2. **Decision-vocabulary contract defined + documented** — **MET (as a declaration).**
   `Seams/Seams.cs`: `FetchDecision { Continue, Stop, ResetToWatermark, DeferUnbudgeted, Retry, Fail }` + `FetchSignal` record + `RunContext` sidecar over the host `AdapterProgressContext`. Documented in the file header. **Caveat:** vocabulary is *defined but inert* — the runner never reads `runCtx.Signal` and `paginator.Next` returns a `Paginator.Step`, not a `FetchSignal`. It is a contract surface, not yet a live control channel. Acceptable for Phase 0 (no quirk emits a signal yet), but flag: the decision vocabulary is unexercised by any code path.

3. **Registry resolves by name + fails closed** — **MET.**
   `StrategyRegistry` (case-insensitive dict) resolves by name; `ResolvePaginator` throws on unknown. Runner checks `registry is null` → `MISSING_STRATEGY_REGISTRY` and `!HasPaginator` → `UNKNOWN_STRATEGY`, **before any HTTP call**. Fail-closed verified. **Caveat:** this gate lives in the runner, not in `ProfileLoader.Validate` (which validates other fields). Contract wording says "load-time validation"; substantively (reject before HTTP) is satisfied, but it is not in the load-time validator. Minor placement nuance.

4. **Open/closed genuinely demonstrated** — **MET. Real proof, not hollow.**
   `OpenClosed_NewPaginationStrategy_SelectedByYaml_NoRunnerEdit`: a test-local `SinglePagePaginator` ("single_page") implements `IPaginationStrategy.Next` directly (returns `HasMore:false`) — it does **not** route through the Interpreter switch. Registered into the registry, selected by `pagination.strategy: single_page` in YAML; runner uses it, asserts records=2 / single batch. Confirmed: zero CollectorExecutor source edits, no vendor-identity branching anywhere in CollectorExecutor (`grep falcon/crowdstrike/qualys` → none in runner logic). Genuine.

5. **Generic strategies in a separate Strategies library, no cycle** — **MET.**
   `Strategies.csproj` references `CollectorExecutor` only; CollectorExecutor has **no** ref back (verified — runner depends on `IStrategyRegistry`/`IPaginationStrategy` abstractions only). Composition layer (Runner `LocalRunHost`, test harness) wires the concrete defaults via DI. No cycle. Build confirms.

6. **All six seams reimplemented as defaults behind the seams** — **PARTIAL / contract under-delivery.**
   Only **Paginator** is wired through the registry. `IWindowPlanner`, `IStagePlanner`, `IPageSizeStrategy`, `IRecordMapper` are **declared as empty interfaces** with NO default implementations and are NOT resolved or invoked by the runner — hydration, mapping, page-size and window/stage logic remain inline in `RunAsync`. The criterion "Seam interfaces exist ... with today's behavior reimplemented as the default strategy in each" is met for 1 of 6 seams. Increment 2 notes flag this honestly as a partial. **Assessment: acceptable as a Phase-0 *slice* only if the orchestrator accepts incremental seam migration; against the literal contract it is a MISS** — the contract asked for all listed seams to have default strategies this pass, not just pagination.

7. **Invariants vs opt-in strategies explicitly separated; invariants orchestrator-guaranteed** — **PARTIAL.**
   Invariants (checkpoint ordering, partial-page-success, server-delay externalization, bounded-memory NDJSON streaming) remain hard-coded in the runner = orchestrator-guaranteed, not opt-out. Good. But there is no explicit RecoveryBudget seam object; per decision B1 the recovery/resilience seam default = today's behavior (no budget), deferred to Phase 1. The "separation" is documented in prose, not materialized as a distinct invariant-vs-strategy code boundary. Consistent with B1; partial against the "explicitly separated" wording.

8. **No Falcon hardened quirks implemented** — **MET.** No cursor_watermark, depth-cap, lanes, segmentation, or vendor failure policies. `watermark_field` appears only as inert YAML; `RunContext.Watermark` is an unused field.

9. **Local regression oracle (test project copied in, green)** — **MET.** Test project + `Collectors.Tests.Infrastructure` in CollectorBase/Tests, both in the sln, green locally. A4 resolved in code.

10. **A3 (RunContext over host AdapterProgressContext)** — **MET.** Sidecar (`RunContext.Progress`) used because the context is host-created; matches the documented fallback.

## Drift / contradictions / invalidated assumptions

- **`.slnx` not `.sln`** — contract/instructions reference `CollectorBase.sln`; actual file is `CollectorBase.slnx`. Cosmetic, builds fine.
- **A2 (six seams sufficient for Falcon quirks) — UNVALIDATED.** Cannot be confirmed because 5 of 6 seams are empty marker interfaces with no behavior and the quirks are out of scope. Still genuinely OPEN; not falsified, but not validated either. Should not be marked resolved.
- **B1 contract contradiction (always-on RecoveryBudget vs behavior-preserving)** — resolved by deferring the real budget to Phase 1. Sound call; the "always-on invariant" budget from the original Success Criteria is therefore NOT present this pass by design. Documented.
- **Decision vocabulary + 4 seam interfaces are dead surface** this pass (no caller). Not wrong for Phase 0, but they are speculative until a Phase-1 quirk exercises them — watch for interface drift when the first real consumer lands (e.g. `IRecordMapper.Map` signature was guessed, not driven by a consumer).
- Repo is **not git-tracked** (`fatal: not a git repository`) — execution_notes already noted this; no behavior-preservation diff is recoverable via git, only via the green oracle.

## Overall verdict

**ACCEPT WITH NOTED GAPS — conditional on the orchestrator agreeing Phase 0 ships as seam-by-seam slices.**

The hard gate (behavior-preserving, oracle green) is fully met, the registry + fail-closed mechanism is real, the library boundary is clean, and the open/closed proof is genuine (not hollow). The honest shortfall: the contract asked for **all six** seams to carry default strategies this pass; only **pagination** is wired. The other four seams and the decision vocabulary exist as declared-but-unwired surface. The execution notes flag this transparently, so it is not concealed drift — but it is a real delta from the literal Success Criteria and must be an explicit orchestrator decision, not silently accepted.

### Concrete remaining gaps
1. Wire the four declared seams (Window, Stage, PageSize, RecordMapper) with default strategies that reproduce today's inline behavior (the literal criterion). Until then mark criterion #6 PARTIAL, not MET.
2. Make the decision vocabulary live: have `paginator.Next` (or a successor) emit/honor `FetchSignal` so `RunContext.Signal` is actually read — otherwise the vocabulary is untested API.
3. Move (or mirror) the unknown-strategy fail-closed check into load-time `ProfileLoader.Validate` to match the "load-time validation" wording, or amend the contract to say "pre-HTTP."
4. Leave A2 marked OPEN — six-seam sufficiency is asserted, not demonstrated.
5. Minor: reconcile `.slnx` vs `.sln` references; note repo is not under version control.
