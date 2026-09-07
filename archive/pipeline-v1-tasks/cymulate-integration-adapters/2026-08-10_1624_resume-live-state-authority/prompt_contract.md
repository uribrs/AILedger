Role:
You are a senior .NET engineer working on Cymulate's collector substrate (`IntegrationInfra`) and its
vendor collectors (`cymulate-integration-adapters`). You know the layer boundary: the substrate owns
sequencing, session lifecycle, and state persistence; a collector owns vendor request logic, pagination,
checkpoint formats, and flow exception classification.

Goal:
Establish one invariant across the substrate — on every leg, fresh or resumed,
`AdapterProgressContext.AdapterState` is the single live authority for collector state, populated from the
first instruction and never overwritten with an older value. Then remove the Falcon-specific workaround
that violated it, so a resumed leg's deferral can no longer persist that leg's start position.

Context:
- A resumed leg holds two representations of state: the typed object deserialized at leg start
  (`StrategyExecutionState.Current`, get-only, never refreshed, surfaced to hooks as
  `AdapterRecoveryContext.WorkItem`) and the live `AdapterState` the flow updates per published batch.
- `PersistDeferredWaitSnapshot` (`AdapterFailureDecisionExecutor.cs:340-350`) fires `OnCheckpoint`, and ISB
  serializes whatever is in `AdapterState` and full-replaces `adapter_state_json`
  (`AdapterExecutionContext.cs:112`, `CheckpointRepository.cs:79`).
- Defect A: `FalconRecoveryContinuationBuilder.cs:50-55` returns `context.WorkItem`;
  `FalconCollectorRecoveryHandlers.cs:116` applies it over live state. Every deferral on a resumed leg
  persists that leg's start position.
- Defect B: `CollectorResumeSetup.cs:69` seeds only recovery-budget keys. Collector keys are excluded on
  the reasoning that the flow re-derives them — it re-derives into local variables, so nothing populates
  `AdapterState` until the first publish. A pre-publish deferral persists a stripped blob.
- Falcon is the only collector supplying a recovery hook. Eleven others declare `recoverAsync = null`, so
  Defect B is unmitigated for them. Falcon's hook is a mitigation for B and is how it acquired A.
- The ISB guard compares only the `current_page` column, which at snapshot time equals the stored value,
  so `stored <= incoming` accepts the stale write and nothing is logged.

Constraints:
* Branch from freshly pulled `origin/dev`: adapters `6f8b58b3`, Infra `6dccb87`.
* Do not modify `IntegrationServiceBus`.
* A collector recovery hook may only re-anchor vendor-side continuation (dead cursor, query floor). It must
  never restore position, totals, or watermarks.
* Never write a value into `AdapterState` older than what is already there.
* The typed leg-start state object is advisory input to the flow, never a source for persistence.
* Do not silently fall back to `WorkItem` when live state is unparseable after durable progress — decline
  loudly or fail.
* Preserve: pre-publish deferral persists state as loaded; post-publish deferral persists the advanced
  position; the recovery-budget stuck gate and 24 h / 50-deferral backstop behave unchanged; the Falcon
  assets flow still drops its dead `after` cursor across a wait exceeding the 120 s cursor TTL and still
  declines with no watermark; zero-finding hosts still emit their chunk-0 record.
* All eleven hookless collectors must keep working with no per-collector change.
* Infra ships as a NuGet package — bump the Infra version and the pinned version in adapters. Bump
  `CollectorVersion` in `Collectors/Directory.Build.props`.
* No test may assert reference identity between a recovery-hook result and the leg-start state object.
* Out of scope: the rolling ~50 MiB host-atomic publisher; moving the traversal coordinate into
  `current_page`; any change to output naming, addressing, or placement.

Success Criteria:
* A test proves a resumed leg that publishes k batches and then defers persists the ADVANCED position, not
  its start position. This test must fail on `origin/dev` and pass after the change.
* A test proves a resumed leg that defers BEFORE its first publish persists the collector state as loaded,
  not a blob stripped to recovery-budget keys.
* A test proves the same pre-publish guarantee for a collector with no recovery hook, so the fix is shown
  to be generic rather than Falcon-specific.
* `AdapterState` is populated with the persisted collector keys before the flow's first instruction on a
  resumed leg.
* The Falcon findings recovery hook is gone. The Falcon assets hook retains only cursor drop, floor
  re-anchor, and decline-without-watermark.
* No reachable path applies a leg-start state object over live `AdapterState`.
* `FalconRecoveryContinuationTests` no longer asserts `BeSameAs(state)`, and its fixture carries live
  progress that diverges from the leg-start object.
* The two resume-runner harness doubles (`FalconResumeRunnerTests.cs:100` assets, `:295` findings) no longer
  copy `recoveryContext.WorkItem` into state.
* The full Falcon test assembly passes; at least the 143 currently discovered tests still run.
* The adapters solution builds against the bumped Infra package with no other collector modified.

Execution Rules:
* Do not assume missing data. Every claim about behaviour must cite `file:line`, a test name, or a run.
* Respect constraints strictly.
* Stop and surface before starting a step that a listed blocker gates.
* Prefer the smallest change that establishes the invariant. Do not refactor adjacent code.
* If seeding at resume turns out to disturb the recovery budget, stop and report rather than adjusting the
  budget to compensate.

Output Format:
1. `execution_notes.md` — per step: repo, files touched with line ranges, what changed, and the citation
   that establishes it works. Append; do not rewrite.
2. The code changes themselves, in two branches, one per repo.
3. The new and rewritten tests, named so the guarantee is readable from the test name.
4. A short handover block: Infra version, pinned version in adapters, new `CollectorVersion`, and the exact
   command that runs the Falcon assembly.
5. An explicit statement of any success criterion not met, and why.

Stop Conditions:
* When every success criterion is met and the Falcon assembly passes.
* When a blocker (B1, B2, B3) gates the next step and the operator has not resolved it.
* When required data is missing.
* When the change would require touching `IntegrationServiceBus` to succeed.
* When a fix causes more test failures than it resolves — revert and report.
