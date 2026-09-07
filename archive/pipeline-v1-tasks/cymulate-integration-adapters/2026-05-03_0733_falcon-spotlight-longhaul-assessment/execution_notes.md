# Execution Notes

## What was inspected

- Falcon findings pagination and publishing flow.
- Falcon checkpoint state and resume helpers.
- Falcon-specific HTTP failure classifier and flow exception classifier.
- Shared adapter HTTP wrapper, retry handling, and failure mapping.
- Local Cymulate.Http.Package OAuth2, reauthorization, streaming transport, retry, and refresh-loop code.
- Public CrowdStrike/FalconPy, GoFalcon, CrowdStrike Tech Hub, and CrowdStrike Spotlight TA documentation.

## Commands run

- `rg --files -g '*Falcon*' -g '*falcon*' -g '*.cs'`
- `rg -n "401|Unauthorized|Refresh|refresh|Retry|Retry-After|rate|Rate|after|AfterToken|LastWatermark|Checkpoint|FindingsApiPageSize|VulnerabilitiesApiPageSize|MaxDegree|Parallel|Semaphore|InProgress|Partial|partial" ...`
- `rg -n "OAuth2|Bearer|Token|Refreshable|ForceRefresh|IForceRefresh|AuthenticationProvider|Authenticator|expires|expiry|Semaphore|lock" ...`
- `nl -ba` on the relevant Falcon, shared orchestration/session, and local Http.Package files.
- Web research against accessible CrowdStrike/FalconPy, GoFalcon, and CrowdStrike Tech Hub sources.

## Validated

- `/spotlight/combined/vulnerabilities/v1` usage is structurally correct: filter required, `after` pagination, supported facets, `limit` within documented maximum, and `updated_timestamp`-based filtering.
- Local `spotlight.pdf` explicitly recommends using the combined vulnerabilities endpoint for baseline/local replica and subsequent differential pulls.
- Local `spotlight.pdf` says vulnerability management responses are sorted chronologically by last updated timestamp by default to preserve completeness for replica maintenance.
- Local `spotlight.pdf` documents `after` tokens as expiring 120 seconds after a call is made.
- Local `spotlight.pdf` says some 404 conditions can appear as code under `errors` with a 200 OK header.
- Production log sample from 2026-05-01 shows an actual HTTP 401, not a 200-with-embedded-error, at `Page=7817` for the April 2026 Spotlight segment with `limit=2500`, all four requested facets, `sort=updated_timestamp.asc`, a long `after` token, and body `errors[0].message="authorization failed"`.
- The same production 401 body includes `powered_by="crowdstrike-api-gateway"`, CrowdStrike `trace_id="bfd2b3e8-6743-4356-a431-7721986eb0f0"`, and `query_time=0.000315688`, indicating a very early gateway/auth rejection rather than an expensive Spotlight backend query timeout.
- The visible sort component inside the `after` token corresponds to `2026-04-27T01:37:16Z`, while the request filter lower bound remained `updated_timestamp >= 2026-04-01T16:32:00Z`; the failure was deep in the cursor chain but not at the segment boundary.
- Follow-up production screenshot shows `POST https://api.crowdstrike.com/oauth2/token` returned 201, then the session logged `Credentials refreshed after authorization failure. Replaying the stream request.`, then the replayed Spotlight GET still failed with 401 roughly one minute later. This confirms `Persistent401AfterRefresh` for the observed incident.
- `falcon401s.txt` contains the available traffic evidence. The HTTP traffic record for the failed Spotlight GET shows a 60,161 ms request duration, `X-Cs-Region=us-1`, `X-Cs-Traceid=2260249f-0d84-4e18-9abb-4e6f7c87fa89`, `X-Ratelimit-Limit=6000`, `X-Ratelimit-Remaining=5998`, no visible `Retry-After`, and no visible `WWW-Authenticate`.
- The traffic evidence weakens the documented-rate-limit hypothesis: CrowdStrike reported nearly the full 6000-request limit remaining on the 401 response.
- CrowdStrike support feedback is unavailable and should not block the implementation path; classification and recovery should be based on observed adapter/session behavior plus documented Spotlight cursor semantics.
- OAuth2 uses client credentials and a local single-flight refresh semaphore in the auth strategy.
- Stream requests reauthorize and replay on 401 up to the configured reauthorization retry count.
- Follow-up inspection of `/Users/user/Dev/Cymulate.Http.Package` shows the OAuth2 strategy itself is not the likely failure point: `OAuth2Authentication.ForceRefreshAsync` bypasses the non-forced freshness short-circuit and `RefreshCoreAsync` is protected by a `SemaphoreSlim`.
- The likely shared-package gap is at the transport boundary. `TrueStreamingTransportService.SendWithTrueStreamingAsync` and `ExecuteTransportService.SendAttemptAsync` refresh/replay internally, then return the final HTTP response as a normal response if replay is exhausted. The only machine-readable hint left on the caller's original request is `RequestContextKeys.ReauthorizationRetryCount`.
- `AdapterHttpClient.ThrowFailure` receives the original request and final response, but does not currently include reauthorization replay count, refresh success/exhaustion state, or auth-replay classification in `AdapterHttpRequestFailedException`.
- Follow-up inspection of `/Users/user/Dev/Cymulate.Http.Package/DefensiveToolkit/Policies/RateLimiterPolicy.cs` shows the rate limiter is static admission control: it acquires a permit before executing the operation and does not observe response headers after the request completes.
- Follow-up inspection of `/Users/user/Dev/Cymulate.Http.Package/DefensiveToolkit/Policies/RetryPolicy.cs` shows standard `Retry-After` is supported for retry delay, with an optional custom response delay delegate, but this is separate from the rate limiter and does not create shared server-header-aware pacing.
- `rg` over `/Users/user/Dev/Cymulate.Http.Package` found no shared parsing for CrowdStrike-style `X-Ratelimit-Limit` / `X-Ratelimit-Remaining`; those headers are visible in `falcon401s.txt` but are not consumed by the package.
- This means the current configured RPM can be conservative yet still blind to server-side quota telemetry, hidden vendor-specific limit windows, or dampening signals. The observed 401 still does not look like a documented rate-limit breach because CrowdStrike reported `5998/6000` remaining, but the header-awareness gap is real.
- Findings pagination publishes one page, then checkpoints `after`, watermark, boundary IDs, page size, segment bounds, and filtered-findings bounded state.
- 401 after refresh is not Falcon-classified; shared failure handling maps 401/403 to `INVALID_CONFIGURATION`.
- Added `next_steps.md` with a minimal implementation sequence for Falcon-specific 401 classification, richer checkpoint/failure context, 120-second Spotlight cursor lifetime handling, controlled long-haul pacing, partial publishing metadata, and targeted tests.

## Residual risks

- Deployed adapter and local HTTP package commit/version remain unproven from the traffic file.
- The production 401 body confirms gateway authorization failure text after successful refresh/replay, which strongly favors CrowdStrike gateway/context rejection or a permission/subscription evaluation edge over ordinary token expiry.
- The previous assessment understated cursor-expiry risk; documented 120-second `after` expiry means multi-minute cooldown/resume must prefer watermark/date-anchor recovery.
- Current code comments and checkpoint staleness logic that refer to CrowdStrike cursor expiry around 24 hours are inconsistent with the local Spotlight PDF.
- Current response parsing does not appear to classify top-level `errors` from 200 OK responses, which may miss documented embedded 404 conditions.
- Current long-haul pacing is client-configured only. It does not learn from vendor headers such as CrowdStrike `X-Ratelimit-*`, so it cannot slow down in response to server-side remaining/reset information unless that happens to arrive as standard `Retry-After`.

## 2026-05-03 External occurrence reference research

- Searched official CrowdStrike/FalconPy/GoFalcon/Tech Hub sources and community/third-party sources for public reports matching long-running Spotlight `/spotlight/combined/vulnerabilities/v1` pagination followed by persistent 401 after successful refresh.
- No exact public occurrence was found for the full incident shape.
- Official and official-adjacent evidence supports the endpoint shape, `after` pagination, 5,000 max limit, required `spotlight-vulnerabilities:read` / `vulnerabilities:read` permission, 30-minute OAuth token lifetime, and short Spotlight `after` token expiry.
- CrowdStrike Tech Hub explicitly warns that pagination approaches that work for thousands of records can fail at scale and recommends persistent state, workflow-style orchestration, backoff, and careful termination/recovery.
- Community and third-party evidence shows `crowdstrike-api-gateway` commonly emits `access denied, authorization failed` for credential, scope, or token misuse, but does not confirm this exact long-haul Spotlight backend behavior.
- Most defensible root-cause ordering remains: gateway/context rejection during deep long-running query first; cursor/page-size/response-size pressure second; ordinary token expiry low because refresh succeeded and replay still failed; explicit rate-limit breach low because observed headers showed high remaining quota and 401 rather than 429.
