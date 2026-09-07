Role:
You are a .NET engineer correcting two terminal-outcome classification defects in a collector runner.

Goal:
(A-M1) Externalize long server-suggested delays as a PartialResult instead of sleeping in-process, with the cap
mapped from the declared `MaxInProcessDelaySeconds`; (A-M2) classify a publish failure after ≥1 emitted page as
partial-success (emitted preserved), not a hard transient failure — matching every other terminal exit.

Context:
- A-M1: `CollectorExecutorSessionFactory.cs:28` hardcodes `ExternalizeServerSuggestedDelays = false`; the
  `catch (ServerSuggestedRetryDelayException)` → PartialResult at `Runner.cs:204` is dead. Shared default is
  true + 24h cap. `RetryOptions` has `ExternalizeServerSuggestedDelays` + `MaxServerSuggestedDelay`.
- A-M2: `Runner.cs:350` returns `TransientFailure("PUBLISH_FAILED")` unconditionally; siblings (:281/:375/:390)
  use `emitted>0 ? PartialSuccess(emitted,page,vendorName,flowName) : <hard fail>`.

Constraints:
- See constraints.md. Salient: only the two RetryOptions fields + the one Runner branch change; reuse Shared
  resilience; no vendor identity; net8.0/CollectorBase.slnx; 79 prior tests stay green (esp. the 1s-Retry-After
  in-process test); the long-Retry-After test must not actually sleep.

Success Criteria:
1. `dotnet build CollectorBase.slnx` clean.
2. `dotnet test CollectorBase.slnx` — 79 prior pass + new tests:
   (A-M1) a LONG Retry-After (> the cap, e.g. 120s vs 60s) on a 429 → run returns a deferred PartialResult
   (externalized), completing fast (no real sleep); a SHORT Retry-After (≤cap) still honored in-process.
   (A-M2) a publish failure AFTER ≥1 emitted page → DONE-with-partial-success (emitted preserved); a publish
   failure with ZERO emitted → transient failure.
3. No regression (full suite green).
4. note.md / execution_notes records: the cap mapping, the partial-success-wins alignment, the two tests + the
   failing-publisher double.

Execution Rules:
- Pin A1 (externalize → immediate PartialResult, no sleep) + A2 (failing-publisher double) + A3 (multi-page
  stream) up front.
- Apply the two specified one-liners; reintroduce a minimal failing `IAdapterDataPublisher` double for A-M2.
- Do not change other behavior. Respect constraints.

Output Format:
- Code: CollectorExecutorSessionFactory.cs (2 fields), CollectorExecutorRunner.cs (1 branch). Tests + the
  failing-publisher double + a long-Retry-After mock in Tests/CollectorExecutor.Test. note + execution_notes.

Stop Conditions:
- The externalized long-delay path actually sleeps (would make the test slow) — adjust threshold + document, or surface.
- A fix would require touching the fixed resilience policy order — stop and surface.
- Goal achieved and full suite green.
