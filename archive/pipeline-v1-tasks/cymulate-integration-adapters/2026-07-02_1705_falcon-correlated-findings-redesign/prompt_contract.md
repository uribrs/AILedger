# Prompt Contract — Falcon Correlated Findings Redesign

Role:
You are a senior .NET/PySpark integration engineer working across cymulate-integration-adapters and cymulate-integration-parsers.

Goal:
Replace the Falcon findings flow with the validated asset-driven correlated model, make the parser consume the new records (while still parsing legacy split batches), and add a per-page content hash to shared egress logging.

Context:
- Behavioral spec: prototype `/Users/user/Dev/Uri/localprojects/IntegrationProbes/Integrations/Falcon/FalconCorrelatedFindingsProbe.cs` — mirror its traversal, chunking, and re-anchor semantics; do not re-derive.
- Current flow to replace: `FalconFindingsFlow` + lane/segment machinery under `Collectors/FalconCollector/Flows/Findings/`; assets stage machinery under `Flows/Findings/Hosts/` becomes the traversal driver.
- Parser today: split-only façade `parsers/deprecated/crowdstrike/crowdstrikeAssetsFindings.py` + `CrowdstrikeAssetsFindingsNotHydrated.py` + yaml spec `crowdstrike-assets-findings.yaml`; it already builds the correlated shape internally (LEFT join on aid, `embed_as="vulnerabilities"`).
- Full decisions, constraints, and validated evidence: `decisions.md`, `constraints.md`, `assumptions.md` in this directory.

Constraints:
- All items in `constraints.md` apply verbatim (architecture boundaries, vendor limits, egress caps, memory bounds, testing, docs sync, version bumps).
- Workstream boundaries: W1/W2 in adapters repo (one coherent change), W3 in parsers repo, W4 in Shared egress (collector-agnostic); W4 must not depend on W1–W3.
- Record schema and knobs are fixed by `decisions.md` (chunk cap ~2000, strip apps/suppression_info, sorted remediation.entities, findings_*.json naming, no assets lane in CollectFindings).
- Parser must auto-detect input shape and keep legacy split batches parsing (parser-first rollout).
- Checkpoints: watermarks + aid-batch position only; never persist cursors; re-anchor on expiry through the existing Resilience layer.

Success Criteria:
- Adapters solution builds; Falcon test suite green with lane/segment tests replaced by traversal/chunking/checkpoint/re-anchor tests.
- Parser tests green for BOTH shapes (legacy split fixtures + new correlated fixtures).
- End-to-end: LocalAdapterRunner run on the lab tenant (base 2026-04-01) emits correlated `findings_*.json` that the fixed parser ingests to the same asset/finding outputs as the legacy pair (parity methodology from the prototype phase).
- Egress publish-completion log line carries a content hash for every published page, all collectors, with no measurable publish slowdown.
- Docs/skills updated: FalconDocs/CollectorDocs, Collectors/README.md, ai/skills lane/segment references.
- Major collector version bump applied; checkpoint version bumped with explicit no-cross-version-resume handling.

Execution Rules:
- Do not assume missing data; consult the prototype and the evidence files first.
- Respect constraints strictly; fix review-surfaced bugs directly during execution (no mid-execution scope questions unless a real fork).
- Run tests at phase boundaries, not per-edit; partition parallel work by file ownership.

Output Format:
- Code changes in both repos; `execution_notes.md` appended per workstream with what changed and how it was verified; updated docs; updated state.json step statuses.

Stop Conditions:
- Goal achieved (all success criteria met), or
- A constraint cannot be satisfied without violating another (surface, do not work around), or
- Required data is missing (e.g., parser fixture conventions ambiguous after inspection).
