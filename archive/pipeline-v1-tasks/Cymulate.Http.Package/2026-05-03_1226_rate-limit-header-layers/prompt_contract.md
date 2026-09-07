Role:
You are a senior .NET backend engineer working in Cymulate.Http.Package.

Goal:
Analyze the current rate-limit and retry mechanism from the codebase, then implement a layered, documented rate-limit header mechanism that supports known industry-used headers and routes retry-delay selection out of embedded `RetryPolicy` logic.

Context:
The package currently has configured client-side rate limiting through `RateLimiterPolicy` and response-aware retry handling through `RetryPolicy`. Existing behavior supports `Retry-After` and custom retry delay hooks. Research identified wider header families including `RateLimit`, `RateLimit-Policy`, `RateLimit-*`, `X-RateLimit-*`, `X-Rate-Limit-*`, Discord reset-after, Shopify call-limit, Zendesk structured headers, Amazon `x-amzn-RateLimit-Limit`, Azure `x-ms-ratelimit-*`, Stripe reason headers, and Salesforce `Sforce-Limit-Info`.

Constraints:

* Preserve existing `Retry-After` behavior.
* Keep client-side `RateLimiterPolicy` separate from server response header parsing.
* Add a new enum for known industry-used rate-limiting headers or header families.
* Add a clear strategy selection space for how retry delay is selected from server headers and fallback behavior.
* Move header parsing and delay selection into documented classes outside embedded `RetryPolicy` logic.
* Treat ambiguous quota metadata as metadata unless a safe delay can be derived.
* Follow existing .NET conventions: small focused methods, no forced abstraction, clear names.
* Do not modify unrelated dirty files.
* Add focused unit tests for header-driven retry delay behavior.

Success Criteria:

* Current rate-limit mechanism is analyzed and reflected in code structure decisions.
* `RetryPolicy` delegates server retry-delay resolution to a dedicated component.
* New enum covers known industry-used header families from research, including existing `Retry-After`.
* Strategy/model classes document layers: explicit classifier delay, server header delay, custom delay hook, fallback retry backoff/jitter.
* Tests cover existing `Retry-After` behavior plus multiple non-`Retry-After` real-world header families.
* Existing retry tests continue to pass.
* Task state files are updated with decisions, validation, and residual risks.

Execution Rules:

* Do not assume missing data.
* Respect constraints strictly.
* Use `rg` for repository search.
* Use `apply_patch` for manual file edits.
* Run focused tests after implementation.
* Run an independent verifier before final response.

Output Format:
Final response must include changed files, validation result, and remaining risks or blockers.

Stop Conditions:

* When goal is achieved.
* When required data is missing.
* When task state conflicts with contract.
* When sandbox or approval restrictions block required execution.
