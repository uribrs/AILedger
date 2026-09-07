# Decisions

- The override is carried as an explicit marker on the dispatch, not inferred from checkpoint age,
  trigger origin, or anything else. Inference is what makes an override unexplainable later.
- The marker rides `PlatformEvent.Metadata` rather than a new `PlatformEvent` field, because that
  type is shared-package surface and a field there is a cross-repo release.
- The gate is bypassed wholesale rather than selectively. `CanResumeFrom` returns a bare bool for
  both "stale" and "damaged"; `FalconResumeDecline` distinguishes them but is `internal` and never
  escapes. Plumbing the reason out is not worth it — a damaged blob fails fast and visibly, which is
  an acceptable outcome for a deliberately-pressed button.
- Proceeding on unverified: no collector re-checks staleness inside `ResumeAsync` or its load path.
  Evidence covers Falcon and CloudGuard; the rest of the fleet is inferred from the same shape.
  If wrong: a forced resume is still declined for those vendors, and fails rather than restarting.
- Proceeding on unverified: the marker cannot reach an automatic dispatch. If wrong: the sweep
  overrides the 23h bound fleet-wide, which is the one outcome this change must not produce.

## Design correction made before any code was written

- **The marker does NOT ride `PlatformEvent.Metadata`.** `ProcessEventCommandHandler.cs:134` writes
  `PlatformEventJson = JsonSerializer.Serialize(platformEvent)`, and `Metadata` is a serialized
  property — a marker there would be persisted into `platform_event_json` and replayed by
  `CheckpointRecoveryHandler` on every later automatic sweep of that row, overriding the 23h bound
  fleet-wide. That is the one outcome this change must not produce, and the original design would
  have produced it.
- **It rides `ProcessEventCommand` instead**, as an init-only flag beside the existing
  `CapacityPreAcquired` — which is precedent for exactly this: an ISB-internal dispatch flag that
  never reaches the wire or the row. The sweep builds its own command
  (`CheckpointRecoveryHandler.cs:390`) and does not set it. Leakage is designed out rather than
  guarded against: the flag is not on the serialized type at all.
