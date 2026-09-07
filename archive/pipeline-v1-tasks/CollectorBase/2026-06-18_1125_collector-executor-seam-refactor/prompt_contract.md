Role:
You are a principal .NET integration-platform engineer refactoring a YAML-driven collector into a
seam-based, strategy-composition architecture (on the Cymulate Shared + http.package substrate).

Goal:
Refactor `CollectorExecutor` (in CollectorBase) from a fixed inlined runner loop into a composition
root that wires named, YAML-selected, registered strategies into a fixed set of seams, and define the
cross-seam decision-vocabulary contract — so future vendor quirks drop in without touching the
orchestrator. Strictly behavior-preserving (Phase 0).

Context:
- Working repo: /Users/user/Dev/Uri/localprojects/CollectorBase (net8.0 sln: Shared, CollectorExecutor, Runner).
- The runner today: CollectorExecutor/Execution/CollectorExecutorRunner.cs (auth→render→fetch→hydrate→map→emit→checkpoint; cursor/offset/page/link pagination; id_style hydration; fingerprint-guarded resume).
- Reference (read-only): /Users/user/Dev/cymulate-integration-adapters — YamlCollector + engine (the strategy-selection MECHANISM to mirror), FalconCollector (hardened content for LATER phases), Shared (AdapterResilienceStrategy, IAdapterFailurePolicy, AdapterRecoveryBudget, AdapterProgressContext, CollectorNdjsonPublisher).
- A real run produced assets=29 / findings≈25.5k with records carrying sourceType + aid; unit tests assert assets=5 / findings=3 / resume-no-overlap.

Constraints:
- See constraints.md. Hard gate: behavior-preserving. Open/closed: new strategy = registered component + profile entry, ZERO orchestrator edits. Coupled quirks coordinate only via RunContext + the decision vocabulary. Invariants are orchestrator-guaranteed, not opt-out. Narrow YAML. net8.0, build on Shared/http.package, no engine copy. Lift, don't reinvent. No edits to the adapters repo.

Success Criteria:
- Seam interfaces exist (WindowPlanner, StagePlanner, Paginator, PageSize, Hydration, ResponseMapper) with today's behavior reimplemented as the default strategy in each.
- A strategy registry resolves seam implementations by name from the profile; load-time validation fails closed on unknown/under-specified strategies.
- The decision-vocabulary contract (ResetToWatermark | DeferUnbudgeted | Retry | Fail) is defined and documented, carried via a RunContext extending/holding AdapterProgressContext.
- Invariants vs opt-in strategies are explicitly separated; always-on invariants remain enforced by the composition root.
- Generic strategy components live in a shared Strategies library in CollectorBase (no project cycle).
- The CollectorExecutor unit test project is copied into CollectorBase and is GREEN (assets=5, findings=3, resume no-overlap); the solution builds; happy-path output is byte-identical in shape (sourceType + correlation key, page-granular checkpoints).
- Open/closed is demonstrated (a stub/example shows a new strategy could be registered + selected without editing the orchestrator) — without implementing any Falcon hardened quirk.
- No Falcon hardened quirks (cursor_watermark, depth-cap, lanes, segmentation, vendor failure policies) implemented.

Execution Rules:
- Do not assume missing data; if AdapterProgressContext cannot be extended (A3) or test infra can't be copied cleanly (A4), record the deviation and choose the documented fallback (sidecar context / copy Collectors.Tests.Infrastructure).
- Respect constraints strictly. Do not implement later-phase quirks. Do not branch on vendor identity.
- Verify behavior preservation by building + running the copied tests, not by inspection alone.

Output Format:
- Code under CollectorBase (CollectorExecutor + new Strategies library + copied test project), the sln updated.
- Update state.json + execution_notes.md as steps complete; record any new decisions/risks.

Stop Conditions:
- Stop when the refactor is complete, behavior-preserving, tests green, and open/closedness demonstrated.
- Stop and surface if behavior cannot be preserved without a functional change, if the seam set proves insufficient for a Phase-0 default (needs a new seam), or if a constraint cannot be met without implementing later-phase content.
