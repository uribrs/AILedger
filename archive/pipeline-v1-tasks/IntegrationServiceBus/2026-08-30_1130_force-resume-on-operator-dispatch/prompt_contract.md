Role:
You are a senior .NET engineer working in the Cymulate IntegrationServiceBus repository.

Goal:
Let an operator-initiated dispatch force the checkpoint resume path, bypassing the collector's
staleness gate, so that the 7-day retention window and the resumable window are the same window.
Leave the automatic recovery sweep's behaviour exactly as it is.

Context:
- Branch `feat/failed-checkpoint-retention-ttl`. The working tree already carries the pass-2
  terminal-status retention change, uncommitted, verified over three verifier and three blind-review
  cycles. A snapshot is at `scratchpad/pass2-terminal-status-baseline.patch`; this task's delta is
  only the marker and its plumbing.
- `ExecuteWithResumeAsync` decides resume on checkpoint presence, then asks `CanResumeFrom`, and
  falls through to `ProcessAsync` when it says no. That fall-through is right for the unattended
  sweep and wrong for an operator resume — it silently recollects.
- The staleness test lives in the gate, not the loaders: Falcon has `IsCheckpointStale` only in
  `FalconCheckpointResumePolicy`; CloudGuard's `CanResumeAssets`/`CanResumeFindings` load first and
  test staleness after. `ResumeAsync` never consults it.
- `PlatformEvent` lives in `Cymulate.Integration.Client`. Its `Metadata` is a free-form string
  dictionary already carrying `SourceQueue`, `AdapterCategory`, `Vendor`.
- `CheckpointRecoveryHandler` rehydrates `PlatformEvent` from `platform_event_json` for automatic
  dispatches.

Constraints:
* ISB-only; no other repo, no package release, no migration.
* No new field on `PlatformEvent`, and **not** `PlatformEvent.Metadata` either — `Metadata` is
  serialized into `platform_event_json` and would be replayed by the sweep. The flag rides
  `ProcessEventCommand`, beside `CapacityPreAcquired`.
* The automatic sweep keeps the 23h behaviour unchanged.
* A marker must not leak into an automatic dispatch via persisted `platform_event_json`.
* Absent marker = today's behaviour byte for byte.
* A forced resume that fails must fail loudly, never fall back to a silent restart.
* Service hub, not decision hub: obey the marker, do not judge it.
* Names types, no `var`; minimal comments; no commit, push or PR; build and test once at the end.

Success Criteria:
* A dispatch carrying the marker, with a checkpoint row present, calls `ResumeAsync` without
  consulting `CanResumeFrom`.
* A dispatch without the marker behaves exactly as before, including the decline-then-restart path.
* The marker cannot reach an automatic sweep dispatch — demonstrated, not asserted.
* The marker is visible in logs when it causes an override.
* A forced resume that fails is reported as a failure; no silent restart.
* `AdapterRunMessage` carries the marker; `PlatformEvent` is unchanged.
* Tests cover: forced resume bypassing the gate; unmarked dispatch unchanged; the sweep unable to
  inherit a marker; and a failing forced resume not restarting.
* Solution builds; affected test projects pass, with Docker-gated tests reported as not-run.
* No migration, no new column, nothing outside this repo.

Execution Rules:
* Do not assume missing data
* Respect constraints strictly
* Read the two archived prior tasks before planning; their citations are audited
* Surface, do not work around, any contradiction with the settled design
* If forcing the resume turns out to need more than the three named pieces, say so before building it

Output Format:
Working tree changes plus `execution_notes.md` recording files changed, where the marker is set and
read, how leakage into the sweep is prevented, and build/test results with Docker-gated projects
called out.

Stop Conditions:
* When the goal is achieved
* When a constraint cannot be satisfied without violating another
* When the change would require touching `PlatformEvent`, another repo, or a migration
