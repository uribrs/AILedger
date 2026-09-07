# Code Review — Kernel Transport Fault Carry

**Reviewer:** Independent senior-engineer code review (isolated).
**Stack:** C# / .NET 8, Kernel layer (BCL + Microsoft.Extensions.* only).
**Change type:** Shared library code (classification primitives + enriched exception) + test-only code.
**Risk level:** Medium. These predicates gate retry/no-retry decisions, so a misclassification changes runtime failure semantics (hammering a dead endpoint, or giving up on a transient blip). The code itself is stateless and CPU-local; no IO, no concurrency primitives, cold-ish path (per-failure, not per-request-byte).

**Scope reviewed:** the four source files and the three test files + csproj listed in the brief. Polly absence and the static-class-of-constants shape are accepted context and not flagged.

---

## Summary of findings

| Severity | Finding |
|---|---|
| Major | `AggregateException` is not traversed — multi-fault chains (Task.WhenAll, parallel HTTP) bypass both classifiers |
| Major | Top-level `OperationCanceledException` short-circuit can swallow HttpClient timeouts that surface as `TaskCanceledException` |
| Minor | `public static readonly TimeSpan[] RetryDelays` is a mutable shared array; a single caller write corrupts it process-wide |
| Minor | Static constructor throw (`TypeInitializationException`) is a poor failure mode for a compile-time invariant |
| Minor | Depth-10 truncation is silent; deeply nested markers are missed with no signal |
| Nit | `IsCircuitBreakerException` XML doc over-specifies Polly half-open semantics the Kernel cannot guarantee |
| Nit | Test coverage gaps: depth bound, AggregateException, marker case-insensitivity asserted only implicitly |
| Observation | `TransientTransportMarkers` linear scan is fine at this scale; do not "optimize" |

---

## Major

### M1 — `AggregateException` is not traversed by the exception-chain walk
**File:** `HttpTransportFailureClassifier.cs:81-92` (`EnumerateExceptionChain`)

**Problem.** The walk follows `current.InnerException` only. `AggregateException.InnerException` returns just the *first* of its `InnerExceptions`; the rest are invisible to the walk. Parallel/`Task.WhenAll`-style HTTP work and several BCL paths surface faults as an `AggregateException` wrapping multiple `SocketException`/`IOException` instances. If the transient one is not first, both `IsRetryableTransportFailure` and `IsCircuitBreakerException` return false and the failure is misclassified as non-retryable.

**Impact.** A genuinely transient transport failure delivered inside an `AggregateException` (or behind a non-transient sibling) is treated as fatal. This is the exact failure mode the classifier exists to catch. Likelihood depends on whether callers ever await fan-out HTTP work — plausible for a collector/exporter integration layer.

**Recommended fix.** In the walk, when `current is AggregateException agg`, enqueue/flatten `agg.InnerExceptions` rather than relying on `.InnerException`. A small breadth-first walk over a work queue (still depth/visit-bounded) handles this cleanly. Local patch, not a refactor.

**Evidence:** confirmed from BCL semantics of `AggregateException.InnerException` (returns first inner). Whether it manifests depends on caller fan-out — likely risk, not certain.

### M2 — Top-level cancellation short-circuit can swallow HttpClient request timeouts
**File:** `HttpTransportFailureClassifier.cs:54-62`

**Problem.** `IsRetryableTransportFailure` returns false for any `ex is OperationCanceledException` at the top level, then returns true for `TimeoutException`. On .NET, an `HttpClient.Timeout` expiry is thrown as a `TaskCanceledException` (derives from `OperationCanceledException`), in recent runtimes with an inner `TimeoutException`. Because the top-level type is OCE, the method returns false at line 56 before the chain (which would find the inner `TimeoutException`) is ever examined.

The test at `HttpTransportFailureClassifierTests.cs:23-28` actually *locks in* this behavior (`TaskCanceledException` → false) without distinguishing "caller cancelled" from "client timed out." Those two cases want opposite retry decisions, and the current code/test cannot tell them apart.

**Impact.** Request timeouts that arrive as `TaskCanceledException` are classified non-retryable, contradicting the `TimeoutException` → retryable intent stated three lines below. Whether this matters depends on how the caller times out (per-request `HttpClient.Timeout` vs. a `CancellationToken`). If a real `CancellationToken` is the only cancellation source, current behavior is correct; if `HttpClient.Timeout` is in play, it is a bug.

**Recommended fix.** Decide the intended contract explicitly. If timeouts should retry, distinguish a cancellation caused by a user token (`OperationCanceledException` whose `CancellationToken.IsCancellationRequested` is true) from a timeout-induced `TaskCanceledException` (token not requested, or inner `TimeoutException` present), and only treat the former as non-retryable. At minimum, add a test that pins the intended decision for `new TaskCanceledException("timeout", new TimeoutException())`. Local patch.

**Evidence:** confirmed runtime behavior (HttpClient timeout → TaskCanceledException with inner TimeoutException on .NET 6+). Operational relevance is a likely risk pending the caller's timeout strategy.

---

## Minor

### m1 — `public static readonly TimeSpan[] RetryDelays` is a mutable shared array
**File:** `UnknownFlowRetryClassification.cs:38-43`

**Problem.** `readonly` freezes the reference, not the contents. Any consumer can execute `UnknownFlowRetryClassification.RetryDelays[0] = TimeSpan.Zero;` and silently mutate global retry behavior for every other caller in the process. `TimeSpan` being a value type protects individual element identity but not the slots.

**Impact.** Process-wide, hard-to-trace corruption of retry delays. Low likelihood (requires a deliberate or careless write) but high blast radius if it happens, and the field is on a `public` type in shared library code.

**Recommended fix.** Expose `IReadOnlyList<TimeSpan>` backed by a private array, or `ReadOnlyCollection<TimeSpan>`, or `ImmutableArray<TimeSpan>`. Update the static-ctor length check and the test accordingly. Local patch. This is the standard guidance for exposing arrays from a public surface; given the Kernel is shared infrastructure, it is worth doing.

### m2 — Static constructor throwing is a poor failure mode for a compile-time invariant
**File:** `UnknownFlowRetryClassification.cs:16-23`

**Problem.** The invariant `RetryDelays.Length == MaxRetries` is over a `const` and a static array literal — both fixed at compile time. It can only ever break when a developer edits the source. Enforcing it in a static constructor converts that authoring mistake into a runtime `TypeInitializationException`, thrown lazily on first access of *any* member, at an unpredictable call site far from the cause, and (because type initialization failure is cached) poisoning the type for the process lifetime.

**Impact.** Low — the throw essentially cannot fire in shipped code. The cost is the misleading failure surface if it ever does, plus a small first-touch initialization cost. Not dangerous, but the mechanism is mismatched to the problem.

**Recommended fix.** The existing test `RetryParameters_AreConsistent` (`UnknownFlowRetryClassificationTests.cs:10-18`) already asserts this invariant at build/test time, which is the right place. Drop the static constructor, or downgrade it to a debug-only `Debug.Assert`. Local patch. (If the team prefers belt-and-suspenders, leaving it is defensible — flagging the mechanism, not mandating removal.)

### m3 — Depth-10 truncation is silent
**File:** `HttpTransportFailureClassifier.cs:86`

**Problem.** The `depth < 10` bound correctly guarantees termination even on a cyclic `InnerException` chain (good — cycle safety is handled). But past depth 10 the walk simply stops with no signal. A transient marker or `SocketException` nested deeper than 10 frames is silently missed and the failure is misclassified non-retryable.

**Impact.** Low. Real HTTP exception chains are rarely more than 3-4 deep; 10 is comfortably generous. Combined with M1, though, note that an `AggregateException` of many faults could consume the budget shallowly. No action strictly required; if you adopt the M1 breadth-first fix, bound by total visited nodes rather than linear depth.

### m4 — Test coverage gaps
**Files:** test trio.

- No test exercises the depth bound or cycle safety of `EnumerateExceptionChain` (e.g., a self-referential or 11-deep inner chain). The bound is the trickiest part of the file and is untested.
- No test for `AggregateException` input (ties to M1) — its absence is why M1 slipped through.
- Marker matching is asserted with mixed-case inputs (`"Unexpected EOF…"`) so `OrdinalIgnoreCase` is covered implicitly, but there is no explicit "same marker, different case" pairing that would localize a regression if someone changed the comparison to `Ordinal`.
- The classifier tests do not cover a `SocketException` nested inside an inner exception (only top-level socket + nested marker). Minor asymmetry.

None of these are vacuous assertions — the existing assertions are real. These are coverage holes, Minor.

---

## Nits / Observations

### n1 — `IsCircuitBreakerException` XML doc over-claims
**File:** `HttpTransportFailureClassifier.cs:25-29`. The doc states the circuit "transitions to half-open after the break duration." The Kernel method only matches a type name; it knows nothing about break duration or state transitions, and `IsolatedCircuitException` (manual isolation) specifically does *not* auto-recover on a timer. The behavioral claim belongs to the Polly pipeline that lives elsewhere, not this predicate. Trim the doc to what the method does: "matches Polly open/isolated-circuit exceptions by type name." Doc-only.

### n2 — Marker linear scan — do not optimize
**File:** `HttpTransportFailureClassifier.cs:114-122`. Ten markers, each `Contains` over a short exception message, on a per-failure (cold) path. This is correct and clear. A `HashSet`/`Aho-Corasick`/regex rewrite would add complexity for no measurable benefit. Leave it. Observation only.

### n3 — `AdapterHttpRequestFailedException` is clean
**File:** `AdapterHttpRequestFailedException.cs`. `sealed`, immutable getters, sensible null-coalescing, passes `statusCode` through to the base `HttpRequestException` so `.StatusCode` works, all properties test-covered including null-coalescing and the `HttpRequestException` assignability. One observation: the type is not `[Serializable]` and has no serialization constructor — fine for modern .NET (binary serialization is obsolete) and only matters if these cross an AppDomain/remoting/BinaryFormatter boundary, which Kernel code should not. No action.

---

## What is solid
- Cycle termination is handled (depth bound) — no infinite loop on a malformed chain.
- Null handling is consistent: `ArgumentNullException.ThrowIfNull` on every public entry, verified by tests.
- `OperationCanceledException`/`TaskCanceledException` exclusion is intentional and tested (modulo the M2 timeout subtlety).
- Socket-error allowlist is a sensible transient set, fully parameterized in tests, with a negative case (`AccessDenied`).
- Exception type is immutable, sealed, and faithfully extends `HttpRequestException`.
- `UnknownFlowRetryClassification.IsUnknownRetryCandidate` correctly composes over the transport classifier and is tested for the "transport-classified → not unknown" interaction.

## Recommended pre-merge actions
1. Resolve M2: decide and pin (with a test) whether an HttpClient-timeout `TaskCanceledException` is retryable.
2. Address M1: traverse `AggregateException`, or consciously document that callers must unwrap before classifying.
3. m1 (read-only `RetryDelays`) and n1 (doc trim) are cheap and worth doing in the same pass.
