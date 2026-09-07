# Prompt Contract: Defender VM Split Collection

Role:
You are a senior engineer working across two repos — a .NET 8 security agent
(AgentService, C#) and a Python integration-parsers library — implementing a collector
output-shape change and downstream parser input wiring.

Goal:
Make the AgentService DefenderVmCollector emit split, non-hydrated `assets_*.json` /
`findings_*.json` output that mirrors the integration-adapters Defender VM collector,
with a 50MB NDJSON byte-cap batch guard and reliable upload-failure propagation; and wire
the Defender Endpoint parser to consume that same split output in DUAL_MODE.

Context:
- Authoritative spec: `/Users/user/Dev/Uri/defender-vm-split-collection-plan.md` (read fully).
- Adapter source of truth: `/Users/user/Dev/cymulate-integration-adapters/.../Collectors/DefenderVmCollector` (RecordFormatter, FindingsFlow, UrlBuilder).
- AgentService target: `/Users/user/Dev/AgentService/Source/CybiCollectors/DefenderVmCollector`.
- Batch-guard reference: `/Users/user/Dev/AgentService/Source/CybiCollectors/CortexXdrCollector/CortexXdrCollector.cs`.
- Parser repo: `/Users/user/Dev/cymulate-integration-parsers/libs/packages/parsers` (defender_vm, defender_endpoint, preparation.py, utilities/input_resolver.py).
- MANDATORY: read `ReadMEs/cybi-attack-flow.md` before changing collector flow; follow `ReadMEs/coding-standards.md` for C#.

Constraints:
- See `constraints.md` (binding). Highlights:
  - Mirror adapter stage order + row shapes exactly; no vulnerability hydration; no in-memory vuln index.
  - Batch guard: 1000 row cap, 50MB NDJSON byte cap, byte count via `Encoding.UTF8.GetByteCount(serializedJson) + 1`.
  - Single row > 50MB → throw clear `InvalidOperationException`; over-cap add → flush first; 1000 rows → flush.
  - Every `SaveUploadAndDeleteBatchAsync` call must check the bool and throw on false.
  - Preserve assets-only `CollectAssetsAsync`.
  - Cortex untouched (50MB cap, not 30MB).
  - Parsers: add `defender-endpoint-assets-and-findings` to `DUAL_MODE_PARSERS`; support `input_mode=="split"`; reuse Defender VM split pre-process; keep Endpoint `process()` output mapping; no output aliasing.
  - Shared `Application.Common` enums/interfaces are append-only wire contracts.

Success Criteria:
AgentService:
- Defender VM findings collection emits BOTH `assets_*.json` and `findings_*.json`.
- Machine rows NOT hydrated with `data.Vulnerabilities[]`.
- Findings rows are flat `sourceType` records: `inventory`, `delta`, `recommendationCatalog`, and `recommendationScopedVulnerability` (with `recommendationReference`).
- No upload batch exceeds 50MB of NDJSON content locally.
- A single row over 50MB fails fast with a clear message.
- A failed batch upload fails the collector.
- Cortex unchanged.
Parser:
- `defender-vm-assets-findings` still consumes split output unchanged.
- `defender-endpoint-assets-and-findings` consumes the same split output.
- Endpoint parser preserves its own downstream output mapping.
- Existing Defender VM parser tests pass; Endpoint split tests added/updated and pass.
Tests:
- AgentService batch-guard unit tests: flush before 50MB, flush at 1000 rows, reject single row > 50MB, throw on upload failure.
- AgentService flow test: mocked pages produce expected `assets_*.json` / `findings_*.json` shapes.
- Parser tests: Endpoint split mode with the same raw rows as Defender VM split tests; output fields/status/source preserved.

Execution Rules:
- Validate the OPEN assumptions in `assumptions.md` against actual source before editing.
- Do not assume missing data; do not change Cortex or the shared uploader.
- Diagnose first, change second; surface genuine blockers instead of working around them.
- Keep changes minimal and local to the Defender VM / Endpoint paths.

Output Format:
- Code changes in both repos, with new/updated tests.
- Update `execution_notes.md` with what changed per repo and validation results.

Stop Conditions:
- Adapter behavior contradicts the plan in a way that changes the target output shape.
- A required upload/flow primitive does not exist as assumed (A2) — surface before improvising.
- Tests cannot be run in either repo.
