# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: All buildable work converges on three files (`CollectorExecutorSessionFactory.BuildAuthSelection`,
  Profile auth validation, one integrations/*.yaml) + tests. Three target vendors but one shared factory
  method — decomposing would cause write-contention, not parallelism. High coupling, low separability → direct.

## Research Decisions
- Topic: auth-selection-surface — triggered by assumption A1 — status: complete (resolved inline via Explore agent; A1 VALIDATED).
- Topic: target-vendor-auth-flows — triggered by assumption A2 — status: complete (resolved inline via Explore agent; A2 VALIDATED).
- A3, A4 resolved inline (A3 VALIDATED, A4 REJECTED). No further research needed.

## Worker Plan
Not applicable — direct path.

## Synthesis Approach
Not applicable — direct path.

## Scope (post-research)
- PRIMARY: `username_password_token_exchange` case → Guardicore proof profile + tests. (Required for SC4.)
- SECONDARY: `token_exchange` case → ServiceNow; resolve Basic-on-token wrinkle or document partial gap.
- DEFERRED: `negotiated` (SentinelOne composite) — documented follow-up unless trivial.
- Excluded: bearer (== api_key+header_prefix, A3), AwsSigV4 (not a Shared variant, A4).

## Verification Obligations
- Cross-check against prompt_contract.md SC1–SC6.
- SC2: full suite stays green (>=63) + new per-auth-type tests.
- SC3: no auth type added that a target doesn't use or Shared can't express; gaps documented not hacked.
- SC4: >=1 previously-unexpressible vendor (Guardicore) now a working YAML profile, auth verified vs native (file:line).
- SC5: spot-check no regression of passthrough / poll-and-drain / fail_if / validate probe.
- SC6: auth-note.md present with mapping + per-vendor trace (file:line) + excluded set.
- Guardrail: secrets only from credentials; no vendor branch in the engine; no Shared changes; no engine side-channel for ServiceNow's Basic-on-token.
