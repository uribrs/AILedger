# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: one branch, strictly sequential steps (copy → packaging → wiring → build/pack proof → docs); every step gates the next — worker decomposition would only add handoffs.

## Research Decisions
- None needed. All four OPEN assumptions (Polly pin coexistence, test-csproj SDK refs, 3.2.0→3.2.1 compatibility, test-pin alignment) resolve mechanically at restore/build inside the repo.

## Worker Plan
Not applicable — direct path.

## Verification Obligations
- Cross-check every prompt_contract.md Success Criterion, especially the pack evidence (SDK nuspec: 3 deps + RepositoryUrl + no IsAdapter metadata; Infra nuspec: SDK dependency) — verifier must inspect actual nupkg contents, not trust notes.
- Verbatim gate re-run: diff -r of the carried .cs tree vs ISB source (only csproj/props/README may differ).
- Confirm the 3.2.0→3.2.1 build proved compatibility (or that a surface delta was properly STOPPED on, not patched).
- Feed-pin removal left no dangling package reference.
- Suite arithmetic: 238 existing untouched + 9 SDK = 247.
- Code-reviewer (minimal context) after verifier — this diff creates a public package's build definition; packaging review matters.
