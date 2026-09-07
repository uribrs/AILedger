# Task: Conversation carry (Session layer)

Carry the Conversation concern into IntegrationInfra, behavior verbatim (namespace-only rewrite +
resolved dependency rewires). Conversation = "hold an authenticated, resilient HTTP conversation with a
vendor API" — the Session layer + reclaimed session lifecycle + 2 helpers. The leaky hybrid concern; the
carry itself is mechanical relocation + rewires.

## In scope (carry → src/IntegrationInfra/Conversation/)
- `Session/*.cs` (18 files: AdapterHttpClient, AdapterStreamResponse, CredentialProvider* (4),
  CredentialTokenCache, DefaultSessionFactory, DefaultSessionProvider, ISessionFactory, SessionHandle,
  SessionRetryDefaults, SessionSpec, SessionTelemetry, StreamedResponseBodyReader)
- `Session/HttpCertValidationHandler/*.cs` (3)
- Reclaim `Orchestration/AdapterSessionLifecycle.cs`
- Helpers: `RateLimiterHelper.cs`, `HttpStatusExtractor.cs`

## Excluded / out of scope
- `Session/TransportErrorHandling/*` (already in Kernel.Transport) — rewire, do NOT re-carry.
- `Session/LogRedaction.cs` (already in Kernel.Redaction) — rewire, do NOT re-carry.
- `CreatePipeline` (deferred Polly part of old UnknownFlowRetryPolicy) — consumers are Conducting.
- Indicator-domain helpers (IocTypeDetector, PrivateIpDetector); BatchProcessingHelper (Kernel).
- Any behavior change; any new facade over the http.package substrate.

## Deliverables
1. Verbatim relocation (logic) under Conversation/, namespaces rewritten.
2. Kernel rewires (Transport/Redaction/Telemetry); reclaim + helpers in place.
3. Added PackageReferences if needed (Authentication / Sdk.Query) so it builds.
4. XML docs on public members; Conversation/README.md extended.
5. Tests for the genuinely-testable pure units.
6. `dotnet build` clean; tests pass.
