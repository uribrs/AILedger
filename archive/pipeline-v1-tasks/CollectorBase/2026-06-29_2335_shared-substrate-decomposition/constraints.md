# Constraints

## Established facts (verified against source this session — do not re-derive)
- Floor = `Session/TransportErrorHandling`, namespace `Cymulate.Integration.Adapters.Shared.Session.TransportErrorHandling`. Contents: `HttpTransportFailureClassifier` (`IsRetryableTransportFailure`/`IsCircuitBreakerException`), `AdapterHttpRequestFailedException`, `UnknownFlowRetryPolicy`, `IHttpFailureClassifier`.
- Floor imports nothing internal — verified leaf.
- `Session` (root) imports the floor; never imports `Resilience` — verified.
- `Resilience` imports the floor only (via `...Session.TransportErrorHandling`); never imports `Session` root — verified.
- Runtime order is fixed: Session within-request retry → throws floor's `AdapterHttpRequestFailedException` → `Resilience.AdapterResilienceStrategy.DecideAsync` fixed-order chain → UnknownFlow/Fallback bottom out in floor's `UnknownFlowRetryPolicy`.
- Current solution: CollectorExecutor + Runner + Strategies + Shared + Tests. No Kernel project exists.

## Hard constraints
- Pure structural carve — **zero behavior change**. No logic edits beyond namespace/reference rewiring.
- Solution must build and the full test suite must stay green at each committed step.
- Floor must retain **zero internal dependencies** after the move (still a leaf).
- After the move, `Session` and `Resilience` each depend only on **Kernel** — never on each other.
- Resilience policy order stays fixed (`Retry → RateLimiter → Timeout → CircuitBreaker`) — not touched by this task, but must not regress.
- Do not reinvent transport/egress/resilience (CLAUDE.md substrate rule) — this is relocation, not rewrite.
- Secret handling, auth flows, egress behavior untouched.

## Process constraints
- Move floor first (unblocks both peers), then peers, then reclaim Orchestration types.
- Each phase independently buildable + green before the next.
