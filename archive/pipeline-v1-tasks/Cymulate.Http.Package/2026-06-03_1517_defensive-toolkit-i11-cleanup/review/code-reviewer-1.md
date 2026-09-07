# Code Review — DefensiveToolkit server-suggested-delay extraction

## Scope & Classification

- **Artifacts:** `ServerSuggestedDelayGate.cs` (new), `RetryPolicy.cs` (modified), `IResiliencePolicy.cs` / `IDefensivePolicyRunner.cs` (XML docs only).
- **Change type:** shared library code (HTTP transport resilience), public-ish surface (interface docs).
- **Risk level:** Medium–High. This sits in a retry/rate-limit hot path inside a Polly v8 pipeline, executed on long-running sessions streaming millions of findings. Behavioral or concurrency regressions here are operationally severe (retry storms, connection leaks, mis-honored server backoff). The change itself, however, is a behavior-preserving extraction with no new logic.
- **Review depth applied:** correctness, behavior preservation, concurrency, SRP/cohesion, idioms.

## Verdict

This is a clean, low-risk refactor. The extraction is faithful: every relocated method body is byte-for-byte equivalent to the prior inline implementation, all five call sites map 1:1 to the old paths, no dangling references remain, and the project builds with 0 errors. No blockers, no majors.

## Verification performed

- Mapped all 5 call sites in `RetryPolicy` to their previous equivalents:
  - `ExternalizeFromHeaders` (RetryPolicy:53, 121) ≡ old `ExternalizeResponseDelayBeforeRetryGates` — same `ExternalizeServerSuggestedDelays` guard, then `Resolve` → `ThrowIfConfigured`.
  - `ForClassifierDelay` + `Externalize` (RetryPolicy:233–234) ≡ old `CreateClassifierDelayResolution` + `ThrowIfConfigured`.
  - `Resolve` + `Externalize` (RetryPolicy:241–243, 258–260) ≡ old `ResolveResponseRetryDelay` + `ThrowIfConfigured`.
  - `Gate.Resolve` body ≡ old `ResolveResponseRetryDelay`; `Gate.Clamp` ≡ old `ClampServerSuggestedDelay`.
- Confirmed invocation **order** is unchanged in `ClassifyResultAsync` (classifier-delay branch still externalizes before the header-resolve branch; same precedence).
- `grep` confirms no remaining references to the removed members/fields (`ResolveResponseRetryDelay`, `CreateClassifierDelayResolution`, `ClampServerSuggestedDelay`, `_retryDelayResolver`, `_serverSuggestedDelayExternalizer`).
- `RetryDelayResolution` and `RetryTelemetryInfo` live in the same `DefensiveToolkit.Policies` namespace, so no new `using` was required and none is missing.
- No external consumers (tests or otherwise) referenced the gate, the externalizer, or the removed private methods — the extraction surface is fully internal.
- `dotnet build DefensiveToolkit` → Build succeeded, 0 errors (only unrelated NU1900 feed warnings).

## Concurrency

A single `RetryPolicy` (and thus a single `ServerSuggestedDelayGate`) instance is shared across all requests of a long-running session and invoked concurrently from the Polly pipeline. This is safe:

- The gate's fields (`_options`, `_resolver`, `_externalizer`) are `readonly` and assigned once in the constructor.
- `_options` is a cloned/frozen snapshot (`RetryPolicy` ctor calls `.Clone()` then `.Validate()`); the gate stores the same reference and never mutates it.
- `RateLimitHeaderRetryDelayResolver` and `ServerSuggestedDelayExternalizer` hold only readonly options/logger and use no instance mutable state — all per-call data flows through method arguments and locals.

No shared mutable state is introduced. (Observation only.)

## Findings

### Minor

**M1 — `Externalize` XML doc overstates a guarantee the method does not enforce locally.**
- **Problem:** `ServerSuggestedDelayGate.Externalize` (lines 103–108) is documented as "Externalizes ... when configured (externalization enabled and a positive delay); otherwise a no-op," but the method body unconditionally delegates to `_externalizer.ThrowIfConfigured`. The guards (`ExternalizeServerSuggestedDelays`, `Delay <= TimeSpan.Zero`) live in the externalizer, as the doc's last sentence admits. The doc reads as if the gate enforces them.
- **Impact:** Documentation clarity only; runtime behavior is correct and identical to the prior code (which also called `ThrowIfConfigured` directly without a local guard).
- **Recommended fix:** Trim the doc to describe delegation, e.g. "Delegates to the externalizer, which throws only when externalization is enabled and the delay is positive." Local patch.
- **Refactor or patch:** Patch.

**M2 — Redundant `ExternalizeServerSuggestedDelays` guard across two layers (pre-existing, carried forward).**
- **Problem:** `ExternalizeFromHeaders` checks `_options.ExternalizeServerSuggestedDelays` (line 95) and then calls `ThrowIfConfigured`, which checks the same flag again (Externalizer line 25). The double-guard predates this change.
- **Impact:** None functionally. The outer guard does buy one thing: it short-circuits the `Resolve(response)` header parse (lines 98) when externalization is off, avoiding wasted work on the pre-gate path. So it is not pure redundancy — worth keeping. Noting only so it isn't "simplified" away later by removing the outer guard, which would add a header-parse cost on every response when externalization is disabled.
- **Recommended fix:** None required. If desired, add a one-line comment that the outer guard exists to skip the resolve, not for correctness.
- **Refactor or patch:** None / optional comment.

### Observations (no action)

**O1 — Naming / SRP.** The split between `ServerSuggestedDelayGate` (resolve + clamp + orchestrate) and `ServerSuggestedDelayExternalizer` (guard + telemetry + dispose + throw) is coherent: the gate owns "what delay, how big," the externalizer owns "surface it / leave it." `RetryPolicy` shrinks by ~75 lines and is now focused on pipeline construction and classification. The `IResiliencePolicy` vs `IDefensivePolicyRunner` doc additions correctly justify why the two share an `ExecuteAsync<T>` shape without being collapsible. This is a net cohesion improvement.

**O2 — `internal sealed` is the right visibility.** The gate is an implementation collaborator of `RetryPolicy`, not part of the package's public contract; `internal sealed` matches the sibling `ServerSuggestedDelayExternalizer` and prevents accidental external coupling.

**O3 — Response disposal semantics unchanged.** The connection-leak-prevention invariant (dispose-before-throw in `ThrowIfConfigured`) is untouched by this change and still the single source of that behavior.

## Summary

Behavior-preserving extraction, verified at the call-site, reference, namespace, and build levels. Concurrency-safe for the shared-instance / long-session usage pattern. No blockers or majors. Two minor documentation nits (M1 most worth fixing); the rest are positive observations.
