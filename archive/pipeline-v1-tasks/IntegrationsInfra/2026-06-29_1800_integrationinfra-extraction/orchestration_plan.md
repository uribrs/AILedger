# Orchestration Plan

## Complexity Decision
- Path: **decompose**
- Rationale: The census + complete/hybrid + decomposition map are per-area analyses over a 15-area / 169-file tree plus its consumers in a second repo. Low coupling between capability clusters; high parallelism; synthesis (census ratio, naming, sequencing, go/no-go) stays centralized. Read-only throughout.

## Research Decisions
- researchNeeded = **false** in the technical-researcher sense: every OPEN assumption (A1/A2/A3/A4) is resolved by **in-repo code-trace**, not external-system behavior. Handled by read-only investigators, not `technical-researcher`.

## Worker Plan (read-only investigators; general-purpose, no mutations)
Each worker owns a capability cluster and returns, for every area it owns: (i) inventory + **complete-vs-hybrid** verdict with evidence; (ii) **consumer usage census** — count import/call sites in the adapters repo and classify each as *capability-shaped* (could route through a small interface) vs *raw-internal* (reaches concrete impl types/statics), with representative examples; (iii) proposed **disposition** {Facade | Mechanics | Stays-in-repo | Evict} + effort (S/M/L) + benefit + 1-line rationale.

- **W1 — Session** (incl. `Session.TransportErrorHandling`): auth/OAuth/http-client/retry surface.
- **W2 — DataPipeline** (`Egress`/`Ingress`/`Json`) + `Converters`: publishing/NDJSON/streaming. Flag the `CollectorNdjsonPublisher` static-entry seam specifically.
- **W3 — Resilience + Recovery**: failure-policy chain, recovery budget, `IAdapterFailurePolicy`.
- **W4 — Events/CollectorEnvelopes + Models + Exceptions**: run-envelope shapes (contract) vs parsers/hydrators (mechanics) split.
- **W5 — Orchestration + DependencyInjection**: the 48-file "name-only" cluster; assess whether it is a composition layer or call-helpers, and what the real composition-layer boundary should be.
- **W6 — Glossary + Contracts + Helpers + Diagnostics + Time**: cross-cutting + the evict candidate (Glossary, incl. the native-collector self-identity wrinkle).

Consumers to inspect (read-only): `/Users/user/Dev/cymulate-integration-adapters/src/Cymulate.Integration.Adapters` — native collectors, `Collectors/CollectorExecutor`, `Strategies`.

## Synthesis Approach
Main thread aggregates: (1) the global **census ratio** + per-cluster numbers → resolves A1 go/no-go; (2) the **complete/hybrid** table (15 areas); (3) the **decomposition map**; (4) **naming**; (5) **phased sequencing + cost/benefit** reconciled with the single-entry hard bar; (6) the **litmus**. Contradictions between workers resolved centrally.

## Verification Obligations
- Cross-check against the six Success Criteria in prompt_contract.md.
- Census must be **measured** (counts + method), not guessed.
- Complete/hybrid verdicts grounded in cited code.
- Decomposition covers all 15 areas; A1 go/no-go explicitly stated.
- No structural changes were made (read-only).
