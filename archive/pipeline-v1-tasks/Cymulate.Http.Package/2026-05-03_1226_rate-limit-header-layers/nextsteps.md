# Next Steps

## Future Task: Retry Attempt Lanes

Goal:
- Split retry orchestration from per-attempt execution so server-directed retry waits do not consume per-attempt timeout budget and do not hold rate-limiter permits.

Current behavior:
- Policy nesting is effectively `RateLimiter -> Timeout -> Retry -> CircuitBreaker -> transport`.
- Server header delays happen inside `RetryPolicy`.
- A long `Retry-After` or rate-limit reset delay can consume timeout budget.
- The rate-limiter permit can be held while waiting between retry attempts.

Recommended target semantics:
- Keep the same defensive wrapping order for each actual outbound attempt.
- Treat the original request as a replay blueprint, not as an object to resend.
- Create separate initial/retry attempt lanes that each pass through the same defensive attempt runner.
- Rate limiting should apply to actual HTTP attempts, not to the sleeping gap between attempts.
- Per-attempt timeout should cap active HTTP work only.
- Server-directed retry delay should sit between attempt lanes and outside per-attempt timeout.
- A separate total/logical operation timeout should be considered if callers need an end-to-end cap.

Do not blindly invert the existing wrapper order:
- Moving `RetryPolicy` outside the whole chain forces retry to understand `HttpRequestMessage` cloning, content replay, auth reapplication, and streaming constraints.
- Request replay logic should stay near Session/Transport, where replayability is already understood.
- The broad change is not "new wrapping order"; it is "same wrapping order per attempt, with retry scheduling between attempts."

Candidate model:
```text
retry orchestration
    attempt 1 lane:
        RateLimiter -> Timeout -> CircuitBreaker -> transport
    wait from header/backoff outside attempt lane
    attempt 2 lane:
        RateLimiter -> Timeout -> CircuitBreaker -> transport
    ...
```

Key decision:
- Decide whether `RateLimiterPolicy` is meant to limit logical user operations or actual outbound HTTP attempts.
- Recommendation: limit actual outbound HTTP attempts, because every retry is another request against the vendor API.
- Decide which component owns the request blueprint/replay contract.
- Recommendation: Session/Transport should own request replay; retry should own retry decision and delay scheduling.

Implementation sketch:
```text
attempt = 1
while attempt <= maxAttempts:
    attemptRequest = requestReplay.CreateAttempt()
    response = attemptRunner.Execute(attemptRequest)

    decision = retryDecision.Resolve(response)
    if decision says stop:
        return response

    wait outside attemptRunner
    attempt++
```

Attempt runner keeps the existing wrapping pattern:
```text
RateLimiter -> Timeout -> CircuitBreaker -> transport
```

Required tests:
- `Retry-After` longer than per-attempt timeout still waits and retries successfully when total timeout allows it.
- Rate-limiter permit is not held during server-directed retry delay.
- Each retry attempt consumes a rate-limiter permit.
- Optional total operation timeout, if added, caps the full retry workflow.
- Non-replayable request content still prevents retry.
- Request content replay creates a fresh safe attempt request where possible.
- Non-seekable streaming requests do not enter retry lanes.
- Authentication/request mutation is applied correctly per attempt.
- Circuit breaker behavior remains consistent with actual transport attempts.

Risks:
- This is a policy contract change, not a small rate-limit header tweak.
- Existing consumers may rely on timeout covering the full retry lifecycle.
- Existing consumers may expect one rate-limiter permit per logical operation.
- Request replay is easy to get wrong for streamed content and mutable headers.
- Streaming and non-replayable request protections must be preserved.

Design blind spots to resolve:
- Retry orchestration may become session/transport-specific instead of policy-generic. That weakens the current `IResiliencePolicy` abstraction unless the split is designed carefully.
- Attempt lanes need a clear ownership boundary. If Session owns replay and Retry owns decisions, the handoff contract must define who disposes failed responses, cloned requests, and content streams.
- Authentication may mutate requests. The future design must decide whether authentication runs once on the blueprint or once per attempt. Recommendation: authenticate per attempt unless the auth strategy is explicitly immutable.
- Headers that are safe to copy are not always obvious. Hop-by-hop headers, generated auth headers, content headers, and request options may need different replay rules.
- Request options currently carry retry controls and custom rate limiters. The replay contract must preserve options that are attempt-invariant and avoid copying attempt-local state.
- Per-attempt timeout plus optional total timeout creates two cancellation paths. Logs, exceptions, and telemetry must make clear which timeout fired.
- Circuit breaker semantics may change if it moves from seeing one logical operation to seeing every attempt. This is probably correct for HTTP transport, but it is still a contract change.
- Metrics may become harder to read unless logical-operation telemetry and per-attempt telemetry are separated.
- Retry lanes can amplify traffic if many requests receive the same reset time. Existing jitter/backoff behavior should be reviewed for server-directed delays.
- Request replay may require buffering content that was previously streamed. That can create memory pressure if the fallback is too eager.
- Some transports may not support replay uniformly. True streaming should probably remain no-retry unless the stream is seekable and reset is verified.
- If the lane runner is introduced only for Session, direct users of DefensiveToolkit policies may still get the old semantics. The future task must define whether that is acceptable or documented.
- A total/logical timeout option is easy to add badly. If added, it should be explicit and not silently replace the meaning of the existing timeout.
