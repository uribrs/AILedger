# Constraints

- Behavior-PRESERVING refactor: no change to runtime semantics of retry, externalization, rate-limiter, timeout, or circuit-breaker.
- Do NOT change the policy execution order (Retry → RateLimiter → Timeout → CircuitBreaker → operation). It is FINAL.
- Sub-item 1 is docs-only — no interface/code/API change (decision: document, keep both).
- Do NOT collapse or merge `IResiliencePolicy` / `IDefensivePolicyRunner`.
- No new external dependencies. Match existing idioms (DefensiveDiagnostics, ILogger, internal/sealed conventions).
- Build green; full suite stays at 268/268 (`dotnet test Cymulate.Http.Package.sln`). Existing retry/externalization tests must pass unchanged.
- Add a new test only if the extracted gate creates a clean seam worth covering; the bar is behavior preservation, not new coverage.
- Do NOT bump the version (Directory.Build.props stays 1.6.3).
- Do NOT commit — the user bumps version + merges after local testing.
- No new CHANGELOG entry expected (docs-only + behavior-preserving internal refactor). Only add one if public surface unexpectedly changes.
- Scope is strictly I11 sub-items 1–3. Do not expand.
