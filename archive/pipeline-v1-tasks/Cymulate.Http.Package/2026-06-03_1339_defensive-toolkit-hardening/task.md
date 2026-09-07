# Task: DefensiveToolkit Ownership / Contract / Race Hardening

Implement a prioritized, multi-reviewer-agreed (Claude + Codex, Codex leading) fix set for the
DefensiveToolkit and the thin session/transport seams it touches in `Cymulate.Http.Package`.

Scope: the defense layer's ownership, contract, race, and quality defects + required tests + required docs.
All code anchors were verified against current source.

## Fix items (priority order)

1. **[Critical] Response-ownership contract** — no live `HttpResponseMessage` may cross the exception boundary.
2. **[Bug] TimeoutRouterPolicy telemetry span** closes before work runs (sync method + `using` activity).
3. **[Race/contract] Freeze/clone defensive options** at construction (retry/CB/timeout held by reference today).
4. **[Contract] Package-owned local-saturation exception** — semantics = retryable backpressure.
5. **[Validation] Range-validate defensive options** (fail fast at construction).
6. **[Bug] Token-bucket fractional refill** — preserve fractional rate (no 0-rate stall).
7. **[Bug] RateLimitHeaderReader blank-alias fallback** — continue to next candidate name on blank value.
8. **[Telemetry race] CircuitBreaker `Activity.Current`** — capture the policy's own activity explicitly.
9. **[Cleanup] Delete/define** empty `IrequestBoundRateLimiter.cs`.
10. **[Lifecycle] Lightweight dispose-drain** — narrowly scoped (see decisions.md), no ref-count framework.
11. **[Later] Contract clarity** — IResiliencePolicy vs IDefensivePolicyRunner; extract server-delay gate from RetryPolicy (after #1); IResiliencePolicy<T> over-promise (lowest).

Plus: required tests (defense lifecycle + edge cases; root-cause the intermittent logger test) and
required docs (contractual-principles doc; policy-ordering rationale doc — order is FINAL).

See `prompt_contract.md` for the execution contract, `decisions.md` for the three resolved design
decisions, and `constraints.md` for hard boundaries.
