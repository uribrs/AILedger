# Constraints

- net8.0; build on Shared + http.package; NO embedded engine copy.
- Behavior-preserving is the HARD gate — no functional change to the happy path this pass.
- Narrow YAML vocabulary: named strategies + declared params only; no code/scripting in YAML.
- Resist inner-platform creep: coarse seams (the listed set), not a plugin per micro-behavior.
- Strategy selection is declarative; the orchestrator must NOT branch on vendor identity.
- Open/closed: adding a strategy = new registered component + profile entry; ZERO orchestrator edits.
- Coupled quirks coordinate ONLY via the shared RunContext + the small decision vocabulary — strategies never reach into each other.
- Always-on invariants (recovery budget, partial-page-success, checkpoint ordering before AdvancePage, server-delay externalization, bounded-memory streaming) are guaranteed by the orchestrator, parameterized but never opt-out.
- Load-time validation fails closed: unknown/under-specified named strategy => reject before any HTTP call.
- Lift from FalconCollector/YamlCollector/Shared; do not reinvent. Mirror YamlCollector's mechanism (PaginatorFactory pattern, checkpoint Strategy discriminator, CanResumeFrom gating).
- Generic strategies live in a shared Strategies library inside CollectorBase.
- No changes to the reference adapters repo.
- Regression oracle must be LOCAL to CollectorBase (copy the unit test project in).
