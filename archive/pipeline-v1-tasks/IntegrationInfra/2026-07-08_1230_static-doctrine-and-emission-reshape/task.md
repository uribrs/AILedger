# Task: Static-Taxonomy Doctrine + Emission Publisher Reshape (Wave 1)

Repo: /Users/user/Dev/IntegrationInfra, branch off `dev`. Two parts, sequenced — Part 1 is the review basis for Part 2.

## Part 1 — Doctrine (DESIGN.md)

Add a normative **"Static taxonomy"** section codifying the toolshed paradigm's rule for statics:

| Class | Ruling | Examples |
|---|---|---|
| Pure-function statics | KEEP — a static method is the correct tool shape | LogRedaction, IocTypeDetector, PrivateIpDetector, AdapterTriggerParsing, RecoveryParsingHelper, HttpTransportFailureClassifier, NormalizedUtf8Json |
| Explicit-args statics | KEEP — mutate only what's handed to them; deps visible; testable as-is (proven by the invariant pin tests) | AdapterRecoveryBudget, BatchScopedStorage, CheckpointAdapter, AdapterSessionLifecycle, AdapterFailureDecisionExecutor |
| Hidden-dependency statics | PROHIBITED — static bodies that resolve services/options from an execution context's provider (service-locator). Reshape to composable instances with explicit ctor deps | the Emission publisher stack (sole current offender) |

Also: update the deferred-seams list (mark "kill the static publisher (DIP)" resolved by this task) and reconcile any "Design decisions" text that contradicts the new doctrine.

## Part 2 — Reshape (Emission publisher stack)

Current: `ResultsBatchPublisher` (public static engine; calls `ThrottlingOptions/BufferingOptions/MemoryPressureOptions.Resolve(context.Services)` inside static bodies — grounded 2026-07-08, lines 56-71/176-191) + `AdapterNdjsonPublisher` (public static façade: findings/assets naming gate, page paths). Internal batch sessions stay internal.

Target: one public instance emitter (behavior-named, e.g. `NdjsonBatchEmitter`):
- Constructed with the four options objects explicitly (ctor or single options record).
- Static `Create(IServiceProvider?)` factory performing today's DI→IConfiguration→env→default resolution ONCE at construction (preserving option-sourcing behavior), not per publish call.
- Same publish operations: string + utf8, page-level (findings/assets naming gate + `BuildMandatoryTargetPath`) and generic batch entry points.
- `IAdapterExecutionContext`/progress-context stay as METHOD parameters (per-call data, not construction deps).
- **Ruling: static entrypoints DELETED, no thin wrappers** (zero consumers exist; wrappers are additive later, breaking to remove later).
- Out of scope: `ThrottlingAdapterExecutionContext` (already instance), `AdapterOutputDefaults`/`AdapterGlobalDefaults` (constants stay), every other concern.

Behavior identical: option defaults + resolution precedence, 5 MiB min-part / 50 MiB part cap / 10,000-part limit / 24 MiB soft warning, four-tier flush semantics, commit-incomplete guard, abort-once, storage-path prefixing.

Blast radius (grounded): Emission concern internals + 24 test call sites (AtomicStreamedObjectsTests ×20, BatchScopedStorageTests ×4). No other src concern references the statics.

Docs: Emission/README.md + README.Publishing.md updated to the new surface; ISink note kept only if a genuine future sink abstraction remains per the README's own framing (the instance emitter retires the DIP debt; ISink is NOT being introduced now).
