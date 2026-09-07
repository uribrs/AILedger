# Constraints

## Scope
- Adapters repo ONLY (`/Users/user/Dev/cymulate-integration-adapters`).
- IntegrationInfra is OUT OF SCOPE. Decided; not to be re-opened during execution.
- Cymulate.Http.Package is OUT OF SCOPE. The `ResponseHeadersRead` boundary is not to be moved.
- `HttpTransportFailureClassifier` may be CONSUMED from Infra (already public, vendor-agnostic).
  It may not be modified.
- Branch `fix/falcon-spotlight-transport-retry`, cut from `dev` @ `77ec50cb`.

## Behaviour
- A skipped aid batch must be impossible by construction. Phase 2's frozen key list is the universe;
  every host must land in exactly one published object, including zero-finding hosts. Falcon has no
  claim/sweep model to catch an omission (TenableIo does — that is why TenableIo may skip and Falcon
  may not).
- The retry must sleep INSIDE `FetchAsync`, abandoning and restarting the scroll, so no live
  Spotlight `after` cursor (120s vendor TTL) is held across a backoff. This preserves the pump's
  load-bearing "never hold a live cursor while blocked" invariant.
- Exhaustion must produce an unbudgeted deferral, not a `PublishFailure`.

## Repo rules
- No version bumps in any `.csproj`. `CollectorVersion` lives in `Collectors/Directory.Build.props`
  and is the operator's call — suggest, never set.
- No commit and no push without explicit per-change approval.
- Package versions are centrally managed in `Directory.Packages.props`; add `<PackageReference>`
  without a version.
- Never name a type/method/field "Legacy".

## Test discipline
- Test stack: xUnit + Moq + FluentAssertions.
- NEVER run the full `FalconCollector.Test` suite — it hangs on the risky simulation classes.
  Filter to change-relevant classes only: `FalconFlowExceptionClassifierTests`,
  `FalconResilienceStrategyTests`, `FalconSpotlightConcurrencyTests`,
  `FalconCorrelatedFindingsTests`, plus any new class.
- Never run ISBLoad or DummyCollector test suites.
- Run tests at phase boundaries, not continuously.
