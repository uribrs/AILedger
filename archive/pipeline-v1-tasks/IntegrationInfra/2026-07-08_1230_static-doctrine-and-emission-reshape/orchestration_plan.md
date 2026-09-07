# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: two sequenced parts on one branch, one concern touched; doctrine wording and reshape choices are tightly coupled — splitting them across workers would only add handoff.

## Research Decisions
- None needed. Both OPEN assumptions (MultipartUploadOptions resolution site; sessions' options-handoff shape) are in-repo, executor-verifiable; no external-system behavior.

## Worker Plan
Not applicable — direct path.

## Verification Obligations
- Cross-check against prompt_contract.md Success Criteria (taxonomy section, grep gates: no public static publish entrypoints / no Services resolution in Emission static bodies, emitter dual construction paths, guards preserved, suite green).
- Test-churn audit: `git diff` of test files must show arrange/act changes only — assertions byte-identical (this is the behavior-identical proof; verifier must diff, not trust).
- Two-commit structure (doctrine, then reshape) present.
- README consistency with the new surface; ISink note disposition justified by the README's own framing.
- Code-reviewer (minimal context) after verifier — this diff is public-API shaping, review depth matters.
