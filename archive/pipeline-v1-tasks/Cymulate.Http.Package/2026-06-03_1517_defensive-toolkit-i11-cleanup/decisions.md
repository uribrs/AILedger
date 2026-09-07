# Decisions

- **Interface boundary = DOCUMENT, keep both** (user, 2026-06-03). Add XML docs: `IResiliencePolicy` is one resilience concern; `IDefensivePolicyRunner` is a composition/orchestration of policies and is NOT itself a single policy. No collapse, no merge, no code/API change. (Lowest risk before merge; rejected collapse-extends and full-merge alternatives.)
- **RetryPolicy SRP**: consolidate server-delay resolution + externalization-gate into ONE collaborator that resolves once per response and is invoked from a single place; build on the existing `ServerSuggestedDelayExternalizer` rather than duplicating it.
- **Behavior-preserving only**: refactor must not alter any runtime semantics or the policy order.
- **No version bump, no commit**: user handles version (→2.0.0) and merge after local testing.
- **Sub-item 3 (generic over-promise)**: doc-note only if cheap; otherwise record as acknowledged/not-changed. No generics rework.
