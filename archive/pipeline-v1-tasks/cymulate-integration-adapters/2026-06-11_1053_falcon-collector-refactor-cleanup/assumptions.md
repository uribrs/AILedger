# Assumptions

- A1 (OPEN, non-blocking): DTOs stay under the existing `Dtos/` namespace unless a
  worker finds a clearly cleaner `Models/` split; either is allowed by the user.
  Executor decides per-case; consistency within a worker's scope preferred.

- A2 (OPEN, RISK): The cursor-expiry → watermark-reset pagination loop is duplicated
  4× but the copies differ in real ways — assets has month-segment windowing +
  terminal-empty-page snapshot persistence; spotlight has repeated-after-token
  detection + filter rebuild + proactive depth cap; AssetIdsFetcher has a streaming
  variant and a dry-run/accumulate variant. A single unified engine may not be
  safely extractable without behavior drift. Assumption: extract the largest
  genuinely-common core (e.g. cursor-drop + watermark re-anchor + depth-cap decision)
  and let each call site keep its specific pre/post logic. If full unification risks
  behavior change, prefer a smaller shared helper over forcing one engine. This is
  the highest-risk item; verifier must confirm behavior parity via the test suite.

- A3 (VALIDATED): "No behavioral change" is verified by the existing
  `FalconCollector.Test` suite passing. New characterization tests are allowed but
  not required; no test may be weakened to make the build pass.

- A4 (VALIDATED): The 400-line ceiling is best-effort. Hard cases:
  `FalconFindingsFlow.cs` (961), `FalconCheckpointHelper.cs` (609),
  `FalconSpotlightVulnerabilitiesRunner.cs` (683), `FalconAssetsFlow.cs` (683).
  Slight deviations acceptable if a forced split would hurt cohesion.

- A5 (VALIDATED): The prior in-conversation analysis (param counts, file:line
  duplication map, ranked targets) is authoritative input. Workers should act on it,
  not re-derive it. Captured in prompt_contract.md Context.

- A6 (OPEN, non-blocking): Whether `SharedFlows` should expand is itself a Phase A
  deliverable (written analysis), not a pre-decided outcome. Default lean: genuinely
  cross-flow helpers (JSON readers, event-emit wrapper, pagination core) belong in
  `SharedFlows`; flow-specific logic stays in its flow. Phase A worker confirms.
