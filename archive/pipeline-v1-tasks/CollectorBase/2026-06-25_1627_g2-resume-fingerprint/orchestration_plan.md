# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: a doc correction + two harness tests on the authoritative Restore gate; no behavioral code change. Tiny, coherent.

## Research Decisions
- None needed. Resume-gate behavior code-grounded this session.

## Worker Plan
Not applicable — direct path.

## Verification Obligations
- SC1–SC5.
- The fingerprint-mismatch test must FORCE a real mismatch (changed input/stream on a v-current checkpoint) and assert a from-start fetch (not a resumed cursor) — not a trivial pass.
- schemaVersion-bump test changes the fingerprint → fresh.
- Guardrail audit: Restore/CanUseState behavior unchanged; CanResume not given an event-dependent check; doc now accurate.
- Verifier runs the suite + inspects the mismatch test for triviality.
