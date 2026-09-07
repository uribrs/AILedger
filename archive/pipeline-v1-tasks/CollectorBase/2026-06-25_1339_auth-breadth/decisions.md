# Decisions

- BuildAuthSelection lives in `CollectorExecutorSessionFactory.cs:36`, not the runner — add cases there. (Brief said "runner"; corrected.)
- Add auth types strictly gated on: target-vendor-uses-it AND Shared-AuthSelection-can-express-it. Both must hold.
- Speculative auth types (a flow no target vendor uses) are NOT built, even if Shared supports them.
- Auth types Shared cannot express are reported as a gap; Shared is not extended in this task.
- Secrets always from `credentials`; YAML never carries secret values — non-negotiable.
- Proof-of-unlock = a real integrations/*.yaml profile for a target vendor, auth verified vs native source with file:line.

## Post-research decisions (A1–A5 resolved against code)
- PRIMARY DELIVERABLE = Guardicore via a new `username_password_token_exchange` case
  (`AuthSelection.UsernamePasswordTokenExchange`). Cleanest single case, fully Shared-expressible,
  secrets (username/password) from credentials. This is the proof vendor + its integrations/*.yaml.
- SECONDARY (wire if clean): ServiceNow via a new `token_exchange` case
  (`AuthSelection.TokenExchange`, grant_type=client_credentials, form-encoded). MUST resolve the
  Basic-on-token-endpoint wrinkle: check whether `TokenExchangeAuthenticationConfiguration` can carry
  the Basic credential for the token request (e.g. via a field), OR whether client creds can ride
  `RequestFields` (form body) and still authenticate. If neither is declaratively expressible without
  an HttpClient side-channel, DO NOT hack a side-channel into the generic engine — document ServiceNow
  as a PARTIAL gap (token_exchange wired generically; ServiceNow's Basic-on-token specifically deferred).
- DEFER SentinelOne `negotiated`: it is a COMPOSITE meta-auth (a candidates list, each with a probe +
  nested AuthSelection). Wiring it generically expands the YAML auth schema well beyond "one more case"
  and risks scope creep. Record as a documented follow-up gap (Shared supports it; our YAML mapping
  doesn't yet) UNLESS it falls out trivially. Note SentinelOne's candidate-2 (ApiKey ApiToken) is
  already api_key-expressible today — so a degraded SentinelOne profile is possible without negotiated.
- No new `bearer` case — A3 proves static bearer is already api_key+header_prefix.
- AwsSigV4 excluded — not a Shared variant.
