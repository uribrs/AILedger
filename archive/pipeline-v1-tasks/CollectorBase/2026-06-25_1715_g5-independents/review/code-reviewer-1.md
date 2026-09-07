# Code review — cursor cycle guard, ProbeAsync request selection, doc edits, tests

Reviewer: independent (no prior context). Build + targeted tests run green.

## Verdict: CLEAN (no BLOCKER/MAJOR). One MINOR, two NITs.

Build: `dotnet build` 0 errors. Both new tests pass:
`Cursor_NonAdvancingToken_Terminates_NoInfiniteLoop`, `Probe_ForEachFirstProfile_HitsInnerRequestPath`.

---

## 1. Cursor guard correctness — `Strategies/Pagination/CursorPaginationStrategy.cs:24-25`

```csharp
var advanced = !string.IsNullOrEmpty(token) && !string.Equals(token, ctx.Cursor, StringComparison.Ordinal);
var hasMore = advanced && recordsThisPage > 0;
```

Correct. Edge cases traced:

- **First page** (`ctx.Cursor` null/empty, token non-empty): `string.Equals("tok", null)` is false → `advanced=true`. Advances. No regression vs the prior `!IsNullOrEmpty(token)` behavior. Verified by the still-passing Falcon scroll test (`Page` helper returns advancing tokens `2`,`4`,`""`).
- **Empty/absent token**: `IsNullOrEmpty` short-circuits → stop. `JsonNav.StringAt` (Interpreter.cs:46) returns `null` for an absent path and the literal otherwise, so a `""` JSON token is also caught by `IsNullOrEmpty` before reaching `Equals` — no null-vs-`""` asymmetry can produce a wrong `Equals` result.
- **Non-advancing vendor** (stuck): page1 null→STUCK advances; page2 STUCK==ctx.Cursor → stop. Bounded. Correct.

**False-positive risk (stopping a valid cursor early): not in any realistic shape.** A normal advancing cursor emits a *new* token each page, so `Equals` is never true mid-scroll. The only loss case is a vendor that returns a *fresh* token on every non-final page and then *repeats the just-used token* on the final page instead of emptying it — that would drop the final page. This is contrived (done is normally signalled by empty/absent, not a duplicate) and is the same accepted trade-off already shipped in `cursor_watermark` (CursorWatermarkStrategy.cs:40) and `next_url` (the runner's `usedUrl` guard, CollectorExecutorRunner.cs:386). Consistent and acceptable.

Parity note: this matches the existing `cursor_watermark` repeated-cursor guard exactly (same `Ordinal` compare against `ctx.Cursor`), so behavior is uniform across the two cursor strategies. Good.

## 2. ProbeAsync request selection — `CollectorExecutorRunner.cs:78`

```csharp
var probeReq = !string.IsNullOrWhiteSpace(step.Request?.Path) ? step.Request : (step.Step?.Request ?? step.Request);
```

Correct for all three first-step kinds, and **no NPE risk**:

- `StepSpec.Request` is `= new()` and never null (Profile.cs:94); `RequestSpec.Path` defaults to `""` (Profile.cs:140). So `step.Request?.Path` is always non-null and the final `?? step.Request` can never yield null. The downstream `probeReq.Method`/`probeReq.Body` derefs are safe.
- **fetch-first** (flat stream → synthesized fetch, ResolveSteps in CollectorExecutorStepHelpers): path present on `Steps[0].Request` → uses it.
- **for_each-first**: `Steps[0].Request` is `new()` (Path empty), real request on `.Step.Request` → falls through to `step.Step.Request`. Proven by the new test asserting `/noauth/query` is hit.
- **poll_until-first / poll_and_drain-first**: the poll status request lives on the step's own `.Request` (RunPollUntilAsync/RunPollAndDrainStepAsync both read `step.Request`), and `.Step` is the *inner drain* fetch. With a populated poll `.Request.Path`, the first branch is taken → probes the poll request. Right target. (Had the fallback been written `.Step?.Request ?? .Request` *unconditionally* it would have wrongly probed the drain step for poll_and_drain — but the `Path`-present guard prevents that.)

The change is a strict improvement over the prior `step.Request` (which would have hit `{base_url}` with no path for a for_each-first profile).

## 3. Doc edits — `CollectorExecutor/docs/yaml-contract.md`

Accurate against the code:
- **`sort_key` reserved/not-consumed (line 80)**: `PaginationSpec.SortKey` is parsed (Profile.cs:151) and grep shows no strategy reads it. Correct.
- **`stop_when` reserved/not-consumed (line 83)**: `PaginationSpec.StopWhen` parsed (Profile.cs:155, default `"empty_page"`); no strategy references it — each strategy hard-codes termination. Correct.
- **`watermark_field` required for `cursor_watermark` (line 82)**: matches `CursorWatermarkStrategy` (reads `ctx.Watermark`, reset depends on it) and the runner's watermark extraction (CollectorExecutorRunner.cs:328). The "R*" + "validated at load" claim is consistent with how the field is used.

## 4. Test quality

**`Cursor_NonAdvancingToken_Terminates_NoInfiniteLoop` (line 1405)** — genuinely proves termination:
- `/stuck/query` (line 283) returns a full 2-record page **and** the same `after=STUCK` every call. Without the guard `hasMore` would stay true forever (full page, non-empty token) → infinite loop / test hang.
- Asserts exactly `4` records (2 pages × 2) — a *bounded* count, so it cannot pass trivially; a regression that kept looping would hang (xUnit no per-test timeout here, but a hang ≠ pass) and any off-by-one in the guard would change the count.

**`Probe_ForEachFirstProfile_HitsInnerRequestPath` (line 1420)** — proves the inner path is hit:
- `ForEachFirstProbeProfile` has `Steps[0]` = for_each with the real request on `.Step` (`/noauth/query`).
- Asserts `httpFactory.RequestedUrls` contains `/noauth/query` (FakeHttpClientFactory records every request URL via RecordingHandler). With the old `step.Request` code the probe would hit base-url-with-no-path and this assertion would fail. Not trivially passing.
- Also asserts `result.IsValid` — exercises the full ValidateConfigurationAsync → ProbeAsync path. `auth: none` keeps the probe focused on the request, not auth. Good.

---

## Findings

### MINOR
- **CursorPaginationStrategy.cs:26** — `Next` returns `new Paginator.Step(token, ...)` carrying the *non-advanced* (equal) token forward as `NextCursor` even when `hasMore=false`. Harmless today (the runner stops on `!HasMore` before re-issuing), but it persists a dead cursor into the final checkpoint write at runner line 383. `cursor_watermark` deliberately returns `NextCursor: null` on its stop path (CursorWatermarkStrategy.cs:47) for exactly this reason. Consider returning `null` for the cursor when `!hasMore` to keep the persisted checkpoint clean and parallel to the sibling strategy. Not a correctness bug.

### NIT
- **Stuck test** has no explicit timeout; relies on the guard to avoid a hang. Fine given the bounded-count assertion, but a `[Fact(Timeout=…)]` would make an accidental-regression failure fast+clear rather than a CI stall.
- **yaml-contract.md:80** still shows `sortKey: device_id` in the worked sketch (lines 231/240) without a "(reserved)" marker; minor inconsistency with the now-explicit reserved wording in the table. Cosmetic.

## Files reviewed
- `/Users/user/Dev/Uri/localprojects/CollectorBase/Strategies/Pagination/CursorPaginationStrategy.cs`
- `/Users/user/Dev/Uri/localprojects/CollectorBase/CollectorExecutor/Execution/CollectorExecutorRunner.cs` (ProbeAsync)
- `/Users/user/Dev/Uri/localprojects/CollectorBase/CollectorExecutor/docs/yaml-contract.md`
- `/Users/user/Dev/Uri/localprojects/CollectorBase/Tests/CollectorExecutor.Test/CollectorExecutorTests.cs`
- Supporting: Profile.cs, CollectorExecutorStepHelpers.cs (ResolveSteps), Interpreter.cs (StringAt), CursorWatermarkStrategy.cs / NextUrlPaginationStrategy.cs (parity), CollectorExecutorAdapter.cs (Validate→Probe), FakeHttpClientFactory.cs.
