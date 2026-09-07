# Current Status After Plan Builder

## Status

This task is superseded before execution.

Do not implement Query Dataflow topology from this folder's original `prompt_contract.md`.

The replacement completed task is:

```text
ai/active/2026-05-12_1728_query-plan-builder-slice/
```

## What Changed Since This Topology Task Was Drafted

SDK/PRD review showed that Dataflow topology was too early. The Query pipeline needed a real planning stage first.

The completed replacement task implemented:

- SDK `IPlanBuilder`
- `QueryPlanBuilder`
- draft `ExecutionPlan` creation from `QueryJobV2`
- validation for job, section, query, targeting, limits, and planned unit count
- exact-result-type `WindowsByResultType` splitting
- UTC time-window canonicalization
- canonical private IPv4 targeting
- deterministic draft unit ids
- focused unit tests

Validation from the completed replacement task:

- Shared project build passed with 0 warnings and 0 errors.
- Query shared tests passed 41 tests.
- Final verifier reported no issues.
- Final code-review pass reported no material implementation-quality issues.

## Current Recommended Sequence

1. Implement `IPlanOptimizer`.
2. Implement `INativeCoalescer`.
3. Reassess Dataflow topology once those stages have concrete implementations and tests.
4. Then proceed to dispatcher, distributor, matcher, section tracker, publication composition, and recovery behavior in separate bounded slices.

## Why Topology Should Wait

Topology code should compose real stage implementations and well-defined stage contracts.

At the time this task was drafted, these stage behaviors were not implemented:

- plan builder
- plan optimizer
- native coalescer
- dispatcher
- distributor
- matcher
- section tracker

The plan builder now exists, but optimizer and native coalescer still do not. Starting topology now would either add placeholder delegates as product code or force topology to absorb policy decisions that belong to planning stages. Both would make the system harder to reason about. Very heroic, very avoidable.

## Future Topology Constraints To Preserve

- Keep Shared concern-first.
- Put topology under the owning Shared concern, likely `Orchestration/Query` or a nested `Orchestration/Query/Dataflow` folder if it grows.
- Use SDK contracts directly.
- Use bounded TPL Dataflow blocks.
- Use explicit cancellation.
- Use explicit bounded capacity.
- Use explicit parallelism.
- Use explicit ordering.
- Link blocks with completion propagation.
- Keep publication through `QuerySectionPublisher` and existing `IAdapterExecutionContext.PublishAsync`.
- Do not implement RabbitMQ, Postgres, outbox, storage upload, host, session, HTTP retry, or plan-store infrastructure in adapters Shared.
- Do not add real or fake Query clients as part of topology.
- Do not decide composite `ResultType`, `ExpressionDialect`, or expanded `QueryTargeting` policy in topology.

## Files To Read Before Future Topology Work

- `ai/active/2026-05-12_1728_query-plan-builder-slice/state.json`
- `ai/active/2026-05-12_1728_query-plan-builder-slice/phase_documentation.md`
- `ai/active/2026-05-12_1728_query-plan-builder-slice/next_step_usage_examples.md`
- `ai/active/2026-05-12_1728_query-plan-builder-slice/next_agent_handoff.md`
- `/Users/user/Dev/Uri/Planning/2026-05-11_1326_crowdstrike-domain-doc-validation/QUERY_INTEGRATION_SHARED_DATAFLOW_BLUEPRINT_PRD.md`
- `/Users/user/Dev/Uri/Planning/2026-05-11_1326_crowdstrike-domain-doc-validation/QUERY_INTEGRATION_SDK_CONTRACT_DESCRIPTOR.md`

## If A Future Agent Reopens This Task

Do not mark it active again unless the user explicitly asks to revive topology work.

Create a fresh task contract for topology using the latest completed planning-stage state. This folder should remain a historical record and a warning label, not the steering wheel.
