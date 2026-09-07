# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: The changes touch tightly coupled files (CortexXdrFindingsFlow, CortexXdrCheckpointHelper, CortexXdrFindingsStage, CortexXdrXqlClient, CortexXdrUrls, the new CortexXdrRecordFormatter, and the single shared test class). Stage rename, checkpoint bump, sourceType stamping, and the new va_endpoints stage all interlock — separating into workers would create more handoff overhead than value. The contract already enumerates the work in a logically sequenced way; the executor can ship it as one direct pass with an optional two-commit split.

## Research Decisions
- None needed. The one open external-behavior question (upstream consumer tolerance for the additive `sourceType` field) is explicitly out-of-band per the operator's framing — coordination with the upstream team is the operator's responsibility, not this task's. The `xql/get_datasets` response shape is already documented in the on-prem reference (`/Users/user/Dev/AgentService/Source/Application/.../PaloAltoCortexApiBase.cs:192` + `CortexXdrCollector.cs:494–595`); no external research is required.

## Worker Plan
Not applicable — direct path.

## Synthesis Approach
Not applicable — direct path. Executor returns a single set of changes; orchestrator runs verifier + code-reviewer against the completed result.

## Verification Obligations

Cross-check against `prompt_contract.md` Success Criteria. Specifically:

1. Three-stage flow shape and order (`FindingsCves → FindingsEndpoints → Assets`).
2. `sourceType` as first property on every published row across all three streams; correct values per stream.
3. Field-collision fail-fast in `CortexXdrRecordFormatter`.
4. `checkpointVersion = 3` written; v2 rejected on resume with documented log.
5. New `NextVaEndpointIndex` (long) cursor written and loaded correctly.
6. Dataset-availability probe via new XQL `get_datasets` endpoint; missing va_endpoints gracefully degrades, does NOT fail flow.
7. DryRun unchanged (no va_endpoints probe or query in dry-run).
8. Reuse of `IngressStream.ReadNdjsonLinesAsync` + `Skip` for the new stage; no new buffering.
9. Trust XQL's `| sort asc endpoint_id` (no SHA-256 tie-breaker).
10. Stage rename ripple confined to Cortex collector + tests; no leakage into other collectors or Shared.
11. Existing CortexXdrFindingsFlowTests assertions preserved (with the documented mechanical updates).
12. New tests cover: sourceType-first-property × 3 streams, va_endpoints-stage happy path, stage transitions emit checkpoints at each boundary, resume from FindingsEndpoints, Host-Insights-missing graceful path, v2 rejection, collision fail-fast.
13. No new public types beyond what's strictly required.
14. `dotnet build` + targeted Cortex test command pass.
15. Pre-commit hooks pass on every commit (no `--no-verify`).

Verifier-specific cross-checks:
- Confirm `CortexXdrXqlClient` has no caller other than `CortexXdrFindingsFlow` post-change (the probe method's introduction is scoped correctly).
- Confirm the stage rename did not break any string comparison outside the Cortex collector + tests.
- Confirm DryRun did NOT regress (no va_endpoints HTTP call in dry-run mode).
- Confirm no per-row JSON parsing in `IngressStream` (validation policy remains lenient at Ingress, strict at Egress — Egress validation handles malformed bytes if they slip through).

Code-reviewer-specific cross-checks (run with minimal context; the orchestrator constructs the prompt without contract/plan/verifier text):
- Stamping helper readability + collision-fail-fast clarity.
- Stage state machine correctness (no fall-through bugs on probe-skip).
- Checkpoint version bump correctness (load/save symmetry, legacy rejection log message).
- Test coverage breadth.
- No dead code from the stage rename.
- Comment policy (only where WHY is non-obvious).
