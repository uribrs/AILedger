# Next Agent Handoff

## Start Here

Read these files first:

1. `ai/active/2026-05-13_1238_query-domain-creation/state.json`
2. `ai/active/2026-05-13_1433_query-matcher-slice/state.json`
3. `ai/active/2026-05-13_1433_query-matcher-slice/phase_documentation.md`
4. `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/README.md`
5. `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Orchestration/Query/Dataflow/README.md`

## Current State

The Query Shared planning, topology composition, unit dispatch, distribution, and admission-only matcher stages are complete:

- `QueryPlanBuilder` implements SDK `IPlanBuilder`.
- `QueryPlanOptimizer` implements SDK `IPlanOptimizer`.
- `NativeQueryCoalescer` implements SDK `INativeCoalescer`.
- `QueryAdapterPipeline` implements SDK `IQueryAdapterPipeline`.
- `QueryUnitDispatcher` implements SDK `IUnitDispatcher`.
- `QueryDistributor` implements SDK `IDistributor`.
- `QueryMatcher` implements SDK `IMatcher` as an admission-only stage.

Final validation for this slice:

```text
dotnet build src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Cymulate.Integration.Adapters.Shared.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false
```

Passed with 0 warnings and 0 errors.

```text
dotnet test src/Cymulate.Integration.Adapters/UnitTests/Query/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false -v minimal
```

Passed: 93 tests (86 prior plus 7 new matcher tests).

NU1900 warnings appeared because external vulnerability indexes were unavailable.

## Behavior To Preserve

- Matcher emits exactly one `Finding` per input `DistributedResponse`.
- `Finding.JobId` is taken from the method parameter; `Finding.SectionId` and `Finding.QueryId` from `DistributedResponse`; `Finding.ResultType` from the matched `QueryV2`; `Finding.Payload` from the inner `RawResponse.Payload` without copying.
- `distributed.QueryId != query.QueryId` fails clearly with both ids in the exception message.
- Cancellation propagates as `OperationCanceledException`.
- Argument-null guards on `distributed` and `query` throw `ArgumentNullException`.
- The matcher does not filter by expression, IP/hostname, or payload-side result type.
- The matcher does not branch on `IAdapterSideMatchingCapability`; capability-aware substitution lives in pipeline composition.
- The matcher does not split composite `[Flags] ResultType` — the combined value passes through unchanged.

## Hygiene Note

The previous distributor slice committed `QueryDistributorTests.cs` with missing `using` directives. The matcher slice restored those imports as an in-scope precondition; no test behavior changed. Future slices should run a quick build before claiming validation in `state.json`.

## Recommended Next Slice

Implement the concrete Query `ISectionTracker` behind the SDK contract.

The section tracker slice should:

- accept SDK `Finding` instances flowing out of the matcher
- accumulate findings per section using SDK section identifiers
- emit SDK section-result and job-completion envelopes when section/job thresholds are met
- not own publication transport, recovery persistence, or client behavior

If you prefer to address the open `[Flags] ResultType` question first, do that as a separate planning slice; it changes downstream finding shape.

## Still Not Next Without A New Contract

- Real Query clients
- Fake Query clients as product code
- Query recovery planner/resume runner
- `IExecutionPlanStore` persistence
- ServiceBus/RabbitMQ/Postgres/outbox/storage/session/HTTP infrastructure
- Structured matcher filtering (requires SDK to define expression dialect, payload-side targeting schema, or payload-side result type first)
- Capability-aware passthrough swap in pipeline composition (requires the structured matcher first; today's default matcher is already a passthrough)

## Open Product/SDK Questions

- Should composite `QueryV2.ResultType` be rejected, split, or treated as a combined result type? (Matcher currently passes the combined value through verbatim.)
- Should `QueryV2.Expression` gain `ExpressionDialect`?
- Should `QueryTargeting` gain cloud/SaaS dimensions?
- Should native query normalization be more than trim-only?
- What exact boundary owns `INativeCoalescingOptOut` decisions?
- Where will per-logical-query output caps be enforced after merged physical requests?
- Should SDK `AdapterProgressContext.FromPlatformEvent` explicitly map Query topics to `AdapterCategory.Queries`?
- Should SDK `DispatchAsync` or `ExecutionPlan` carry job settings so dispatcher can honor `QueryJobSettings.MaxConcurrentUnits` exactly?
- Should duplicate logical query ids across sections remain supported fan-out behavior?
- Where does the SDK schema for payload-side fields (IP, hostname, result type) live so the matcher can apply structured filtering?
