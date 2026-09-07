# Verifier-1 — auth-breadth

Independent verification. All claims checked against actual files / native source; tests run live.

## SC1 — `dotnet build CollectorBase.slnx` clean — PASS
`Build succeeded. 0 Error(s)` (only pre-existing NU1507 package-source-mapping warnings, unrelated to this change).

## SC2 — tests: existing + new per-auth-type — PASS
Ran `dotnet test CollectorBase.slnx`: `Passed! Failed: 0, Passed: 65, Skipped: 0, Total: 65`.
Baseline was 63; +2 new tests = 65. Confirmed real, not taken on faith.
New tests: `UsernamePasswordTokenExchange_FetchesTokenAndAppliesBearer_Emits` and
`UsernamePasswordTokenExchange_BadCreds_FailsClosed` (CollectorExecutorTests.cs:843-873).

## SC3 — every added type vendor-used AND Shared-expressible; rest reported as gap — PASS
One case added: `username_password_token_exchange` → `AuthSelection.UsernamePasswordTokenExchange`
(SessionFactory:64-65, 76-110). Variant exists in Shared (AuthSelection.cs:12;
UsernamePasswordTokenExchangeConfiguration.cs). Guardicore actually uses it (native builder:57-65).

Deferrals checked against source, NOT cut corners:
- **ServiceNow / token_exchange** — native sets `Authorization: Basic base64(clientId:clientSecret)`
  on `HttpClient.DefaultRequestHeaders` to auth the token request itself
  (ServiceNowCmdbCollectorConfigurationBuilder.cs:58,80). `TokenExchangeAuthenticationConfiguration`
  (verified:5-43) has NO field to authenticate the token endpoint — only TokenEndpoint, RequestFields
  (body), AccessTokenPropertyPath, AuthorizationScheme. Expressing it generically would need an
  HttpClient side-channel in the engine — forbidden by constraints. Justified gap.
- **SentinelOne / negotiated** — native uses `AuthSelection.Negotiated` = candidates list, each with a
  nested AuthSelection + probe endpoint (SentinelOneCollectorConfigurationBuilder.cs:85-123). Wiring it
  is a multi-field schema expansion, not "one more case" — out of scope. Justified deferral. Note in
  auth-note.md that candidate-2 (`Authorization: ApiToken`) is already expressible as `api_key` today
  is accurate.

## SC4 — at least one previously-unexpressible native vendor now works as YAML, auth verified — PASS
`integrations/guardicore.yaml` is a working profile. Auth verified line-by-line vs native:
- token_url `/api/v3.0/authenticate` = GuardicoreUrls.cs:5 (`Authenticate`). MATCH.
- UsernamePasswordTokenExchange, RequestBodyMode=Json, AccessTokenPropertyName=`access_token`,
  scheme Bearer = native builder:57-65. MATCH.
- `grant_type: ""` → engine maps empty to `null` (SessionFactory:105-107) = native `GrantType=null`
  (builder:63). Faithful.
- assets path `/api/v3.0/assets`, query `status=on&offset&limit&sort=-first_seen` = GuardicoreUrls.cs:9-10. MATCH.
- records_path `objects`, offset paging = matches native assets response/query. MATCH.

## SC5 — no regression — PASS
Full suite green (65/65). Pre-existing feature tests (passthrough, poll-and-drain, fail_if, validate
probe, hmac, api_key, basic, oauth2) all present and passing in the same run; only additive changes to
the mock handler (new `/gc/*` endpoints) and 2 new tests.

## SC6 — auth-note.md present with mapping table, per-vendor file:line, excluded+deferred — PASS
auth-note.md has: declared-type→AuthSelection table (5 rows incl. new one), per-vendor traces with
file:line for Guardicore/ServiceNow/SentinelOne, deferred set (token_exchange, negotiated) with
reasons, and excluded set (AwsSigV4 — not among the 10 AuthSelection variants). Cross-checked each
cited line; all accurate.

## Constraint compliance
- Secrets only from credentials: username/password read from `credentials` (SessionFactory:82-83);
  YAML carries only token_url + non-secret params. No secret in guardicore.yaml. PASS.
- No vendor branch in engine: case is a generic auth-type switch; no "if guardicore"/vendor identity. PASS.
- Reuse Shared AuthSelection only: uses `AuthSelection.UsernamePasswordTokenExchange`; no Shared edits,
  no HttpClient side-channel, no bespoke transport. PASS.
- Fail-closed: returns null on missing username/password/token_url (SessionFactory:88-91) → null session
  → run fails closed. Bad-creds test confirms (`Assert.False(result.IsValid)`), and the path is genuine —
  the mock token endpoint returns 401 on wrong password, so the exchange fails before any data call. PASS.

## Unjustified gaps / violations
None found. Both deferrals are backed by source evidence (side-channel auth field absent; Negotiated is
a composite schema). The empty-grant_type-suppression trick is faithful to native, not a hack.

## VERDICT: PASS
