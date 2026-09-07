# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: one coherent change in the adapter (SetConfiguration retain, ValidateConfigurationAsync probe,
  bus delegates) + a `dryRun` thread through the runner + tests. Tightly coupled; decomposition adds nothing.

## Research Decisions
- None needed (no EXTERNAL research). OPEN assumptions A2–A7 are INTERNAL SDK/code questions — the exact bus
  delegate signature + IsDryRun property, how the validate path reuses the preparer/input-builder, the dry-run
  cap-point, checkpoint policy — resolved by reading the SDK surface + Execution/ helpers during execution.

## Worker Plan
Not applicable — direct path.

## Execution order (within contract-driven-execution)
1. Read the adapter's bus-entrypoint construction + RunAndCountAsync signature + the SDK delegate types to
   pin A4 (IsDryRun) and A2 (validate delegate vs method). Read CollectorExecutorRunInputBuilder/Preparer/
   SessionFactory to pin A3 (build session from a config dict at validate time).
2. Validation: `SetConfiguration` retains config; `ValidateConfigurationAsync` (method + delegate) → build
   inputs/creds (decrypt) → session (auth) → one capped probe request (page_size 1, no publish) → real
   Success/Failure via the failure classifier; fail-closed on no yaml.
3. dry-run: stop discarding the request arg (:172/173); read IsDryRun; thread `dryRun` through
   RunAndCountAsync → RunAsync → the step loop; cap (page_size→1, stop after first emitting page/step);
   suppress the resumable checkpoint.
4. Tests (4): validate bad/good/no-yaml; dry-run capped + success; dry-run-then-real no corruption. Build +
   full suite green.

## Verification Obligations
- Cross-check the 5 Success Criteria.
- Prove: no unconditional Success in ValidateConfigurationAsync; isDryRun honored end-to-end; dry-run far
  fewer requests/records than full; dry-run doesn't poison a real run.
- No regression: passthrough/envelope/poll-and-drain/conformance/reserved-__/pagination; full suite green.
- Verifier subagent (full context) then isolated code-reviewer (changed files only).
