Role:
You are an independent verification lead auditing a YAML-driven collector against the native Cymulate
integration-adapters collectors and the Shared substrate contract.

Goal:
Independently confirm or refute that CollectorExecutor (and, for contrast, YamlCollector) conforms to
the way the native collectors consume Shared — grounded in source, adversarial, read-only — and obtain
three independent code reviews of the CollectorExecutor implementation.

Context:
- Executor under audit: /Users/user/Dev/Uri/localprojects/CollectorBase (CollectorExecutorAdapter.cs;
  CollectorExecutor/{Execution,Checkpointing,Seams,Profile,Interpreter,Composition}; Strategies/).
- Native reference + YamlCollector + Shared: /Users/user/Dev/cymulate-integration-adapters/src/
  Cymulate.Integration.Adapters/Collectors/{FalconCollector,TenableIoCollector,QualysCollector,
  DefenderVmCollector,CortexXdr*,YamlCollector} and the Shared library (same code in both repos).
- Recently-asserted conformance claims to re-test (treat as UNVERIFIED): full interface set; ProcessAsync
  via CollectorBusEntrypointDefinitionBuilder + DelegateCollectorBusEntrypointSource (13 delegates) ->
  AdapterBusEntrypointRunner; ResumeAsync via CollectorResumeRunner + CollectorResumeDefinition; per-emit-
  target monotonic byte-sliced page counter persisted in CheckpointState; RUN-envelope ingress
  (RunPayloadCredentialHydrator + action.lastRanAt floor); pre-flight validation; no-op flow-retry pipeline
  + DelegateAdapterFailurePolicy preserving the engine's real error codes.

Constraints:
- See constraints.md (authoritative). Read-only; cite file:line; no speculation; isolated code-reviewers;
  adversarial cross-verifier.

Success Criteria:
1. native-reference baseline doc in the task dir, grounded in the native collectors with citations.
2. cross-verifier report (review/) comparing YamlCollector AND CollectorExecutor against the baseline per
   Shared subsystem, explicitly listing any "looks-conformant-but-isn't" findings (or none, with evidence).
3. three independent code-review reports (review/code-reviewer-1..3.md) on the Executor, each with concrete
   file:line findings + severity.
4. a synthesis separating real conformance gaps / material code findings from minor / accepted items.

Execution Rules:
- Do not assume; verify against source. Treat prior task claims as unverified.
- Respect constraints strictly. Code-reviewers get minimal context only.

Output Format:
- review/native-reference-baseline.md (or equivalent), review/cross-verifier-1.md,
  review/code-reviewer-1.md, code-reviewer-2.md, code-reviewer-3.md, and a synthesis (in the orchestration
  notes or a synthesis section). state.json kept in sync.

Stop Conditions:
- All four success criteria met.
- A required reference source is missing (note it; proceed with what exists).
- A finding would require a code change (record it; do not apply — read-only).
