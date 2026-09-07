# Decisions

## Operator decisions — settled

- **D1 — The task is named "Engine API Contract"**, not "task 3". Narrowing is the mechanism; the
  deliverable is the contract the package commits to under SemVer.
- **D2 — The engine is not consumer-facing beyond its entry points.** Operator, on being shown that
  authenticators and paginators are factory-dispatched from YAML: *"okay, so it is not consumer facing,
  got it."* Implementation families stay registered, not public.
- **D3 — Fold the four prerequisites into this task as phase 1**, rather than shipping them as a
  separate release. One breaking change for the consumer instead of two, and the guards get written
  once. *(Recommended and accepted — see the sequencing note in `prompt_contract.md`.)*

## Method decisions

- **D4 — Validation has three legs, and leg (a) alone is insufficient.** Static C# reachability
  measures entry points; per-member traversal decides most cases; YAML-driven dispatch is evidence for
  `internal`, not against it. Any narrowing justified by leg (a) alone is unsupported.
- **D5 — "Plugged in" is proven by a corpus conformance test, not by visibility.** Every
  `authentication.type` and `pagination.strategy` present in the 279-definition corpus must resolve to
  a registered implementation. This check does not exist today and is strictly stronger than the status
  quo, where nothing verifies that an authored YAML key binds to anything.
- **D6 — The result gets pinned by a test.** The repo already has Roslyn-based architecture tests
  (`Architecture/EngineSources.cs`, `EngineFile.cs`, `RuleAudit.cs`, `DependencyRuleTests.cs`,
  `RuleComplianceTests.cs`); the surface pin belongs beside them. Without it this is a one-time cleanup
  that decays.
- **D7 — Nothing is deleted.** The task narrows, decomposes and adds. A type that appears to have no
  purpose is still not removed under this task; removal needs its own justification and its own change.

## Recorded corrections to earlier positions

- **D8 — The "13 reachable vs 89 unreachable" framing is retired.** It measured names in consumer
  source, which for a YAML-composed engine is entry points only. The honest decomposition is: 13 entry
  points, a small traversed member-set hanging off them, 6+6 factory-dispatched implementation
  families whose contract is a YAML key, and the remainder pure internals.
- **D9 — `IsCollector` assembly metadata is NOT the engine's concern and was misdescribed in the
  adapters task.** Recorded here only to prevent the wrong rationale being carried across repos: ISB
  discovers adapters by the assembly *filename* pattern `Cymulate.Integration.Adapters.*.dll`
  (`AdapterLoaderOptions.cs:18`), and nothing reads the `IsCollector` attribute. Irrelevant to this
  task; noted so it is not re-derived wrongly.

## Deferred, with reasons

- **The `InternalsVisibleTo` decision is IN scope and must be made explicitly** (`CLAUDE.md` requires
  it at exactly this point and forbids adding it earlier). It is listed as a deliverable, not a
  decision, because it depends on how much ends up internal.
- **Shared/SDK retirement** (`/Users/user/Dev/IntegrationInfra/IntegrationInfra.slnx`) — operator FYI,
  explicitly "not a call to action as yet". Out of scope. Relevant only in that the adapter's coupling
  to both is now localized to ~7 files, so this task need not anticipate it.
