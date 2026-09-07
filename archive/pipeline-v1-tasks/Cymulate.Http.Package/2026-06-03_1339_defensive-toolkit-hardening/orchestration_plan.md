# Orchestration Plan

## Complexity Decision
- Path: **decompose**
- Rationale: 11 fix items + tests + docs across the defense layer. Partitioned by **exclusive file ownership** so workers fan out in parallel without clobbering shared files. The genuinely-coupled core (RetryPolicy.cs + RateLimiterPolicy.cs + exception/record + transport catch sites) is given to a single worker so its interdependent edits stay coherent — parallelizing edits to those shared files would be merge-hell, not speed.

## Research Decisions
- None needed. All OPEN assumptions were code-grounded (internal usages), resolved by grep, not external-system behavior. `researchNeeded = false`.

## Worker Plan (exclusive file ownership; siblings must not edit each other's files)
- **W1 — Telemetry fixes.** I2 + I8 (+ CB ctor calls options.Validate()). Owns: `Wrappers/TimeoutRouterPolicy.cs`, `Policies/CircuitBreakerPolicy.cs`. Deps: none.
- **W2 — Header + cleanup.** I7 + I9. Owns: `RateLimiting/RateLimitHeaderReader.cs`; deletes `Contracts/Interfaces/IrequestBoundRateLimiter.cs`. Deps: none.
- **W4 — Dispose-drain.** I10 (narrow scope per decisions.md). Owns: `Logic/Lifecycle/HttpSession.cs`, `Logic/Lifecycle/SessionManager.cs` (only if needed), a NEW drain/stream-wrapper file. Also root-causes the intermittent logger test. MUST NOT edit the transport service bodies (W7 owns them) — wrap at the HttpSession boundary. Deps: none.
- **W5 — Docs.** D1. New files only: contractual-principles doc + policy-ordering rationale doc. Deps: none.
- **W6 — Options freeze/clone + options-class validation.** I3 + I5(options classes). Owns: `Contracts/Options/*` (Retry/CircuitBreaker/Timeout/RateLimiter + RateLimiterKindOptions/*), `Logic/Wiring/SessionBuilder.cs`. Implements `Clone()`/freeze + range validation in each `Validate()`. MUST NOT edit policy ctors (W1/W7 own those). Deps: none.
- **W7 — Core ownership contract + limiter.** I1 + I4 + I6 + I5(RetryPolicy/RateLimiterPolicy ctor guards). Owns: `Policies/RetryPolicy.cs`, `Policies/RateLimiterPolicy.cs`, `Policies/ServerSuggestedDelayExternalizer.cs`, `Contracts/Models/Retry/ServerSuggestedRetryDelay.cs`, `Contracts/Exceptions/ServerSuggestedRetryDelayException.cs`, NEW package saturation exception, `Logic/Transport/TransportService.cs` + `TrueStreamingTransportService.cs` (catch sites only), `Policies/RetryPolicyTelemetry.cs` + `Telemetry/SessionServerSuggestedDelayTelemetry.cs` (only if they read Response), and the 2 affected tests. Deps: none for files (disjoint), but defines the saturation exception type other code references.

I11 (collapse/clarify IResiliencePolicy vs IDefensivePolicyRunner; extract server-delay gate from RetryPolicy; IResiliencePolicy<T>) is LOWEST priority and depends on I1 — deferred to a follow-up pass after W7 lands; may be recorded as deferred if budget-constrained.

### Cross-worker semantic contracts (not file contention)
- W1's CB ctor calls `CircuitBreakerOptions.Validate()` which **W6** implements (parameterless). 
- W7 defines the new saturation exception type; the 2 tests it updates are W7-owned.
- Tests: each worker writes unit tests for its own slice (tests live in `UnitTests/...` — disjoint test files per worker to avoid contention).

### Build discipline
- Workers DO NOT run `dotnet build`/`dotnet test` on the solution (avoids parallel obj/bin races). The orchestrator builds + runs the full suite centrally after the wave and dispatches targeted repairs.

## Synthesis Approach
After all workers return, orchestrator: (1) `dotnet build` the solution, (2) dispatch targeted repair agents for any compile errors (by file owner), (3) `dotnet test Cymulate.Http.Package.sln`, (4) repair failures, (5) confirm contract success criteria coverage before verifier.

## Verification Obligations
- Cross-check every Success Criterion in prompt_contract.md.
- Item-1: prove no response reachable through exception/record; both no longer IDisposable; leak test green.
- Item-4: saturation retried not failed; 2 affected tests updated.
- Item-6: 0.5/s → ~1 token/2s; no 0-rate stall.
- Build + full suite green; intermittent logger test root-caused.
- Policy order unchanged; injected HttpClient never disposed; no new deps.
