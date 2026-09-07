# Task: I11 — DefensiveToolkit Contract-Clarity + RetryPolicy SRP Cleanup

Behavior-preserving refactor; the deferred lowest-priority item from the committed
DefensiveToolkit hardening effort (HEAD bf55d6d). Three sub-items:

1. **Document the policy/runner interface boundary** (decision resolved: keep both, docs only).
   `IResiliencePolicy` = a single resilience concern; `IDefensivePolicyRunner` = a composition of
   policies. Add XML docs distinguishing them. No code/API change.

2. **Extract the server-suggested-delay externalization gate out of `RetryPolicy`** (main work).
   Consolidate the smeared resolve/decide/throw logic (invoked from 3+ sites, re-resolves headers up
   to 3× per response) into a single collaborator invoked once per response, shrinking RetryPolicy.
   Build on the existing `ServerSuggestedDelayExternalizer` (the throw mechanism). Behavior-preserving.

3. **(Only if cheap)** Acknowledge/doc the `IResiliencePolicy.ExecuteAsync<T>` HTTP-first
   result-handling contract (CircuitBreaker only classifies `HttpResponseMessage`). No generics rework.

See `prompt_contract.md`, `decisions.md`, `constraints.md`.
