# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: the Egress core change is one tightly-coupled seam; the cross-collector cleanup is strictly sequential to it (nothing is dead until the new law exists) and mutates the same publisher signatures the core changes — parallel workers would collide on shared files. One execution unit, ordered: inventory (A5/A6/A7) → Egress core → cleanup sweep → observability → tests → docs.

## Research Decisions
- None needed. All external-behavior assumptions VALIDATED firsthand (ISB S3AdapterDataPublisher read in-conversation; S3 limits; production measurements). OPEN items A5/A6/A7 are code-inspection tasks assigned to execution.

## Worker Plan
Not applicable — direct path (single contract-driven execution unit).

## Synthesis Approach
Single unit — main thread reviews the diff, the cleanup inventory, and test results before verification.

## Verification Obligations
- Cross-check every Success Criterion in prompt_contract.md (10 test criteria + build + cleanup inventory + docs + version bumps).
- Specifically: byte-identical small-call regression is the load-bearing safety claim — verifier must re-run and inspect the test's actual assertions, not trust names; Abort call-count assertions on all three trigger paths; checkpoint-after-Complete ordering verified in code, not just tests; cleanup inventory complete with per-site justification and no behavior-bearing deletion; MaxBytesPerBatch survivals are genuinely memory-discipline-only; test exclusions honored (ISBLoadTestCollector, DummyCollector never executed).
- Confirm no ISB/feature-branch/foreign-repo edits in the diff.
