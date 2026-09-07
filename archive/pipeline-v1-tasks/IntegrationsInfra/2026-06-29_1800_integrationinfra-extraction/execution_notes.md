# Execution Notes

Contract phase complete. Decompose path: 6 read-only investigators (W1–W6) over the infra tree + the
adapters-repo consumers; synthesized centrally. Census → `research/census.md`. No structural changes (read-only).

---

## Deliverable 2 — Complete-vs-hybrid (the spine)

| Area | Verdict | Why |
|---|---|---|
| Resilience | **COMPLETE** | Clean `CreateDefault(...)` contract; fixed policy order is the invariant; 0 consumers rebuild the chain. |
| Contracts | **COMPLETE** | Clean public adapter/handler interfaces; facade seed as-is (`BaseFlowHandler` is the one base-class blemish). |
| Exceptions | **COMPLETE** | Two sealed types, zero deps. |
| Time | **COMPLETE** | Cohesive UTC/base-date resolution. |
| Events — envelope **shapes** | **COMPLETE** (as data) | Pure records; the real contract consumers want. |
| Glossary — `AdapterTopics` | **COMPLETE** (mis-filed) | Protocol/routing vocab (546 refs); infra, not catalog. |
| Glossary — `CollectorGlobalDefaults` | **COMPLETE** (mis-filed) | Egress/lookback substrate invariants. |
| DataPipeline — Json readers | **COMPLETE** (not a capability) | ~20 per-vendor `new` sites; keep exposed mechanics. |
| Session — creation (`ISessionFactory`/provider/handle) | **HYBRID** | Real seam, but `new`-ed concretely; `SessionSpec` leaks Http.Package options. |
| Session — http client (`AdapterHttpClient`) | **HYBRID** | Cohesive verb, no interface; concrete exception is the contract. |
| Session — retry/transport/cert/redaction | **HYBRID/INCOMPLETE** | Polly in contract; static classes; cannot bank behind one interface. |
| DataPipeline — Egress (`CollectorNdjsonPublisher`) | **HYBRID** (complete behavior, wrong form) | Static + assets/findings hardwired in 3 places + SDK-context-coupled. |
| DataPipeline — Ingress | **INCOMPLETE/thin** | 79 LOC, 2 callers (CortexXDR only). Leave. |
| Events — Logic (parsers/hydrator/builder) | **HYBRID** | Glued to shapes; hydrator returns untyped dict + swallows exceptions. |
| Orchestration | **HYBRID (name-only)** | 24-delegate template-method, not a layer; 74% reach-past. Rebuild as the real composition layer. |
| Glossary — Names/ZipNames/IndicatorNames | **HYBRID (mis-binned)** | Vendor/deployment catalog; evict to bus/BE. |
| Helpers | **HYBRID (junk-drawer)** | 5 unrelated statics; split by domain. |
| Recovery | **FRAGMENT** | 3 leaf checkpoint-dict utils; not a concept. Leave. |
| Converters | **ORPHANED** | 1 in-project consumer; Indicators carry own copies. Evict/fold. |
| Diagnostics | **COMPLETE but trivial** | 1 consumer (Falcon). Leave/fold. |

## Deliverable 3 — Decomposition map

| Area | Disposition | Effort | Benefit |
|---|---|---|---|
| Resilience (`CreateDefault` + `IAdapterFailurePolicy` + decision/backoff models) | **Facade** | S | High — 96% already use it |
| Resilience executor / budget internals | **Mechanics** | S | High |
| Recovery (3 utils) | **Stays-in-repo** | S | Low |
| Session — creation seam | **Facade** (extend `ISessionFactory`; impl→mechanics) | M | High |
| Session — `IAdapterHttpClient` | **Facade** (introduce interface) | S–M | High |
| Session — retry/transport/cert/redaction | **Mechanics** (stays concrete; leaks if abstracted) | L-to-abstract / S-to-leave | Low |
| DataPipeline — Egress → `IPageSink` | **Facade** (design the sink interface; `StreamKind` discriminator) | M | High |
| DataPipeline — Json readers, Ndjson/Multipart engine, options | **Mechanics** | S | Med |
| DataPipeline — Ingress | **Stays-in-repo** | S | Low |
| Events — envelope shapes + event-args | **Facade** | M | High |
| Events — Logic (parse/hydrate/build) | **Mechanics** (behind a "parse run envelope" capability) | L | High |
| Models/Platform (Ioa/Ioc) | **Evict** (not envelope material; drags external converter) | S | Low |
| Exceptions | **Facade** (as-is) | S | Med |
| Contracts | **Facade** (as-is) | S | High |
| Time, `CollectorGlobalDefaults` | **Mechanics** | S | Med |
| `AdapterTopics` | **Mechanics** (split out of Glossary into a routing ns) | S | High |
| Glossary — Names/ZipNames/IndicatorNames | **Evict** → bus/BE | M | High |
| Helpers | **Mechanics, split by domain** | M | Med |
| Diagnostics | **Stays-in-repo** | S | Low |
| **Orchestration** | **Stays-in-adapters-repo → the real composition layer** | M–L | High |

## Deliverable 4 — Naming (proposal, for actual operation)

- Mechanics package: **`Cymulate.IntegrationInfra`**
- Facade/abstractions package: **`Cymulate.IntegrationInfra.Abstractions`** (the "capability bank" contract)
- Composition layer (replaces "Orchestration", lives in adapters repo): namespace **`...Collectors.Runtime`**, entry type **`CollectorHost`** with **`RunAsync` / `ResumeAsync`**. ("Runtime/Host" = it composes and runs a collector flow — its actual operation.)
- Routing vocab (out of Glossary): **`...Topics`** (holds `AdapterTopics`).

## Deliverable 5 — Sequencing + cost/benefit (with the honest tension)

**The tension, stated plainly:** a "fat" infra package that simply re-publishes today's wide surface
does NOT meet the single-entry bar — adapters would still import mechanics. The single entry is delivered
by the **composition layer**, not the package split. So the package work is necessary but not sufficient;
the composition layer is the part that actually earns the requirement.

| Phase | Scope | Effort | Why first |
|---|---|---|---|
| **0** | Split Glossary: evict Names/ZipNames/IndicatorNames → bus/BE; keep `AdapterTopics`+`GlobalDefaults`. Re-home ~15 collectors' self-identity (~30 edits). | M | Decouples vendor identity before packaging; the one true catalog consumer (`CollectorRegistry`) moves with it. |
| **1** | Build the REAL composition layer (`CollectorHost`) in the adapters repo; absorb session-lifecycle/telemetry/guards/triggers wiring. Migrate adapters onto it, **CollectorExecutor first** (closest today). | L | This is what delivers the single-entry litmus. Highest benefit. |
| **2** | Package infra = `IntegrationInfra` (mechanics) + `IntegrationInfra.Abstractions` (facade for the COMPLETE concepts: Resilience, Contracts, envelope shapes, Time). Leaky bits (Session retry/transport, Json readers, hydrator) stay mechanics, consumed by `CollectorHost`, not adapters. | M | The composition layer is now the single substrate consumer → the facade narrows naturally. |
| **defer** | Forcing Session into a clean facade. | — | The leak is hidden behind `CollectorHost`; off the critical path. |

## Deliverable 6 — Litmus / definition-of-done

Post-change, a native adapter imports **`...Collectors.Runtime` (`CollectorHost`) + `Cymulate.Integration.Sdk`**
and **ZERO** infra-mechanics namespaces (no `Session.*`, `DataPipeline.*`, `Resilience.Policies`,
`Events.CollectorEnvelopes.Logic`, etc.). Baseline today: CollectorExecutor is closest (imports 3
Orchestration ns + substrate); it is the first migration target and the litmus probe.

**Graduated-power vs the litmus (resolving the D3 contradiction):** the bottom rung — raw building blocks
for control-heavy adapters — must be reached **through the bank** (re-exported via `CollectorHost` /
`IntegrationInfra.Abstractions`), NOT by importing an infra-mechanics namespace directly. The litmus
forbids importing `Session.*`/`DataPipeline.*`/etc.; it does NOT forbid a control-heavy adapter obtaining
a lower-level handle *from the capability bank*. If an adapter genuinely needs a raw block, that is a
signal to expose it as a bank rung — not a license to import mechanics. So D3 and the litmus are
consistent: one import surface, graduated depth behind it.

## A1 / go-no-go
**GO — CONDITIONAL on the Phase-1 proof gate.** Single entry is feasible **at the composition layer**,
not as a substrate facade (see `research/census.md`). The "74% reach-past is absorbable into
`CollectorHost`" claim is a **hypothesis**, not a settled fact; the **CollectorExecutor migration is the
proof gate** — if it cannot reach the litmus (zero mechanics imports), A1 flips to **NO-GO** and the
effort stops. Today CollectorExecutor imports 3 Orchestration namespaces; that must go to zero to confirm.
