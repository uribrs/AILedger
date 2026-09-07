# Orchestration Plan

## Complexity Decision
- Path: **decompose**
- Rationale: spans three repos (legacy reference, .NET adapter, Python parser) in two languages with a hard dependency (the feed-shape contract must be settled before collector/parser code is written). Clean worker boundaries by repo.

## Research Decisions
- Topic: `split-model-and-legacy` — triggered by assumptions **A1** (split-model + preparation.py feed shape; no-vuln-asset representation), **A2** (legacy batchful upload), **A4** design (no-vuln survival / SPARK-explode avoidance) — status: pending.
- Note: this is **codebase-internal** research (our own repos), not external-vendor. Delegated to a read-only Explore worker, not technical-researcher. External Tenable behavior (ACR/export/filters) was already validated this session (see decisions.md) — not re-researched.

## Worker Plan
- **W1 — research (read-only).** scope: determine (a) the exact feed shape the split-model parser + `preparation.py` expect, how no-vuln assets are represented, and how phantom findings are avoided; (b) the legacy TenableCollector's asset+finding shaping and batchful upload mechanism; (c) the adapter's existing `Shared/DataPipeline/Egress` batching + `IResumableAdapter` checkpoint shape. inputs: contract + the three repos. output: `research/split-model-and-legacy.md` resolving A1/A2/A4-design. dependencies: none. **Gates W2 and W3.**
- **W2 — collector .NET rework.** scope: two-feed asset-centric collector in the Uri checkout (assets/export + vulns/export all-severity; remove info filter + `/assets/{id}` hunt; preserve resumability; emit feeds in the shape W1 specifies; wire batchful upload per W1/A2). inputs: W1.output, decisions.md. output: code + execution_notes#w2. dependencies: W1.
- **W3 — parser Python alignment.** scope: align the Tenable parser to the split model per W1 (read both feeds, correlate uuid==id, ACR ← ratings.acr.score, asset field-name deltas, no-vuln survival). inputs: W1.output, decisions.md, prototype feeds. output: code + execution_notes#w3. dependencies: W1. (Independent of W2 — different repo/language; may run parallel to W2 after W1.)

## Synthesis Approach
After W2+W3 return, reconcile the feed contract across both sides (collector emits exactly what parser consumes), confirm the correlation key + ACR repath match, and confirm no-vuln-asset handling is consistent end-to-end. Resolve any contract drift in the main thread before verification.

## Verification Obligations
- Cross-check against prompt_contract.md Success Criteria.
- Defining gate: no-vuln assets survive end-to-end with ZERO phantom/empty findings (run the parser against the prototype feeds incl. synthesized no-vuln rows).
- Sanity gates: assets ≈ ~102K; findings all-severity ≈ ~6.5–6.8M; ~3× volume tolerated (batchful upload).
- Field/correlation mapping exactly per decisions.md.
- Collector builds; parser checks pass; no regressions to other `preparation.py` consumers.

---

## Cycle 2 — corrected wiring

### Complexity Decision
- Path: **direct** (contract-driven execution via one focused worker).
- Rationale: single-repo, coherent re-wire (façade + phase checkpoint) on existing flows; no decomposition benefit.

### Research Decisions
- None needed. Cycle-1 research (`research/split-model-and-legacy.md`) covers the egress/checkpoint patterns; A7 is a platform-routing fact (non-blocking), not external-behavior research.

### Worker Plan
- W4 — scope: re-wire collector — CollectFindings runs assets flow THEN findings flow through ONE shared AdapterProgressContext/egress (co-located lanes); CollectAssets assets-only; phase-aware checkpoint (Falcon Recovery/ style) with correct-phase resume that never re-pulls completed assets. Keep both flows separate; no engine extraction; do not touch cycle-1 endpoints/filters/enrichment. inputs: contract Cycle-2 Addendum, decisions.md Cycle 2, FalconCollector Recovery/. output: code + execution_notes#w4.

### Verification Obligations (cycle 2)
- Verifier MUST confirm from code/inspection: (1) one CollectFindings run → both `assets_*.json` + `findings_*.json` in the SAME batch dir; (2) CollectAssets assets-only; (3) phase checkpoint resumes correct phase, never re-pulls completed assets; (4) cycle-1 endpoints/filters/enrichment unchanged; (5) build green, tests pass (known flake aside). Co-location must be provable, not asserted.
