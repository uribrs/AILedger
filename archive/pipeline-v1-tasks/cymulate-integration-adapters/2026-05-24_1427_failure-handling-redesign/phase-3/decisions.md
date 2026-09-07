# Decisions

(Carries: D-P1-* and D-P2-* still apply unless explicitly overridden.
D-P2-9 keeps `DefaultFailurePolicy`'s ctor public — needed here for
optional cap parameter. D-P2-17/18 stand.)

- **D-P3-1** — Execution path: **direct**. Scope is one coherent
  unit: new `RetryBudget` static class + runner edits + policy edit +
  tests. Decomposition would split tightly-coupled changes.

- **D-P3-2** — LoC budget: stretch 250 LoC net add; hard ceiling 600.
  Plan estimate ~150-200 LoC; budget allows margin.

- **D-P3-3** — Prereq 1 resolution: **option (3) — best-effort
  durability**. Initial plan was option (2) (explicit flush via
  `AdvancePage(0, 0)`), but empirical testing during execution
  proved `AdvancePage(0, 0)` increments the page counter regardless
  of its args (code-reviewer Blocker #1, confirmed by
  `RetryBudgetTests.Write_DoesNotChangeProgressCounters` failing
  against the option-2 design). SDK 2.0.26 has no
  flush-without-page-advance primitive. Fall back to plan option (3):
  `_retry.*` keys are written to `progressContext.AdapterState`
  in-memory; durability piggy-backs on the next natural
  `AdvancePage` call in the flow (typically the next successful
  batch publication). **Accepted weakening of C6**: cross-redelivery
  cap is best-effort, not hard. Worker crash during a retry sleep
  before any subsequent `AdvancePage` loses the budget; redelivery
  starts with fresh budget. May exceed the cap across the crash
  boundary by up to the cap value. Documented in `RetryBudget.cs`
  xmldoc.

- **D-P3-4** — Prereq 2 resolution: **clear**. Grep across 19
  checkpoint helper/writer/reader files returned zero matches for
  underscore-prefixed or retry-named keys. Safe to introduce
  `_retry.*` prefix.

- **D-P3-5** — Prereq 3: **assume downstream consumers filter
  underscore-prefixed keys**. Platform team verification deferred to
  operator follow-up; documented in execution_notes. If the
  assumption breaks, the keys leak into downstream rendering but
  carry no security or correctness impact (they're plain int +
  string + ISO timestamp).

- **D-P3-6** — `RetryBudget` is a `public static class` with three
  methods (`Load`, `Write`, `Clear`) and three public const string
  keys. No state. Matches `ProgrammerBugClassifier`'s shape — the
  fellow Failure-namespace static helper.

- **D-P3-7** — `RetryBudgetSnapshot` is a `public readonly record
  struct` with three fields: `AttemptCount` (int), `LastErrorCode`
  (string?), `LastAtUtc` (DateTime?). Value-type to match
  `FailureContext`'s shape.

- **D-P3-8** — Cross-redelivery cap default: **4** (matches
  `ClassifiedRetryablePolicy.MaxRetries`). Configurable via a new
  optional ctor parameter on `DefaultFailurePolicy`:
  `DefaultFailurePolicy(DelayPlanner planner, int
  maxRetriesAcrossRedeliveries = 4)`. `Instance` uses the default.
  Cap-breach semantics: convert `RetryInProcess` to
  `FailFast(handling with ErrorCode = "RETRY_BUDGET_EXHAUSTED",
  IsRetryable = false)`. Per A-P3-5 / A-P3-7.

- **D-P3-9** — `RetryBudget.Write` writes
  `attemptCount.ToString(CultureInfo.InvariantCulture)`,
  `lastErrorCode ?? string.Empty`, and `lastAtUtc?.ToString("O",
  CultureInfo.InvariantCulture) ?? string.Empty`. Then calls
  `progressContext.AdvancePage(0, 0)`. Matches existing
  `Falcon`/`Dummy` checkpoint-helper serialization conventions.

- **D-P3-10** — `RetryBudget.Load` reads via
  `CheckpointAdapter.GetData(checkpoint)` (existing helper). On any
  parse failure (e.g., bogus int), logs warning and falls back to
  default snapshot — backwards-compatible with old / corrupt
  checkpoints.

- **D-P3-11** — `RetryBudget.Clear` writes empty strings to all
  three keys and calls `AdvancePage(0, 0)`. SDK has no `RemoveKey`
  API. Empty-string semantic matches the existing
  `FalconCheckpointHelper.SaveAssetsState` pattern (which writes
  `?? string.Empty` for nullable fields).

- **D-P3-12** — `RetryBudget.Write` exception handling: catch any
  exception from `AdvancePage`, log error at `LogError`, swallow.
  The flow is already in an exception path (we're recording a
  retry); failing here doubles up failure semantics. Per A-P3-12
  option (b).

- **D-P3-13** — Where the budget is loaded on resume: in
  `AdapterFlowRunner.ResumeCoreAsync`, immediately after
  `progressContext.RestoreProgress(...)`. Snapshot stashed in a
  local; the closure-captured snapshot feeds
  `FailureContext.AttemptNumber` and `LastErrorCode` /
  `LastAtUtcUtc` (if we add those fields — see D-P3-14).

- **D-P3-14** — **No new fields on `FailureContext`** in P3 for
  `LastErrorCode` / `LastAtUtc`. The policy only consumes
  `AttemptNumber` for cap consultation. The other two fields are
  persisted for *diagnostic / future-use* purposes (e.g., a future
  collector-specific policy could read them via the budget directly).
  Avoids widening `FailureContext`'s shape without a current consumer.

- **D-P3-15** — `RetryBudget.Write` is called from inside the
  runner's `FailureAction.RetryInProcess` arm on both fresh-run and
  resume paths, BEFORE sleeping or rethrowing the marker. The
  `attemptCount` written is `context.AttemptNumber + 1` — recording
  that the runner has now initiated this many retries.

- **D-P3-16** — `RetryBudget.Clear` is called from BOTH fresh-run
  and resume success paths. Fresh-run: after `BuildSuccessResult` is
  ready, before publishing the success `CompletionRequest`. Resume:
  after `flowResult` is null-checked, before constructing
  `AdapterResult.SuccessResult`. Idempotent in the no-prior-retry
  case (writing empty strings is a no-op against missing keys).

- **D-P3-17** — Test scaffolding lives in `DummyCollector.Test`:
  `RetryBudgetTests.cs` (new) exercises Load/Write/Clear with a fake
  `IAdapterExecutionContext`. `DefaultFailurePolicyTests.cs` gains a
  cap-exhaustion test. Resume-budget integration coverage is
  light-touch in this phase; the heavy integration test arrives in
  P4 (Falcon enables classified-retry).
