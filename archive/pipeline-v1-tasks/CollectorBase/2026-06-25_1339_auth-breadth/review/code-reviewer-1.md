# Code review — `username_password_token_exchange` auth

Scope reviewed: `CollectorExecutorSessionFactory.cs`, `integrations/guardicore.yaml`,
the two new tests + `GuardicoreProfile` const + mock endpoints in `CollectorExecutorTests.cs`,
and the `yaml-contract.md` auth additions. Build and the two new tests pass.

Overall: the builder is correct and consistent with its siblings. One MINOR finding about the
bad-creds test asserting less than it implies, plus a couple of NITs. No blockers.

---

## MINOR — `UsernamePasswordTokenExchange_BadCreds_FailsClosed` passes via the catch-all, not the auth-classification path it implies

`Tests/CollectorExecutor.Test/CollectorExecutorTests.cs:858-873`

The test comment says "token endpoint 401 -> the exchange fails before any data call -> probe reports
invalid", and the sibling api_key bad-creds test (`Validate_BadCreds_ReturnsInvalid`,
line 1771) asserts `ErrorCode == "UNAUTHORIZED"`. This test deliberately asserts only
`Assert.False(result.IsValid)` and omits the error-code check. I confirmed empirically (temporary
diagnostic, reverted) why:

```
DIAG ErrorCode=PROBE_ERROR Msg=Connection test error: Username/password token exchange failed: 401 (Unauthorized). Response: {"error":"invalid credentials"}
```

Root cause: the token-exchange strategy throws `InvalidOperationException` on a 401 token response
(`UsernamePasswordTokenExchangeAuthentication.cs:132`), raised during `AuthenticateRequestAsync`
before the data request completes. `CollectorExecutorRunner.ProbeAsync`'s catch only handles
`AdapterHttpRequestFailedException` / circuit-breaker exceptions (`CollectorExecutorRunner.cs:89`),
so the `InvalidOperationException` escapes to the adapter's outer catch-all
(`CollectorExecutorAdapter.cs:147-151`) and is reported as `PROBE_ERROR`.

Consequence: the test is not a false positive — it does drive the real token exchange and a wrong
password does yield `IsValid == false`. But it is weaker than it reads. As written it would also pass
for an *unrelated* failure (malformed URL, network error, a bug that makes any probe throw), because
`PROBE_ERROR` is the generic catch-all bucket. Bad creds and a typo'd base URL are indistinguishable
to this assertion.

This is a real behavioral gap, not just a test gap: for api_key/basic/hmac, invalid credentials
classify as `UNAUTHORIZED`; for token-exchange they classify as `PROBE_ERROR`. The platform UI/operator
sees a different (less actionable) error code for the same class of problem. Whether that matters is a
product call, but the test currently masks the difference rather than documenting it.

Fix (pick one):
- If `PROBE_ERROR` for token-exchange bad creds is acceptable, assert it explicitly
  (`Assert.Equal("PROBE_ERROR", result.ErrorCode)`) and drop/adjust the comment so the next reader
  isn't misled into thinking it mirrors the api_key path.
- If token-exchange bad creds *should* surface as `UNAUTHORIZED` (consistent with siblings), widen
  `ProbeAsync`'s catch to map an auth-stage `InvalidOperationException` carrying a 401/403 to
  `UNAUTHORIZED`, then assert it. (The strategy's exception message embeds the status text but not a
  typed status code, so this needs care — likely a string/`HttpStatusCode` check or a typed exception
  in the strategy.)

I lean toward at least the assert-and-document option; the silent divergence in error codes across
auth types is the kind of thing that bites an operator later.

---

## NIT — unknown `request_body_mode` silently falls back to form-encoded

`CollectorExecutorSessionFactory.cs:98-100`

`ParseEnum` returns the fallback (`FormUrlEncoded`) for any unrecognized value. A profile author who
writes `request_body_mode: jsonn` (typo) or `request_body_mode: form` gets form-encoded silently. For
a vendor like Guardicore that requires JSON, this is a silent misbehavior that only shows up as a
failing token call at runtime, with no hint that the cause was a typo'd mode. This is consistent with
how every sibling builder uses `ParseEnum` (hmac does the same for ~8 params), so it's a pre-existing
design stance, not a regression — flagging only because the JSON-vs-form distinction here is
load-bearing for whether auth works at all. No change required for consistency; if you want defense,
it belongs as a profile-load validation across all `ParseEnum` sites, not a one-off here.

## NIT — `grant_type: ""` suppression is correct but the empty-string convention is subtle

`CollectorExecutorSessionFactory.cs:105-107`

The three-way logic is sound and matches the documented intent:
- key absent -> `"password"` (config default, standard OAuth password grant)
- key present but blank -> `null` (field omitted; the strategy's `AddIfHasValue` drops null/blank, so no
  `grant_type` is sent — matches Guardicore's native `GrantType=null`)
- key present with value -> that value

Verified against the strategy: `BuildTokenRequestFields` -> `AddIfHasValue(body, GrantTypeFieldName,
GrantType)` only adds when non-blank (`UsernamePasswordTokenExchangeAuthentication.cs:219,349-353`), so
`GrantType=null` genuinely omits the field. The mock asserts `b?["grant_type"] is null`
(`CollectorExecutorTests.cs:152`), which faithfully checks the absence. Good.

The subtlety: "empty string means suppress, absent means default" is the opposite of the intuitive
reading (one might expect empty == default). It's documented in both the code comment and
yaml-contract.md, so it's acceptable — just noting it is a convention a profile author must know.

---

## Things checked and found correct

- **YAML -> config mapping**: `token_url` templated with `{{config.base_url}}` exactly like the oauth2
  builder (line 84-86 vs 46-49); `username`/`password` pulled from `credentials` only, never YAML
  (matches the secret-handling invariant); `access_token_property_name`/`authorization_scheme` default
  correctly to `access_token`/`Bearer`. Init-only `required` props on the config are all satisfied.
- **Fail-closed**: missing username, password, or token_url all return `null`
  (lines 88-91) -> `BuildAuthSelection` returns null -> `ProbeAsync`/`RunAsync` return
  `INVALID_PAYLOAD`. Consistent with basic/api_key/hmac null-return discipline.
- **`request_body_mode` parsing**: `ParseEnum` strips underscores, so `json` -> `Json` and
  `form_url_encoded` -> `FormUrlEncoded` both resolve against the 2-value enum. Correct.
- **Mock fidelity**: `/gc/authenticate` requires a JSON content-type, a JSON body with
  `username`/`password`, and asserts `grant_type` is absent — this matches exactly how the real
  strategy's `BuildJsonContent` serializes the body (`JsonSerializer.Serialize` of the field dict,
  `application/json`). The happy-path test then asserts the exchanged token is carried as
  `Authorization: Bearer gc-tok` on `/gc/assets` (line 161), so the full exchange -> bearer flow is
  genuinely exercised, not stubbed. The 3-record short page correctly terminates offset paging after
  page 1.
- **guardicore.yaml**: `status: "on"` is quoted, so no YAML 1.1 boolean coercion of `on`. Templating
  tokens (`{{offset}}`, `{{page_size}}`, `{{config.base_url}}`) are the standard ones; `records_path:
  objects` matches the documented native shape; offset strategy + `page_size: 100` is coherent.
  Assets-only with no findings stream is fine (header documents why). Mirrors the inline test profile.
- **Docs**: `yaml-contract.md:25-27,37` accurately describe the wired type, the `token_url` templating,
  the `params` defaults, the empty-string grant_type suppression, and the `username`/`password`
  credential convention. No inaccuracies.
