# Constraints

- New auth flow = one `case` in `CollectorExecutorSessionFactory.BuildAuthSelection` (`:36`); no other dispatch site.
- No vendor identity / no vendor branch in the engine — all shape declared in YAML.
- Reuse Shared session/auth (`http.package` `AuthSelection`) only — no bespoke transport, no new auth in Shared.
- Secret VALUES come from dispatch `credentials` by convention; YAML carries only non-secret shape (`type`, header names, placement).
- Any new auth type validated in Profile auth-shape validation; snake_case via YamlDotNet.
- Add ONLY auth types that a TARGET vendor actually uses AND Shared can already express.
- Anything Shared cannot express → report as a gap; do NOT invent transport or extend Shared.
- No regression: passthrough-only emit, envelope modes (verbatim/typed_wrapper/source_type_prefix), poll-and-drain, fetch `fail_if`, `ValidateConfigurationAsync` probe, SDK conformance (bus routing / ResumeAsync / per-emit-target page counter / RUN-envelope ingress), reserved `__`-capture guard, pagination strategies.
- net8.0; solution `CollectorBase.slnx`; async-only.
- Tests use the existing in-process harness (FakeHttpClientFactory + captured IAdapterExecutionContext); nothing stubbed at transport.
