# Constraints

- Read-only audit. No code changes during this task. Must-fix findings are surfaced, not applied.
- Ground EVERY claim in actual source with `file:line` citations. No speculation; if unverifiable, say UNKNOWN.
- Native collectors are the reference of record (Falcon, TenableIo, Qualys, DefenderVm, CortexXdr) under
  `/Users/user/Dev/cymulate-integration-adapters/src/Cymulate.Integration.Adapters/Collectors/`.
- The cross-verifier must be adversarial — actively hunt for where CollectorExecutor only *looks* conformant;
  not a rubber stamp. Re-test each conformance claim against the real code, not the prior task's notes.
- The three code-reviewers must be genuinely isolated: minimal context only (the CollectorExecutor source).
  Do NOT pass them the prompt contract, the prior task's verifier/notes, the orchestration plan, or any
  "this passed verification / satisfies requirement" framing.
- Cover every Shared subsystem: bus entrypoint wiring, resume, session/transport, egress + page numbering,
  resilience (failure policies + recovery budget), events (ICollectorEventSink), credential/RUN-envelope
  ingestion, checkpoint/page-number contract.
