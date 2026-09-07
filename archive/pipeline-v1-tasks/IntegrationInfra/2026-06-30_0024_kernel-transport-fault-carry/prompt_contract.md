Role:
You are a senior .NET library engineer relocating battle-tested primitives into a clean Kernel layer.

Goal:
Carry the transport-fault vocabulary from the in-production `Shared` library into
`IntegrationInfra/Kernel`, splitting `UnknownFlowRetryPolicy` along its dependency seam so the Kernel
stays Polly-free — behavior preserved verbatim, with XML docs, documentation, and unit tests.

Context:
- Source (reference-only, DO NOT mutate): `/Users/user/Dev/Uri/localprojects/IntegrationsInfra/Session/TransportErrorHandling/`
  (4 files) — battle-tested, in production.
- Target: `/Users/user/Dev/Uri/localprojects/IntegrationInfra` — net8.0, `RootNamespace` `Cymulate.IntegrationInfra`.
  Only Kernel has code today; concern folders are skeleton READMEs.
- Kernel charter: pure, dependency-light utilities; depends on nothing beyond `Microsoft.Extensions.*`.
- The 4 source files and their seam are fully analyzed in decisions.md (D2) and assumptions.md.

Constraints:
- See constraints.md. Behavior verbatim; Kernel stays Polly-free; source repo not mutated;
  `CreatePipeline` NOT carried; net8.0; build clean + tests pass; XML docs on every public member.

Success Criteria:
1. `Kernel/Exceptions/AdapterHttpRequestFailedException.cs` — relocated verbatim, namespace
   `Cymulate.IntegrationInfra.Kernel.Exceptions`, XML docs intact/complete. (Note: source XML doc
   references `AdapterHttpClient` via `<see cref>` — that type is not carried; rewrite the reference to
   plain text so the doc compiles without a dangling cref.)
2. `Kernel/Transport/HttpTransportFailureClassifier.cs` — relocated verbatim, namespace
   `Cymulate.IntegrationInfra.Kernel.Transport`, behavior identical (markers, socket codes, chain walk).
3. `Kernel/Transport/IHttpFailureClassifier.cs` — relocated verbatim, same namespace.
4. The Polly-free residue of `UnknownFlowRetryPolicy` (`MaxRetries`, `MaxAttempts`, `RetryDelays`,
   static-ctor length invariant, `IsUnknownRetryCandidate`) relocated into `Kernel/Transport/` (per D2/D5);
   `CreatePipeline` NOT present anywhere in IntegrationInfra; no `Polly` reference reachable from Kernel.
5. Every relocated/new public member has an accurate XML doc comment.
6. `Kernel/Transport/README.md` created (charter-style: what the room holds, the Polly-free boundary,
   and the note that pipeline construction lives outside Kernel); `Kernel/README.md` "Holds:" line updated
   to include the transport-fault vocabulary.
7. Test project established (none exists): unit tests covering
   `HttpTransportFailureClassifier.IsRetryableTransportFailure` (transient markers, socket codes,
   cancellation=false, timeout=true, inner-exception chain), `IsCircuitBreakerException` (type-name match),
   `IsUnknownRetryCandidate` (cancellation/http/timeout/transport all → false; genuinely-unknown → true),
   and `AdapterHttpRequestFailedException` shape (property mapping, null-coalescing defaults, RetryAfter).
8. `dotnet build` clean (0 errors); all tests pass. Build/test output recorded in execution_notes.md.

Execution Rules:
- Do not assume missing data; do not mutate the source repo.
- Preserve behavior verbatim — only namespace rewrites and the seam split.
- Append a dated action log to execution_notes.md as you go (crash-resilience is a hard requirement).
- Respect constraints strictly. Naming discretion only where D5 allows.

Output Format:
- Relocated `.cs` files + new README + updated Kernel README + test project, under IntegrationInfra.
- execution_notes.md with an action log and final build/test results.

Stop Conditions:
- A relocated primitive cannot preserve behavior without a logic change → stop, surface.
- Carrying the residue would require pulling Polly into Kernel → stop, surface (would violate the seam).
- Required source information is missing or contradictory → stop, surface.
