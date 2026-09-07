# Task: Kernel — transport-fault vocabulary carry + UnknownFlowRetryPolicy split

Relocate the transport-fault **vocabulary** from the in-production `Shared` library into
`IntegrationInfra/Kernel`, and split `UnknownFlowRetryPolicy` along its dependency seam so the Kernel
stays Polly-free.

Behavior-preserving relocation (namespace-only rewrite, logic intact) plus a sanctioned
separation-of-concerns split. The approach is an operator-fixed decision.

## In scope (carry into IntegrationInfra/Kernel)
- `AdapterHttpRequestFailedException` → `Kernel/Exceptions/`
- `HttpTransportFailureClassifier` → `Kernel/Transport/`
- `IHttpFailureClassifier` → `Kernel/Transport/`
- The **Polly-free residue** of `UnknownFlowRetryPolicy` (`MaxRetries`, `MaxAttempts`, `RetryDelays`,
  the static-ctor length invariant, `IsUnknownRetryCandidate`) → `Kernel/Transport/`

## Out of scope
- `UnknownFlowRetryPolicy.CreatePipeline` (the only Polly-dependent member) — NOT carried. Stays in the
  source repo until its concern (Conversation / FaultGovernance / Conducting) is relocated.
- Any mutation of the source repo (`/Users/user/Dev/Uri/localprojects/IntegrationsInfra`) — reference only.
- Assigning a domain owner to pipeline construction.

## Deliverables
1. Vocabulary relocated, behavior verbatim, Kernel Polly-free.
2. Excellent XML docs on every public member.
3. `Kernel/Transport/README.md` + updated `Kernel/README.md` "Holds:" line.
4. Test project established (none exists) + unit tests for classifier predicates, the unknown-flow
   predicate, and the exception shape.
5. `dotnet build` clean; tests pass.
