# Constraints

## Architecture / ownership (per CLAUDE.md boundaries)
- Collector owns vendor request logic, pagination, checkpoint *formats*, flow exception
  classification. Shared owns sequencing, session lifecycle, DONE payload, policy ordering,
  recovery budget, terminal publication. Do not re-implement the Shared pipeline.
- Planned yields are a **Resilience-layer decision** (collector classifies → emits a deferred
  decision); the host (ISB/Quartz) owns the wait and re-invokes. Adapter never sleeps in-proc
  for a scheduled wait.

## Additive only
- New config fields, new checkpoint fields, new exception/decision path are **additive**.
- `findings_*.json` and `assets_*.json` output content unchanged.
- Standalone `FalconAssetsFlow` untouched.
- Do not reuse the existing `Stage` field for lane state — add separate Spotlight lane fields.

## Config defaults (in `FalconCollectorConfiguration` + builder)
- `spotlightStatusStages = open,reopen,closed`
- `spotlightStatusStageDelay = 00:10:00`
- `spotlightFindingsYieldEvery = 1000000`
- `spotlightFindingsYieldDelay = 00:05:00`
- `spotlightCursorTtl = 00:02:00` — **internal constant**, not parsed from user config in v1.
- Invalid/empty stage config → fall back to defaults OR validation failure, matching the
  existing builder style (`TryGetInt`/`TryGetBool`/`TryGetDateTime`, `cfg with {...}`).

## Spotlight filters
- Each lane filter = existing suppression filter + `status:'open'|'reopen'|'closed'` + existing
  timestamp/month-segment/AID-scoped/sort/facet behavior.
- Status clause must compose with (not replace) suppression, timestamp range, and AID clause.
- Do not emit an `expired` lane.

## Planned-yield behavior
- Maps to `AdapterResult.PartialResult(delay, waitReason, data)` (the codebase mechanism the
  spec calls "PartialWaitRequired") via a deferred-recovery decision.
- Wait reasons: `deferred-recovery:falcon-planned-yield:spotlight-volume` (5m) and
  `deferred-recovery:falcon-planned-yield:spotlight-status-stage` (10m).
- **Unbudgeted**: planned yields must NOT increment/consume the 5xx recovery `attemptCount`
  budget. A long run yielding many times must never exhaust `MaxRetries`.
- No `PublishFailure` and no completion event on a planned yield.
- Write a **cursorless** checkpoint (cleared `after` tokens, re-anchored from watermark) before
  yielding.
- Volume yield fires only *after* a page is safely published + checkpointed, and never after
  the final page of the final lane.

## Cursor TTL safety (120s)
- On resume: if `CheckpointCreatedUtc` is missing OR older than `120s`, clear `AfterToken`,
  `AssetsStageAfterToken`, `AssetsAfterToken`; re-anchor each from its matching
  watermark/floor when available.
- Apply the same sanitizer across fresh + resume recovery hooks for 401 / 5xx / cursor-expired
  paths.
- This 120s gate is **independent** of the existing ~23h `DefaultStaleThreshold` (whole-
  checkpoint staleness). Do not conflate or replace the 23h gate.
- Cursor may be kept on immediate resume within 120s ONLY if the checkpoint is not from a
  scheduled wait. (All scheduled waits here are >120s and already write cursorless, so this is
  defensive.)

## Failure behavior preserved (no change)
- 5xx: session retries first → Falcon scheduled recovery 5m/15m/30m → fallback.
- Cursor 404 / Spotlight 5xx-with-`after`: watermark fallback.
- Existing 5xx recovery budget + fallback semantics unchanged.

## Engineering style (operator + repo)
- Small methods, helpers, mirror neighboring collectors; no speculative abstraction.
- Use Glossary constants for any shared strings; no magic strings.
- Tests: xUnit + Moq + FluentAssertions, mirror existing Falcon test files. Central package mgmt.
- Pin builds to `net8.0` via csproj TargetFramework; do not pass `-f net9.0`.
- Version bump reflects magnitude (architectural findings-flow change → not a patch bump).
