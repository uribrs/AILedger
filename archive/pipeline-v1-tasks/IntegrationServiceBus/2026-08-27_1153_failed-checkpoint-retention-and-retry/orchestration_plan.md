# Orchestration Plan

## Problem Classification

- Contract sufficiency: sufficient
- Classes (maximum 3):
  - `checkpoint-resume` — the task is entirely about checkpoint retention and the resume path;
    exact match against existing ledger tags (L-1e94a68a, L-57acd253, L-dae004f6).
  - `integration-service-bus` — ISB owns the state and the servicing surface; existing tag.
  - `cross-repo-contract` — the feature's blocking change may live in a shared package
    consumed by ISB and every collector; new lowercase slug, no existing tag matched.
- Additional classified prior art: none beyond the designer's `## Prior Art` block. The
  classified delta over `checkpoint-resume` + `integration-service-bus` returned the same four
  rows already seeded (L-882455c1, L-dae004f6, L-96bb76cc, L-57acd253/L-1e94a68a).
- Recon correction: pending — this task IS the recon, so classification correction happens at
  synthesis rather than before worker fan-out.
- New or changed artifacts:
  - None introduced — recon-only task. The sole output is a cited report; no type, config key,
    DTO, migration, or persisted state is added or changed in any repo.

### Attention Items

| id | failure mode | causal path and impact | planned handling | source |
|---|---|---|---|---|
| R1 | A finding is asserted from naming or file layout rather than read from source, and enters the report as fact | Recon over 4 repos rewards pattern-matching on filenames (`*CheckpointHelper`, `Recovery/`); an inferred uniformity claim would misallocate the per-repo obligation split and understate the change's blast radius | `accept: every claim requires a repo/path:line citation per constraints.md; the verifier resolves a sample of citations rather than trusting them. Not mechanically testable — recon has no test surface.` | constraints.md |
| R2 | The collection-trigger owner is not on this machine, leaving contract section 4 hollow and the retry request path undesigned | Candidate backend repos may be Cymulate platform services checked out elsewhere or not at all; a worker that finds nothing may nominate a plausible repo instead of reporting the gap | `accept: worker briefed to report absence explicitly and fall back to enumerating ISB's inbound message/API surface as the evidence of what a trigger would have to speak.` | task contract §Constraints |
| R3 | `AdapterDoneMessage` is assumed to live in `Cymulate.Integration.Client` and be additively changeable, but prior art shows that package has drifted from its fork in BOTH directions | L-96bb76cc found Client carries 33 stale copies of files ISB deleted and is missing an entire SiemRules delta; a one-directional check would report the wrong definition site and the wrong release cost | `accept: worker W3 briefed to locate the type's real binding via the restored package and Directory.Packages.props, and to diff both directions before stating release cost.` | lessons.md#L-96bb76cc |
| R4 | Collector resume uniformity is overstated because collectors WITHOUT a resume path are invisible to a grep for resume code | L-57acd253 and L-1e94a68a both recorded collectors (Tenable.io, AgentService) that have no checkpoint/resume path at all; enumerating only `CanResumeFrom` implementers counts the haves and silently drops the have-nots, inflating fleet coverage | `accept: worker W2 briefed to enumerate ALL collectors first, then mark which lack a resume path, reporting the denominator not just the numerator.` | lessons.md#L-57acd253, lessons.md#L-1e94a68a |

### Research Questions

No research needed — every question in the contract is answerable from source on this machine.
No vendor, protocol, or external-system behavior gates the recon; the one external-behavior
prior-art entry (L-882455c1, Spotlight cursor expiry) does not bear on whether a checkpoint is
retained or serviced, only on whether a resumed leg later succeeds, which is out of scope.

## Complexity Decision
- Path: decompose
- Axis scores: Complexity medium | Separability high | Coupling low | Dependency order none | Execution risk low | Worker clarity high
- Rationale: Hard trigger — the work spans four repositories with separate trees. Coupling is
  structurally low and read-only, so no worker can conflict with another. Sections of the
  required report map one-to-one onto repo boundaries.

## Research Decisions
- External research: none needed. See Research Questions above.
- Internal recon: this task IS the internal recon; the worker fan-out below is the recon pass.
  Output lands in `research/` per worker rather than in a single `internal-recon.md`.

## File Ownership
- Disjoint sets found: 4 (one per repo/subsystem; all read-only)
- W1 owns: `/Users/user/Dev/IntegrationServiceBus` — checkpoint lifecycle, sweep, cleanup,
  resume entry, servicing surface. Writes `research/isb-checkpoint-lifecycle.md`.
- W2 owns: `/Users/user/Dev/cymulate-integration-adapters` — collector resume-state landscape
  and decline taxonomy. Writes `research/collector-resume-landscape.md`.
- W3 owns: `/Users/user/Dev/IntegrationInfra` plus the restored `Cymulate.Integration.Client`
  package — staleness threshold, parameterization surface, wire contract. Writes
  `research/infra-staleness-and-contracts.md`.
- W4 owns: backend/platform repos under `~/Dev` — trigger ownership. Writes
  `research/trigger-ownership.md`.
- Shared surface frozen in phase 0: the report section shape and citation format, specified
  identically in every brief. No worker produces an artifact another worker consumes, so
  phase 0 requires no separate worker.

## Worker Plan
- W1 — scope: ISB checkpoint lifecycle + minimal service-hub change  owns: IntegrationServiceBus  inputs: contract  output: research/isb-checkpoint-lifecycle.md  phase: 1  continuity: fresh
- W2 — scope: collector resume-state landscape + decline taxonomy (R4)  owns: cymulate-integration-adapters  inputs: contract  output: research/collector-resume-landscape.md  phase: 1  continuity: fresh
- W3 — scope: staleness threshold, parameterization surface, wire contract (R3)  owns: IntegrationInfra + Client package  inputs: contract  output: research/infra-staleness-and-contracts.md  phase: 1  continuity: fresh
- W4 — scope: trigger ownership (R2)  owns: backend repos under ~/Dev  inputs: contract  output: research/trigger-ownership.md  phase: 1  continuity: fresh

All four run concurrently. No dependency order — read-only over disjoint trees.

## Synthesis Approach
Main thread assembles the four worker reports into the 9-section recon report required by
`prompt_contract.md` Output Format, writing `recon_report.md` at the task root. Synthesis
resolves contradictions between workers (notably any disagreement about where a type is
defined vs. where it is bound), corrects the classification, and produces the per-repo
obligation split with sequencing dependencies — which no single worker can see.

## Verification Obligations
- Cross-check against `prompt_contract.md` Success Criteria (10 criteria)
- Dispose A1–A15 plus the 4 prior-art entries, each with actor and citation
- Resolve a sample of cited `path:line` references to confirm they say what the report claims
- Confirm the collector denominator is reported, not just the resume-capable numerator (R4)
- Confirm the trigger-owner section states evidence or an explicit absence (R2)
