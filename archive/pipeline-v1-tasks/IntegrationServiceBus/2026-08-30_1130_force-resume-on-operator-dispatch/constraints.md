# Constraints

- ISB-only. No other repo, no package release, no migration.
- **Do not add a field to `PlatformEvent`** — it lives in `Cymulate.Integration.Client`. And do
  **not** use `PlatformEvent.Metadata` either: it is serialized into `platform_event_json`
  (`ProcessEventCommandHandler.cs:134`) and would be replayed by the sweep. The flag rides
  `ProcessEventCommand`, beside `CapacityPreAcquired`.
- The automatic recovery sweep keeps the 23h behaviour exactly. Only a marked dispatch overrides.
- A marker must not be able to leak into an automatic dispatch. `CheckpointRecoveryHandler`
  rehydrates `PlatformEvent` from `platform_event_json`, so a marker persisted there would be
  replayed on every later sweep of that row.
- Service hub, not decision hub: ISB obeys the marker; it does not judge whether overriding is wise.
- A forced resume that fails must fail loudly, never fall back to a silent restart.
- Absent marker = today's behaviour, byte for byte.
- Both repository implementations stay behaviourally identical where touched.
- New code names its types. No `var`. Far fewer comments than feel warranted.
- Do not commit, push, or open a PR. Build and test once at the end.
