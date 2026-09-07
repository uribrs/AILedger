# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: one coherent simplification (delete a code path + its config + its tests) concentrated in
  ResponseMapper + MappingSpec/validation + the synthetic-step builder + DotPathMapper + the 5 profiles +
  tests + one doc. Tightly coupled (the emit shape change ripples through all of them); decomposition would
  only add handoff overhead.

## Research Decisions
- None needed. All local. Native parity reference (Tenable/Defender/Cortex formatters) already surveyed this
  session. OPEN assumptions A2–A5/A7 are internal-code questions (Cortex/Falcon/Qualys native shape, loader
  IgnoreUnmatchedProperties, seam-signature cleanliness, test rewrite) resolved by reading local code.

## Worker Plan
Not applicable — direct path.

## Execution order (within contract-driven-execution)
1. `ResponseMapper.Map` → always emit the full element; delete the mapped branch + Fields/sourceType/
   correlation injection. Simplify the seam signature (IRecordMapper + DotPathMapper + runner calls) if clean.
2. `MappingSpec` — remove `Fields`; `EmitMode` default → "verbatim". Remove `CorrelationKeys` (StepSpec +
   StreamSpec) + the synthetic-step builder assignment. `ValidateMapping` — new default + valid set, drop the
   mapped/correlation requirement; keep type_value/envelope_fields rules.
3. Profiles — strip dead `fields:`/`correlation_keys:`; falcon/qualys verbatim; cortex source_type_prefix
   (glance CortexXdrRecordFormatter for discriminator values) or verbatim; tenable/defender already done.
4. Tests — remove/rewrite mapped-subset assertions to full-record passthrough; update validation tests for
   the new default + removed `mapped`; keep envelope tests. Build + full suite green.
5. yaml-contract.md — mapping = records_path + envelope only; "always passthrough — request shapes the data".

## Verification Obligations
- Cross-check the 5 Success Criteria.
- Grep-prove: no `mapped`, no `Fields` projection, no correlation injection in the emit path; default verbatim.
- No regression: poll-and-drain + SDK conformance (bus/resume/page-counter/ingress) + reserved `__`-guard;
  all 5 profiles validate.
- Verifier subagent (full context) then isolated code-reviewer subagent (changed files only).
