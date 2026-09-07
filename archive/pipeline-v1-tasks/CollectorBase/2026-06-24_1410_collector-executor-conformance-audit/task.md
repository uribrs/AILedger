# Task: Self-audit — CollectorExecutor conformance vs native collectors + 3 code reviews

Independently check the just-completed CollectorExecutor SDK-conformance work against the native
collectors as the reference of record. READ-ONLY audit.

## The four parts
1. Read the core native collectors (Falcon, TenableIo, Qualys, DefenderVm, CortexXdr) and determine
   exactly how each consumes Shared (bus entrypoint, resume, session/transport, egress + page
   numbering, resilience + recovery budget, events, credential/RUN-envelope ingestion, checkpoint).
2. Write that as a durable native-reference baseline.
3. One adversarial cross-verifier: check BOTH YamlCollector and CollectorExecutor against the baseline,
   per subsystem — and re-test the recently-asserted conformance claims in the actual code. Hunt for
   places CollectorExecutor only *looks* conformant.
4. Three independent, isolated code-reviewers over the CollectorExecutor implementation.

## Under audit
Prior task: `ai/active/2026-06-24_1244_collector-executor-sdk-conformance/`. Claims to re-test:
full interface set; ProcessAsync via AdapterBusEntrypointRunner (13 delegates); ResumeAsync via
CollectorResumeRunner; per-emit-target byte-sliced monotonic page counter; RUN-envelope ingress
(RunPayloadCredentialHydrator + lastRanAt floor); pre-flight validation; no-double-retry +
classified-failure-code preservation. (Build clean; 43/43 tests; live Tenable 56 files / 55,016 records.)

## Out of scope
No code changes. Must-fix findings are surfaced for separate approval, not applied here.
