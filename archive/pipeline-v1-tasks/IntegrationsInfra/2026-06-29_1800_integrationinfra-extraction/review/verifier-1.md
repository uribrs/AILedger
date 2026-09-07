# Verifier-1 — IntegrationInfra extraction plan

Independent verification against the six Success Criteria + the original request (single capability
bank, ZERO mechanics namespaces, planning-only, A1 go/no-go). I spot-checked the load-bearing census
numbers against the real trees rather than trusting them.

## Spot-checks I actually ran (evidence)

- **Trees exist as described.** `IntegrationInfra` has exactly the 15 named areas; 175 `.cs`
  (contract said 169 — close, non-material). Consumer repo
  `/Users/user/Dev/cymulate-integration-adapters/src/Cymulate.Integration.Adapters` exists (1634 `.cs`).
- **AdapterTopics = 547 refs** (census said 546). Essentially exact — high confidence in the census method.
- **Orchestration = 48 files** (census/D5 said 48). Exact. 125 delegate/`Func`/`Action` sites, 2 public
  interfaces → the "24-delegate template-method, not a layer" characterization is grounded in real shape.
- **10 public interfaces** total (contract/D5 said 10). Exact. (My `public class` grep found 93 not 136
  — pattern misses `record`/`static class` variants; the 13:1 ratio's load-bearing half — 10 interfaces —
  is exact.)
- **Session leak is real, not asserted:** `ResiliencePipeline` appears in a public signature
  (`UnknownFlowRetryPolicy.CreatePipeline(...)`); Session has 2 interfaces vs 19 classes;
  `AdapterHttpRequestFailedException` appears in **24 catch-filters** across consumers; Polly
  `ResiliencePipeline` in **31** consumer files. The per-symbol-site figures in census (58 / 55) run
  higher than my file-level counts, but symbol-sites legitimately exceed file counts and the **direction
  and existence** of the leak reproduce. Not fabricated.

No contradiction found between census.md and execution_notes.md — the tables are consistent (e.g.
Orchestration 74% reach-past appears in both; Egress hybrid framing identical). No constraint violated:
everything is read-only; nothing claims a structural change was made (execution_notes explicitly states
"No structural changes (read-only)").

## Per-criterion verdict

### 1. Census measured, not guessed — **PASS**
Method is stated (6 investigators, symbol-usage sites not `using` lines, capability-shaped vs
raw-internal). Per-cluster counts + ratios given. My independent re-counts (AdapterTopics 547≈546,
Orchestration 48 exact, exception catch-filters present, Polly in public signature) reproduce the
claims. This is the gating artifact and it is genuinely measured. Minor: per-site numbers (58/55) are
not independently reproducible at file granularity, but the classification is sound.

### 2. Complete-vs-hybrid grounded — **PASS**
All 15 areas (plus sub-areas) carry a verdict with a code-cited "why" (e.g. Resilience "0 consumers
rebuild the chain"; Orchestration "74% reach-past, 24-delegate"; hydrator "untyped dict + swallows
exceptions"). The Session/Events/Orchestration HYBRID calls reconcile with the leak evidence I checked.
Hybrids are flagged for reconsideration (rework/drop/defer), not enshrined — matches the user's spine.

### 3. Decomposition covers all 15 areas — **PASS**
Every area appears in the Deliverable-3 map with disposition {Facade | Mechanics | Stays-in-repo | Evict}
+ effort (S/M/L) + benefit + one-line rationale. Sub-areas (Session creation vs http-client vs
retry/transport; Egress vs Json vs Ingress; Events shapes vs Logic) are split out, which is correct
given the hybrids. Complete coverage.

### 4. Naming + sequencing concrete — **PASS**
Concrete package ids (`Cymulate.IntegrationInfra` mechanics, `Cymulate.IntegrationInfra.Abstractions`
facade), composition-layer name (`...Collectors.Runtime` / `CollectorHost` with `RunAsync`/`ResumeAsync`),
routing ns (`...Topics`). Named for operation, not cool names — satisfies the contract's explicit
constraint. Phased path (Phase 0/1/2/defer) present with effort + "why first".

### 5. A1 go/no-go stated + tension reconciled — **PARTIAL** (see pressure-test)
Go/no-go is explicit: "GO, reframed." The tension (fat package ≠ single entry) is named plainly in
Deliverable 5. The reasoning that Session/Events/Orchestration structurally leak is honest and
evidence-backed. The reframe itself needs scrutiny — below.

### 6. Litmus present and measurable — **PASS**
DoD is concrete and checkable: adapter imports `...Collectors.Runtime` (`CollectorHost`) + SDK and ZERO
infra-mechanics namespaces, with named forbidden namespaces (`Session.*`, `DataPipeline.*`,
`Resilience.Policies`, `Events.CollectorEnvelopes.Logic`). CollectorExecutor named as first probe.
Measurable by grepping adapter `using`s — exactly how I'd verify it.

## The reframe pressure-test (the crux)

**Claim under test:** "GO, reframed — single entry at the composition layer, not a substrate facade."
Is this honest, or a relabel that quietly fails "zero mechanics namespaces"?

**Verdict: HONEST, and logically valid — with one unproven load-bearing assumption that must be stated
as a risk, not a conclusion.**

Why it is honest:
- The user's literal requirement is about what **the adapter** imports, not about whether the substrate
  has a clean facade. "Else it's not really needed" targets adapter-facing surface. If `CollectorHost`
  is the one import and it internally pulls Session/Events mechanics, the **adapter** still imports zero
  mechanics namespaces. That satisfies the requirement as written. The relabel concern ("adapters import
  CollectorHost which pulls mechanics, so mechanics still present") conflates *transitive runtime
  dependency* with *namespace import* — the requirement is the latter. So this is NOT a dodge.
- The plan does not over-claim a clean facade. It explicitly says the facade-over-substrate is "partial
  and secondary" and that Session/Events stay mechanics. That matches the leak evidence. No over-claim.

The load-bearing assumption that is NOT yet proven (and the plan should flag harder):
- The reframe only holds **if `CollectorHost` can actually absorb 100% of the 74% Orchestration
  reach-past + the Session/Events leaks** such that no adapter needs to reach past it. The census proves
  the leaks are *currently* in adapter code; it does NOT prove they are *all absorbable* into a single
  host without re-exposing a mechanics handle. The plan asserts "all absorbable" (census.md line ~41)
  but that is a forward design claim, not a measured fact. CollectorExecutor is cited as the prototype,
  but CollectorExecutor is itself an adapter that currently imports 3 Orchestration namespaces +
  substrate — i.e. the prototype does **not yet** pass the litmus. So "GO" rests on a hypothesis the
  census cannot settle.
- D3's "graduated power — raw building blocks at the bottom for control-heavy adapters, all via ONE
  handle" is the escape hatch that could quietly readmit mechanics. If a control-heavy adapter reaches
  the "raw building blocks" tier through the handle, does that count as importing a mechanics namespace?
  The litmus (Deliverable 6) forbids the namespaces outright, which would forbid the bottom tier — there
  is an unresolved tension between D3's bottom tier and the litmus. The plan does not reconcile this.

This is why Criterion 5 is PARTIAL not PASS: the go/no-go is stated and the reframe is honest, but the
"GO" is conditional on an unverified absorbability hypothesis and an unreconciled D3-vs-litmus tension,
and the plan presents "all absorbable" with more confidence than the evidence carries.

## Overall verdict: **PASS WITH MUST-FIX QUALIFICATIONS** (signed-off-ready after the fixes below)

The plan delivers all six deliverables, the census is genuinely measured and reproduces, the
complete/hybrid and decomposition work is grounded, and the reframe is intellectually honest rather than
evasive. It is immediately usable by an executor for Phase 0 (Glossary evict) and Phase 2 (packaging the
complete concepts). It is **not** yet de-risked for Phase 1, which is the phase that actually earns the
requirement.

### Must-fix before execution sign-off (cheap, planning-level)
1. **Downgrade "all absorbable" from conclusion to hypothesis.** State the GO as conditional: feasible
   *iff* `CollectorHost` absorbs the reach-past with no adapter-facing mechanics handle. Make Phase-1
   migration of CollectorExecutor the **proof gate**: if the prototype cannot pass the litmus, A1 flips
   to NO-GO. Right now census.md asserts absorbability as fact.
2. **Reconcile D3's "raw building blocks via one handle" with the Deliverable-6 litmus.** Either the
   bottom tier is reachable (then define why exposing it through the handle does not count as a mechanics
   import) or it is not (then control-heavy adapters need an answer). This is a real contradiction an
   executor will hit on the first control-heavy adapter.

### Should-fix (non-blocking)
3. Census per-symbol-site figures (58/55 for Session) aren't reproducible at file granularity; state them
   as symbol-site counts with the raw file counts alongside so the numbers are auditable.
4. ".cs count" drift (169 contract vs 175 actual) — trivially update, non-material.

None of these require structural changes; all are edits to the plan text. The planning-only, read-only
constraint was respected throughout.
