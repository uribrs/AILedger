# Prompt Contract

Role:
You are a senior data-pipeline engineer working in the cymulate-integration-parsers repo (PySpark parser platform, declarative yaml_engine + deprecated hand-written parsers).

Goal:
Make parser_key `tenable-assets-findings` parse BOTH input generations correctly: old split-lane collector output via the existing deprecated delegate (unchanged), and the new correlated single-lane collector output (v5.0.0 envelopes) via a new declarative yaml_engine lane — one asset per host across chunk envelopes, findings joined to their asset, zero-vuln assets preserved.

Context:
- Target spec: `libs/packages/parsers/yaml_engine/specs/tenable-assets-findings.yaml` (delegate shell today).
- Reference idiom: `specs/tenable-sc-assets-findings.yaml` + `parsers/tenable_sc/tenable_sc_yaml_fns.py`.
- New envelope: `{uuid, chunk, isLastChunk, findingsInChunk, host{assets-export shape}, findings[{asset, output, plugin, port, scan, severity, severity_id, severity_default_id, severity_modification_type, first_found, last_found, state, indexed, source, finding_id}]}`; NDJSON in `findings_NNNNNN.json`, one object per vendor chunk; a host spans multiple envelopes.
- Real sample data: `/Users/user/Dev/cymulate-integration-adapters/logs/published-batches/20260706-102603/collector-run` (17 GB, 2,168 files).
- Mode sniffing, engine seams, dedup/join ordering: see assumptions A1-A6 (VALIDATED with file:line evidence).
- Parity baseline: deprecated `TenableAssetsAndFindings` output contract (A8).

Constraints:
- See constraints.md — binding. Highlights: parser_key/flow ids/zip_file_key unchanged; split mode byte-equivalent via untouched delegate; NO stamp_asset_id/stamped_uuid; engine changes only if a small generic feature is genuinely required; no other specs touched; repo house style; all tests green; commit but never push without user go-ahead.

Success Criteria:
- Hydrated correlated lane parses the real-data fixture into correct frames: exactly one asset row per `host.id` across multi-chunk envelopes; every finding row carries the `asset_id` of its host's surviving asset row; zero-findings hosts still yield asset rows; asset/finding field semantics match the hand-written baseline (deliberate differences documented in execution_notes).
- Split mode regression-safe: `input_mode == "split"` still routes to the deprecated delegate and its output path is untouched by this change.
- A7 resolved from repo evidence and recorded (legacy-hydrated supported via shape-sniff, or replacement documented).
- New tests: per-parser test over the real-data fixture covering multi-chunk dedup, join correctness, zero-vuln survival, and mode routing; harness wiring consistent with SC where applicable; full repo test suite green.
- Spec comments explain the mode routing and the no-stamping decision (SC-style commentary).
- Task artifacts updated (execution_notes.md, assumptions.md statuses, state.json) and mirrored to codex-state.

Execution Rules:
- Do not assume missing data — inspect the real sample data and the deprecated parser for every mapping decision.
- Respect constraints strictly; deviations are stop conditions, not judgment calls.
- Fix review-surfaced bugs directly during execution; reserve questions for genuine forks.

Output Format:
Code changes on branch `feature/tenableio-correlated-parser`, updated task artifacts, and a final execution summary in execution_notes.md listing: files changed, A7/A8/A9 resolutions, parity deltas, test evidence, residual risks.

Stop Conditions:
- Goal achieved (all success criteria met) — stop and hand back.
- A7 turns out to require legacy-hydrated support AND shape-sniffing proves infeasible in the engine seams — stop and surface.
- Field parity requires an engine change beyond a small generic feature — stop and surface.
- Any conflict between task state files — stop and report.
