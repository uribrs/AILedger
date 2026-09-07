# Prompt Contract

## Role
You are a senior .NET engineer doing a behavior-preserving refactor of resilience-policy code (Polly v8 retry pipeline, HTTP transport).

## Goal
Complete I11: (1) document the `IResiliencePolicy` vs `IDefensivePolicyRunner` boundary, (2) extract and consolidate the server-suggested-delay externalization gate out of `RetryPolicy` into a single collaborator invoked once per response, and (3) optionally doc the HTTP-first generic contract — all without changing runtime behavior or the policy order.

## Context
- Behavior-preserving cleanup; the prior hardening work is committed (HEAD bf55d6d). Decision on sub-item 1 is resolved (decisions.md): DOCUMENT, keep both interfaces.
- Files: `DefensiveToolkit/Contracts/Interfaces/IResiliencePolicy.cs`, `IDefensivePolicyRunner.cs`, `DefensiveToolkit/Policies/RetryPolicy.cs`, `DefensiveToolkit/Policies/ServerSuggestedDelayExternalizer.cs` (existing throw mechanism — build on it).
- `ServerSuggestedDelayExternalizer` already disposes the response before throwing (prior task item 1) — do not regress that.

## Constraints
- See constraints.md (all binding). Behavior-preserving; no policy-order change; docs-only for sub-item 1; no new deps; no version bump; no commit; suite stays 268/268.

## Success Criteria
- **Sub-item 1:** `IResiliencePolicy` and `IDefensivePolicyRunner` each carry XML docs stating the distinction (policy = single concern; runner = composition, not itself a single policy). No signature/code/API change.
- **Sub-item 2:** The server-suggested-delay resolution + externalization gate is consolidated into a single collaborator; `RetryPolicy` invokes it from one logical place; the rate-limit headers are resolved at most once per response (no 3× re-resolution); `RetryPolicy` is meaningfully smaller/clearer. Retry-decision precedence (classifier vs header vs custom delay, and the throw-to-externalize behavior) is exactly preserved.
- **Sub-item 3:** Either a small XML-doc note on `IResiliencePolicy` clarifying the HTTP-first result-handling contract, or an explicit "acknowledged, not changed" record in execution_notes.md. No generics rework.
- Build green; `dotnet test Cymulate.Http.Package.sln` = 268/268 (or more if a refactor-seam test is added). No behavior change observable in tests.
- Directory.Build.props still 1.6.3; no commit.

## Execution Rules
- Do not assume missing data; the sub-item 1 decision is in decisions.md.
- Preserve externalization precedence exactly — verify the OPEN assumption (invocation sites + which decision wins) before collapsing resolution to one point.
- Respect constraints strictly; behavior preservation is the bar.
- If consolidation would require any behavior change to stay correct, STOP and surface it rather than changing behavior silently.

## Output Format
- In-repo code/doc changes matching existing idioms.
- execution_notes.md appended with: what was extracted, the new collaborator's shape, how precedence/behavior was preserved, sub-item 3 disposition, and verification results (build + test counts).

## Stop Conditions
- All success criteria met (build + 268 tests green, no behavior change, no version bump, no commit).
- A behavior-preserving consolidation is not achievable without changing semantics (stop, surface).
- Externalization precedence cannot be preserved under consolidation (stop, surface).
