# Can an HTTP 401 terminate a Falcon collector run?

Read-only source audit. Every claim below carries a `path:line`. Repos read:

- `/Users/user/Dev/Cymulate.Http.Package` (session transport, DefensiveToolkit)
- `/Users/user/Dev/IntegrationInfra` (AdapterHttpClient, FaultGovernance, Conducting)
- `/Users/user/Dev/cymulate-integration-adapters/.../Collectors/FalconCollector`

---

## Verdict

**A vendor API 401 cannot terminate a run in the invocation it occurs in.** The first
401-derived failure in any invocation that has made forward progress is always a
`PartialResult` deferral, never a published failure. Termination on a 401 requires
**four consecutive deferrals at an identical progress coordinate**, i.e. four ISB resume
hops that each collected literally nothing.

**One genuine terminal 401 path exists, and it is not the API 401:** a 401 from the
**OAuth2 token endpoint** (`POST /oauth2/token`) surfaces as `InvalidOperationException`,
misses every Falcon classifier arm, and dies terminally after the 30/60/120 s unknown-retry
pipeline. Details in §6.

---

## 1. The replay mechanism

**Max = 2, per request, from a constant (not config).**

`Session/Logic/Transport/TrueStreamingTransportService.cs:295-352` — the reauth loop lives
*inside* the Polly execution:

```
request.Options.Set(RequestContextKeys.ReauthorizationRetryCount, 0);   // :309, once per logical request
var response = await Policies.ExecuteAsync(async token => {
    var reauthorizationRetries = TransportHelpers.GetReauthorizationRetryCount(request);  // :324
    while (true) {
        var currentResponse = await SendStreamingRequestCoreAsync(request, token);
        if (!ShouldReauthorizeAndReplay(request, retryAllowed, currentResponse, reauthorizationRetries))
            return currentResponse;                                      // :331
        if (!await TryRefreshCredentialsAsync(token))
            return currentResponse;                                      // :334
        if (prepareForReplay is not null && !prepareForReplay())
            return currentResponse;                                      // :337
        currentResponse.Dispose();
        reauthorizationRetries++;
        request.Options.Set(RequestContextKeys.ReauthorizationRetryCount, reauthorizationRetries);  // :342
    }
}, ct);
```

Gate: `Session/Logic/Transport/TransportHelpers.cs:25-43` — `CanReauthorizeAndReplay` requires
(a) `ShouldRefreshOnStatusCode` (401 by default, `:45-50`), (b) `retryAllowed` (request body
replayable), (c) auth is `IRefreshableAuthenticator`, (d) `reauthorizationRetries < max`.

Max resolution: `TransportHelpers.cs:53-59` — `IReauthorizationPolicy.MaxReauthorizationRetries`
if the provider implements it, else the constant `DefaultMaxReauthorizationRetries = 2`
(`TransportHelpers.cs:11`).

Falcon's provider chain: `SessionSpec.Auth = new AuthSelection.OAuth2(...)`
(`Processing/Configuration/FalconCollectorConfigurationBuilder.cs:354`), wrapped by
`CredentialProviderAuthenticator`, whose policy is
`_innerPolicy?.MaxReauthorizationRetries ?? 2`
(`IntegrationInfra/src/IntegrationInfra/Conversation/CredentialProviderAuthenticator.cs:204-205`).
`OAuth2Authentication` does **not** implement `IReauthorizationPolicy` (it is absent from the
implementor set — only `TokenExchangeAuthentication`, `UsernamePasswordTokenExchangeAuthentication`,
`NegotiatedAuthenticationProvider`, `RefreshableAuthenticationProvider` do). **So Falcon's max is
the default 2**, which is exactly the observed `(2/2)`.

**Scope: per logical request.** The counter is reset once per `SendWithTrueStreamingAsync`
call (`:309`) and stored in `request.Options`, so it *persists across Polly retry attempts of
the same request* (`:324` reads it back each attempt) but starts fresh for the next page.
It is **not** per-session and does **not** reset mid-request.

Identical logic on the non-streaming path: `Session/Logic/Transport/TransportService.cs:229-270`.

## 2. What happens when replays are exhausted

`TrueStreamingTransportService.cs:330-331` — `ShouldReauthorizeAndReplay` returns false, so
the branch taken is `return currentResponse;`. **The session returns the 401 response. It does
not throw.** Same for a failed credential refresh (`:334`) and a non-resettable body (`:337`).

`StreamResponseAsync` then hands back a `StreamedResponse` with `IsSuccessStatusCode == false`
(`:225-232`), recording outcome `Failure` but not faulting.

## 3. Is a 401 retried by Polly, or counted by the breaker?

**No to both.**

- Retry: `DefensiveToolkit/Contracts/Options/RetryOptions.cs:14-21` — `RetryStatusCodes` =
  {408, 429, 502, 503, 504}. `DefensiveToolkit/Policies/RetryPolicy.cs:250` —
  `RetryStatusCodes.Contains(response.StatusCode)` is false for 401 → `RetryDecision(false)`
  with reason `"status is not retryable"` (`:251-256`). Falcon supplies no
  `ClassifyResponseAsync` (its `SessionSpec` sets only `Retry`, `RateLimiter`,
  `CircuitBreaker` — `FalconCollectorConfigurationBuilder.cs:359-362`), so the default status
  set is authoritative.
- Circuit breaker: `DefensiveToolkit/Policies/CircuitBreakerPolicy.cs:138-149` — handles 429,
  408, any ≥500, plus `FailureStatusCodes` = {408, 429, 502, 503, 504}
  (`CircuitBreakerOptions.cs:19-26`). 401 is in none of them.

So after exhaustion the 401 travels straight back to the caller, unretried and un-counted.

## 4. How a 401 leaves the HTTP layer

`IntegrationInfra/src/IntegrationInfra/Conversation/AdapterHttpClient.cs:127-146` — the legacy
`IHttpSession` path reads a bounded body snippet and calls `ThrowFailure`.

`ThrowFailure` (`:148-194`):
1. Logs `HTTP request failed. StatusCode=401 …` with redaction (`:154-160`).
2. Offers the body to the vendor classifier first (`:162-166`). Falcon's
   `Flows/SharedFlows/FalconHttpFailureClassifier.cs:45-62` claims any CrowdStrike-shaped
   error JSON (`meta.trace_id` / `errors[]`) and returns its **own**
   `AdapterHttpRequestFailedException` with `statusCode: response.StatusCode` — so
   `StatusCode` is populated on this path too.
3. Otherwise constructs `AdapterHttpRequestFailedException` with `statusCode` (`:188-194`).

`AdapterHttpRequestFailedException : HttpRequestException` and passes the status to the base
constructor (`Kernel/Exceptions/AdapterHttpRequestFailedException.cs:9, :29`), so
`ex.StatusCode == HttpStatusCode.Unauthorized` holds either way.

**Not wrapped on the way up.** The Spotlight pump's consumer does `await fetch`
(`Flows/Findings/Correlated/FalconSpotlightBatchPump.cs:248`), which unwraps the
`Task`/`AggregateException`, and the pump's in-flow retry only catches transport-marker
failures (`:410`, `:489-490` → `HttpTransportFailureClassifier.IsRetryableTransportFailure`).
A 401 matches none of the markers
(`IntegrationInfra/src/IntegrationInfra/Kernel/Transport/HttpTransportFailureClassifier.cs:11-23`),
so it propagates as-is. The flow's own transport catch
(`Flows/Findings/FalconFindingsFlow.cs:386`) likewise declines it.

## 5. The terminal question — when does the `PublishFailure` fallback fire?

Chain order is fixed: user-cancellation → definitive programmer-bug → server-suggested-delay →
retryable-transport → vendor → **mapped** → ambiguous programmer-bug → unknown-flow → fallback
(`FaultGovernance/Logic/AdapterResilienceStrategy.cs:39-67`).

A 401 is claimed by none of the earlier four:
- `UserCancellationPolicy` — not a cancellation.
- `ProgrammerBugPolicy.Definitive` — only NRE / IOoR / InvalidCast / ArrayTypeMismatch /
  Unreachable (`Policies/ProgrammerBugPolicy.cs:46-51`).
- `ServerSuggestedRetryDelayPolicy` — needs a `ServerSuggestedRetryDelayException`.
- `RetryableTransportFailurePolicy` — needs a transport marker or a broken circuit
  (`Policies/RetryableTransportFailurePolicy.cs:21-27`). *This is the policy that would
  publish terminal for Falcon (no `transientTransportBackoff` passed → `:37-44`), but a
  401 message does not carry a transport marker.*

So it reaches the mapped policy → `FalconFlowExceptionClassifier.TryClassify`
(`Processing/FalconFlowExceptionClassifier.cs:60-66`) → `FALCON_UNAUTHORIZED`, retryable →
`FalconResilienceStrategyFactory.CreateRetryableDecision` (`:112-124`) →

```
RecoverAndRetry(handling, 5m/15m/30m MaxRetries=3, "falcon-unauthorized",
                FallbackDecision: PublishFailure(handling))
```

### The budget gate is the only door to the fallback

`FaultGovernance/Logic/AdapterFailureDecisionExecutor.cs:136-156` — `UseRecoveryBudget` is
true (default), so `EvaluateBudget` runs; only `ShouldFallback` reaches
`ExecuteFallbackAsync` → `PublishFailure`. The other fallback trigger,
`ResolveContinuationDescriptionAsync` returning null (`:158-162`), requires a recovery hook
that declines or throws — Falcon's unauthorized path has no such decline.

`FaultGovernance/Logic/RecoveryBudgetEvaluator.cs:34-69`:

```
progressed = prior.LastProgressCoordinate is null || prior.LastProgressCoordinate != currentCoordinate
consecutiveNoProgressCount = progressed ? 1 : prior.Count + 1        // :41-43
if (totalDeferralCount > 50)          -> Exhausted("backstop-total-deferrals")  // :55-59
if (consecutiveNoProgressCount > maxRetries /* 3 */) -> Exhausted("stuck-no-progress")  // :61-65
otherwise Defer                                                       // :67-68
```

Coordinate = `page:items:findings` from the live progress context
(`AdapterFailureDecisionExecutor.cs:225-228`); the prior snapshot is loaded from checkpoint
`AdapterState` (`:223`), so it survives resumes.

**Concretely, for a run that has already made progress:**

| Invocation | prior coordinate | current | count | outcome |
|---|---|---|---|---|
| 1 (progress made, then 401) | none / older | advanced | 1 | Defer 5 m |
| 2 (401 again, zero pages collected) | same | same | 2 | Defer 15 m |
| 3 (same) | same | same | 3 | Defer 30 m |
| 4 (same) | same | same | 4 > 3 | **PublishFailure** |

So: **a run that has made progress cannot reach `PublishFailure` on a 401 in that
invocation.** It takes 4 consecutive no-progress 401 deferrals — ≥50 minutes of scheduled
waits and 3 extra ISB resume hops — or 51 budgeted deferrals with no 25-page clean interval
(`RecoveryBudgetEvaluator.cs:19, :32, :83-92`).

## 6. Other 401 paths — one real hole

### 6a. Token-endpoint 401 → terminal (REAL)

`Cymulate.Http.Package/Authentication/Logic/Strategies/OAuth2Authentication.cs:108-117`:

```
if (!response.IsSuccessStatusCode) {
    ...
    if (!IsRetryableTokenStatus(statusCode) || attempt == maxAttempts) {
        _logger.LogError("Failed to retrieve OAuth2 token. Status: {Status}. {Details}", ...);
        throw new InvalidOperationException(message);   // "OAuth2 token request failed: 401 (Unauthorized). Response: …"
    }
```

`IsRetryableTokenStatus` = {408, 429, ≥500} (`:271-276`), so a 401/403 from
`/oauth2/token` throws immediately as **`InvalidOperationException`, not
`HttpRequestException`**.

Where it escapes: `TrueStreamingTransportService.SendStreamingRequestCoreAsync:369` calls
`Auth.AuthenticateRequestAsync` *inside* the Polly lambda but **outside** any 401 handling.
`InvalidOperationException` is not in Polly's default retryable-exception set
(`RetryPolicy.cs:280-288`), so it escapes on the first attempt.

Then:
- `FalconFlowExceptionClassifier.TryClassify` — `InvalidOperationException` matches no arm
  (`FalconStagedPlaneCorruptException` / `DataPipelineException` are the only
  `InvalidOperationException` subtypes handled) → `_ => null` (`:135`).
- `IsUnauthorized` in the factory tests `HttpRequestException { StatusCode: Unauthorized }`
  (`FalconResilienceStrategyFactory.cs:222-223`) → false.
- `ProgrammerBugPolicy.Ambiguous` — only `ArgumentException` (`:53-54`) → declines.
- `UnknownFlowFailurePolicy` — `IsUnknownRetryCandidate` is true for it
  (`Kernel/Transport/UnknownFlowRetryClassification.cs:48-60`), and Falcon passes no
  `unknownFailureBackoff`, so → `RethrowForUnknownRetry` (`Policies/UnknownFlowFailurePolicy.cs:47-48`).
- Executor returns `Rethrow` (`AdapterFailureDecisionExecutor.cs:38`) →
  `AdapterBusStrategyFlowExecutor.cs:126-129` rethrows.
- The unknown-retry pipeline re-runs the whole flow 3× on 30/60/120 s
  (`AdapterBusFlowExecutor.cs:16-46`, delays at `UnknownFlowRetryClassification.cs:38-43`),
  then rethrows → `AdapterBusEntrypointRunner.cs:157-175` → `HandleUnhandledExceptionAsync`
  → **terminal failed DONE**.

This is a real "a 401 killed the run" path. It is a 401 on the *token* request, costs
~3.5 minutes and 4 whole-flow attempts, and is unmistakable in logs.

### 6b. Force-refresh failure mid-run → absorbed

If `ForceRefreshAsync` throws (token endpoint down, credentials revoked),
`TransportHelpers.TryRefreshCredentialsAsync:88-111` catches, logs
`"Stream credential refresh failed after authorization failure; returning original response."`,
and returns false → `TrueStreamingTransportService.cs:334` returns the 401 → §4/§5 path
(deferral). **Absorbed.**

### 6c. Non-streaming 401 → identical

`TransportService.cs:229-270` shares `TransportHelpers`, same max, same
return-the-response-on-exhaustion behaviour. `AdapterHttpClient.SendForStringAsync:70-89`
throws the same typed exception.

### 6d. Non-replayable body → zero replays

`CanReauthorizeAndReplay` requires `retryAllowed`, which for `StreamResponseAsync` is
`IsRequestReplayable(request)` (`TrueStreamingTransportService.cs:174`) — content must be
`ByteArrayContent` / `StringContent` / `FormUrlEncodedContent` (`TransportHelpers.cs:18-23`).
A non-replayable body means **no reauth replay at all**; the 401 returns immediately. Still
not terminal — it lands on §5's deferral path.

### 6e. Adjacent (not 401, noted in passing)

A **dispatcher**-side fault in the Spotlight pump is delivered via
`writer.TryComplete(ex)` (`FalconSpotlightBatchPump.cs:344`), which surfaces at the consumer's
`WaitToReadAsync` as `ChannelClosedException` with the original as `InnerException`
(`:241`). Falcon's classifier matches on the top-level type only, so a non-transport
dispatcher fault (e.g. an object-store exception reading a staged page) would be seen as an
unmapped `ChannelClosedException` rather than its mapped arm. Out of scope for 401 — recorded
because it is the same class of shape mismatch as 6a.

## 7. What a 401-caused termination would look like in logs

**Path A — budget exhaustion (API 401).** All of these, in order, across ≥4 invocations:

```
[HttpSession:…] Stream request received HTTP 401. Refreshing credentials and replaying the request (1/2).
[HttpSession:…] Stream request received HTTP 401. Refreshing credentials and replaying the request (2/2).
[HttpSession:…] Received stream response HTTP Unauthorized from … (attempts: 1, reauth replays: 2).
HTTP request failed. StatusCode=401 Url=… Body(firstN)=…
recover.decision Classification=unauthorized, ErrorCode=FALCON_UNAUTHORIZED, …, DelaySec=300, UseRecoveryBudget=True, Fallback=publish-failure
Adapter deferred recovery requested. … ConsecutiveNoProgress=1/3 … Delay=00:05:00 Reason=falcon-unauthorized
   … (resume) ConsecutiveNoProgress=2/3 Delay=00:15:00
   … (resume) ConsecutiveNoProgress=3/3 Delay=00:30:00
   … (resume) Adapter recovery budget exhausted (stuck-no-progress). … ConsecutiveNoProgress=4 …
```

Log sources: `TrueStreamingTransportService.cs:389-395`, `:344-350`;
`AdapterHttpClient.cs:154-160`; `FalconResilienceStrategyFactory.cs:145-159`;
`AdapterFailureDecisionExecutor.cs:256-265`, `:142-150`.

**Nothing terminal is printed on the first 401 of a progressing run.** The absence of
`Adapter recovery budget exhausted (stuck-no-progress)` alone rules Path A out.

**Path B — token-endpoint 401.**

```
Failed to retrieve OAuth2 token. Status: Unauthorized. Response: {…}
Flow failed with unclassified exception. Vendor=… Flow=… Attempt=4. Unknown-error retries exhausted.
```

(`OAuth2Authentication.cs:116`; `AdapterBusFlowExecutor.cs:39-44`.) Preceded by three
30/60/120 s gaps and three re-runs of the whole flow. Terminal error text would be
`OAuth2 token request failed: 401 (Unauthorized). …`.

## 8. Bearing on the observed production failure

The evidence quoted in the task — `(2/2)`, `force-refresh requested. Trigger="forced-401"`,
`HTTP RESPONSE 201 POST /oauth2/token`, `Credentials refreshed … Replaying the stream
request.`, then **four more pages published** — describes the reauth machinery **succeeding**:
the refresh worked, the replay worked, and the run kept collecting.

For the terminal outcome 112 s later to be 401-caused, the log would have to contain either
the `stuck-no-progress` budget-exhaustion warning (impossible: pages were published between
the 401 and the failure, so `progressed` is true and the count resets to 1 —
`RecoveryBudgetEvaluator.cs:41-43`) or the `Failed to retrieve OAuth2 token. Status:
Unauthorized` line plus a ~3.5-minute unknown-retry gap (the failure came 112 s later, too
soon for four attempts at 30/60/120 s).

**The 401 is not a plausible cause of that termination.** It is consistent with the other
candidate explanation, not this one.

---

## Certainty

**Read directly (high confidence):** the replay loop and its exhaustion branches; the max of 2
and its source; Falcon resolving to the default 2 via `OAuth2` + `CredentialProviderAuthenticator`;
401 absent from both the retry status set and the breaker set; the exception type and populated
`StatusCode`; the full policy chain order and each policy's predicate; the budget evaluator's
arithmetic; the `Rethrow` → unknown-retry → terminal path; the `OAuth2Authentication`
`InvalidOperationException` throw.

**Inferred (medium confidence):**
- That a mid-run token acquisition inside `SendStreamingRequestCoreAsync` can throw the
  token-endpoint `InvalidOperationException` at all depends on the mirror being expired at
  that moment. Read from `CredentialProviderAuthenticator.cs:152-159` + `:228-269`, not
  observed live. The exception *type* and the classifier gaps downstream are read, not inferred.
- The `ChannelClosedException` wrapping in §6e is standard `System.Threading.Channels`
  behaviour, inferred from the `TryComplete(ex)` / `WaitToReadAsync` pairing, not from a test.
- Page-count arithmetic in §5's table assumes `CurrentPage`/`ProcessedItems`/`ProcessedFindings`
  are all unchanged between deferrals; that is what "no progress" means, but the exact Falcon
  write points for those counters were not traced end to end.

**Not checked:** whether any Falcon unit test pins the max-2 value or the four-deferral
boundary; the ISB-side redelivery semantics of `PartialResult`.
