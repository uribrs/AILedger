# Orchestration Plan

## Complexity Decision

- Path: **decompose**
- Axis scores: Complexity `high` | Separability `high` | Coupling `medium-high` | Dependency order `strict` | Execution risk `high` | Worker clarity `high`
- Rationale: Four repositories with a hard dependency chain (contract → implementation → consumer) and one
  independent slice. Repo boundaries are natural, low-overlap worker boundaries and the operator asked for
  one worker per repo explicitly. Five of six axes point to decompose; nothing argues for `direct`.

## Phase Structure

The contract carries an operator-mandated hard stop. Execution is therefore two phases, and **Phase B
does not begin in this invocation.**

- **Phase A — pre-implementation.** Settle the gating assumption, run external research, produce scope +
  pseudo code for all four repos. Then STOP for operator review.
- **Phase B — implementation.** Workers, coordinator, verifier and code-reviewer per repo. Begins only
  after the operator approves the Phase A scope document.

## Research Decisions

- Topic: `spotlight-batch-and-schema-limits` — triggered by assumptions A9 and A14, and by prior art PA1 —
  status: **pending**
  - External-vendor behaviour, cannot be resolved from our codebase. Covers FQL `aid:[...]` bounds, page
    limits, rate-limit budget for a lowered batch size, and whether `updated_timestamp` is guaranteed on
    every Spotlight record (which the parser's freshest-wins dedupe depends on).
- Not routed to research, deliberately:
  - **A1** (middle-ground folder invisibility) is *internal* codebase behaviour, not vendor behaviour. It
    is being settled by a read-only code investigation of the parser input-resolution path (`P0` below),
    because the answer lives in our own read path and its existing `tests/test_input_resolver.py`.
  - **A3** (IRSA read+delete on the data bucket) is an environment/IAM fact. It cannot be settled from
    docs and must not be settled by attempting a destructive delete in a production bucket. It is carried
    as a deployment-time verification obligation and will be reported NEVER-TESTED if unresolved.
  - **A7, A10** (Phase 1 speed and host-block size at 100K hosts) are extrapolations from a measured
    5,479-host tenant. No external source can settle them; they are load-test obligations, recorded as
    such rather than researched.

## Pre-Implementation Investigations (Phase A)

- **P0 — settle A1.** Read-only investigation of `cymulate-integration-parsers` input discovery: does a
  subfolder under the `zip_file_key` prefix get ingested? Must return a verdict plus `file:line`
  citations, ranked mitigations, and whether an automated test can assert it.
  **Gates the Phase 1 write-path design.** If the verdict is WOULD-BE-INGESTED, the staging location moves
  outside `storageUrl` and the scope document changes before the operator sees it.
- **P1 — research topic above.**

## Worker Plan (Phase B — not yet launched)

Dependency chain is real; workers are not launched blind or all at once.

- **W1 — IntegrationInfra.** Scope: object-store contract in `Cymulate.Integration.Client/Contracts/`
  (named as a functional sibling of `IAdapterDataPublisher`, NOT `IAdapterCapability` — that name is a
  taken marker interface), request/result models beside `PublishRequests.cs`, and a guarded façade concern
  sibling to `Emission/` owning read-side limits. Inputs: `constraints.md`, `IQueryCacheObjectStore` and
  `IAdapterDataPublisher` as shape prior art, `Emission/MemoryPressureOptions`. Output: compiling contract
  + façade, zero `AWSSDK.*` in `IntegrationInfra.csproj`. Dependencies: **none — must land first.**
- **W2 — IntegrationServiceBus.** Scope: concrete S3 implementation beside
  `Services/S3AdapterDataPublisher.cs`, reusing `S3ClientFactory`, `S3CredentialProvider`,
  `S3UrlParser.ParseObject`; registered in the project's `DependencyInjection.cs`. Inputs: **W1 output**.
  Output: implementation + DI wiring + tests for batched and unbatched paths. Dependencies: **W1**.
- **W3 — cymulate-integration-adapters.** Scope: Falcon two-phase collector — Phase 1 Discover to the
  staging location, frozen key list, Phase 2 aid-batched Spotlight + correlate + publish to `storageUrl`,
  page and staging cleanup, `AidBatchSize` lowered with recorded reasoning, deterministic output page keys.
  Inputs: **W1 output**, P0 verdict, P1 research. Output: collector + tests 2/3/5/6 + the four gap cases.
  Dependencies: **W1**, and P0 for the staging location.
- **W4 — cymulate-integration-parsers.** Scope: replace `dropDuplicates(["id"])` at
  `CrowdstrikeAssetsFindingsCorrelated.py:221` with a deterministic freshest-wins window over
  `updated_timestamp`. Inputs: P1 research (A14), the untracked `test_crowdstrike_live_edge_replay.py` as
  the behavioural oracle. Output: change + green replay tests with the stale-copy behaviour gone.
  Dependencies: **none — runs in parallel with W1.**
- **W5 — documentation.** Scope: one centralized design document (single source of truth) plus four short
  per-repo references, and the mutable-field invariant recorded centrally. Inputs: all worker outputs.
  Dependencies: W1–W4.
- **C1 — coordinator subagent.** Periodic status across W1–W4; collects each worker's raised assumptions
  and deltas from the contract; reconciles contradictions and reports them to the main thread rather than
  resolving them itself.

Launch order: `W1 ∥ W4` → `W2 ∥ W3` → `W5`. C1 spans all of them.

## Synthesis Approach

Main thread integrates. Specifically: reconcile the contract surface W1 published against what W2
implemented and W3 consumed (the most likely drift point); confirm the envelope W3 emits still matches the
invariants in `constraints.md`; confirm W4's dedupe change does not alter envelope-level expectations; then
fold every worker's raised assumptions into `assumptions.md` before the verifier runs.

## Verification Obligations

- Cross-check every Success Criterion in `prompt_contract.md`.
- All six operator-required tests, plus the four named gap cases: deterministic-key overwrite,
  crash-mid-page resume, delete-is-GC-not-cursor, gated delete permission.
- `IntegrationInfra.csproj` still has zero `AWSSDK.*` references — this is a grep, assert it.
- No collector references `AWSSDK.*` or constructs an S3 client directly.
- The new contract is not named `IAdapterCapability`.
- Envelope byte-compatibility, via the existing egress page-hash mechanism.
- Every emission invariant from `constraints.md` preserved, especially the canonical
  `remediation.entities` sort (its removal silently regresses the parser — see PA2).
- Disposition of every assumption A1–A16 and PA1–PA4 with actor and citation. NEVER-TESTED is the default;
  A3, A7 and A10 are the likeliest honest NEVER-TESTEDs and must be reported as risks, not glossed.
- Decision drift for every entry in `decisions.md`, including whether the deliberate supersession of the
  2026-07-02 checkpoint design held.

## Context Budget

The operator asked to be warned rather than silently served reduced coverage. Phase A is deliberately cheap
(two subagents, one document). Phase B is the expensive half. The task directory is the resume point: every
Phase B worker can be launched from `prompt_contract.md` + this plan + the P0/P1 outputs in a fresh session
with no loss of fidelity.
