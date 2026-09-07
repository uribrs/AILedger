# Decisions

- Spotlight runs in ordered status lanes open→reopen→closed after the internal assets stage; no fixed delay before lane 1.
- Lane state lives in NEW checkpoint fields on `FalconFindingsCheckpointState`; `Stage` keeps its assets/findings meaning.
- Planned yields use the existing `AdapterResult.PartialResult` / deferred-recovery path — no new result type. (A6)
- Two planned-yield wait reasons: `deferred-recovery:falcon-planned-yield:spotlight-volume` (5m), `...:spotlight-status-stage` (10m). (A7)
- Planned yields are unbudgeted: they bypass the 5xx recovery `attemptCount`/`MaxRetries` budget. (A11 to confirm)
- No volume yield after final page/final lane; no stage delay after `closed`.
- 120s `spotlightCursorTtl` is an internal constant + a NEW cursor-drop gate, separate from the 23h whole-checkpoint staleness gate. (A8)
- Cursor sanitizer clears `AfterToken`/`AssetsStageAfterToken`/`AssetsAfterToken` and re-anchors from watermark/floor; applied in fresh + resume hooks for 401/5xx/cursor-expired. (A9)
- Planned yield writes a cursorless, re-anchored checkpoint before deferring.
- Volume threshold is cumulative across lanes; yield only after a published+checkpointed page. (A2, A3)
- Existing 5xx (session→5m/15m/30m→fallback) and cursor-404/Spotlight-5xx-with-after watermark fallback unchanged.
- Config builder additions mirror existing `TryGet*` + `cfg with {...}` style; invalid-stage handling mirrors existing builder convention. (A12)
- New tests mirror `FalconCollectorConfigurationBuilderTests`, `FalconUrlsTests`, `FalconRecoveryContinuationTests`, `FalconResilienceStrategyTests`.
