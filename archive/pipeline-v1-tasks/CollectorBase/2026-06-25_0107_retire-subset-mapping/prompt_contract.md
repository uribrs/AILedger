Role:
You are a senior .NET engineer simplifying the generic CollectorExecutor engine to a passthrough-only
emit model, retiring field-subset mapping without regressing the rest.

Goal:
Make the engine always publish the FULL record (what is gotten is published); the request shapes the data,
the response content is never picked apart. The only per-stream shaping knob is the envelope. Retire the
`mapped` emit mode, field projection, and correlation-key injection entirely. Convert all shipped profiles,
update tests and the YAML contract doc.

Context:
- Repo: /Users/user/Dev/Uri/localprojects/CollectorBase (net8.0; CollectorBase.slnx). Runner was refactored
  into helpers (see CollectorExecutor/Execution/README.md) — runner = step loop; mechanics in
  CollectorExecutorStepHelpers; prep/session/checkpoint in their own helpers.
- Emit/mapping today spans:
  - Interpreter/Interpreter.cs `ResponseMapper.Map` — mapped-vs-passthrough branch, Fields projection,
    sourceType injection, correlation injection.
  - Profile/Profile.cs `MappingSpec` (EmitMode default "mapped", Fields, TypeValue/WrapperKey/DataKey/
    EnvelopeFields), `CorrelationKeys` (StepSpec + StreamSpec), `ValidateMapping`.
  - Execution/CollectorExecutorStepHelpers.cs `ApplyEnvelope` (keep) + the synthetic single-fetch step
    builder (sets CorrelationKeys).
  - Strategies/Mapping/DotPathMapper.cs (IRecordMapper seam → ResponseMapper.Map).
  - Runner `mapper.Map(...)` call sites (query + hydrate).
- Native parity reference (read-only): Tenable verbatim, Defender envelope (DefenderVmRecordFormatter),
  Cortex stamps sourceType (CortexXdrRecordFormatter: va_cves/endpoint/...).

Constraints:
- See constraints.md (authoritative). Passthrough-only; envelope is the sole knob; remove mapped/Fields/
  correlation injection; keep envelope mechanism + IRecordMapper seam; runner stays thin; no regression of
  poll-and-drain / SDK conformance / reserved `__`-guard; net8.0; async-only.

Success Criteria:
1. `dotnet build CollectorBase.slnx` clean.
2. `dotnet test CollectorBase.slnx` all pass — mapped-subset tests removed or rewritten to assert FULL-record
   passthrough; envelope tests (verbatim/typed_wrapper/source_type_prefix/collision/templated-metadata) and
   validation tests intact/updated for the new default + removed `mapped`.
3. No `mapped` mode, no Fields projection, no correlation injection anywhere in the emit path; emit_mode
   default = verbatim; `ResponseMapper.Map` always emits the full record element.
4. All 5 shipped integrations/*.yaml validate and emit full records (verbatim, or envelope where the native
   stamps/wraps — Defender envelope kept, Cortex source_type_prefix); dead `fields:`/`correlation_keys:` stripped.
5. CollectorExecutor/docs/yaml-contract.md updated (mapping = records_path + envelope only). No regression:
   poll-and-drain + conformance wiring intact (full suite green).

Execution Rules:
- Resolve OPEN assumptions A2–A5, A7 against the code during execution (Cortex/Falcon/Qualys native shapes;
  loader IgnoreUnmatchedProperties; seam-signature simplification cleanliness; test rewrite).
- Keep the runner thin (README boundary); do not add vendor branches.
- Respect constraints strictly; async-only.

Output Format:
- Code: Interpreter/Interpreter.cs, Profile/Profile.cs, Execution/CollectorExecutorStepHelpers.cs (+ runner
  call-site edits), Strategies/Mapping/DotPathMapper.cs, integrations/*.yaml, Tests/CollectorExecutor.Test,
  CollectorExecutor/docs/yaml-contract.md. execution_notes.md updated; state.json steps in sync.

Stop Conditions:
- All success criteria met and verified.
- Removing a piece would force a vendor branch into the runner or break a hard invariant (stop, surface).
- An OPEN assumption resolves against a constraint (stop, surface).
