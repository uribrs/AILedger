# Execution Notes

Changed:
- Replaced broad `KnownRateLimitHeader` with focused `RateLimitDelaySource`.
- Added `RateLimitHeaderCatalog` and `RateLimitHeaderDescriptor` for broader known header identification, including older and metadata-only headers.
- Added `RateLimitHeaderStrategy` and `RateLimitHeaderDelay`.
- Added `RateLimitHeaderRetryDelayResolver` to parse safe retry-delay hints from server rate-limit headers.
- Updated `RetryOptions` with `RateLimitHeaderStrategy`.
- Updated `RetryPolicy` so classifier delay stays authoritative, server header delay is delegated, custom delay remains fallback, and Polly backoff/jitter remains final fallback.
- Split `RateLimitHeaderRetryDelayResolver` helpers into header names, header reading, value parsing, and delay safety rules.
- Clarified `GetRetryDelayFromResponse` docs so vendor body parsing belongs in `ClassifyResponseAsync`.
- Added resolver tests and one retry-policy integration test.
- Added dedicated rate-limit header handling documentation with decision diagram, strategy table, header family table, consumer examples, package developer guide, and pseudocode.
- Updated DefensiveToolkit, policies, session, and examples docs to link or summarize the new rate-limit header behavior.
- Rechecked documentation/examples after the timeout discussion and updated copy-paste retry examples to use a timeout budget above long server-directed delay caps.
- Documented the current wrapping caveat in the DefensiveToolkit and Session docs: header-derived retry waits happen inside `TimeoutOptions.Timeout` and while the rate-limiter permit is held.
- Added a high-level rate-limiting behavior and strategy decision tree to `DefensiveToolkit.RateLimitHeadersReadme.md`.
- Added retry/rate-limit telemetry tags for delay source, header family, concrete header, strategy, effective delay, original delay, clamp state, status code, and exception type.
- Added robust structured retry logs with the same retry delay context.
- Moved retry telemetry shaping into focused helper classes so `RetryPolicy` remains orchestration-focused.
- Updated telemetry documentation so header-driven retries can be searched by activity tags.

Validated:
- `dotnet test UnitTests/UnitTests.csproj --no-restore --filter FullyQualifiedName~DefensiveToolkit`
- `dotnet test UnitTests/UnitTests.csproj --no-restore`
- `dotnet test UnitTests/UnitTests.csproj --no-restore` after the final documentation sweep: 150 passed.
- `dotnet test UnitTests/UnitTests.csproj --no-restore` before commit: 150 passed.
- `dotnet test UnitTests/UnitTests.csproj --no-restore` after telemetry/logging update: 152 passed.

Residual risks:
- Long server-provided retry delays still occur inside the current policy nesting, so they hold the rate limiter permit and consume timeout budget.
- Metadata-only headers are identified in `RateLimitHeaderCatalog` but are not emitted as telemetry unless they produce a retry decision.
