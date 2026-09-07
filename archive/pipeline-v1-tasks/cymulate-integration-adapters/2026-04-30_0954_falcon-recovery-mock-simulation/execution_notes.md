# Execution Notes

- Contract design started from the previous Falcon partial-page mock task.
- A prior interrupted attempt began unit-test-style changes; the worktree was checked afterward and no such code diff remained.
- Added `--falcon-recovery-simulation <scenario>` to `CollectorRunner`.
- Added `FalconRecoverySimulationRunner`, which starts a local CrowdStrike-shaped HTTP mock server and runs the real `FalconCollector` through token, probe, findings pagination, checkpoint capture, and resume.
- Implemented scenarios:
  - `same-month-after`: fail after checkpointing April page 1, then resume from the saved April `after` cursor.
  - `cursor-expired-fallback`: fail after checkpointing April page 1, then simulate an expired saved cursor on resume and verify fallback completes.
  - `month-boundary-after`: complete April, fail after checkpointing March page 1, then resume from the saved March cursor.
- Successful run command:
  `dotnet src/Cymulate.Integration.Adapters/Tools/Cymulate.Integration.Adapters.Tools.LocalAdapterRunner/bin/Debug/net8.0/Cymulate.Integration.Adapters.Tools.LocalAdapterRunner.dll --falcon-recovery-simulation all --output-dir logs/falcon-recovery-mock --timeout-minutes 5 --dump-payload`
- Successful run log:
  `/Users/user/Dev/cymulate-integration-adapters/logs/local-adapter-runner-20260430-100607.log`
- Successful run artifacts:
  `/Users/user/Dev/cymulate-integration-adapters/logs/falcon-recovery-mock`
- Same-month resume evidence: attempt 1 publishes `v-apr-001` and `v-apr-002`; attempt 2 resumes with `after=apr-a1` and publishes `v-apr-003`, then March and February.
- Cursor-expired fallback evidence: attempt 2 first receives simulated 404 for `after=apr-a1`, then completes with `v-apr-003`, March, and February.
- Month-boundary evidence: attempt 1 checkpoints `monthSegmentStartUtc=2026-03-01T00:00:00Z`, `monthSegmentEndExclusiveUtc=2026-04-01T00:00:00Z`, and `afterToken=mar-a1`; attempt 2 resumes March and completes.
- Note: the current/open-month ceiling drift bug is only visible at sub-second precision in the fast local run because the resumed request happened in the same logged second. A deterministic regression for that specific bug should either inject a fixed clock or delay between attempts.
