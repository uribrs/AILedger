Role:
You are a senior .NET engineer extending the generic CollectorExecutor interpreter with an async-job
"poll-and-drain" collection capability, without regressing its SDK-conformance or prior remediation.

Goal:
Build the full feature (all four primitives, one task): a `poll_and_drain` step that polls a status
endpoint and drains a growing list of items INTERLEAVED (downloads each newly-available item as it appears,
not after a terminal status); a shared item-failure tolerance policy used by both `for_each` and
`poll_and_drain`; terminal-status classification (fail_states + 404→restart-fresh); and a `best_effort`
hydrate/fetch flag. Plus checkpoint/resume of the processed-item set, the tenable-io.yaml findings
conversion, tests, and a live Tenable verification.

Context:
- Repo: /Users/user/Dev/Uri/localprojects/CollectorBase (net8.0; CollectorBase.slnx).
- Interpreter: CollectorExecutor/Execution/CollectorExecutorRunner.cs (RunFetchStepAsync / RunForEachStepAsync
  / RunPollUntilAsync, the step loop, WriteCheckpoint, the per-emit-target page counter); Profile model in
  CollectorExecutor/Profile/Profile.cs (StepSpec, ConditionSpec); CheckpointState in Checkpointing/.
- Existing building blocks to fuse/reuse: poll_until (poll + until/fail_if + in-process/externalize wait),
  for_each (capture_list/over + per-item step + continue_on_item_failure/max_failure_ratio + yield), the
  per-emit-target monotonic page counter (AssetsPage/FindingsPage), captures/capture_list/accumulate.
- Reference (read-only, model on these, hardcode nothing): adapters monorepo
  Collectors/TenableIoCollector/Flows/Assets/TenableIoAssetsFlow.cs (ProcessChunksProgressivelyAsync,
  FindNewChunkIds, ProcessSingleChunkAsync, MaxSkippedChunkRatio=0.5, MaxChunkRetryAttempts, FAILED/ERROR/
  CANCELLED + 404 handling, ExportTimeoutMinutes, processed-chunk-id resume); CortexXdr XQL client.

Constraints:
- See constraints.md (authoritative). Generic engine; interleaved drain; one shared item-tolerance policy;
  best_effort separable; checkpoint-before-AdvancePage; idempotent resume; reuse the page counter; no
  conformance/remediation regression; async all the way; net8.0.

Success Criteria:
1. dotnet build CollectorBase.slnx clean.
2. dotnet test CollectorBase.slnx all pass, PLUS new in-process-harness tests: (a) poll_and_drain interleaved
   drain — items downloaded while status is still PROCESSING, exit on terminal+drained; (b) item-failure
   tolerance — skip + per-item retry budget + max_failure_ratio abort; (c) terminal fail_state and 404→restart;
   (d) best_effort hydrate — parent emitted when the child fetch fails; (e) resume mid-drain restores the
   processed set (no re-download, no skip).
3. integrations/tenable-io.yaml findings uses poll_and_drain; a live Tenable findings run shows chunk GETs
   INTERLEAVED with status polls in the log (not all after FINISHED) and completes with contiguous
   findings_000001..N.
4. Conformance + remediation intact (full suite green; spot-check adapter wiring + the remediation fixes).
5. poll_and_drain resume restores processed-item set + captured handles + poll anchor; mid-drain re-entry is idempotent.

Execution Rules:
- Build all four primitives; no staging. Keep edits within the named files. Resolve OPEN assumptions A4/A5
  (processed-set persistence shape; externalize-with-processed-set) against the code during execution.
- Model the interleave + tolerance on native; do not assume vendor specifics — drive everything from YAML.
- Respect constraints strictly; async-only.

Output Format:
- Code: Profile.cs, CollectorExecutorRunner.cs, CheckpointState.cs, integrations/tenable-io.yaml,
  Tests/CollectorExecutor.Test. execution_notes.md updated; state.json steps in sync.

Stop Conditions:
- All success criteria met and verified.
- A primitive can't be built without regressing conformance or touching a deferred finding (stop, surface).
- The live Tenable verification is blocked (creds/availability) — fall back to the in-process harness to prove
  interleave + resume, and record that the live check is pending.
