# Assumptions

Status legend: OPEN / VALIDATED / REJECTED.

---

**A-P3-1** — `SetState` alone does not durably persist; durability
requires `AdvancePage(0, 0)` to fire the platform's `OnCheckpoint`
callback. **VALIDATED.**
Evidence:
- SDK 2.0.26 xmldoc on `OnCheckpoint` callback property: "Optional
  callback invoked **after each page advance** to persist a
  checkpoint."
- SDK xmldoc on `SetState`: only "Stores a key-value pair in the
  adapter state dictionary" — no durability claim.
- All existing `*CheckpointHelper`-using collectors (Falcon, Dummy,
  etc.) follow the pattern: `foreach kvp { SetState(k, v); }` then
  `AdvancePage(...)`.

Conclusion: plan Prereq 1 option (2) — explicit `AdvancePage(0, 0)`
flush after `_retry.*` writes — is the right choice. Honors the
plan's full C6 cross-redelivery cap goal.

---

**A-P3-2** — `_retry.attemptCount` semantic: "number of retries done
so far" (0-based retry index). **VALIDATED.**
Aligns with the P2 post-execution patch D-P2-18 which normalized
`FailureContext.AttemptNumber` to 0-based retry index. The persistent
budget loads into `FailureContext.AttemptNumber` directly on resume.

---

**A-P3-3** — `_retry.lastErrorCode` is the `Handling.ErrorCode` from
the most recent `FailureAction` the policy returned (e.g., from
`RetryInProcess.Handling.ErrorCode` on the retry that led to this
resume). **VALIDATED.**
Use case: cross-redelivery cap can distinguish flapping errors from
sustained ones if we ever want a hetero-error cap.

---

**A-P3-4** — `_retry.lastAtUtc` is the UTC timestamp of when the most
recent retry was *initiated* (not when the error happened). Stored as
ISO 8601 round-trip format (`"O"`). **VALIDATED.**
Symmetry with existing `checkpointCreatedUtc` patterns in
`FalconCheckpointHelper` / `DummyCheckpointHelper`.

---

**A-P3-5** — Cross-redelivery cap value. **OPEN — operator pick.**
Plan §6 Phase 3 mentions a cross-redelivery cap but does not pin a
number. Reasonable choices:
- (a) Same as `ClassifiedRetryablePolicy.MaxRetries` = 4. Total
  attempts before fail-fast = 5 (initial + 4 retries).
- (b) Higher, e.g., 8. Allows two full classified-retry cycles across
  redeliveries before giving up.
- (c) Configurable per collector via `IFailurePolicy` ctor; default 4.

Recommend (c) — keep `DefaultFailurePolicy` ctor accepting an
optional `int maxRetriesAcrossRedeliveries = 4` parameter. Tests can
override. Default matches single-pipeline budget so P3 doesn't
silently widen retry behaviour.

---

**A-P3-6** — Clearing `_retry.*` on success. **VALIDATED — write
empty strings then flush.**
SDK has no `RemoveKey` or `ClearState` API; the only way to "clear"
is to overwrite with empty/sentinel values. Tests assert that on a
successful run, the next resume sees 0/null/null when reading the
budget. Implementation: write `""` for all three keys, then
`AdvancePage(0, 0)`.

---

**A-P3-7** — Cross-redelivery cap consultation site in
`DefaultFailurePolicy.DecideAsync`. **VALIDATED.**
Add the cap check inside the `RetryInProcess` branch
(`DefaultFailurePolicy.cs:104-115`) — when
`context.AttemptNumber >= maxRetriesAcrossRedeliveries`, convert
`RetryInProcess` to `FailFast(handling with "RETRY_BUDGET_EXHAUSTED"
code)`. Tests assert this conversion.

---

**A-P3-8** — Where in the runner to write `_retry.*` on retry
transition. **VALIDATED.**
For resume path (lines 1140-1210 of `AdapterFlowRunner.cs`): inside
the `FailureAction.RetryInProcess` switch arm, before
`throw new ClassifiedRetryableTriggerException(...)`, call
`RetryBudget.Write(progressContext, attemptCount, errorCode, atUtc)`
which does the three `SetState` calls + `AdvancePage(0, 0)`. The
budget that's written is `context.AttemptNumber + 1` (we're recording
that this many retries have been initiated).

For fresh-run path (lines 605-694): same pattern in the
`RetryInProcess` arm of `ActionFreshAsync`. The runner-local
`attemptNumber` increment already happens; the write captures the
state *before* sleeping and re-entering.

---

**A-P3-9** — Where to clear `_retry.*` on success. **VALIDATED.**
Resume path's success path is at lines 1362-1371 (after the inner
pipeline `flowResult` is null). Before constructing
`AdapterResult.SuccessResult`, call
`RetryBudget.Clear(progressContext)` which writes empty strings +
`AdvancePage(0, 0)`. Fresh-run success path (line 330+) does the
same.

---

**A-P3-10** — `RetryBudget` API surface. **VALIDATED.**

```csharp
public static class RetryBudget
{
    public const string AttemptCountKey  = "_retry.attemptCount";
    public const string LastErrorCodeKey = "_retry.lastErrorCode";
    public const string LastAtUtcKey     = "_retry.lastAtUtc";

    /// Loads budget from a checkpoint (called on resume). Returns
    /// (attemptCount: 0, lastErrorCode: null, lastAtUtc: null) for
    /// new flows.
    public static RetryBudgetSnapshot Load(AdapterCheckpoint? checkpoint);

    /// Writes the budget into progressContext and flushes via
    /// AdvancePage(0, 0). Call after each retry transition.
    public static void Write(
        AdapterProgressContext progressContext,
        int attemptCount,
        string? lastErrorCode,
        DateTime? lastAtUtc);

    /// Clears the budget (writes empty strings) and flushes.
    /// Call on successful run completion.
    public static void Clear(AdapterProgressContext progressContext);
}

public readonly record struct RetryBudgetSnapshot(
    int AttemptCount,
    string? LastErrorCode,
    DateTime? LastAtUtc);
```

Static class — matches existing `ProgrammerBugClassifier` /
`DelayPlanner` shapes. Snapshot is a record struct.

---

**A-P3-11** — Where to load the budget on resume. **VALIDATED.**
In `AdapterFlowRunner.ResumeCoreAsync` (file
`AdapterFlowRunner.cs`), the checkpoint is parameter-available.
After `progressContext.RestoreProgress(...)` (lines 1091-1095), call
`RetryBudget.Load(checkpoint)` and stash the snapshot in a local
that's captured by the `runInner` closure. The catch block at line
1142 reads `snapshot.AttemptCount` and feeds it into
`FailureContext.AttemptNumber`.

For fresh-run, the budget snapshot is always
`(AttemptCount: 0, …)`. No checkpoint to load from.

---

**A-P3-12** — `RetryBudget.Write` failure modes. **OPEN — operator
pick.**
What if `AdvancePage(0, 0)` throws? Two options:
- (a) Propagate the exception — fails the flow. Strong durability.
- (b) Swallow + log error — best-effort, keep going. Weaker
  durability.

Recommend (b) — log error, keep going. The flow is already in an
exception-handling code path (we're recording a retry, about to
sleep/retry); failing here doubles up the failure semantics. The
budget is advisory; if persistence fails this round, the worker
crash semantics simply revert to "best-effort durability" for that
cycle.

---

**A-P3-13** — Downstream consumer iterating `AdapterState` without
underscore filter. **OPEN — platform-team confirmation needed.**
Risk: if any downstream pipeline (analytics, dashboards) iterates
`AdapterState` and renders or logs every key, the `_retry.*` entries
will leak as if they were collector business state. Plan says "flag
for verification with the platform team". Per operator's "continue
to P3", we ship with the assumption that downstream consumers filter
underscores (a convention used by Linux dotfiles and many ad-hoc
internal-key schemes). Flag in execution_notes for operator follow-up.
