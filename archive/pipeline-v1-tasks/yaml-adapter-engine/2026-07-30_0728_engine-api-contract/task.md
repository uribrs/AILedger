# Task — Engine API Contract

Formerly "task 3", the last of the three quality tasks mapped onto this repo's `CLAUDE.md`. Task 1 was
rules 5/1, task 2 was rules 3/4/7 (SRP decomposition, merged as PR #6). This one is the **Public
surface** section.

## What

Establish what `Cymulate.Integration.Yaml.Engine` commits to as a published package, and make the code
say it. `CLAUDE.md` already states the intent — "the engine contract, the definition model, the sink
abstraction and the result types" — and names the hazard: *every `public` type is a support commitment
and a SemVer hazard once the package ships.* It has shipped. 103 types are public.

## Three distinct operations — do not blur them

| operation | scope | breaking |
|---|---|---|
| **Narrow visibility** — `public` → `internal` | the bulk | **No.** Nothing removed, renamed or behaviourally changed. This *reveals* the surface that was already true. |
| **Decompose** | `IExecutionSink` → three interfaces; the `ExecuteOperationAsync` overload chain → request/options records; `WorkflowRunner`'s two delegates → `IWorkflowHost` | **Yes** — real shape changes for the consumer |
| **Add** | the OAuth `static_value`/`credential_key` + `TrimEnd()` forward-port | No, and not surface work — behaviour |

**Nothing is removed.** No type is deleted.

## The insight that shapes the whole task

The engine is **composed from YAML at runtime**, so static C# reference counting measures entry points,
not the surface. Measured: every authenticator and every paginator is constructed at *exactly one site*
— its own factory — selected by `authentication.type` / `pagination.strategy`.

Those types must be **registered**, not **public**. Their contract is the YAML key. A consumer holding
`ApiKeyAuthenticator` as a C# type is doing something the design does not intend.

The right proof that composition stays wired is therefore a **conformance test over the 279-definition
corpus** — every `authentication.type` and `pagination.strategy` appearing in a real definition
resolves to a registered implementation — not public visibility. That check does not exist today.

## Deliverables

1. The measured required-public set (entry points + traversed members + their closure).
2. Everything outside it narrowed to `internal`.
3. The three decompositions.
4. The OAuth forward-port.
5. An explicit `InternalsVisibleTo` decision.
6. Two guards: corpus conformance, and a public-surface pin so the surface cannot silently regrow.

## Out of scope

The adapters repo. The `assets`/`findings` topic parameterization. Anything in
`/Users/user/Dev/IntegrationInfra` (the Shared/SDK retirement — FYI only, not yet actionable).
