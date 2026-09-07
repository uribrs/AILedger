Role:
You are a senior .NET platform architect producing an SDK-extraction plan.

Goal:
A signed-off-ready PLAN to extract the former `Shared` library into a properly-structured
**IntegrationInfra** whose hard success criterion is a single capability surface ("the capability
bank") that adapters plug into while importing ZERO mechanics namespaces. Read-only analysis of the
real tree is expected; produce no mutations.

Context:
- Standalone tree at /Users/user/Dev/Uri/localprojects/IntegrationsInfra (169 .cs, 15 areas:
  Contracts, Converters, DataPipeline, DependencyInjection, Diagnostics, Events, Exceptions, Glossary,
  Helpers, Models, Orchestration, Recovery, Resilience, Session, Time). Consumers live in the
  cymulate-integration-adapters repo (native collectors + CollectorExecutor).
- Decisions D1–D6 in decisions.md are established inputs (acyclic deps; facade+mechanics+composition-
  layer topology; single-entry-not-god-object; no compat burden; Orchestration is name-only;
  complete-vs-hybrid spine). Do not re-debate them.
- Measured: import sites — Orchestration 182, Session 136, Events 130, Glossary 122, Resilience 65,
  DataPipeline 62, Recovery 36, Time 21, Contracts 19, DI 16, Exceptions 11; 10 public interfaces vs
  136 public classes (13:1). New tree does not build standalone (no .sln, not git, CPM refs lack
  versions, no Directory.Packages.props).

Constraints:
- See constraints.md. Planning only; read-only; single-entry is the hard bar; SDK 3.2.0 is the floor;
  auth via http.package; no vendor identity in infra; deps.json plugin deploy preserved; census before
  any interface sketch.

Success Criteria (the plan must deliver all six):
1. USAGE CENSUS (gating): quantify, across the real adapter consumers in cymulate-integration-adapters,
   how much substrate usage is capability/contract-shaped vs raw-internal. The ratio resolves A1
   (single-entry feasible vs leaks). Report method + the number, not a guess.
2. COMPLETE-vs-HYBRID assessment of all 15 areas: genuinely-complete concepts vs hybrids/incomplete;
   hybrids flagged for reconsideration (rework/drop/defer), not enshrinement.
3. DECOMPOSITION MAP: per area → disposition {Facade-abstraction | Mechanics | Stays-in-adapters-repo
   (composition layer) | Evict} + effort (S/M/L) + benefit + one-line rationale.
4. NAMING: concrete package ids + the composition-layer name replacing "Orchestration", named for
   actual operation (not cool names).
5. SEQUENCING + cost/benefit: phased path, reconciled honestly with the single-entry hard requirement
   (a "fat" package that preserves wide imports does NOT satisfy it — name that tension).
6. LITMUS / definition-of-done: post-change an adapter imports the composition layer + SDK contracts +
   ZERO mechanics namespaces.

Execution Rules:
- Do not assume missing data — read the real tree and the real consumers.
- Respect constraints strictly; no structural changes this run.
- Census precedes interface design. If the census REJECTS A1, stop and surface the go/no-go.

Output Format:
- Update task artifacts (orchestration_plan.md, research/<topic>.md, review/) per the orchestrator.
- Final synthesis must present, in order: census ratio + method, complete/hybrid table, decomposition
  map, naming, phased sequencing with cost/benefit, and the litmus — plus an explicit go / no-go on A1.

Stop Conditions:
- Goal achieved when a signed-off-ready plan with all six deliverables exists and the census ratio +
  go/no-go are surfaced for the user.
- Stop before any structural change (rename/rewire/fat-removal).
- Stop and surface if the census rejects A1 (single-entry not cleanly feasible).
