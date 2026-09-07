# Assumptions

Status legend: OPEN / VALIDATED / REJECTED.

---

**A-P2-1** — ProgrammerBugClassifier heuristic set. **OPEN.**
Plan §6 Phase 2 names `NullReferenceException` and "our-own-argument-
exceptions". A pragmatic set:

- `NullReferenceException` → always a bug.
- `IndexOutOfRangeException` → always a bug.
- `InvalidCastException` → always a bug.
- `ArrayTypeMismatchException` → always a bug.
- `ArgumentNullException`, `ArgumentOutOfRangeException`,
  `ArgumentException` → **conditionally** a bug — only when the stack's
  topmost user-code frame is in `Cymulate.Integration.Adapters.*`
  (i.e., our own validation, not framework-rejected user input).

The conditional rule for `Argument*` is what plan §6 calls "our own
validation". Two implementation choices, OPEN until operator picks:

- (a) Simple: ALL `Argument*Exception` count as bug. False positives
  possible if a vendor SDK throws `ArgumentException` for an upstream
  data shape we don't control, but the blast radius is low (we
  short-circuit retries that would never succeed anyway).
- (b) Stack-aware: check `Exception.TargetSite.DeclaringType?.Assembly`
  or `StackTrace` against our assemblies. Adds reflection cost on every
  exception. Plan does not require this.

Recommend (a) — simpler, matches the spirit of "short-circuit retries
that would never succeed".

---

**A-P2-2** — DelayPlanner injection into static `CreatePipeline`. **OPEN.**
`UnknownFlowRetryPolicy` and `ClassifiedRetryablePolicy` are `public static
class`es. Their `CreatePipeline(ILogger, vendor, flow)` methods currently
read `RetryDelays` inline. Three injection patterns:

- (a) Add an optional `DelayPlanner? planner = null` parameter to
  `CreatePipeline`, defaulting to a process-shared static
  `DelayPlanner.Default` (zero jitter — preserves today's exact delays).
- (b) Add `CreatePipeline(... , DelayPlanner planner)` overload with no
  default; callers pass an explicit planner.
- (c) Make the planner ambient (static `DelayPlanner.Current` or
  `AsyncLocal`). Rejected — global mutable state is worse than the
  problem.

Recommend (a). Lets DummyCollectorTests pass a zero-delay planner
(`new DelayPlanner(jitterRange: TimeSpan.Zero)` with delays overridden —
see A-P2-7) without changing callers.

---

**A-P2-3** — DefaultFailurePolicy singleton vs instance. **OPEN.**
P1 ships `DefaultFailurePolicy.Instance` (singleton, private ctor). P2
must consult a `DelayPlanner` to fill in `RetryInProcess.Delay` (no
longer `TimeSpan.Zero`). Two choices:

- (a) `DefaultFailurePolicy` holds a `DelayPlanner` field, injected via
  ctor. Singleton becomes a "default singleton" (`Instance` =
  `new DefaultFailurePolicy(new DelayPlanner())`); collectors that want
  custom planners build their own `DefaultFailurePolicy` instance.
- (b) `FailureContext` gains a `DelayPlanner Planner` field; runner
  passes it in per-call. Singleton stays stateless but every catch site
  carries the planner.

Recommend (a). Matches D-P1-3 (sealed class with `Instance`); keeps
`FailureContext` lean; Falcon (P4) builds its own `DefaultFailurePolicy`
with a Falcon-tuned planner.

---

**A-P2-4** — `PROGRAMMER_BUG` error-code constant location. **OPEN.**
No existing constant. Two choices:

- (a) Add `public const string ProgrammerBug = "PROGRAMMER_BUG"` next to
  whatever `"ADAPTER_EXCEPTION"` lives near (search the codebase).
- (b) Inline the string literal at the single use site in
  `DefaultFailurePolicy.cs`.

Recommend (a) if a constants file already groups other codes; otherwise
(b). Executor decides at draft time based on grep.

---

**A-P2-5** — Order of `ProgrammerBugClassifier` branch in
`DecideAsync`. **OPEN.**
Three positions are reasonable: (1) FIRST — even before cancellation;
(2) after cancellation, before transport check; (3) after both, before
vendor classification.

- (1) is wrong: `OperationCanceledException` must always cancel; never
  classify as a bug.
- Plan §6 Phase 2 implies a "fast fail" semantics. Place AFTER
  cancellation (preserves D7 cancellation contract) but BEFORE transport
  check — if the underlying cause is a null-deref while reading a
  transport stream, it's still a bug, not transport.

Recommend (2): cancellation first, then ProgrammerBug, then transport,
then vendor classification.

---

**A-P2-6** — Jitter range semantics. **OPEN.**
Plan §6 Phase 2 says "±20% jitter". Two interpretations:

- (a) Relative: `picked_delay * (1 ± 0.2)` — range = 40% of the picked
  delay, centred on it. Larger delays get larger jitter windows.
- (b) Absolute via existing `DelayPlanner._jitterRange` field. The P1
  `PickFrom` already supports an additive absolute jitter (0 to
  `_jitterRange`).

The P1 contract says "Jitter parameter exists but defaults to zero
range". P2 needs ±20% RELATIVE jitter. Current `DelayPlanner`
implementation is one-sided absolute (`baseDelay + 0..jitterRange`).
Must change to symmetric relative (`baseDelay * (1 + uniform(-0.2,
+0.2))`).

Recommend: change the `DelayPlanner` jitter semantics to relative
fraction. Ctor signature changes from `TimeSpan jitterRange` to
`double jitterFraction = 0.0`. Default zero preserves identity. Add
range validation `[0.0, 1.0]`.

---

**A-P2-7** — Test override of DelayPlanner pick delays. **OPEN.**
`DummyCollectorTests` needs zero-delay retries to fix C10. Two paths:

- (a) `DelayPlanner.Default` reads from static `RetryDelays`. Tests
  cannot easily override unless we add a `TimeSpan[]` injection point.
- (b) Subclass `DelayPlanner` in tests with `TestDelayPlanner` returning
  `TimeSpan.Zero` regardless of attempt. Requires making `PickFrom`
  protected virtual OR exposing a `TimeSpan PickFor(int, TimeSpan[])`.
- (c) Add a ctor overload `DelayPlanner(TimeSpan[] unknownDelays,
  TimeSpan[] classifiedDelays, ...)` — tests inject `[TimeSpan.Zero]`.

Recommend (c). Minimal API surface change; production callers continue
to use the parameterless ctor; tests pass deterministic delays. The
classes `UnknownFlowRetryPolicy`/`ClassifiedRetryablePolicy` still
expose their `RetryDelays` as production defaults.

---

**A-P2-8** — Deterministic seed value for jitter tests. **VALIDATED.**
Seed = `42`. Standard, easy to recognise in failure messages, no special
meaning. Tests construct `new Random(42)` and assert exact resulting
delays after multiplication.

---

**A-P2-9** — Resume path's `RetryInProcess.Delay` post-P2. **VALIDATED.**
Per D-P1-7, resume rethrows the marker and the outer
`ClassifiedRetryablePolicy` Polly pipeline drives the cadence. After P2,
`DefaultFailurePolicy` returns `RetryInProcess(planner.PickClassified(
ctx.AttemptNumber), ...)` — but on resume the runner still rethrows the
marker, so the policy's `Delay` is consumed only on fresh-run (where
the runner sleeps and re-enters per D-P1-8). On resume, Polly's pipeline
gets its own delay from its `DelayGenerator` which is ALSO wired to
`DelayPlanner.PickClassified` in P2 — so the same delay sequence
applies in both paths. Path-shape mismatch (Shape A vs Shape B)
unchanged; both shapes consume the same planner output. Convergence
still owed to P5.

---

**A-P2-10** — `ProgrammerBugClassifier` invocation gating. **VALIDATED.**
The runner's policy-invocation gating from P1 (D-P1-12) is reused
unchanged. `ProgrammerBugClassifier` is consulted INSIDE
`DefaultFailurePolicy.DecideAsync` (early branch). The runner does NOT
add a separate pre-check for ProgrammerBug — it would duplicate logic
and create two truth sources. Per A-P2-5, the policy's order ensures
correctness.

---

**A-P2-11** — `FailFast(PROGRAMMER_BUG)` publish behaviour. **VALIDATED.**
Per D3: `FailFast` publishes to host (RabbitMQ completion event)
with `IsRetryable=false`. The existing `FailureAction.FailFast`
branch in the runner already routes to publish. P2 introduces no
new publishing wiring — only the new `FlowExceptionHandling`
construction with `("Unhandled programmer error: …", "PROGRAMMER_BUG",
Severity.Error, IsRetryable=false)`.

---

**A-P2-12** — Logging on ProgrammerBug short-circuit. **OPEN.**
Should the runner / policy emit a distinct log level for
`PROGRAMMER_BUG` (e.g., `LogError` instead of `LogWarning`) so
dashboards alert? Plan does not specify.

Recommend: yes — `logger.LogError(ex, "Programmer bug detected; …")`
in the runner's `FailFast` handler when `ErrorCode == "PROGRAMMER_BUG"`.
Minimal change; high signal-to-noise for the alerting team.

---

**A-P2-13** — `UnknownFlowRetryPolicy.IsUnknownRetryCandidate` excludes
`ProgrammerBugClassifier.IsBug` cases? **OPEN.**
Today, an `NRE` would be a candidate for unknown-retry (passes
`IsUnknownRetryCandidate`). With Phase 2's ProgrammerBug fast-fail,
the policy short-circuits BEFORE the Polly inner pipeline sees the
exception — IF the policy is consulted. Per the runner's gating, the
policy is consulted INSIDE the inner Polly catch block (P1 D-P1-2).
That means Polly's inner pipeline catches the NRE first, then on rethrow
the policy catches it. The classifier short-circuit happens correctly.

BUT — `IsUnknownRetryCandidate` returns `true` for NRE today, so the
inner Polly will retry the NRE 3 times BEFORE the runner's policy
sees it post-Polly. That's wasted retries.

Two choices:
- (a) Don't fix — accept 3 NRE retries before fail-fast publishes.
  Misses C10's spirit (no jitter is the bloat; not the retry count).
- (b) Update `IsUnknownRetryCandidate` to call
  `ProgrammerBugClassifier.IsBug` and return false when true. This is
  a 1-line change but pulls forward classifier wiring.

Plan §6 Phase 2 does NOT explicitly schedule this. Recommend defer to
operator. If chosen (b), it's <10 LoC and rounds out the fast-fail
intent.

---

**A-P2-14** — Test infrastructure for jitter. **VALIDATED.**
`DummyCollector.Test` already references P1 stubs and Moq + xUnit.
Existing `DelayPlannerTests.cs` (P1) tests zero-jitter pick paths.
P2 extends it with new assertions for non-zero jitter under seed=42.
