# Next Agent Handoff

## Start Here

Read these files first:

1. `ai/active/2026-05-13_1238_query-domain-creation/state.json`
2. `ai/active/2026-05-13_1528_query-section-tracker-slice/state.json`
3. `ai/active/2026-05-13_1528_query-section-tracker-slice/phase_documentation.md`
4. `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/README.md`
5. `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/Dataflow/README.md`

## Current State

The Query Shared planning, topology composition, unit dispatch, distribution, admission-only matcher, and section tracker stages are complete:

- `QueryPlanBuilder` implements SDK `IPlanBuilder`.
- `QueryPlanOptimizer` implements SDK `IPlanOptimizer`.
- `NativeQueryCoalescer` implements SDK `INativeCoalescer`.
- `QueryAdapterPipeline` implements SDK `IQueryAdapterPipeline`.
- `QueryUnitDispatcher` implements SDK `IUnitDispatcher`.
- `QueryDistributor` implements SDK `IDistributor`.
- `QueryMatcher` implements SDK `IMatcher` as an admission-only stage.
- `QuerySectionTracker` implements SDK `ISectionTracker` with store-driven completion detection.

Final validation for this slice:

```text
dotnet build src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Cymulate.Integration.Adapters.Shared.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false
```

Passed with 0 warnings and 0 errors.

```text
dotnet test src/Cymulate.Integration.Adapters/UnitTests/Query/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false -v minimal
```

Passed: 105 tests (93 prior plus 12 new tracker tests).

NU1900 warnings appeared because external vulnerability indexes were unavailable.

## Behavior To Preserve

- Tracker constructor takes `ExecutionPlan`, SDK `IExecutionPlanStore`, and optional `QuerySectionTrackerOptions`.
- Findings accumulate thread-safely per `(SectionId, QueryId)` and preserve arrival order in emission.
- `RecordUnitFailureAsync` attributes the error to every `(SectionId, QueryId)` whose `QueryId` is in the failing unit's `RepresentedQueryIds` AND in the section's membership; first error per pair wins.
- `RecordUnitFailureAsync` throws `InvalidOperationException` for an unknown unit id.
- Section emission requires (a) every representing unit in terminal state per the store AND (b) every unit in `Failed`/`Cancelled` to have a matching `RecordUnitFailureAsync` recorded. The tracker waits rather than emit a section with a missing error.
- `QueryResultV2.Findings` are inline `FindingRefV2` only; `ByteSize` is UTF-8 byte count of the raw JSON text; `S3Uri` and `StreamedBatchCount` are always null in this stage.
- `QueryResultV2.ResultType` is taken from the representing unit's `ResultType` (planner-enforced invariant: all representing units share the same `ResultType`).
- `SectionResultV2.EmittedAt` is set to `DateTimeOffset.UtcNow` at emission.
- `CompletedSections` is single-consumer; a second call throws `InvalidOperationException`.
- Cancellation propagates as `OperationCanceledException` from all three SDK methods.
- The tracker does not emit `QueryJobCompletedV2` (publisher's responsibility) and does not own Spillover, publisher transport, recovery, or store implementation.

## Hygiene Note

`GlobalUsings.cs` was added to the Query shared test project so it compiles after the operator/linter trimmed per-file usings from `QueryDistributorTests.cs` and `QueryMatcherTests.cs`. New test files in this project can be written with minimal explicit usings; the global file covers the common Dataflow/SDK namespaces. If the operator later restores per-file usings, the global file can be removed and individual `using` directives reinstated. Either pattern is acceptable; mixing both produces no warnings under current build settings.

## Recommended Next Slices

Choose based on what the operator wants to unblock next:

1. **Wire `QuerySectionPublisher` to consume `QuerySectionTracker.CompletedSections`** — currently the publisher exists but no stage feeds it. A bounded composition slice would connect tracker output to publisher input inside `QueryAdapterPipeline`. Includes `QueryJobCompletedV2` emission at job end.
2. **Concrete `IExecutionPlanStore` port + in-memory test fake** — the tracker depends on this interface; without a concrete implementation, dispatcher cannot drive the tracker in production. Decision needed on whether Shared hosts an in-memory store as a fallback or whether the Postgres concrete (per SDK doc) lives only in IntegrationServiceBus.
3. **Spillover stage** — convert inline `FindingRefV2` into S3-backed refs above a size threshold. Needs the operator's call on whether Spillover lives in Shared or stays in IntegrationServiceBus.
4. **Real or fake Query client base** — a thin SDK-derived `BaseQueryAdapter` subclass useful for adapter authors. Requires the operator's call on whether fake clients belong in product code.

## Still Not Next Without A New Contract

- Real Query clients
- Fake Query clients as product code
- Query recovery planner/resume runner
- ServiceBus/RabbitMQ/Postgres/outbox/storage/session/HTTP infrastructure
- Structured matcher filtering (requires SDK to define expression dialect, payload-side targeting schema, or payload-side result type first)
- Capability-aware passthrough swap in pipeline composition (requires the structured matcher first)
- Per-job large-plan paging of `GetUnitStatesAsync` (out of scope until benchmarks demand it)

## Open Product/SDK Questions

- Should composite `QueryV2.ResultType` be rejected, split, or treated as a combined result type? (Matcher and tracker currently pass the combined value through verbatim.)
- Should `QueryV2.Expression` gain `ExpressionDialect`?
- Should `QueryTargeting` gain cloud/SaaS dimensions?
- Should native query normalization be more than trim-only?
- What exact boundary owns `INativeCoalescingOptOut` decisions?
- Where will per-logical-query output caps be enforced after merged physical requests?
- Should SDK `AdapterProgressContext.FromPlatformEvent` explicitly map Query topics to `AdapterCategory.Queries`?
- Should SDK `DispatchAsync` or `ExecutionPlan` carry job settings so dispatcher can honor `QueryJobSettings.MaxConcurrentUnits` exactly?
- Should duplicate logical query ids across sections remain supported fan-out behavior?
- Where does the SDK schema for payload-side fields (IP, hostname, result type) live so the matcher can apply structured filtering?
- Should `ISectionTracker` gain a `RecordUnitCompletedAsync` API to avoid store polling, or is the persistence-store path the long-term design?
- Where does Spillover sit in the pipeline — between tracker and publisher, or inside the publisher?
