# Implementation prompt — item 64, Part 1

Implement **Phase 1 only — shared contract definitions and plan validation** from:

`/Users/user/Dev/Uri/localprojects/AILedger/Backlog/deliver-contracts-before-dispatch.md`

The repository is `/Users/user/Dev/Uri/localprojects/AILedger`. Item 64 is priority 1. Read the full
current item, especially V1 coverage, phase boundaries and deferred extensions, before changing code.

## Explicit development workflow exception

**Do not invoke ai-kernel or use the kernel to govern this development task. Work directly in this
session.** This is the user's explicit exception for this Part 1 implementation and overrides the
repository/installed-skill directions to open a governed development task or take a ledger brief.

- Do not open/resume a governed development task, build a development brief through `ailedger`, write
  claims/events/refusals to the live ledger, or dispatch development agents through either the kernel
  or the harness. Do not invoke real/billable provider episodes.
- Read and follow the .NET development skill and existing small-method/SRP conventions, while honoring
  this explicit exception wherever another instruction would send development through the kernel.
- You are implementing kernel code, so exercising application/CLI behavior through automated tests
  against **disposable fixtures** is required and allowed. That is different from governing this work
  through the installed CLI. Do not mutate live `.ailedger` histories or the lesson store.
- Preserve existing user changes. Inspect working-tree status first. Backlog item 64 and its prompt
  may be untracked or uncommitted; read them from the working tree. Do not reset unrelated files.
- No installation, global skill changes, merge or push in this task. Do not begin phases 2–4.

## Problem and intended result

In the Glue task, an orchestration plan with the wrong attention-table headers was accepted, then
rejected only when the verifier submitted its output. Part 1 makes the plan contract explicit and
shared, and catches malformed new plans when their producer files them. It also establishes a bounded
inventory for subsequent automatic contract delivery. This phase does not deliver contracts to the
coordinator yet and must not claim that the whole V1 experience is complete.

## Required work

1. Inspect the current implementation and tests. Map only the covered recon/research → planning →
   worker → verifier → reviewer handoffs. Record each action's requirements, required output shape,
   relevant bindings, role/producer ownership and authoritative source. Distinguish executable rules
   from workflow guidance and host-dependent checks. Record missing or conflicting definitions rather
   than inventing a unified policy. Keep this inventory in a concise repository design/coverage document
   linked from item 64; this is an authorized direct development artifact, not a parallel live ledger.
2. Extract the minimal shared definitions needed for these flows, starting with the orchestration
   plan attention-table contract. Keep definitions beside their owning components. Expose the exact
   authoring shape so phase 2 can deliver it, and have producer validation and verifier consumption
   use the same definition. Reuse existing typed contracts and checks elsewhere; the inventory does
   not authorize rewriting every prerequisite into a new framework.
3. Validate newly filed/revised plans at the command-time producer boundary. Diagnose the plan and
   offending shape precisely, rather than attributing an upstream defect to the verifier's submission.
   Preserve valid existing formats, including the supported explicit no-material-attention-items case.
   Inspect current row/identifier behavior before deciding what belongs in the shared validator; do
   not silently add new substantive plan requirements.
4. Preserve historical replay. Trace command-time and replay callers before changing a shared helper.
   A newly tightened filing rule must not make an old legal plan unreadable. When an old malformed
   plan is selected downstream, produce a precise supported-repair diagnostic; do not rewrite it or
   waive the consumer's requirement.
5. Add meaningful tests and update only documentation needed for this phase. Keep Phase 1's completion
   status separate from the overall item and the later client observation.

Useful starting seams (verify against current source):

- `src/AILedger.Core/Artifacts/Logic/ArtifactDocumentRules.cs`
- `src/AILedger.Core/Artifacts/Logic/VerifierOutputRules.cs`
- `src/AILedger.Core/Artifacts/Logic/MarkdownTableReader.cs`
- `src/AILedger.Core/Artifacts/Logic/InternalReconDocuments.cs`
- `src/AILedger.Core/Stages/Logic/StageTransitionPolicy.cs`
- `src/AILedger.Core/Stages/Logic/StagePrerequisiteRules.cs`
- `src/AILedger.Core/Stages/Logic/EntryActionStageRules.cs`
- `src/AILedger.Storage/Inspection/FileGovernedTaskService.Readiness.cs`
- `cognitive/skills/task-orchestrator/SKILL.md`
- `cognitive/skills/workflow-coordinator/SKILL.md`

## Scope exclusions

Do not implement automatic coordinator-response delivery, new worker/researcher return declarations,
brief-size or assurance launch preparation, or reviewer-profile fixes in Part 1. Identify their
contracts/gaps only. Do not add preparation receipts, acknowledgment handshakes, new CLI preparation
statuses, generalized step identities, completion aggregation, launch idempotency/crash recovery,
universal ingress coverage, or a whisperer. Do not weaken recon, consultation, authorization,
candidate freshness, independent assurance or blind-review isolation.

## Validation and completion

Prove through the real filing path in disposable fixtures that:

- A valid plan is accepted; its shared shape is usable by both authoring output and verifier parsing.
- The Glue-style wrong headers are rejected when the plan is filed, with a plan-specific diagnostic
  and no accepted malformed artifact or downstream verifier needed to discover the problem.
- Supported no-attention-item plans and relevant existing revision/ownership rules remain intact.
- Historical events carrying a formerly accepted malformed plan still replay. Selecting that plan
  for verification diagnoses the upstream issue without silently accepting or rewriting it.
- Existing verifier disposition requirements remain enforced after the extraction.

Use focused tests and the full .NET suite. Put build/test outputs outside the checkout where supported
by the repository's existing procedure. No real provider calls or live ledger mutations are needed.
Investigate failures and report actual results; do not substitute a source-only claim for validation.

Finish with a concise report of the implementation, affected files, tests and any remaining limits.
Update item 64 to mark **Phase 1 only** complete if its criteria are met; phases 2–4 remain pending.
If a required check is blocked, record the exact limit and leave the corresponding completion claim
pending. Do not install or claim reduced time/cost: those belong to later authorized work.
