# Execution Notes

## 2026-07-16 — Contract creation

- Planning only; no product code changed by this task.
- Current branch: `feature/yaml-engine-declarative-enrichment` at base commit `d5b0c93`.
- The working tree contains uncommitted predecessor implementation and tests. It is intentional baseline material and must not be overwritten.
- Read global engineering principles and predecessor records:
  - `ai/active/2026-07-15_1746_yaml-engine-declarative-enrichment`
  - `ai/active/2026-07-16_1128_yaml-engine-streaming-merge`
- Inputs from prior analysis:
  - `src/Cymulate.Integration.Adapters/Collectors/YamlCollector/TECHNICAL_MAP.md`
  - full structural survey of 279 YAML definitions / 885 operations;
  - native collector matching and representative vocabulary review.
- This artifact intentionally contains open decisions. Those are gates for implementation, not gaps that prevent review of the direction.

## Resume instructions

1. Read `state.json`, then `prompt_contract.md`, constraints, assumptions, decisions, and the two predecessor task records.
2. Read the current git status and diffs; never assume the July 16 working tree is unchanged.
3. If the user has accepted the direction, use `contract-driven-execution` before product-code work.
4. Begin at Phase 0. Do not jump directly to Dataflow classes.
5. Update this file with commands, tests, changed files, deviations, and the exact next action at every handoff.

