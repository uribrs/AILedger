# Verifier-1 — Runner decomposition (behavior-preserving refactor)

**Overall verdict: PASS.** Build clean, 88/88 green, no test edits, all invariants preserved by verbatim move, public adapter surface intact.

---

## 1. Build / test / no-test-edits — PASS
- `dotnet build CollectorBase.slnx` → **0 errors** (12 pre-existing NU1507 warnings only; no new warnings).
- `dotnet test CollectorBase.slnx` → **Passed! Failed: 0, Passed: 88, Skipped: 0, Total: 88**.
- No-test-edits confirmed (no git here): the only test source is `Tests/CollectorExecutor.Test/CollectorExecutorTests.cs` (plus infra `FakeHttpClientFactory.cs`, `CollectorEventCapture.cs`). Its mtime is **2026-06-25 17:21** — *before* every refactored source file (all 2026-06-28 12:59–13:14). The test source predates the work, so nothing was touched to make it pass.

## 2. Success Criteria 1–6 — PASS (all six)
1. Build clean — yes (above).
2. 88/88, no test edits — yes (above).
3. Runner is a ~193-line conductor (`CollectorExecutorRunner.cs` = 193 ln: prep-unpack + step-loop switch dispatch + run-level catches + success mapping); executors/scope/sender/outcome-factory/failure-resolution under `Execution/Steps/` (8 files) and `Execution/Outcomes/` (2 files); probe at `Execution/ConnectionProbe.cs`. Layout matches the signed-off target exactly. (193 vs ~150 target — documented as residual, acceptable.)
4. Partial-success-wins lives in ONE place: `Outcomes/StepOutcomeFactory.cs` (`PartialOrElse` ln 17-18, `PartialSuccess` ln 20-22, `TerminalResult` ln 27-33). The only `["partial"] = true` literal in the whole project is StepOutcomeFactory.cs:22.
5. 4× HTTP-build duplication gone: a single `Steps/StepRequestSender.cs` (BuildUrl → method → JSON body → SendForString). Fetch/poll_until/poll_and_drain/probe all call `StepRequestSender.SendAsync`. The only other `new HttpMethod` sites are SessionFactory + StepHelpers.BuildHydrateRequest (pre-existing, not part of the 4 query copies).
6. `execution_notes.md` documents before/after responsibility map + per-piece destinations + per-step gates. Present and accurate.

## 3. The three target invariants — PASS

**(a) checkpoint BEFORE AdvancePage** — `Steps/FetchStepExecutor.cs:89` `CollectorExecutorCheckpointManager.Write(...)` immediately precedes `:90` `runCtx.Progress.AdvancePage(...)`. Ordering preserved.

**(b) partial-success-wins centralized, no bypass** — Grep for `emitted > 0 ?` across `CollectorExecutor/` returns exactly two hits, both legitimate: `StepOutcomeFactory.cs:18` (the single ternary home) and `FailureResolutionRunner.cs:38` (the `CompletePartialAsync` hook, which routes through `StepOutcomeFactory.PartialSuccess`). Every fetch call site routes through `PartialOrElse` (FetchStepExecutor.cs:60, 103, 205, 216, 238). No step executor carries a raw inline ternary that bypasses the factory.
  - FailureResolutionRunner's own `emitted>0` branches (ln 38 CompletePartial hook, ln 49-51 Rethrow) are faithful to the original `ExecuteDecisionAsync`: PublishFailure/FailFast → `TerminalResult` (which itself applies partial-wins), CompletePartial → PartialSuccess-or-null, Rethrow → PartialSuccess-or-TransientFailure. All emit-preserving paths go through `StepOutcomeFactory.PartialSuccess`, so the invariant stays single-sourced.

**(c) poll_and_drain PER-CYCLE checkpoint** — `Steps/PollAndDrainStepExecutor.cs`: the per-item `foreach` spans ln 92-117; the cycle checkpoint `CollectorExecutorCheckpointManager.Write(...)` is at ln 124-126, **outside** the foreach (once per cycle). The propagate-branch checkpoint at ln 105-107 is inside the foreach (correct — persists processed set before early return). Both present, matching the documented design.

## 4. Spot-checked verbatim moves — PASS
- **next_url non-advance guard**: `FetchStepExecutor.SendQueryAsync` (ln 123-131) returns `(body, usedUrl)`; the guard at ln 92-96 compares `pstep.NextUrl` to `usedUrl` (Ordinal) and breaks. next_url fast-path correctly uses `GetStringAsync` (ln 127) vs `StepRequestSender`/`SendForStringAsync` for built requests (contract constraint preserved).
- **404 → EXPORT_GONE**: `PollAndDrainStepExecutor.cs:62-68` catches `AdapterHttpRequestFailedException` with `StatusCode == NotFound` → `FailureResult(..., "EXPORT_GONE", ...)`, ordered before the general transport catch (ln 69). Correct.
- **reset-to-watermark**: `FetchStepExecutor.HandleHttpFailureAsync` (ln 225-247) returns `(ResetAndContinue=true, null)` when `res.ResetAndContinue && watermark present`; caller at ln 108-113 sets `cursor = null; runCtx.ScrollDepth = 0; continue`. Reset-without-watermark → transient (ln 235-240). Faithful.

## 5. Public adapter surface — PASS
`CollectorExecutorAdapter.cs` still wires all entry points to the Runner: ctor (54), `ProbeAsync` (137), `Preflight` (195), `CanResume` (297, static `CollectorExecutorRunner.CanResume`), `RunAsync` (308). `Fingerprint` remains `internal static` on the Runner (ln 187-192). No signature change.

## Notes / residual (non-blocking)
- Runner is 193 ln vs the ~150 target — accounted for by verbatim prep-unpacking + xmldoc; clarity-over-line-count, documented. Not a behavior issue.
- `StepExecutionScope` is a `record` but `RunCtx` is a reference-type (`RunContext` class) field, so by-reference mutation semantics are preserved exactly (executors mutate cursor/offset/page/watermark/captures in place). Verified against constraint.
- Hydrate path keeps its own `SendForStringAsync` calls (FetchStepExecutor.cs:155, 164) using pre-built `(hMethod,hUrl,hContent)` — never part of the 4× query-build copies, correctly left as-is.
