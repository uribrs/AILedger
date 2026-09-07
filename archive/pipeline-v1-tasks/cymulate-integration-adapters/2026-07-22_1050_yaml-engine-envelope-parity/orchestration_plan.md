# Orchestration Plan

## Complexity Decision
- Path: decompose (per-feature workers) — but SEQUENCED, not parallel.
- Rationale: user prefers per-responsibility decomposition, but all four features edit the
  same files (MergeEnrichment.cs, MergeEnrichmentSink.cs, WorkflowConfig.cs, Loader,
  Schema). Phase-1's parallel workers were safe only because they were file-disjoint; these
  are not. Sequencing gives per-feature isolation and review granularity without
  concurrent-edit collisions. The orchestrator owns the cross-cutting steps (schema re-sync,
  adapter gate, live parity, YAML consumption, comparison) so workers stay code-only.

## Research Decisions
- None needed. Three threads (egress boundary, native chunking algorithm, engine hook
  points) are settled and recorded in ../2026-07-21_1426_.../envelope-parity-design.md.
  OPEN assumptions A4/A5/A6 are executor design choices; A8 is a local harness check.

## Worker Plan (sequential; engine gate green is each worker's exit criterion)
Baseline engine gate: 660/660 (phase 1).
- W-B — Feature B require_key. Files: Models/WorkflowConfig.cs (RequireKey), Workflow/MergeEnrichment.cs (ApplyEmbed/ApplyGrouped key-guard), Loader (reject on array anchor), Schemas (adapters, additive), MergeIntoTests (+tests). dependencies: none.
- W-C — Feature C strip. Files: WorkflowConfig (Strip), MergeEnrichment.EmbedValue/EmbedGroup, Loader, Schema, tests. dependencies: W-B (same files, sequential).
- W-D — Feature D sort. Files: WorkflowConfig (Sort{path,order}), MergeEnrichment.EmbedValue, Loader, Schema, tests. dependencies: W-C.
- W-A — Feature A chunk. Files: WorkflowConfig (Chunk{array,max,index_field,last_field,count_field}), MergeEnrichmentSink post-plan pass, Loader, Schema, tests (incl. exact-cap off-by-one + zero-finding). dependencies: W-D.

Each worker: adapters-side only (engine + adapters schema + tests); run the engine test project
green before reporting; return final YAML grammar, files touched, native-parity notes, test
names + count, and a paste-ready execution_notes entry. Workers do NOT edit ai/, magic-integration,
the YAML, the magic-side schema; do NOT run live vendor collections; do NOT redesign.

## Synthesis Approach (orchestrator, between + after workers)
After each worker: re-sync adapters schema -> magic-integration (byte-identical), run the
YamlCollector adapter test gate, and — after B and after A (the features that move observable
parity numbers) plus a spot run after C — a live parity rerun vs the native baseline. After
W-A: update crowdstrike-falcon.yaml to consume all four, drop the spine chunk/isLastChunk
annotate, final gates (engine + adapter + magic catalog/schema), final record-for-record
comparison report, update the YAML booked-deltas header.

## Verification Obligations
- Every Success Criterion in prompt_contract.md (gates, schema byte-identity, assets==357,
  per-host counts vs native, record-for-record envelope match with chunk on, comparison report).
- Flag-off invariance: existing qualys/insightvm merges + cursor YAMLs unchanged (engine gate
  + magic catalog test are the guards).
- Verifier subagent -> review/verifier-N.md; code-reviewer (minimal context) -> review/code-reviewer-N.md.
