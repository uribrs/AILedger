# Assumptions

Every entry carries a status. An OPEN assumption must be resolved to VALIDATED or REJECTED with recorded
evidence before the step depending on it is reported complete.

---

## A1 — `Cymulate.Integration.Adapters.YamlAdapter` is the only consumer that matters — OPEN

The whole measurement rests on this. It is the only consumer that exists *today*, and it compiles
against the package, which is what makes leg (a) and leg (b) possible at all.

**What is NOT established:** that no other consumer exists or is planned. `YamlLocalRunner` names one
additional type. The old `Collectors/YamlCollector` still compiles the engine as *source* (so it is not
constrained by visibility at all and must not be used as evidence). And the Shared/SDK retirement in
`IntegrationInfra` may introduce a new host.

**Resolve by:** asking the operator to confirm the consumer set before anything is narrowed. A type
narrowed against an unknown consumer is a break discovered at their build, not ours.

## A2 — The 279-definition corpus is the right conformance population — OPEN

D5 makes the corpus the proof that YAML composition stays wired. But the capability-drift audit
established (`2026-07-29_1102_capability-drift-inventory`, A3 REJECTED) that **definitions also reach
the engine from outside the corpus** — inline in the ISB dispatch payload, or self-downloaded from S3
by name. `YamlOperationRunner.cs:450-453` in the pre-extraction collector is the evidence.

**Consequence:** "every key in the corpus resolves" is necessary but not sufficient. A vendor could
author `authentication.type: something_new` in an inline payload.

**Resolve by:** making the conformance test assert over the corpus *and* making unresolved-key failure
a clear, early, named error at load time rather than a silent no-op. The second part is what actually
protects the out-of-corpus case.

## A3 — Narrowing a type never changes behaviour — OPEN, and it is not free

Visibility is compile-time, so the intuition is that it is inert. Two ways that fails here:

1. **Serialization.** `WorkflowCheckpoint` and the checkpoint state are JSON round-tripped through the
   host. Some serializers ignore non-public types or members. A silently-broken checkpoint contract
   breaks *resume for runs already in flight* and no test would catch it — the drift audit named this
   the highest-severity risk class in the whole engine.
2. **Reflection.** Anything resolved by name rather than by reference.

**Resolve by:** before narrowing any type on the checkpoint or definition-deserialization path, grep
for reflection and serializer attributes, and round-trip a checkpoint through the actual host path in a
test. Do not assume `internal` is inert on those two paths.

## A4 — The engine's 735 tests will still compile after narrowing — OPEN

The repo deliberately has **no** `InternalsVisibleTo` (measured: 0 occurrences in the csproj), and its
seven existing `internal` types are covered through public entry points. Narrowing ~90 types may break
compilation of tests that reach them directly.

**This is the crux of the `InternalsVisibleTo` decision and must not be resolved by reflex.** If a large
number of tests break, the options are: add `InternalsVisibleTo` (which `CLAUDE.md` permits *at this
point* and not before), or rewrite those tests to drive through public entry points (which keeps the
tests honest about what consumers reach, at the cost of effort).

**Resolve by:** narrowing in a scratch branch first and counting the compilation failures by test file
*before* choosing. The count is the input to the decision. Report it.

## A5 — The three decompositions can be validated against the consumer — OPEN

They are breaking changes. Validation requires packing a local preview, pointing the adapter at it, and
keeping the adapter's 167 tests green.

**Risk:** the adapter branch is uncommitted work. Changing its engine version mid-flight risks
conflating engine breakage with adapter churn.

**Resolve by:** confirming the adapter branch is committed (or stashed to a known state) before the
first breaking change lands, so a failure has one cause.

## A6 — `CursorRecoverySnapshot` and `DelaySource` genuinely belong on the public surface — OPEN

Both are in the measured 13, so a consumer does name them — but they read like internal mechanics
leaking through a signature rather than intended API. `DelaySource` in particular is a *resilience*
concept, and resilience is exactly the kind of thing `CLAUDE.md` calls implementation detail.

**Resolve by:** finding the member that exposes each, then deciding whether the exposure is intended API
or an encapsulation failure worth fixing under the "decompose" operation. This is leg (b) work and is
where the interesting findings are likely to be.

## A7 — The existing architecture tests are a suitable home for the surface pin — VALIDATED

Measured: `tests/Cymulate.Integration.Yaml.Engine.Tests/Architecture/` contains `EngineSources.cs`,
`EngineFile.cs`, `RuleAudit.cs`, `DependencyRuleTests.cs`, `RuleComplianceTests.cs` — a Roslyn parse of
`src/` with a declared-type index already built. A public-surface pin is an additional assertion over
the same index, not new infrastructure.

Note the anti-vacuity lesson from that work: those tests once all passed while parsing **1 of 124
files**. The pin needs its own floor assertion proving it actually parsed the engine.

## A8 — 103 is the correct current count — VALIDATED

Measured 2026-07-30T07:28:03Z by regex over `src/**/*.cs`; per-namespace breakdown in
`execution_notes.md`, raw data in `measurements/engine_public_types.json`. Cross-checks: the count was
independently ~102 in the task-2 era notes, and the namespace distribution matches the concept folders.
