# Task: Sequential block execution for the YAML engine

## Intent

Restructure `YamlCollector` execution so that the existing YAML vocabulary is expressed internally as explicit, composable execution blocks. Use TPL Dataflow only as the sequential execution substrate when it improves composition and lifecycle control; it is not a replacement language and must not leak into YAML.

This is an evolutionary architecture task. This plan is deliberately reviewable and amendable rather than pretending to specify every implementation detail before characterization work. Each phase must update `state.json`, assumptions, decisions, and execution notes so another engineer or Codex task can resume safely.

## Why

The YAML already describes independent concepts—operations, workflows, polling, capture, iteration, publication, pagination, hydration, and merge enrichment—but the current implementation distributes their orchestration across large mutable runners. Explicit block boundaries should make the declared structure visible in code, make ordering and outcomes testable, and allow features to compose without adding more conditional branches to a central runner.

The objective is structural clarity, not concurrency. A single collector execution must remain globally sequential: no simultaneous block delegates, no page/request overlap, and no background publication racing later work.

## Observed baseline

- YAML bank: `/Users/user/Dev/cymulate-magic-integration/integrations`.
- Surveyed corpus: 279 YAML files, 885 operations, all structurally parseable at survey time.
- Most definitions use direct operations. Only `defender-vm.yaml` and `tenable.io.yaml` currently declare workflows.
- High-value native parity set: Defender VM, Tenable.io, CrowdStrike Falcon, InsightVM Cloud, and Qualys.
- Existing enrichment/streaming work is recorded in the two predecessor task directories and is present as uncommitted work on `feature/yaml-engine-declarative-enrichment`.
- Architectural inventory: `src/Cymulate.Integration.Adapters/Collectors/YamlCollector/TECHNICAL_MAP.md`.

## Proposed internal model

The existing loader continues to parse and validate the existing model. A compiler then creates an immutable execution plan from that model. The plan uses domain-owned nodes rather than YAML-aware control flow spread through runners:

- `SequenceBlock`: ordered composition; exactly one child active at a time.
- `OperationBlock`: cohesive reusable leaf for request/authentication, pagination, error rules, response extraction/mapping, hydration, and sink interaction.
- `PollBlock`: repeated sequential invocation of an operation with delay and stop conditions.
- `CaptureBlock`: extracts named workflow state from a completed invocation.
- `ForEachBlock`: sequentially invokes a child plan for each source item.
- `PublishBlock` or publication annotation: commits output through the existing sink and checkpoint boundary.
- `MergeBlock`/sink decoration: preserves the predecessor task's per-page streaming enrichment semantics without reintroducing held replay.

The exact class split is not frozen by this document. The stable architectural boundary is `existing YAML -> existing validated model -> immutable execution plan -> sequential executor -> existing engine services/sinks`.

## Outcome model

Blocks must return an explicit domain outcome instead of communicating expected control flow through generic exceptions. The minimum outcome vocabulary is:

- success/continue;
- retry (transient attempt inside the current execution policy);
- defer (durable stop with a resumable checkpoint and next-run intent);
- terminal failure;
- cancellation.

This design must close the current workflow gap where retry/defer-style operation results can be flattened into `YAML_WORKFLOW_FAILED`.

## Phased execution plan

### Phase 0 — Reconcile and characterize the live baseline

1. Read the current branch, working tree, both predecessor task records, and the current engine code before editing.
2. Attribute overlapping files and preserve all existing uncommitted changes. Do not restore, reset, or reconstruct predecessor work.
3. Run the current engine and YamlCollector tests and record the actual baseline, including any pre-existing failures.
4. Characterize direct operations, workflow outcomes, publication ordering, checkpoint ordering, resume, fingerprint mismatch, streaming merge, and cancellation with tests before changing orchestration.
5. Resolve or explicitly defer the canonical engine-home question between this repository and `cymulate-magic-integration/Platform.Integrations.Sdk`.

Exit: a reviewed baseline note, characterization suite, dependency decision, and an approved implementation location. No executor replacement begins before this exit.

### Phase 1 — Define the domain plan and compiler

1. Introduce immutable plan-node contracts, execution context, typed outcome, and compilation diagnostics.
2. Compile direct operations into a single-operation plan and workflows into ordered composite plans.
3. Keep all existing YAML models, schema, vocabulary, defaults, validation errors, template rules, and operation names intact.
4. Compile all 279 bank definitions in a compatibility test without editing them.
5. Add plan-shape tests for the representative native-matched YAMLs.

Exit: every current YAML that loads also compiles; invalid definitions fail at load/compile time with actionable messages; no production execution path has changed.

### Phase 2 — Add the strictly sequential execution substrate

1. Implement a domain executor whose public contracts do not expose TPL Dataflow types.
2. If Dataflow is approved, configure bounded capacity 1 and enforce one globally active execution token. Do not rely only on `MaxDegreeOfParallelism = 1`, because separate blocks could otherwise overlap.
3. Define completion, fault propagation, cancellation, and resource-disposal behavior explicitly.
4. Prove with instrumentation tests that requests, transforms, publication, and checkpoints never overlap.

Exit: executor lifecycle and strict sequencing are verified independently; no collector production path has switched.

### Phase 3 — Migrate the direct-operation path behind an internal seam

1. Route classic `search_repository`, `lookup`, `action`, and `push` operation execution through compiled `OperationBlock` plans behind an internal selection seam.
2. Preserve request generation, auth, retry/error rules, pagination, response mapping, hydration, streaming ingest, output naming, progress, recovery budget, and done-event behavior.
3. Compare legacy and new execution results using contract-level tests; do not dual-publish.
4. Validate CrowdStrike hydration and Qualys error/control semantics explicitly.

Exit: direct-operation parity is demonstrated; the old path remains removable but available for controlled fallback until workflow parity is complete.

### Phase 4 — Migrate workflow composition

1. Implement ordered stages as `SequenceBlock` composition.
2. Add poll, capture, and sequential foreach composites without changing their YAML.
3. Preserve topic publication and the streaming merge sink-decoration behavior already implemented by predecessor work.
4. Validate Defender VM first (simple workflow), then Tenable.io (poll/capture/foreach), then synthetic tests for currently unused vocabulary.
5. Preserve typed retry/defer/failure outcomes across workflow boundaries.

Exit: both real workflow YAMLs have behavioral parity and defer/retry outcomes retain their domain meaning.

### Phase 5 — Make checkpoint and resume semantics explicit

1. Define the durable unit-of-work cursor for every block type and version the checkpoint representation if needed.
2. Preserve the invariant that persisted state points to the next safe unit before advancing execution.
3. Reconcile Shared-restored progress/recovery state with engine fingerprint mismatch. A fresh engine cursor must not silently continue with stale host counters/output numbering.
4. Preserve target-page resume for streaming merge and document unavoidable restart-fresh cases.
5. Verify no loss, duplication, output-path collision, or mixed accounting across cancellation, failure, defer, and restart.

Exit: resume matrices pass for direct operations, workflows, pagination strategies, merge targets, and fingerprint mismatch.

### Phase 6 — Cut over and simplify

1. Switch production wiring only after all parity gates pass.
2. Remove superseded orchestration code and mutable `Last*` result state; do not leave two permanent engines.
3. Keep feature-specific implementation close to its block or existing subsystem; avoid a generic block framework that obscures the YAML concepts.
4. Synchronize or port the accepted implementation to the second engine home according to the Phase 0 decision.
5. Update technical maps and operator/developer documentation.

Exit: one production executor, full test suite green, corpus compile green, native parity matrix green, and no YAML migration.

## Validation matrix

Minimum representative definitions:

| Definition | Primary concern |
|---|---|
| `defender-vm.yaml` | sequential workflow, capture, multiple topics |
| `tenable.io.yaml` | poll, capture, foreach, workflow resume |
| `crowdstrike-falcon.yaml` | hydration/id-list behavior |
| `qualys.yaml` | error rules, `set_control`, defer/retry semantics |
| `insightvm-cloud.yaml` | native parity and enrichment behavior |
| all 279 YAMLs | unchanged load and plan compilation |

Also cover authentication variants, every pagination strategy supported by the engine (including those not yet used by the bank), XML conversion, mappings/transforms, template expansion, streaming sinks, cancellation, and checkpoint compatibility.

## Review and continuation protocol

- Treat this directory as the handoff record and `state.json` as current truth.
- At every phase boundary, update statuses, validated/rejected assumptions, decisions, verification evidence, changed files, and next action.
- Create a new decision entry when changing intent; do not silently rewrite historical decisions after implementation starts.
- Keep commits phase-sized and independently reviewable when implementation is authorized.
- If concurrent branch changes touch planned seams, stop, reconcile behavior, and update the plan before continuing.
- Do not claim parity from unit tests alone; record corpus, representative YAML, and collector-level evidence.

## Not in scope by default

- Changing YAML syntax, vocabulary, or files.
- Introducing parallel stages, concurrent foreach, request pipelining, or speculative prefetch.
- Adding DAG semantics (`depends_on`, branch/join scheduling) that the current YAML bank does not require.
- Rewriting authentication, pagination, mapping, or sinks merely to make them look like standalone blocks.
- Changing Shared contracts, ISB host contracts, S3/output layout, or done-event payloads.
- Vendor-conditional behavior in the engine.

