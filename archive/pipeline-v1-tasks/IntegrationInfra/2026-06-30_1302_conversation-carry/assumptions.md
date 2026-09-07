# Assumptions

- **A1 (VALIDATED):** Conversation's source files are the Session layer + AdapterSessionLifecycle +
  RateLimiterHelper/HttpStatusExtractor. Confirmed by survey of the source tree + the concern README.

- **A2 (VALIDATED):** `TransportErrorHandling/*` and `LogRedaction.cs` are already in Kernel (commits
  9630194 / 72292d5), so they are rewired, not re-carried. `DataPipelineTelemetry` is in Kernel.Telemetry.

- **A3 (VALIDATED):** The http.package / DefensiveToolkit / Authentication coupling is by-charter
  (Conversation depends on http.package). It is not a defect to fix in this carry. Confirmed by README
  altitude boundary + the leak census.

- **A4 (VALIDATED):** `CreatePipeline` (deferred Polly part of the old UnknownFlowRetryPolicy) is consumed
  by Orchestration/Conducting, not Conversation — out of scope. Executor confirms no Conversation reference.

- **A-pkg (OPEN — build-driven, non-blocking):** Conversation references
  `Cymulate.Http.Package.Authentication.Contracts.*` and `Cymulate.Integration.Sdk.Query.*`. Whether these
  resolve transitively (via the existing Http.Package.Session / Integration.Sdk references) or need an
  explicit PackageReference is resolved by the build. If a package or its floor-resolved version cannot be
  obtained, STOP and surface (do not invent a version).

- **A-test (OPEN — scope detail, non-blocking):** Test coverage is realistically narrower than
  FaultGovernance because Session creation/auth is heavily http.package-coupled. Target the pure/testable
  units (RateLimiterHelper, HttpStatusExtractor, SessionSpec defaults, any pure logic). Reuse or add a
  Conversation test project — executor picks the simplest that builds. Do NOT force brittle integration tests.

- **A-fragile (VALIDATED → constraint):** SessionHandle dispose ordering must be verbatim (see constraints).
