# Code review — connection-test probe (ValidateConfigurationAsync / ProbeAsync)

Scope: the connection-test probe added to `CollectorExecutorAdapter` + `CollectorExecutorRunner.ProbeAsync`
+ the three Validate tests. Reviewed in isolation against correctness, async, edge cases, idiom, and
maintainability. Grounded in file:line. No files modified.

Verdict: the happy path is sound and the design reuses the real session/auth/transport substrate
correctly. There is **one blocker** (uncaught exception surface for a class of bad-but-plausible
configs), **one major** (the probe creates real server-side side effects for export-job vendors), and
several minors/nits. The "entire config dict as Credentials" concern is, on inspection, benign.

---

## Blocker

### B1 — Non-HTTP exceptions escape `ValidateConfigurationAsync` as an unhandled throw to the platform
`CollectorExecutor/Execution/CollectorExecutorRunner.cs:60-96`,
`CollectorExecutor/CollectorExecutorAdapter.cs:108-140`

`ProbeAsync` only catches `AdapterHttpRequestFailedException` / circuit-breaker
(`CollectorExecutorRunner.cs:89`). `ValidateConfigurationAsync` only wraps `ProfileLoader.Load` in a
catch (`CollectorExecutorAdapter.cs:120`); the `await _runner.ProbeAsync(...)` call at line 136 is
**not** guarded. So any exception that is neither an HTTP failure nor a YAML-load failure propagates
out of `ValidateConfigurationAsync` to the platform caller. This is reachable with plausible config,
not just programmer error:

- **Malformed `base_url`.** The preparer only checks `base_url` is non-empty
  (`CollectorExecutorRunPreparer.cs:64`); it never validates it is an absolute URI. `ProbeAsync` then
  calls `CollectorExecutorSessionFactory.Build` → `new Uri(baseUrl)`
  (`CollectorExecutorSessionFactory.cs:21`) with no guard. A value like `"falcon.local"` (no scheme)
  or `"not a url"` throws `UriFormatException`, which is uncaught all the way up. A connection *test*
  given a typo'd URL should report Failure, not throw.
- **Template / body-build errors.** `TemplateRenderer.Render(bodyTpl, ctx)` and
  `new HttpMethod(step.Request.Method)` (`CollectorExecutorRunner.cs:80-82`) run before the HTTP call.
  A bad method token or a template error throws outside the HTTP catch.

The whole point of a connection test is to convert "can I reach the vendor with this config" into a
boolean+message. An unhandled throw defeats that and likely surfaces as a 500/opaque error in the
setup UI.

Fix: wrap the body of `ValidateConfigurationAsync` (everything after the no-yaml guard) — or at minimum
the `ProbeAsync` call — in a broad `catch (Exception ex)` that returns
`ConfigurationValidationResult.Failure(ex.Message, "PROBE_ERROR")`. Validation is exactly the place
where catch-broad-and-report is correct (contrast with `RunAsync`, where the bus owns classification).
Cheapest correct version:

```csharp
try
{
    var result = await _runner.ProbeAsync(probeEvent, cancellationToken).ConfigureAwait(false);
    return result.Status == AdapterResultStatus.Success
        ? ConfigurationValidationResult.Success(result.Message ?? "Connection OK.")
        : ConfigurationValidationResult.Failure(result.Message ?? "Connection failed.", result.ErrorCode ?? "PROBE_FAILED");
}
catch (OperationCanceledException) { throw; }            // honor caller cancellation
catch (Exception ex)
{
    return ConfigurationValidationResult.Failure($"Connection test failed: {ex.Message}", "PROBE_ERROR");
}
```

(Keep rethrowing `OperationCanceledException` so a cancelled setup call is not misreported as a config
Failure.)

---

## Major

### M1 — `ProbeAsync` issues `Steps[0]`, which for export-job vendors is a side-effecting POST that creates a job
`CollectorExecutor/Execution/CollectorExecutorRunner.cs:76-84`

`var step = p.Steps[0]` then sends that step's request. For Tenable (and any export-job profile), the
first step is `{ kind: fetch, request: { method: POST, path: "/export/start" }, capture: { export_uuid } }`
(see `TenableProfile`, Tests `CollectorExecutorTests.cs:1856`; same shape at lines 1878, 1900, 1913,
1936, 1969). So a "connection test" **creates a real export job on the vendor** every time setup is
validated, with no poll, no drain, and no cleanup. Consequences:

- Orphaned/abandoned export jobs accumulate on the vendor side (Tenable export slots are a limited,
  rate-limited resource; repeated setup validations can exhaust them).
- The probe's success/failure semantics are muddier than intended: it proves "auth + I can POST to
  start", not "I can read data". For a GET-first vendor that is fine; for a POST-create-first vendor it
  is a mutation masquerading as a read.

This is a deliberate trade (the prompt flags it), and there is no perfectly generic read-only probe
that works for every YAML profile. But the current choice silently mutates vendor state for a whole
class of profiles. Options, cheapest first:

1. **Cheapest, recommended for now:** probe **auth only** for steps whose first request is a mutating
   verb. Building the session (`provider.Create(...)`) already triggers the OAuth2 token fetch / will
   exercise api_key+basic on the first call; for OAuth2 you get a real credential check from session
   creation alone. Concretely: if `Steps[0].Request.Method` is POST/PUT/PATCH/DELETE, skip the request
   and treat a successfully-built+authenticated session as the probe (return Success). This avoids the
   job-creation side effect while still failing closed on bad OAuth2 creds. Caveat: api_key/basic are
   not validated until a request is issued, so an auth-only probe would not catch a wrong api_key for a
   POST-first vendor — acceptable, and better than creating jobs.
2. Add an optional `probe:` hint to the YAML contract (a read-only endpoint to hit for connection
   tests) and use it when present, falling back to `Steps[0]` only for GET-first profiles. More work,
   cleaner long-term, but is new surface — defer unless product needs it.

At minimum, document the side effect at the call site (`CollectorExecutorRunner.cs:76`) and in the
adapter's `ValidateConfigurationAsync` summary so it is a known, intentional trade rather than a latent
surprise. Recommend option 1.

---

## Minor

### m1 — `Credentials = entire config dict` is redundant, not unsafe — but worth tightening
`CollectorExecutor/CollectorExecutorAdapter.cs:131-133`

The synthesized event sets `Credentials = new Dictionary(_lastConfiguration)` (the whole dict, incl.
`yaml`, `base_url`) **and** `Payload.config = _lastConfiguration`. I traced the leak concern: it does
**not** leak. Auth selection reads only named keys via `GetValueOrDefault` — `client_id`,
`client_secret`, `api_key`, `username`, `password`, etc. (`CollectorExecutorSessionFactory.cs:50-51,
74, 97-98, 113-117`). Extra keys like `yaml`/`base_url` are never enumerated into headers, so an
inert oversized credentials map is the only effect. There is no confusion of auth selection and no
header leak of the YAML.

It is, however, doubly redundant: `CollectorExecutorRunInputBuilder.Build` already (a) copies
`platformEvent.Credentials` and (b) merges every `config` key into `credentials` that isn't already
present (`CollectorExecutorRunInputBuilder.cs:20-28`). So whether you pass the config dict as
`Credentials` or only in `Payload.config`, the preparer ends up with the same merged credentials map.
Passing it in both places is belt-and-suspenders with no added value.

Cleaner: pass `Credentials = new Dictionary(_lastConfiguration)` is fine to keep, but you could equally
pass an empty `Credentials` and rely solely on `Payload.config` merging — the run path does exactly
that for real dispatches. Pick one channel. Lowest-risk edit: leave as-is functionally but drop a
one-line comment that the double-feed is intentional/redundant-but-harmless, OR stop populating
`Credentials` and let the config-merge do it (matches the real-run ingress more faithfully). Not a
blocker either way.

Note the YAML string itself does ride in `Credentials["yaml"]` here. It is non-secret, and nothing
reads it as a credential, so no functional issue — but if credential maps are ever logged/redacted as
a unit downstream, a multi-KB YAML blob in the "credentials" bucket is noise. Minor.

### m2 — Duplicated session-setup block between `ProbeAsync` and `RunAsync`
`CollectorExecutor/Execution/CollectorExecutorRunner.cs:66-73` vs `147-154`

Both do the identical sequence: `CollectorExecutorSessionFactory.Build` → null-check →
`AdapterResult.ValidationFailure("Auth type ... unsupported/misconfigured", "INVALID_PAYLOAD")` →
`new DefaultSessionProvider(...)` → `provider.Create(sessionSpec, CorrelationId, ct)` →
`new AdapterHttpClient(handle.Session, _logger)`. The two failure messages even differ slightly
(line 69 vs line 150) for the same condition, which is a small inconsistency. This is ~8 lines and the
disposal (`await using`) must stay at each call site, so a full extraction has limited payoff — but a
small helper that returns `(AdapterResult? failure, SessionSpec? spec)` for the Build+classify step
would remove the duplicated message and keep the two `await using handle` statements local. Acceptable
to leave; flagging the divergent error text as the concrete nit to fix if you touch it.

### m3 — Tests are meaningful but don't assert no vendor mutation / don't cover the throw path
`Tests/CollectorExecutor.Test/CollectorExecutorTests.cs:1691-1736`

Good news: Good/Bad/NoYaml are **real round-trips**, not shallow. `Validate_GoodCreds` drives a real
api_key request to `/apikey/query` → 200 (gated on `X-Api-Key: secret-123`,
`CollectorExecutorTests.cs:128-131`); `Validate_BadCreds` sends `api_key=wrong` → real 401 → asserts
`UNAUTHORIZED` (the exact mapping at `CollectorExecutorRunner.cs:92`); `Validate_NoYaml` asserts the
fail-closed `NO_DEFINITION`. These exercise the auth header application and the status→code mapping
end-to-end. Solid.

Gaps:
- No test for the **B1 throw path** (e.g. `base_url` without a scheme, or a profile whose `Steps[0]` is
  a bad method). Add one that asserts `ValidateConfigurationAsync` returns `IsValid == false` (and does
  not throw) for a malformed `base_url`. This is the regression guard for the blocker fix.
- No test pins the **M1 side effect**. If you adopt the auth-only-for-mutating-verb fix, add a test
  that validates a POST-first (export) profile and asserts `/export/start` was **not** requested
  (`httpFactory.RequestedUrls` does not contain it). Today such a test would prove the job gets created
  — which is the behavior in question.
- All three use api_key (GET-first). None covers OAuth2 validation, where session creation alone
  performs the token fetch — worth one case so the OAuth2 probe path is exercised.

---

## Nits

### n1 — `topic` resolution in `ValidateConfigurationAsync` duplicates the preparer's stream resolution
`CollectorExecutor/CollectorExecutorAdapter.cs:114-123`

`ValidateConfigurationAsync` loads the profile a **second** time (once here at line 117, then again
inside `ProbeAsync` → `_preparer.Prepare`) only to pick `Streams.Keys.FirstOrDefault()` for the event
`Topic`. The preparer already resolves the stream from the topic (`CollectorExecutorRunPreparer.cs:28`,
`ResolveStream`), and `ProbeAsync` uses `p.Steps[0]` regardless of `Topic`. So the topic computed here
is essentially only used to satisfy `ResolveStream` inside the probe. Loading + parsing the YAML twice
per validation is wasteful (minor — validation is not hot). Could pass the already-loaded profile down,
but that widens `ProbeAsync`'s signature; not worth it. Leave unless the double-parse matters.

### n2 — `_lastConfiguration` mutability/threading is fine for the documented lifecycle
`CollectorExecutor/CollectorExecutorAdapter.cs:44, 98-103, 110-131`

The field is reassigned in `SetConfiguration` and read in `ValidateConfigurationAsync`. The SDK setup
contract is sequential (SetConfiguration then ValidateConfigurationAsync on the same instance), and the
read takes a defensive copy into the event (`new Dictionary<>(_lastConfiguration)`, line 131). No
torn-read risk for the dictionary reference under .NET (reference assignment is atomic), and the
collect path (`ProcessAsync`) does not read `_lastConfiguration` at all — it uses the per-event payload.
No concurrency concern given the lifecycle. The non-null initializer (line 44) makes the no-yaml guard
safe even if `ValidateConfigurationAsync` is called before `SetConfiguration`. Good. Flagging only to
confirm it was checked.

### n3 — Probe correctly ignores the response body
`CollectorExecutor/Execution/CollectorExecutorRunner.cs:84`

`_ = await http.SendForStringAsync(...)` discards the body. Correct for a probe: a 2xx is the signal;
`SendForStringAsync` throws `AdapterHttpRequestFailedException` on non-2xx (`AdapterHttpClient.cs:79-86`),
which is exactly what the catch classifies. Nothing downstream of the probe needs the body (no parse,
no capture, no publish — the method returns immediately after). No issue.

### n4 — Fully async, no sync-over-async
`ProbeAsync` is `async`, every I/O call is awaited with `.ConfigureAwait(false)`, and `await using`
disposes the `SessionHandle` (which is genuinely `IAsyncDisposable`, `SessionHandle.cs:10,59`) — so the
async disposal path is taken, not the blocking `Dispose()` (`SessionHandle.cs:49`). Cancellation token
is threaded into both `provider.Create` and `SendForStringAsync`. Resource hygiene is correct. No gaps.

---

## Summary of required action
- **B1 (blocker):** make `ValidateConfigurationAsync` catch broadly and return Failure; a malformed
  `base_url` currently throws unhandled.
- **M1 (major):** decide and document the export-job side effect; recommend probing auth-only for
  mutating first-steps so a connection test never creates a vendor job.
- Minors: tighten the redundant Credentials double-feed (m1), de-duplicate/align the session-setup
  block's error text (m2), add throw-path + no-mutation + OAuth2 tests (m3).
