# Prompt Contract — TenableIo asset-centric split-model rework

## Role
You are a senior engineer working across a .NET 8 integration adapter and a Python/PySpark parser, converting a vulnerability collector to an asset-inventory-centric, split-data-model design.

## Goal
Ship a TenableIo collector that emits two independent raw feeds (assets + findings) and a Tenable parser aligned to the split data model, such that all assets — including those with no vulnerabilities and all severities — flow through to storage without loss, with v3 ACR populated from the bulk assets feed.

## Context
- Validated this session via an IntegrationProbes prototype (cf4e tenant). See `decisions.md` (validated facts) and `assumptions.md` (open items A1–A5).
- Precedent: the Qualys assets-perspective switch (combined-feed left-join + parser decouple; findings-less assets were lost at 3 points incl. SPARK explode).
- Prototype feeds: `/Users/user/Dev/Uri/localprojects/IntegrationProbes/ProbeResults/TenableIo_20260611_104324_803Z/{assets,findings}.ndjson`.

## Constraints
See `constraints.md` (authoritative). Headline:
- Collect no-vuln assets; collect all severities; preserve every parser-required field; FIXED-skip allowed but all assets required; no-vuln assets must not spawn phantom findings.
- Collector stays dumb (raw JSON, no join/enrichment, no `/assets/{id}` hunt); preserve resilience/resumability of both chunked exports.

## Success Criteria
- Assets feed ≈ ~102K (`last_assessed` 30d ≈ 101,810); UI 102,617 not required (accepted gap).
- Findings feed all-severity ≈ ~6.5–6.8M; downstream path tolerates ~3× volume (A3).
- v3 ACR populated via `ratings.acr.score` → parser `risk_score`; CA-72614 satisfied (null allowed when missing).
- No-vuln assets present end-to-end with **zero** phantom/empty findings (A4) — the defining correctness gate.
- Field/correlation mapping applied exactly per `decisions.md` (uuid↔id, plural→singular, tags key→tag_key, ACR repath).
- Collector builds; parser unit/integration checks pass against the prototype feeds; other integrations using `preparation.py` unaffected.

## Execution Rules
- Resolve open assumptions A1–A5 before or during the matching repo's work; do not guess the split-model feed shape — read `preparation.py` and an existing split-model parser first.
- Do not assume missing data; respect constraints strictly.
- Per-repo subagents: (1) legacy-reference + split-model research, (2) collector .NET changes, (3) parser Python changes. Research (1) gates (2)/(3).
- Legacy repo read-only.

## Output Format
- Code changes in the target collector + parser repos.
- `execution_notes.md` updated per worker (scope, files touched, decisions, residual risk).
- Research findings for A1/A2 in `research/`.

## Stop Conditions
- Split-model feed shape (A1) cannot be determined from `preparation.py` + existing parsers → stop, surface.
- Downstream cannot tolerate ~3× volume (A3) and no batching mitigation exists → stop, surface.
- No-vuln-asset survival (A4) cannot be achieved without an entity-creation-stage change (not ours) → stop, surface.

---

## Cycle 2 Addendum — corrected wiring (collector only)

**Goal:** A single `CollectFindings` run emits BOTH lanes (`assets_*.json` + `findings_*.json`) co-located in ONE batch, so the split parser receives them together. Fix the cycle-1 miss (two separate flows → separate batches).

**Scope:** `src/Cymulate.Integration.Adapters/Collectors/TenableIoCollector` only. Parser + endpoints/filters from cycle 1 are validated and unchanged.

**Constraints (additive):**
- Thin façade by topic: `CollectAssets` → assets flow only; `CollectFindings` → assets flow THEN findings flow, sharing ONE `AdapterProgressContext`/egress address.
- Phase-aware checkpoint spanning the combined run (Falcon Recovery/ style): resume continues the correct phase; never re-pull the completed assets export.
- Keep both flow classes separate; NO shared-engine extraction. Do NOT undo cycle-1 endpoint/filter/enrichment changes.

**Success Criteria (cycle 2):**
1. One `CollectFindings` run produces `assets_*.json` AND `findings_*.json` in the SAME batch dir.
2. `CollectAssets` produces assets-only.
3. Phase checkpoint resumes the correct phase; a mid-findings failure does not re-pull/re-publish the completed assets lane.
4. Endpoints, filters, and enrichment-removal unchanged from cycle 1.
5. Collector builds (net8.0, 0 errors); existing tests pass (the known pre-existing resume-timing flake aside).

**Verifier must explicitly confirm criteria 1–3** (the co-location gap is what cycle-1 verification missed).
