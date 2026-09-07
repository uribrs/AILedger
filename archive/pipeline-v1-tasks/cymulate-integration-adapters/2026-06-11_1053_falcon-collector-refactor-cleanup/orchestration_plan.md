# Orchestration Plan

## Complexity Decision
- Path: decompose
- Rationale: 5–6 bounded, mostly file-disjoint refactor tasks with a clean phase
  boundary; decomposition maps directly to collision-safe parallelism. One coherent
  context would serialize work that is naturally parallel after Phase A.

## Baseline (established before execution)
- Branch `falcon-collection-strategy-refactor`, working tree clean.
- `FalconCollector.Test`: 179 passed / 0 failed. Build green, CodeArtifact auth OK.
- This is the "no behavioral change" contract: 179 tests must still pass.

## Research Decisions
- None needed. Internal refactor against an existing, well-understood codebase; the
  authoritative prior analysis (param counts, file:line duplication map) is captured
  in prompt_contract.md Context. No OPEN assumption depends on external-system behavior.

## Worker Plan

### Phase A — foundational (single worker; BLOCKS Phase B)
- WA — scope: create shared helpers + write SharedFlows analysis; rewire ONLY
  AssetIdsFetcher.cs as the proof-of-use consumer.
  - Owns/edits: new files under `Flows/SharedFlows/` (+ a `Models`/constants file as
    needed) and `Flows/Findings/AssetIdsFetcher.cs`.
  - Deliverables: `FalconJson` (JSON field readers); `FalconCollectorEvents` (event-emit
    try/catch wrapper); shared constants (`_checkpoint.*` metadata keys + UTC format);
    a `progressContext` apply-checkpoint-state helper; the safe common core of the
    cursor-expiry→watermark-reset loop as a SharedFlows helper/engine (per assumption A2
    — extract only what is behavior-safe); written SharedFlows analysis appended to
    execution_notes.md.
  - Does NOT edit: FalconCollector.cs, FalconFindingsFlow.cs, FalconFindingsCheckpointWriter.cs,
    FalconSpotlightVulnerabilitiesRunner.cs, FalconAssetsFlow.cs, FalconCheckpointHelper.cs,
    FalconCollectorConfigurationBuilder.cs (those are Phase B's; WA only delivers the helpers).
  - inputs: contract Context. output: helpers + analysis. dependencies: none.
- Orchestrator builds + runs FalconCollector.Test after WA. Phase B does not start until green.

### Phase B — parallel fan-out (file-disjoint; all consume WA's helpers)
- WB1 — findings trio + DTOs. Owns: FalconFindingsFlow.cs, FalconFindingsCheckpointWriter.cs,
  FalconSpotlightVulnerabilitiesRunner.cs + new DTO files (AID-scoped resume state;
  Spotlight lane/yield state; named return record for the RunAsync 5-tuple). Adopts WA helpers
  in these files. dependencies: WA.
- WB2 — split FalconCheckpointHelper.cs (serializer / deserializer / key-schema / resume-policy);
  public statics remain thin delegators. Owns: FalconCheckpointHelper.cs + new split files.
  dependencies: WA (may use constants/apply-helper if relevant).
- WB3 — FalconCollectorConfigurationBuilder.cs Build-method decomposition (credential decrypt,
  options-override application, session-spec assembly). Owns: that file only. dependencies: WA (low).
- WB4 — FalconAssetsFlow.cs: decompose the ~410-line CollectAsync; adopt WA's pagination
  core + FalconJson + FalconCollectorEvents. Owns: FalconAssetsFlow.cs only. dependencies: WA.
- WB5 — FalconCollector.cs (615 lines): extract the 3 recovery methods (RecoverFreshAsync,
  RecoverResumeAssetsAsync, RecoverResumeFindingsAsync) + ApplyCheckpointState into a new
  internal handler class. MECHANICAL move, zero logic change. Owns: FalconCollector.cs + new file.
  dependencies: WA (low). Highest-care item (public-surface file + recovery behavior).
- Orchestrator builds + runs FalconCollector.Test after all of Phase B.

## File-ownership guard (no overlap within a phase)
- WA: SharedFlows/* (new), constants (new), AssetIdsFetcher.cs.
- WB1: FalconFindingsFlow, FalconFindingsCheckpointWriter, FalconSpotlightVulnerabilitiesRunner, DTOs.
- WB2: FalconCheckpointHelper + split files.
- WB3: FalconCollectorConfigurationBuilder.
- WB4: FalconAssetsFlow.
- WB5: FalconCollector + recovery-handler file.
- No two workers in a phase share a file. AssetIdsFetcher is WA-only; WB1/WB4 must not touch it.
- Workers must NOT run `dotnet build`/`dotnet test` (avoids obj/bin races in the shared tree);
  the orchestrator builds/tests at phase boundaries.

## Synthesis Approach
After each phase, orchestrator integrates in the main thread: build the FalconCollector test
project, run the 179-test suite, diagnose any failure to the owning worker's files, repair or
re-dispatch that worker. Disjoint file ownership keeps failures localized.

## Verification Obligations
- 179 FalconCollector.Test tests still pass; build green on net8.0 (TargetFramework pinned).
- Public/SDK surface + FalconCheckpointHelper public statics unchanged.
- Param-farm methods (15–18 params) replaced by DTOs; 5-tuple replaced by named record.
- Listed duplications removed; helpers homed (SharedFlows analysis written).
- Class line counts: report before/after; flag any remaining >400 with justification.
- Skill docs updated if documented flow/recovery architecture changed.
