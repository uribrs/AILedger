# Prompt Contract — Recovery outcome shape

Role:
You are a senior .NET engineer working on Cymulate's collector platform, spanning IntegrationInfra
(shared contracts), IntegrationServiceBus (the host that dispatches and recovers work), and
cymulate-integration-adapters (the vendor collectors).

Goal:
Make a checkpoint authoritative state-zero at resume. After this task, a resumed run starts from the
recorded position with no interpretation, a refused checkpoint write is visible and actionable, a host
never claims a checkpoint it cannot read, and the distance between the recorded position and the real
stopping point is either zero (cooperative interruption) or measured (non-cooperative).

Context:
Collections run for hours or days on volatile Kubernetes pods. Work is interrupted routinely: pod
restarts, transient vendor errors, deferrals, cancellation. The checkpoint row in Postgres — not the
queue message — is the authority on what remains.

Two failures observed in production on 2026-08-11 motivate this work:

- STG run `6a7b8d01152ba4568bff7e17`: the recovery sweep dispatched a checkpoint to a pod whose Falcon
  adapter accepts `findingsFormatVersion` 4. The checkpoint had been rewritten to version 3 by a pod
  carrying a stale adapter. The collector declined, the run failed, and 34 published objects holding
  1.46M findings were discarded although they were intact in S3.
- The same run: a checkpoint write was refused because the row was claimed by a different pod. The
  refusal was reported to the collector as success, so the losing worker kept collecting.

Constraints:

- See `constraints.md`. All entries there are binding.
- Checkpoint state shapes stay per-collector. Do not unify them.
- The checkpoint moves forward only. No path may rewind it or silently restart a published run.
- Nothing is pushed or merged. Local commits on the named branches only.
- Every change carries a mutation check in a throwaway clone, with red counts reported.
- Never run the DummyCollector or IsbLoadTestCollector test suites.
- Do not create or rebase branches. Use the three named in `constraints.md`.

Success Criteria:

1. One outcome vocabulary exists in IntegrationInfra covering Completed, Deferred, InterruptedRecoverable,
   FailedTransient, FailedPermanent, Cancelled, Superseded and Incompatible, and every existing
   vocabulary maps onto it with the mapping covered by tests.
2. A cooperative interruption in the Falcon findings flow flushes what it holds before the position is
   recorded, and a test proves the resumed run neither re-collects nor skips.
3. A refused checkpoint write reports which clause refused it, and a refusal caused by lost claim stops
   the execution instead of returning success. Both covered by tests.
4. The recovery sweep does not claim a checkpoint whose state version this host cannot read, and leaves
   it for a host that can. Covered by a test.
5. Object naming derives from a single position owned by the collector; no second host-owned counter is
   consulted. Covered by a test.
6. Resume waste is recorded on each resume and is observable in logs.
7. The Falcon test assembly compiles and its tests pass.
8. The uncommitted partial-completion change in `FalconCollector.cs` has a test and a mutation check.
9. Each repository builds with zero warnings and zero errors.

Execution Rules:

- Do not assume missing data. Read the code before asserting behaviour.
- Respect constraints strictly.
- Diagnose before changing. If a fix causes more failures than it resolves, revert and report.
- Report outcomes faithfully. If a test fails, say so with the output.
- If a genuine blocker or contradiction appears, stop and surface it. Do not work around it silently.

Output Format:

- Code changes on the three named branches, committed locally.
- `execution_notes.md` updated with what was changed, where, and the evidence for each claim.
- `review/verifier-N.md` and `review/code-reviewer-N.md` per the pipeline.
- A final report derived from the committed diff, not from the task record.

Stop Conditions:

- The goal is achieved and all success criteria carry evidence.
- Required data is missing and proceeding would risk wrong implementation.
- A constraint would have to be violated to continue.
