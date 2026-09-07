# Constraints

- `ValidateConfigurationAsync` must do a REAL probe: build the session (auth) + one capped request; never return Success unconditionally. Fail CLOSED when no yaml is supplied.
- Wire validation on BOTH the adapter method (CollectorExecutorAdapter:100) and the bus-entrypoint delegate (:165); retain config via `SetConfiguration` (:95 + delegate :164).
- The probe is read-only: page_size→1, the first stream's first request, NO egress/publish, no checkpoint write.
- dry-run must be honored end-to-end: read `IsDryRun` off the request the bus already passes (delegates :172/173 currently discard it); thread it through `RunAndCountAsync`/`RunAsync`/step loop.
- dry-run = real-but-tiny: still auth→request→map→publish, but capped (page_size→1, stop after the first emitting page per stream / first emitting step in a multi-step flow).
- dry-run must NOT poison a real run: do not persist a checkpoint that a later real run would resume from incorrectly.
- Generic engine: runner stays thin orchestration (Execution/README.md); no vendor branches; vendor shape stays in YAML.
- Reuse Shared session/auth/egress + the failure classifier; no bespoke transport/resilience.
- No regression: passthrough-only emit, envelope feature, poll-and-drain, SDK conformance (bus routing/ResumeAsync/per-emit-target page counter/RUN-envelope ingress), reserved `__`-guard, pagination. Full suite green.
- net8.0; CollectorBase.slnx; async-only.
