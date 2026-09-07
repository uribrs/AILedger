# Decisions

(Carried from phase-1: D-P1-* still apply unless explicitly overridden.)

- **D-P2-1** — Execution path is **direct**. P2 scope is one coherent
  unit (classifier body + planner wiring + pipeline updates + tests);
  decomposition would create coordination overhead between policy and
  pipeline edits.

- **D-P2-2** — LoC budgets: stretch ~250 LoC net add; hard ceiling
  600 LoC. Above hard ceiling, executor stops and surfaces. Phase 1
  came in at ~1127 LoC (~225 over stretch, well under 2000 ceiling);
  P2 is intentionally smaller — most of the surface (interface, action
  hierarchy, switch sites) already exists.

- **D-P2-3** — SDK stays at 2.0.26. The executor MUST NOT touch
  `Directory.Packages.props` or `Directory.Build.props`.

- **D-P2-4** — Marker-leak fixes (both chain-walk and exclude-marker)
  are out of scope for P2, per source D8.

- **D-P2-5** — `_retry.*` checkpoint persistence is out of scope; that
  is P3. The `AttemptNumber` field on `FailureContext` continues to
  carry only the runner-local counter (resume path's `AttemptNumber`
  stays at 0; fresh-run's counter increments per re-entry as in P1).

- **D-P2-6** — No collector edits. P4 is the first place a collector
  changes failure behaviour.

(Operator sign-off 2026-05-24: "consider the contract signed off, as
long as we're sticking to the plan." Promoting the recommended A-P2-N
options below.)

- **D-P2-7** — `ProgrammerBugClassifier.IsBug` returns true for:
  `NullReferenceException`, `IndexOutOfRangeException`,
  `InvalidCastException`, `ArrayTypeMismatchException`,
  `ArgumentNullException`, `ArgumentOutOfRangeException`,
  `ArgumentException`. False for everything else. (A-P2-1 option (a).)

- **D-P2-8** — `UnknownFlowRetryPolicy.CreatePipeline` and
  `ClassifiedRetryablePolicy.CreatePipeline` each gain an optional
  `DelayPlanner? planner = null` parameter, appended to the existing
  signature. When null, a process-shared `DelayPlanner.Default` (zero
  jitter) is used. Production callers stay source-compatible. (A-P2-2
  option (a).)

- **D-P2-9** — `DefaultFailurePolicy` becomes ctor-injected with a
  `DelayPlanner`. Ctor visibility changes from `private` to
  `public`. `Instance` stays a singleton:
  `public static IFailurePolicy Instance { get; } = new
  DefaultFailurePolicy(new DelayPlanner(jitterFraction: 0.20));`.
  Falcon (P4) constructs its own `DefaultFailurePolicy` with a
  Falcon-tuned planner. Public-API surface change is intentional and
  acknowledged. (A-P2-3 option (a) + verifier MEDIUM 1.)

- **D-P2-10** — `DelayPlanner` ctor post-P2:
  `public DelayPlanner(Random? random = null, double jitterFraction =
  0.0, TimeSpan[]? unknownDelays = null, TimeSpan[]? classifiedDelays =
  null)`. The existing `Random? random` parameter is preserved. The
  jitter parameter changes from `TimeSpan jitterRange` (absolute,
  one-sided) to `double jitterFraction` (relative, symmetric — range
  `[0.0, 1.0]`; multiplies the picked delay by `1 + uniform(-jitter,
  +jitter)`). When `unknownDelays` / `classifiedDelays` are non-null,
  `PickUnknown` / `PickClassified` route through them; otherwise they
  fall back to the static `UnknownFlowRetryPolicy.RetryDelays` /
  `ClassifiedRetryablePolicy.RetryDelays`. (A-P2-6 + A-P2-7 +
  verifier MEDIUM 2 + LOW 5.)

- **D-P2-11** — A `public static DelayPlanner Default { get; }` field
  is added to `DelayPlanner` — `new DelayPlanner(jitterFraction:
  0.20)`. Polly pipelines use it when the caller passes `null`.
  Production-default behaviour is +/-20% jitter on the existing
  retry delay arrays.

- **D-P2-12** — `PROGRAMMER_BUG` error code: introduced as an
  inline string literal in `DefaultFailurePolicy.cs` at the single
  use site. No new constants file. Executor records this decision
  in `execution_notes.md` per verifier LOW 2. (A-P2-4 resolved.)

- **D-P2-13** — `ProgrammerBug` branch in `DefaultFailurePolicy.DecideAsync`
  fires **after** the cancellation check, **before** the transport
  check. (A-P2-5 option (2).)

- **D-P2-14** — `FailFast(PROGRAMMER_BUG)` log level: `LogError` (not
  `LogWarning`). Wired in the runner's `FailFast` handler when
  `Handling.ErrorCode == "PROGRAMMER_BUG"`. (A-P2-12 resolved per
  verifier MEDIUM 3.)

- **D-P2-15** — `UnknownFlowRetryPolicy.IsUnknownRetryCandidate` is
  **NOT** changed to short-circuit on `ProgrammerBugClassifier.IsBug`.
  Default per A-P2-13. Accepted risk: an NRE running through the
  unknown-retry path burns ~21 minutes of retry delay (60s + 5m + 15m)
  before the policy's `FailFast(PROGRAMMER_BUG)` fires. Executor must
  surface this in `execution_notes.md` explicitly. (Operator chose
  "stick to the plan"; plan does not schedule this.)

- **D-P2-16** — LoC measurement instrument: net add measured as
  `git diff --numstat | awk '{add+=$1; del+=$2} END {print add - del}'`
  not `git diff --stat | tail -1`. (Verifier LOW 4.)

