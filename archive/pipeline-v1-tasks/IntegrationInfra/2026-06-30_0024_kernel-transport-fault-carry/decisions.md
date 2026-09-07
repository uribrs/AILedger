# Decisions (established — inputs, not to be re-debated)

- **D1 (operator):** Split `UnknownFlowRetryPolicy` along its dependency seam; relocate the transport-fault
  vocabulary into Kernel. Fixed — not open for re-litigation.

- **D2:** The dependency seam carries **all Polly-free members** into Kernel — `MaxRetries`, `MaxAttempts`,
  `RetryDelays`, the static-ctor length invariant, and `IsUnknownRetryCandidate`. Only `CreatePipeline`
  (the sole Polly consumer) is excluded. Rationale: the seam is defined by the Polly dependency, and
  `MaxRetries`/`RetryDelays` are zero-dep data — splitting them off would fracture the
  `MaxAttempts = MaxRetries + 1` relationship and the `RetryDelays.Length == MaxRetries` invariant across
  the seam (DRY violation). `CreatePipeline`, when later carried, consumes these from Kernel.

- **D3:** `CreatePipeline` stays in the source repo for now; ownership of pipeline construction is not
  assigned to any of the three domains in this task (operator-stated). See assumptions A3/A4.

- **D4:** Placement — `AdapterHttpRequestFailedException` → `Kernel/Exceptions/` (joins the two sibling
  exceptions already there); classifier + seam + retry residue → new `Kernel/Transport/` room.

- **D5:** Kernel type holding the Polly-free residue is renamed to reflect its reduced responsibility
  (pipeline construction has left it). Proposed: `UnknownFlowRetryClassification`. Naming is the one point
  of executor discretion within SOLID/clarity; the original name may be retained if preferred. Behavior is
  unchanged either way.

- **D6 (standing — every carry):** SOLID separation of domains, DRY, excellent XML docs, documentation,
  and tests are standing goals for all relocation work, not just this task.

- **D7:** Source of truth is the battle-tested in-production `Shared` library; relocations preserve
  behavior verbatim (namespace-only rewrites), same spirit as the prior Kernel carry (commit 9630194).

- **D8:** Code-reviewer findings (review/code-reviewer-1.md) all describe **pre-existing source behavior**,
  not relocation defects. Per the verbatim constraint + operator rule (no change to battle-tested code
  without a unification/separation/reshaping decision), they are **recorded, NOT repaired** in this carry.
  Repairing M1/M2 here would silently diverge the "verbatim relocation" from the production source.
  Deferred for an operator decision (candidate "reshaping of ops" — and any fix must be decided for the
  SOURCE too, not just the carried copy):
  - **M1 (latent):** chain walk follows `InnerException` only — an `AggregateException` exposes just its
    first inner fault, so a transient fault behind a non-transient sibling is misclassified non-retryable.
  - **M2 (latent):** top-level `OperationCanceledException` → false short-circuits before the inner
    `TimeoutException` check; an `HttpClient.Timeout` surfaces as `TaskCanceledException` and is treated
    as non-retryable, contradicting the `TimeoutException → retryable` intent.
  - **m1:** `public static readonly TimeSpan[] RetryDelays` is a mutable shared array (reference frozen,
    contents not). Source shape, carried verbatim.
  - **m2:** static-ctor invariant throws `TypeInitializationException` lazily; the unit test already
    covers the invariant. Mechanism note only.
  - **m3:** depth-10 chain truncation is silent.
  - **nit:** `IsCircuitBreakerException` XML doc over-claims Polly half-open/break-duration semantics the
    type-name match cannot guarantee.
  - **m4 (ours, safe to act on later):** test coverage could add characterization tests for the depth
    bound and the `AggregateException` / `TaskCanceledException(inner: TimeoutException)` edges — these
    would PIN current behavior (incl. the M1/M2 quirks), not change it.
