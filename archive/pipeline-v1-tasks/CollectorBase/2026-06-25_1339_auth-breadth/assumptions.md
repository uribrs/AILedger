# Assumptions

- A1 — http.package `AuthSelection` variant surface.
  STATUS: VALIDATED. Package `Cymulate.Http.Package.Authentication` 2.0.2
  (~/.nuget/packages/cymulate.http.package.authentication/2.0.2/lib/net8.0/...dll). `AuthSelection`
  is abstract with 10 sealed variants: OAuth2, ApiKey, Basic, Hmac (WIRED in
  CollectorExecutorSessionFactory.cs) + Bearer, Jwt, TokenExchange, UsernamePasswordTokenExchange,
  Negotiated, None (UNWIRED). Runtime plumbing (AuthenticationOrchestrator.Create +
  AuthProviderCollection.BuildXxxProvider) exists for all non-None variants — so all are
  Shared-expressible without touching Shared.

- A2 — target-vendor native auth flows (monorepo /Users/user/Dev/cymulate-integration-adapters/).
  STATUS: VALIDATED.
  - Guardicore = `AuthSelection.UsernamePasswordTokenExchange`: POST <base>/api/v3.0/authenticate,
    JSON {username,password}, GrantType=null, AccessTokenPropertyName="access_token", scheme Bearer.
    Creds: username, password. (GuardicoreCollectorConfigurationBuilder.cs:53-81; GuardicoreUrls.cs:5.)
  - ServiceNow CMDB = `AuthSelection.TokenExchange`: POST <instance>/oauth_token.do, form-urlencoded
    grant_type=client_credentials, AccessTokenPropertyPath="access_token", scheme Bearer — PLUS a
    Basic `clientId:clientSecret` header set on HttpClient.DefaultRequestHeaders to auth the token
    endpoint itself (side-channel). Creds: clientId, clientSecret.
    (ServiceNowCmdbCollectorConfigurationBuilder.cs:58,64-75,80; ServiceNowUrls.cs:21.)
  - SentinelOne = `AuthSelection.Negotiated` (2 candidates, probe POST /web/api/v2.1/threats?limit=1):
    (1) TokenExchange login POST /web/api/v2.1/users/login/by-api-token (nested JSON,
    AccessTokenPropertyPath="data.token", scheme "Token"); (2) ApiKey fallback
    Authorization: ApiToken <apiToken>. Creds: apiToken.
    (SentinelOneCollectorConfigurationBuilder.cs:80-124; SentinelOneUrls.cs:11,13-14.)

- A3 — bearer/static-token == api_key + header_prefix.
  STATUS: VALIDATED. `ApiKeyAuthenticationConfiguration` has `HeaderName` + `HeaderPrefix` (both
  string, already mapped at SessionFactory.cs:87-88). "Authorization: Bearer <token>" =
  api_key with header_name=Authorization, header_prefix=Bearer. No new `bearer` case needed.
  (SentinelOne candidate-2 "Authorization: ApiToken <token>" is likewise already api_key-expressible.)

- A4 — AwsSigV4.
  STATUS: REJECTED (excluded). Not among the 10 AuthSelection variants — not available in Shared at
  all. Confirmed out of scope; record in auth-note as "excluded — no Shared AuthSelection variant".

- A5 — at least one target needs an unwired-but-Shared-expressible variant.
  STATUS: VALIDATED. All three do. Guardicore is the cleanest single-case proof.
