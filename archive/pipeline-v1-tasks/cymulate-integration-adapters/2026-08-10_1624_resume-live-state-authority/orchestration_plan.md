# Orchestration Plan

## Complexity Decision

- Path: **decompose**
- Axis scores: Complexity **high** | Separability **high** | Coupling **medium** | Dependency order **high** | Execution risk **high** | Worker clarity **high**
- Rationale: Two repos, a substrate change whose blast radius is twelve collectors, and a deliberate
  deletion of shipped behaviour. Five of six axes favour decomposition, and the seams are genuinely
  low-coupling once the substrate's shape is fixed. Execution risk is the deciding axis: a wrong seeding
  change breaks every collector at once, so the evidence that justifies it must be produced before any
  code is written.

## Phasing

Execution is split into two waves. This is a consequence of operator directive 2 (assumptions settled with
evidence, never judgement) and of A4 being the sole justification for touching Infra at all.

- **Wave 1 — evidence.** Six parallel workers settle the load-bearing assumptions against the repos and
  produce an empirical reproduction of the defect. No production code is modified.
- **Wave 2 — implementation.** Planned only after Wave 1 is synthesised. If A4 is refuted, the Infra
  change loses its justification and the plan is re-derived rather than forced.

Re-planning between waves is expected, not a failure. `orchestration_plan.md` is appended, not rewritten.

## Research Decisions

- **None needed.** Every load-bearing OPEN assumption (A1, A2, A3, A4, A7, A8) is settleable against the
  two local repos and the existing test suite — internal behaviour, not external-system behaviour. The two
  external-behaviour assumptions (A5 platform config plumbing, PA1 vendor batch bounds) attach only to B3,
  which the operator moved out of scope; they are carried forward as deferred, not researched.

## Worker Plan — Wave 1

All Wave-1 workers are read-only except W4. None may modify production code. None may perform external
research. Each returns a written verdict with `file:line` citations; plausibility reasoning is not an
acceptable output.

- **W1 — cross-collector conformance survey.** Settles **A4**.
  scope: all twelve resume runners and their flows; determine per collector whether and when collector
  keys reach `AdapterState`, and what a pre-publish deferral of a resumed leg would persist.
  inputs: adapters `origin/dev`, `CollectorResumeSetup.cs`, `AdapterFailureDecisionExecutor.cs`.
  output: per-collector table + explicit A4 verdict.  dependencies: none

- **W2 — recovery-budget collateral analysis.** Settles **A1**.
  scope: every consumer of `AdapterState` in the budget, stuck-gate, and backstop paths; whether seeding
  collector keys changes any computation; any key-name collision between collector and budget keys.
  inputs: Infra `origin/dev`.
  output: A1 verdict + collision list.  dependencies: none

- **W3 — Falcon hook load-bearing audit.** Settles **A2** and **A3**.
  scope: everything the two Falcon resume hooks do beyond state restoration; what survives deletion of the
  findings hook; the exact residual behaviour the assets hook must retain.
  inputs: adapters `origin/dev` Falcon recovery + assets flow.
  output: residual-behaviour spec for the assets hook + A2/A3 verdicts.  dependencies: none

- **W4 — empirical reproduction on `origin/dev`.** Converts the diagnosis from source-derived to observed.
  scope: drive `CheckpointDriftSimulationRunner` so a resumed leg starting at position 2 publishes three
  batches, takes a classified `FALCON_SERVER_ERROR`, and the value handed to `OnCheckpoint` is captured.
  inputs: adapters `origin/dev`; dotnet 8/9/10 SDKs confirmed present.
  output: the observed value (expected 2, correct 5) and a runnable command.  dependencies: none

- **W5 — B1 dependency check and duplication scan.** Serves **B1** and operator directive 4.
  scope: whether anything depends on the Falcon findings flow's own `RestoreProgress` call; and whether a
  critical mass of the same checkpoint-handling logic is duplicated across best-match collectors.
  inputs: adapters `origin/dev`.
  output: B1 safety verdict + a duplication finding that either proposes an Infra move with justification,
  or states plainly that none is warranted.  dependencies: none

- **W6 — packaging and version coordination.** Settles **A8**.
  scope: how adapters pins Infra, what the Infra→adapters release path is, what version numbers this change
  requires, and whether the Infra delta pulls unrelated changes.
  inputs: both repos.
  output: version plan + A8 verdict.  dependencies: none

## Synthesis Approach

Main thread only. W1 and W2 decide whether Wave 2 proceeds as planned: A4 refuted removes the Infra
justification, A1 refuted forces a different seeding shape. W3 fixes the assets hook's residual contract.
W4 supplies the failing test the Success Criteria require. W5 either clears B1 or escalates a duplication
proposal to the operator before any code is written. W6 bounds the release. Contradictions between workers
are resolved against `file:line` evidence, never by preferring the more convenient verdict.

## Verification Obligations

- Cross-check against every Success Criterion in `prompt_contract.md`.
- A4 and A1 must not end NEVER-TESTED — operator directive 2 names them as load-bearing.
- The decisive test must be shown FAILING on `origin/dev` before it is shown passing after the change.
- The hookless-collector case must be covered, or the fix is not demonstrated to be generic.
- Confirm no reachable path applies a leg-start state object over live `AdapterState`.
- Confirm the assets hook retains cursor drop, floor re-anchor, and decline-without-watermark.
- Confirm `IntegrationServiceBus` is untouched in the final diff.
