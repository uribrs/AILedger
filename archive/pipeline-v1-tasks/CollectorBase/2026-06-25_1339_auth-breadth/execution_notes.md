# Execution Notes — auth breadth

## Outcome
Shipped ONE new auth case + a proof vendor. Disciplined scope: the two other targets are documented gaps,
not hacks. Build clean; `dotnet test CollectorBase.slnx` → **65/65** (63 prior + 2 new).

## What changed
- `CollectorExecutor/Execution/CollectorExecutorSessionFactory.cs` — new `case
  "username_password_token_exchange"` + `BuildUsernamePasswordTokenExchangeAuth(...)` →
  `AuthSelection.UsernamePasswordTokenExchange`. Mirrors the oauth2 case (token_url templated with
  `config.base_url`; secrets username/password from credentials). Fail-closed: returns null if
  username/password/token_url missing. `grant_type: ""` → null (suppress), absent → default `"password"`.
  `request_body_mode` via the existing `ParseEnum` helper.
- `integrations/guardicore.yaml` — NEW proof profile (assets-only; offset paging; records_path `objects`),
  every auth/endpoint choice cited to native source.
- `Tests/CollectorExecutor.Test/CollectorExecutorTests.cs` — 2 new tests + `GuardicoreProfile` const +
  two mock endpoints (`/gc/authenticate` validates JSON body {username,password} with NO grant_type → access_token;
  `/gc/assets` requires the exchanged Bearer, offset paging, list at `objects`):
  - `UsernamePasswordTokenExchange_FetchesTokenAndAppliesBearer_Emits` → Success, 3 records.
  - `UsernamePasswordTokenExchange_BadCreds_FailsClosed` → wrong password → token 401 → probe IsValid false.
- `CollectorExecutor/docs/yaml-contract.md` — auth table: new `username_password_token_exchange` type + its
  params; noted `bearer` is unneeded (== api_key+header_prefix); secret-by-convention entry added.
- `ai/active/.../auth-note.md` — full mapping table + per-vendor trace (file:line) + deferred/excluded set.

## Assumption resolutions used (from research, all confirmed against on-disk source)
- A1 VALIDATED — http.package 2.0.2 `AuthSelection` has 10 variants; UsernamePasswordTokenExchange present
  with full runtime plumbing. Source: `/Users/user/Dev/Cymulate.Http.Package/Authentication/Contracts/Models/AuthSelection.cs`
  + `UsernamePasswordTokenExchangeConfiguration.cs` (required TokenEndpoint/Username/Password; GrantType default
  "password"; RequestBodyMode default FormUrlEncoded; scheme Bearer) + the strategy
  `Logic/Strategies/UsernamePasswordTokenExchangeAuthentication.cs` (POST JSON/form {username,password,…},
  reads `access_token`, applies `<scheme> <token>`, 401/403 force-refresh).
- A2 VALIDATED — Guardicore=UPTE, ServiceNow=TokenExchange+Basic-on-token, SentinelOne=Negotiated (cited in auth-note).
- A3 VALIDATED — `ApiKeyAuthenticationConfiguration` has HeaderName+HeaderPrefix → no `bearer` case needed.
- A4 REJECTED — AwsSigV4 not a Shared variant; excluded.
- A5 VALIDATED — Guardicore is the clean single-case unlock; shipped.

## Residual gaps (documented, not built)
- **token_exchange / ServiceNow** — TokenExchangeAuthenticationConfiguration has no field to auth the token
  request itself; native uses an HttpClient.DefaultRequestHeaders Basic side-channel (barred from the generic
  engine). No standalone consumer once that's barred → not wired (would be speculative). See auth-note.md.
- **negotiated / SentinelOne** — composite meta-auth (candidates+probes+nested selection); expands the YAML
  schema beyond one case → deferred. Candidate-2 is already api_key-expressible today.

## Commands
- `dotnet build CollectorBase.slnx` → clean (only pre-existing NU1507 source-mapping warnings).
- `dotnet test CollectorBase.slnx` → 65/65.

## No-regression spot check
Full suite green, including passthrough/envelope (Verbatim/TypedWrapper/SourceTypePrefix), poll-and-drain
(Pad* tests), fetch fail_if (Fetch_FailIf_BodyError_FailsTheStream), and the validate probe (Validate_* tests).
