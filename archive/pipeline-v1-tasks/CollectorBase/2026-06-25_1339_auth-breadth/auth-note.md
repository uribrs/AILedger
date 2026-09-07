# Auth breadth — declared-type → AuthSelection mapping, per-vendor trace, excluded/deferred set

## Shipped this task
One new case in `CollectorExecutorSessionFactory.BuildAuthSelection`:
`username_password_token_exchange` → `AuthSelection.UsernamePasswordTokenExchange`.
Proof profile: `integrations/guardicore.yaml` (Guardicore, assets-only). 65/65 tests green (+2).

## Declared auth type → http.package AuthSelection (wired)
| YAML `auth.type` | AuthSelection variant | Secrets (from credentials) | Non-secret YAML shape |
|---|---|---|---|
| `oauth2_client_credentials` | `OAuth2` | client_id, client_secret | token_url, scopes |
| `api_key` | `ApiKey` | api_key/access_key [+secret_key] | header_name, header_prefix, use_query_parameter, query_parameter_name, combine_as_access_secret_pair |
| `basic` | `Basic` | username, password | — |
| `hmac` | `Hmac` | api_key(secret) [+api_key_id] | placement, algorithm, encoding, string_to_sign, headers, … |
| **`username_password_token_exchange`** (NEW) | **`UsernamePasswordTokenExchange`** | username, password | token_url, request_body_mode, access_token_property_name, authorization_scheme, grant_type |

`grant_type` convention: config default is the standard `"password"` grant; an explicit empty value
(`grant_type: ""`) suppresses the field entirely — matching Guardicore (native sets `GrantType=null`).

## Per-target-vendor auth trace (monorepo /Users/user/Dev/cymulate-integration-adapters/)
- **Guardicore = UsernamePasswordTokenExchange** — POST `<base>/api/v3.0/authenticate` JSON `{username,password}`,
  GrantType=null, AccessTokenPropertyName=`access_token`, scheme Bearer.
  `GuardicoreCollectorConfigurationBuilder.cs:53-81`; `GuardicoreUrls.cs:5` (`/api/v3.0/authenticate`),
  data at `/api/v3.0/assets` (`BuildAssetsQuery` → `status=on&offset&limit&sort=-first_seen`), list at `objects`
  (`GuardicoreApiClient` response). **SHIPPED** as `integrations/guardicore.yaml`.
- **ServiceNow CMDB = TokenExchange + Basic-on-token-endpoint** — POST `<instance>/oauth_token.do`
  form-urlencoded `grant_type=client_credentials`, scheme Bearer, **plus** `Authorization: Basic
  base64(clientId:clientSecret)` set on `HttpClient.DefaultRequestHeaders` to authenticate the token
  request itself. `ServiceNowCmdbCollectorConfigurationBuilder.cs:58,64-75,80`; `ServiceNowUrls.cs:21`.
  **DEFERRED — gap (see below).**
- **SentinelOne = Negotiated (2 candidates + probe)** — candidate 1 `TokenExchange` login
  POST `/web/api/v2.1/users/login/by-api-token` (nested JSON, AccessTokenPropertyPath `data.token`,
  scheme `Token`); candidate 2 `ApiKey` `Authorization: ApiToken <apiToken>`; probe
  `POST /web/api/v2.1/threats?limit=1`. `SentinelOneCollectorConfigurationBuilder.cs:80-124`;
  `SentinelOneUrls.cs:11,13-14`. **DEFERRED — gap (see below).**

## Deferred — buildable, not impossible (corrected 2026-06-25)
- **token_exchange / ServiceNow** — CORRECTION of an earlier wrong call. `TokenExchangeAuthenticationConfiguration`
  has no field to authenticate the token request itself, but the native does NOT need one: it uses
  `SessionSpec.ConfigureClient` (a delegate) to set `Authorization: Basic base64(clientId:clientSecret)` as a
  default header for the `oauth_token.do` POST, alongside `AuthSelection.TokenExchange` with
  `RequestFields={grant_type:client_credentials}`. Source:
  `ServiceNowCmdbCollectorConfigurationBuilder.cs:58,63-80`. `ConfigureClient` is a SUPPORTED, first-class
  `SessionSpec` member — and OUR Shared exposes it: `Shared/Cymulate.Integration.Adapters.Shared/Session/SessionSpec.cs:68`
  (`public Action<HttpClient>? ConfigureClient { get; init; }`). So it is NOT an "engine side-channel the
  constraints forbid" (earlier reasoning was wrong) — it's a sanctioned seam our engine already has.
  Therefore ServiceNow IS expressible cleanly: a `token_exchange` case + a NARROW declared capability that
  builds a `ConfigureClient` delegate setting a Basic default header from credentials (e.g. auth param
  `token_endpoint_auth: basic`). Generic, secrets-from-credentials, no vendor branch. Deferred only because it
  needs a deliberate seam (a named "session default headers / token-endpoint Basic" capability) — `ConfigureClient`
  must be exposed as a NARROW declared option, never a general "run arbitrary code" hook (that would be
  inner-platform creep). Bigger than Guardicore's one-case unlock, but well within the architecture.
- **negotiated / SentinelOne** — `AuthSelection.Negotiated` is a composite meta-auth: a candidates list,
  each with a probe endpoint + a nested `AuthSelection`. Wiring it generically expands the YAML auth schema
  well beyond "one more case" (candidates, probes, nested selections) — scope creep for this task. NOT wired.
  Note: SentinelOne's candidate-2 (`Authorization: ApiToken <apiToken>`) is ALREADY expressible today as
  `api_key` (header_name=Authorization, header_prefix=ApiToken) — a degraded SentinelOne profile (static
  token, no negotiated fallback/login) is possible now without new code.

## Excluded (not available in Shared at all)
- **AwsSigV4** — NOT among the 10 `AuthSelection` variants in `Cymulate.Http.Package.Authentication` 2.0.2
  (OAuth2, ApiKey, Basic, Hmac, Bearer, Jwt, TokenExchange, UsernamePasswordTokenExchange, Negotiated, None).
  No Shared transport for it; also matches yaml-contract "Explicitly EXCLUDED" (custom auth beyond AuthSelection).

## Accepted minor: bad-creds classification divergence
A token-exchange auth failure throws at the AUTH stage (token endpoint 401 → `InvalidOperationException`
from the http.package strategy) BEFORE any data request, so the probe's catch-all classifies it as
`PROBE_ERROR` — whereas a data-request 401 (api_key/basic) classifies as `UNAUTHORIZED`. Both report
`IsValid=false` (the operationally meaningful result). Making them uniform would require either string-
sniffing a status code out of the exception message (fragile) or a Shared change (out of scope), so the
divergence is accepted and the test asserts the real `PROBE_ERROR`. Revisit if the probe ever needs a
uniform credential-failure code across auth flows.

## Note on auth-type validation
No whitelist was added to `ProfileLoader` — consistent with the 4 pre-existing types, auth-type handling is
fail-closed at session build (`BuildAuthSelection` returns null for an unknown/under-specified type → null
session → the run fails closed, surfaced by the same path the other types use).
