# Handoff: task 3 only — structured findings tool endpoint

## Assignment and workspace

Implement task 3 of [backlog 63](../../Backlog/structured-agent-interface.md): a local MCP adapter for `record_findings`, using the atomic application operation completed in task 2. Stop at the endpoint, trusted host binding, transport diagnostics, and meaningful end-to-end tests. Do not implement provider adoption/configuration (task 4), YAML, workflow changes, or an unrestricted command batch API.

Work directly in `/Users/user/.codex/worktrees/structured-findings-contract/AILedger`, branch `codex/structured-findings-contract`. Inspect `git status` first and preserve subsequent work. Task 1 is `a62053c`; task 2 is the scoped implementation commit containing this handoff (locate it with `git log`, rather than relying on a self-referential hash in the file). Do not copy changes into the original checkout, install the global tool, merge, or publish automatically.

The user's kernel-workflow exception remains local to this effort: do not invoke `ai-kernel`, create a governed task, or use ledger writes to govern development. Existing authorization and validation **inside the application and isolated tests** remain authoritative. Do not edit global instructions/skills or dispatch other agents unless the user explicitly changes that instruction. Follow the .NET skill and repository small-method/SRP conventions.

## Read in order

1. [Task-2 validation and limitations](../structured-findings-v1/task-2-validation.md), especially compatibility and measurement gaps.
2. [Settled v1 design](../structured-findings-v1.md), then its request/response schemas and examples under `docs/structured-findings-v1/`. Do not silently change fingerprint, binding, limits, or retry semantics.
3. [Measurement baseline](../structured-findings-v1/measurement-baseline.md). Verify its frozen hashes before production edits; preserve the existing harness and expected reports.
4. `src/AILedger.Core/Findings/Contracts/FindingsContracts.cs`, `FindingsValidation.cs`, `FindingsFingerprint.cs`; then `src/AILedger.Storage/Findings/FileGovernedTaskService.Findings.cs`, `FindingsCandidate.cs`, `FindingsReceiptEnvelope.cs`, and `FindingsAttempt.cs`.
5. `tests/AILedger.Tests/Findings/` for tested behavior, and existing CLI composition/transport patterns only as needed. Task 2 made no provider production changes. Do not reopen the entire architectural investigation by default.

## The application seam to call

```csharp
IFindingsRecorder recorder = new FileGovernedTaskService(
    taskWorkspaceRoot, new CommandHandler(), new TaskReducer());
FindingsResult result = await recorder.RecordAsync(binding, request, cancellationToken);
```

Imports: `AILedger.Core.Findings`, `AILedger.Core.Application`, `AILedger.Core.Domain`, `AILedger.Storage`. The service instance owns the task workspace root; `FindingsBinding` owns the trusted task/actor/run/correlation/causation and grants. Resolve the correct ledger location through host composition, as existing service callers do. It is never a request-body field.

`AllowRecordFindings` defaults false. A runless host must also set `AllowRunless` true and supply a stable correlation. A bound run must exist, belong to the bound actor, and have correlation equal to its run ID. An existing run need not be active: task 2 deliberately preserves current command policy. The host grant does not confer kernel capabilities. Both new writes and receipt access enforce current authorization.

`FindingsRequest` contains only schema version, request ID, findings, and evidence. References are `FindingReference(Finding: "f1")` or `FindingReference(ClaimId: "C1")`. Local keys are unique across both arrays. All created claims remain open; evidence does not resolve them.

Do not translate this into shell strings or call `ExecuteAsync` in a loop. The recorder owns validation, allocation, local resolution, lock, candidate state, canonical append, receipt lookup, refusal recording and application attempts. Do not add a transport receipt cache as another source of truth.

## Task-3 implementation obligations

- Parse only the exact v1 request shape. Reject unknown and duplicate properties, wrong casing, null required fields, invalid Unicode scalar sequences, invalid JSON/numbers, and bodies over 256 KiB. Duplicate-property rejection must happen **before** ordinary deserialization discards duplicates. The typed service rechecks shape/limits but cannot recover wire information already discarded by a parser.
- Construct binding only from trusted host configuration/session state. Never accept actor/task/run/correlation/causation/grant fields from agent arguments, headers the agent can fabricate, or request ID. Keep the service's bound ledger root outside the payload as well.
- Map `FindingsResult` explicitly to the frozen response schema. Success has `schema_version`, `attempt_id`, `status`, `replayed`, `receipt`; error has `schema_version`, `attempt_id`, `status`, `error`. Do not blindly serialize the application result: it also has `CollectionStatus`, null alternative branches, and C# casing. Receipt fields use snake_case, including explicit null binding fields. Preserve original receipt version/time/event maps on replay.
- `CollectionStatus` is adapter-side diagnostic information, not a new v1 response member. Capture transport attempts separately, including calls rejected before service entry. Join an admitted call to the returned application attempt ID, request key, and receipt transaction/events when available; do not duplicate prose or provider cost. Missing observations remain missing. Application and transport times are nested intervals, not additive elapsed time.
- Keep diagnostics off the MCP protocol output stream. Keep malformed input, host grant refusal, kernel refusal, storage error, and uncertain outcome distinguishable.
- Preserve recovery instructions. `outcome_unknown` means retry the **same body/key/binding**; do not invent a new key. `idempotency_conflict` means an earlier key use committed while the new content did not. Failure to inspect a protected receipt has commit state `unknown`. A transport disconnect/cancel after append can lose a success response without undoing the commit.
- Reconnection must retain the original trusted attribution to recover the receipt. Reusing a key from a fresh run with different attribution conflicts. Cross-actor recovery is not supported.

## Acceptance and verification

Exercise a real local MCP request through parser, host binding, recorder, kernel and storage against disposable ledgers. Include valid batches, exact response-schema conformance, adversarial quotes/metacharacters/multiline Unicode, duplicate/unknown fields, spoofed identity, revoked grants/capabilities, malformed/truncated input, oversized bodies, concurrent retries and lost-response recovery. Verify protocol stdout stays clean and transport failures/denials have their own observations without changing canonical truth. Task 2's 86 tests remain part of the regression suite.

Run `dotnet test`; the external-output host's repository-location failures are documented in the validation record. Use `sh scripts/test-governed.sh all` for the repository-aware in-process xUnit run. Its application-data tests require filesystem access beyond the narrow worktree sandbox. Do not alter fixtures just to bypass host limitations. Preserve the existing timing-sensitive `ReconsiderationConsultantTests.R8_ConsultationRequiresARecordedSubjectRole` test and report any recurrence separately.

Run `python3 tools/FindingsBaseline/verify-fixtures.py` and the read-only report probe from the measurement document. Do not use `--capture` to hide a difference. Build outputs belong outside the checkout.

General storage binaries must include task 2 before consuming findings-enabled logs: task-1 storage rejects singleton markers even though its typed event reader/reducer accepts the additive envelope. No live provider testing or MCP implementation was done by task 2. Do not describe this as already provider-enabled or claim a measured workflow speedup.

Finish with a scoped commit, task-3 backlog status based on actual acceptance, validation results and limitations, and a focused task-4 handoff. Leave tasks 4–8 untouched.
