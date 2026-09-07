# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: one coherent feature concentrated in `Interpreter/Interpreter.cs` (ResponseMapper) +
  `Profile/Profile.cs` + the runner publish wiring + two YAML profiles + tests. The envelope/passthrough
  mechanism feeds BOTH the Tenable and Defender parity edits, so splitting into workers would fragment
  interdependent edits and add integration overhead for no gain. Tight build/test iteration in one context.

## Research Decisions
- None needed. All source is local; native reference (TenableIo{Assets,Findings}ChunkProcessor,
  DefenderVmRecordFormatter, DefenderVmUrlBuilder, DefenderVm{Assets,Findings}Flow) already surveyed this
  session. OPEN assumptions A3–A8 are INTERNAL-contract questions (mapper "keep element" seam, watermark
  read under passthrough, envelope-metadata templating scope, Defender base-date input key, whether native
  base-date/lookback is templatable) resolved by reading local runner/mapper/Profile/Runner code during
  execution — not external research.

## Worker Plan
Not applicable — direct path.

## Execution order (within contract-driven-execution)
1. Read the mapper + runner publish path: `ResponseMapper.Map`, the runner's map→watermark→SliceByBytes→
   publish block, `StepSpec`/`MappingSpec`, ApplyCaptures, the templating/token scope for a step.
2. Profile model: add the envelope/passthrough fields (mode selector: `mapped` default | `verbatim` |
   `typed_wrapper` | `source_type_prefix`; discriminator value; optional templated metadata map). Validate.
3. Engine: implement passthrough (emit the whole record element) + the three envelope shapes in the
   mapper/runner. Resolve A3 (keep-element seam), A6 (watermark must read the RAW element under passthrough),
   A5 (metadata templating uses the emit step's token scope, incl. `{{<as>}}` inside for_each).
4. tenable-io.yaml → verbatim full records (no wrapper/sourceType/correlation), matching the chunk processors.
5. defender-vm.yaml → typed_wrapper (machines/software) + source_type_prefix (vulns/changes/recommendations/
   recVulns, native sourceType values) + recVulns `recommendation_reference: {{rec_id}}`.
6. Complete defender-vm.yaml to native scope: resolve A7 (base-date input key from our Runner/RUN-envelope),
   add `$filter=lastSeen ge <base_date>` + base-date logic, the vuln-changes stage (14-day lookback), correct
   native endpoints/stages. A8: if base-date/lookback isn't templatable, DOCUMENT as a parity gap — no runner
   control flow.
7. Tests (4): verbatim full-record; typed_wrapper; source_type_prefix incl. templated recommendation_reference;
   mapped-subset unchanged. Build + full suite (≥52 green).
8. parity-note.md: native shape → YAML mapping with file:line; list the 3 deferred vendors.

## Verification Obligations
- Cross-check the 5 Success Criteria in prompt_contract.md.
- No regression: full suite green; poll-and-drain + SDK conformance (bus/resume/page counter/ingress) +
  remediation + reserved `__`-guard intact.
- Native shapes reproduced byte-for-byte (Tenable verbatim; Defender {type,data} and {sourceType,...}).
- Verbatim mode injects neither sourceType nor correlation keys; watermark/pagination still work under it.
- Verifier subagent (full context) then isolated code-reviewer subagent (changed files only).
