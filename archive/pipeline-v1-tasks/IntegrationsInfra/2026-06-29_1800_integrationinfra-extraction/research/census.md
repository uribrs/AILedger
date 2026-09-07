# Usage Census — capability-shaped vs raw-internal (the make-or-break, resolves A1)

Method: 6 read-only investigators counted symbol-usage sites (not `using` lines) across the consumers
(`/Users/user/Dev/cymulate-integration-adapters/src/Cymulate.Integration.Adapters` — native collectors,
CollectorExecutor, Strategies), classifying each as **capability-shaped** (could route through a small
capability interface) vs **raw-internal** (reaches a concrete impl type / static / sub-namespace).

## Per-cluster numbers

| Cluster | Sites (≈) | Capability-shaped | Raw-internal | Ratio | Verdict |
|---|---|---|---|---|---|
| **Resilience + Recovery** | 45 files | ~96% | ~4% | 24:1 | **Clean.** Already a real `CreateDefault(...)` select-and-parameterize contract; 0 consumers hand-build chain policies. |
| **DataPipeline / Egress** | ~52 | ~62% | ~38% | 1.6:1 | **Designable.** One seam (`CollectorNdjsonPublisher`) — but static + assets/findings hardwired + SDK-context-bound. Json readers (~20 sites) stay exposed mechanics, not banked. |
| **Events / Envelopes** | ~145 files | ~75% (shapes+event-args) | ~25% (Logic) | 3:1 | **Leaky-uniform.** The raw quarter (37 files) is load-bearing: every collector's trigger pipeline reaches the parsers/hydrator directly. Hydrator returns untyped dict + swallows exceptions (leakiest API). Shape→logic back-edge (`CollectorStatus`→converter). |
| **Session (+Transport)** | ~733 | ~36% | ~64% | 1:1.8 | **Structurally leaks.** Polly `ResiliencePipeline` in the public contract (58 sites); `AdapterHttpRequestFailedException` inspected by type in `catch` filters (55 sites); `SessionSpec` re-exports Http.Package option types. Cannot hide behind a small interface. |
| **Orchestration (+DI)** | 144 files | ~26% | ~74% | 1:2.8 | **Name-only "layer" CONFIRMED.** 107/144 files reach PAST the entrypoint into sub-helpers (Guards, Triggers, Telemetry, SessionLifecycle). It's a 24-delegate template-method, not a layer adapters sit on. |
| **Glossary + cross-cutting** | see below | mixed | mixed | — | **Mis-binned, not monolithic.** `AdapterTopics` (546 refs) = protocol vocab (KEEP). `CollectorGlobalDefaults` = egress invariants (KEEP). Only `CollectorNames`/`ZipNames`/`IndicatorNames` = vendor catalog (EVICT). `Contracts` = clean facade seed. |

## A1 resolution — QUALIFIED GO (with a reframe), not a clean unconditional yes

The ratio is **not uniform — and that variance is the finding.** Complete concepts (Resilience, Contracts,
envelope shapes, Time, AdapterTopics) are clean-facade-ready. Hybrid concepts (Session, Orchestration,
Events) **structurally leak** and cannot be hidden behind a thin substrate facade:
- Session: Polly + concrete exception type + Http.Package options are *in the contract* across 100+ sites.
- Events: the parsers/hydrator are reached directly by every collector's trigger pipeline.
- Orchestration: 74% of usage reaches past the entrypoint.

**Therefore: a clean single-entry facade *over the substrate* is NOT feasible.** BUT the user's hard
requirement (an adapter imports ONE place + SDK + zero mechanics) **is** feasible — because the single
entry is the **composition layer (`CollectorHost`)**, NOT a facade over Shared. The 74% Orchestration
reach-past is exactly the scattered wiring (session lifecycle, telemetry hub, guards, triggers) that a
real composition layer *absorbs*. The Session/Events leaks then become **internal to the composition
layer + mechanics, hidden from adapters.**

So: **GO (conditional)** — the single-entry litmus is achievable via the composition layer absorbing the
wiring; it is NOT achievable by wrapping the substrate in a facade (Session/Events would leak through).
The infra facade is real but **partial and secondary**: clean where the concept is complete, mechanics
where it leaks — and that's acceptable precisely because adapters won't touch infra directly.

This confirms the session thesis: *the missing composition layer is the rub; the Executor is its
prototype.* The census quantifies the surface — 74% reach-past — and **hypothesizes** that this wiring is
absorbable into `CollectorHost`. That is a forward design claim the census cannot itself settle. **The
proof gate is the Phase-1 CollectorExecutor migration: if `CollectorHost` cannot absorb the reach-past
such that the litmus passes (CollectorExecutor imports only `Runtime` + SDK, zero mechanics), A1 flips to
NO-GO and the effort should stop.** Today CollectorExecutor does NOT yet pass the litmus (it imports 3
Orchestration namespaces) — that is the baseline the migration must move to zero.
