# Assumptions

- A1 — The current runner's behavior (auth → render → fetch → hydrate → map → emit → checkpoint, cursor/offset/page/link pagination, id_style hydration) can be expressed as default strategies behind the seams with NO functional change. STATUS: OPEN (validate by green tests + identical output).
- A2 — The six seams (WindowPlanner, StagePlanner, Paginator, PageSize, Hydration, ResponseMapper) + resilience + recovery-budget are sufficient to host Falcon's later quirks without adding seams. STATUS: OPEN (confirm against the Falcon quirk list; if a quirk needs a 7th seam, record it, don't force-fit).
- A3 — A shared RunContext extending AdapterProgressContext is the right coordination channel for the decision vocabulary, and AdapterProgressContext is extensible/wrappable for that. STATUS: OPEN (verify the Shared type allows it; if sealed, use a sidecar context holding the AdapterProgressContext).
- A4 — The CollectorExecutor unit test project (in the adapters repo) can be copied into CollectorBase and run against the local projects with only reference-path edits. STATUS: OPEN (verify test infra deps — Collectors.Tests.Infrastructure — are available or copied too).
- A5 — The real-Falcon run shape (assets 29 / findings ~25.5k, sourceType + aid) is a valid regression signal even though live counts drift; the structural invariants (record shape, correlation key, page-granular checkpoints) are what must stay identical, not exact counts. STATUS: OPEN.

# Open question (raise with orchestrator if it blocks)
- Q1 — Do generic strategies belong in CollectorBase's Shared, in a NEW Strategies library, or inside CollectorExecutor? Plan says a separate Strategies library; confirm placement doesn't create a cycle with Shared.
