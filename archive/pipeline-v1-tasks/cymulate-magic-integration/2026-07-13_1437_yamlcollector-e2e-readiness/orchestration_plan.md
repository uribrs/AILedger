# Orchestration Plan

## Complexity Decision
- Path: decompose
- Rationale: Three repos, each a self-contained trace scope with a crisp output; low coupling
  (only synthesis needs all three). Parallel workers cut wall-clock and keep each context small.

## Research Decisions
- None needed. All OPEN assumptions (A2–A5) resolve by reading local repo code — that reading is
  the task itself, not external-system research.

## Worker Plan
- W1 — scope: IntegrationServiceBus repo. Trace backend run-message ingress → dispatch →
  adapter activation seam (phases A+B). inputs: /Users/user/Dev/IntegrationServiceBus.
  output: evidence-cited trace of message shape, consumer, routing/selection logic, the exact
  payload handed to the adapter layer, and how YamlCollector vs native adapters are chosen.
  dependencies: none.
- W2 — scope: cymulate-integration-adapters repo. Trace YamlCollectorAdapter activation
  (phase C: YAML resolution, credentials, checkpoint/resume) and collection execution
  (phase D: engine run, batching, S3, events, completion/error), plus how tenable.io maps to
  it. inputs: /Users/user/Dev/cymulate-integration-adapters. output: evidence-cited trace +
  list of gaps/bugs observed. dependencies: none.
- W3 — scope: cymulate-magic-integration repo. tenable.io YAML definition capabilities, ADR
  0001/0002/0003 expectations checklist, canonical engine feature surface relevant to the
  trace. inputs: /Users/user/Dev/cymulate-magic-integration. output: what the ISB route is
  SUPPOSED to look like per ADRs + what tenable.yaml demands from the engine.
  dependencies: none.
- W4 (main thread) — engine drift: mechanical diff of Platform.Integrations.Sdk vs
  Cymulate.Integration.Yaml.Engine + per-repo branch/HEAD snapshot. dependencies: none.

## Synthesis Approach
Main thread merges W1–W4 into the A→B→C→D narrative, reconciles contradictions (esp. the
W1/W2 seam: dispatch payload vs what the adapter expects), produces findings table
(bug/gap/drift/OK + severity), writes final report + mermaid diagram, publishes rendered
artifact.

## Verification Obligations
- Cross-check against prompt_contract.md Success Criteria (phases A–D concrete, findings
  severity-ranked with evidence, drift status, diagram rendered + source, snapshot recorded).
- Verify W1/W2 seam consistency: the payload ISB sends is the payload the adapter parses.
- Verify assumptions A2–A5 got statuses updated.
