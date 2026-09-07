# Next Steps

## Objective

Make the Falcon Spotlight vulnerabilities collector safe for mega-tenant historical extraction without replacing the current architecture.

The immediate implementation target is to handle confirmed `Persistent401AfterRefresh` as a recoverable long-haul Falcon condition:

```text
Spotlight GET returns 401
OAuth refresh succeeds
same Spotlight GET is replayed
replayed GET returns 401 again
```

This is not ordinary token expiry. The collector should persist durable progress, cool down, and resume from the watermark/date anchor instead of retrying the same cursor indefinitely or surfacing the failure only as invalid configuration.

## Implementation Order

1. Add shared transport reauthorization outcome metadata

   Files to inspect/change:

   - `/Users/user/Dev/Cymulate.Http.Package/Session/Logic/Transport/TrueStreamingTransportService.cs`
   - `/Users/user/Dev/Cymulate.Http.Package/Session/Logic/Transport/TransportService.cs`
   - `/Users/user/Dev/Cymulate.Http.Package/DefensiveToolkit/Core/RequestContextKeys.cs`
   - `/Users/user/Dev/Cymulate.Http.Package/Session/Contracts/Models/Transport/StreamedResponse.cs`
   - `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Session/AdapterHttpClient.cs`
   - `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Session/TransportErrorHandling/AdapterHttpRequestFailedException.cs`

   Expected behavior:

   - Preserve existing first-401 refresh/replay behavior.
   - When refresh succeeds and replay still returns a refresh-triggering status, expose that as structured metadata instead of only logs.
   - Include reauthorization replay count, max replay count, whether refresh was attempted, whether refresh succeeded, and whether replay was exhausted.
   - Propagate this metadata into adapter-level failure classification.

2. Add shared server rate-limit header awareness

   Files to inspect/change:

   - `/Users/user/Dev/Cymulate.Http.Package/DefensiveToolkit/Policies/RateLimiterPolicy.cs`
   - `/Users/user/Dev/Cymulate.Http.Package/DefensiveToolkit/Wrappers/RateLimiterRouterPolicy.cs`
   - `/Users/user/Dev/Cymulate.Http.Package/DefensiveToolkit/Contracts/Options/RateLimiterOptions.cs`
   - `/Users/user/Dev/Cymulate.Http.Package/DefensiveToolkit/Policies/RetryPolicy.cs`
   - `/Users/user/Dev/Cymulate.Http.Package/Session/Logic/Transport/TrueStreamingTransportService.cs`
   - `/Users/user/Dev/Cymulate.Http.Package/Session/Logic/Transport/TransportService.cs`
   - Falcon collector session/profile configuration

   Expected behavior:

   - Keep standard `Retry-After` support in retry policy.
   - Add a separate server rate-limit telemetry layer that can parse configured response headers and feed future pacing.
   - Support a small list of known header profiles, starting with:
     - Standard retry delay: `Retry-After`
     - Emerging standard-style quota telemetry: `RateLimit-Limit`, `RateLimit-Remaining`, `RateLimit-Reset`
     - CrowdStrike/Falcon: `X-Ratelimit-Limit`, `X-Ratelimit-Remaining`; only add `X-Ratelimit-Reset` if observed or documented
     - Common GitHub-style casing: `X-RateLimit-Limit`, `X-RateLimit-Remaining`, `X-RateLimit-Reset`
   - Treat vendor headers as dampening/diagnostic signals. Do not automatically raise configured RPM just because the server limit appears higher.
   - Persist/log parsed limit, remaining, reset/retry delay if available, header profile name, and route/host key.
   - If remaining is low or reset information indicates the current window is exhausted, delay future requests before acquiring permits or before dispatch, rather than waiting for a 429.

   Minimal design:

   - Prefer a small response-observer/feedback component over mutating `System.Threading.RateLimiting` internals directly.
   - Keep the existing static limiter as the hard configured ceiling.
   - Layer server feedback as an additional delay/cooldown gate keyed by host and optionally route/profile.
   - Add tests with CrowdStrike headers showing that parsed `X-Ratelimit-*` values are recorded and can dampen future requests without relying on `Retry-After`.

3. Add Falcon-specific 401 classification

   Files to inspect/change:

   - `src/Cymulate.Integration.Adapters/Collectors/FalconCollector/Flows/SharedFlows/FalconHttpFailureClassifier.cs`
   - `src/Cymulate.Integration.Adapters/Collectors/FalconCollector/Flows/Findings/FalconSpotlightVulnerabilitiesRunner.cs`

   Expected behavior:

   - Keep normal first 401 handling in the shared session layer.
   - After shared transport reports refresh/replay exhaustion and the final response is still 401, classify Spotlight requests with `after` as a distinct Falcon long-haul authorization-context failure.
   - Include endpoint, page context, segment bounds, CrowdStrike trace ID, response body snippet, and whether `after` was present.

4. Persist richer failure context

   Files to inspect/change:

   - `src/Cymulate.Integration.Adapters/Collectors/FalconCollector/Recovery/FalconCheckpointState.cs`
   - `src/Cymulate.Integration.Adapters/Collectors/FalconCollector/Flows/Findings/FalconFindingsCheckpointWriter.cs`
   - `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Session/AdapterHttpClient.cs`

   Add or verify persisted fields for:

   - tenant/CID if available
   - segment start/end
   - current filter
   - page size
   - last cursor
   - last successful watermark/date anchor
   - last successful boundary IDs
   - records/pages collected
   - last response headers
   - last error body
   - CrowdStrike trace ID
   - failure classification

5. Correct Spotlight cursor lifetime assumptions

   Files to inspect/change:

   - `src/Cymulate.Integration.Adapters/Collectors/FalconCollector/Recovery/FalconCheckpointState.cs`
   - `src/Cymulate.Integration.Adapters/Collectors/FalconCollector/Recovery/FalconCheckpointHelper.cs`
   - `src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Recovery/RecoveryParsingHelper.cs`

   Expected behavior:

   - Do not use the generic 23-hour staleness rule for Spotlight `after` tokens.
   - Treat Spotlight `after` as unsafe after roughly 120 seconds, per `FalconDocs/spotlight.pdf`.
   - After cooldown, rebuild the query from the watermark/date anchor and boundary IDs.

6. Add controlled long-haul pacing

   Files to inspect/change:

   - `src/Cymulate.Integration.Adapters/Collectors/FalconCollector/Flows/Findings/FalconSpotlightVulnerabilitiesRunner.cs`
   - Falcon collector settings/configuration classes

   Minimal controls:

   - checkpoint every page, as today
   - optionally publish every page, as today
   - voluntarily pause after a configurable page budget
   - force token refresh before resuming
   - if the pause exceeds 120 seconds, drop `after` and resume from watermark/date anchor
   - consult parsed server rate-limit telemetry before continuing; if CrowdStrike reports low remaining or a reset/cooldown signal, let the shared rate-limit feedback gate extend the pause

7. Improve partial publishing semantics without status bloat

   Files to inspect/change:

   - `src/Cymulate.Integration.Adapters/Collectors/FalconCollector/Processing/FalconPartialSuccessResultBuilder.cs`
   - shared collector result/envelope models
   - downstream consumers that interpret `Success`, `Failed`, and `Partial`

   Minimal target:

   - Keep existing partial-success behavior for now.
   - Add enough metadata for downstream consumers to know the segment is resumable and not logically failed.
   - Avoid adding `Paused`, `Resuming`, or many new statuses unless consumers need them.

8. Add tests and simulation

   Test cases:

   - normal first 401 refresh succeeds and replay succeeds
   - refresh succeeds and replay returns 401, classified as `Persistent401AfterRefresh`
   - shared transport exposes replay-exhausted metadata on final 401
   - 401 after refresh with `after` present persists checkpoint and resumes from watermark
   - 404 with `after` still uses cursor-poisoned fallback
   - 200 OK with embedded CrowdStrike `errors[].code=404` does not silently complete the segment
   - cooldown longer than 120 seconds does not reuse Spotlight `after`
   - CrowdStrike `X-Ratelimit-*` headers are parsed and recorded separately from `Retry-After`
   - server rate-limit telemetry can dampen future requests without increasing above the configured client-side ceiling

## Acceptance Criteria

- A replayed Spotlight 401 after successful token refresh is not reported only as `INVALID_CONFIGURATION`.
- The collector persists enough state to resume deterministically from the last durable watermark/date anchor.
- The collector does not reuse Spotlight `after` after a long cooldown.
- Logs include CrowdStrike trace ID, page, segment, filter floor, cursor presence, response body snippet, and final classification.
- Logs or failure metadata include parsed server rate-limit telemetry when present, including CrowdStrike `X-Ratelimit-*` headers.
- Existing normal pagination behavior remains unchanged for healthy runs.
- Existing 404/500 cursor fallback behavior remains intact.

## Remaining Information To Capture

- Deployed `Cymulate.Http.Package` version or commit SHA for the production pod.
- Whether the same request succeeds after dropping `after` and resuming from the watermark/date anchor.

The supplied `falcon401s.txt` traffic file is sufficient to proceed without CrowdStrike support. It includes response headers for the 401 and shows rate-limit headers with `X-Ratelimit-Limit=6000` and `X-Ratelimit-Remaining=5998`; there is no visible `Retry-After` or `WWW-Authenticate`.

## Suggested Rollout

1. Ship observability/classification first.
2. Add shared server rate-limit header parsing/telemetry in passive mode.
3. Enable watermark/date-anchor resume for confirmed `Persistent401AfterRefresh`.
4. Add configurable voluntary cooldown with cursor drop after 120 seconds.
5. Enable conservative server-header dampening if passive telemetry shows pressure.
6. Validate on one historical monthly segment.
7. Only then consider broader status-model changes for explicit `InProgress`.
