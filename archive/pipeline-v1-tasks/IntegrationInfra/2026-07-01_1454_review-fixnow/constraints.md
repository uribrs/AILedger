# Constraints

- net8.0; RootNamespace Cymulate.IntegrationInfra; branch fix/review-fixnow.
- Fix ONLY S1 + C4 + C1. Preserve all other behavior exactly.
- Verbatim-preservation rule is lifted for these three fixes only (operator-authorized); everything else stays verbatim.
- No new dependencies (LogRedaction, Polly, Moq, xUnit already present).
- S1: scrub at the raise site only; the failure classifier call keeps RAW inputs; do NOT double-scrub the already-scrubbed `LogError` line.
- C4: keep the `DateTime.MinValue` sentinel short-circuit; document the `Unspecified` = assume-UTC contract in XML.
- C1: guard callbacks with try/catch + `LogWarning` + continue; the failure publish must not be gated on callback success. Terminal error/completion publishes use `CancellationToken.None`. Do NOT alter resume-leg retryable-rethrow (M3) or the OnClassifiedFlowException-fires gap (M12).
- Each fix ships with a test; all existing tests stay green.
- Full `IntegrationInfra.slnx` builds clean (0 errors; pre-existing NU1507/NU1900 acceptable); all 7 test projects pass.
- XML docs updated where a contract changes (DateTimeUtc; any scrubbing note).
- Git diff limited to: `Conversation/AdapterHttpClient.cs`, `Kernel/Time/DateTimeUtc.cs`, `Conducting/AdapterBusEntrypointRunner.cs`, `Conducting/Bus/Logic/AdapterBusFailurePublisher.cs`, `Conducting/Collectors/Recovery/CollectorResumeFailurePublisher.cs`, plus the touched test files. `Kernel/Exceptions/AdapterHttpRequestFailedException.cs` only if a ctor change is unavoidable (prefer scrubbing at the call site).
- SOLID, DRY.

## Out of scope (do NOT touch)
- C2 Bus/Resume + NDJSON dedup reshape.
- C3 broad test pass.
- M7 UnknownFlow deny-list (operator-ratified design).
- TLS cert-bypass handler (opt-in; hardening not removal — separate decision).
- RunPayloadCredentialHydrator silent catch (accepted).
- All other single-reviewer minors/nits.
