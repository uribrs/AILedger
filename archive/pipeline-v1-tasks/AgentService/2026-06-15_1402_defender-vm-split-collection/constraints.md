# Constraints

## AgentService

- Mirror adapter Defender VM collection stage order and output structure exactly.
- Stage order: machines → assets; vulnerabilities (inventory) → findings; vulnerability changes (delta) → findings; recommendations (recommendationCatalog) → findings; recommendation-scoped vulnerabilities (recommendationScopedVulnerability) → findings; software → assets.
- Machine rows: `{"type":"machine","data":{...raw machine...}}`. Software rows: `{"type":"software","data":{...raw software...}}`.
- Findings rows are flat with a `sourceType` discriminator (`inventory` / `delta` / `recommendationCatalog` / `recommendationScopedVulnerability`); scoped rows also carry `recommendationReference`.
- Do NOT embed vulnerabilities in machine rows.
- Do NOT build an in-memory vulnerability index for hydration.
- Do NOT fetch the vulnerability-names endpoint unless the adapter fetches it.
- Findings path must NOT use `AssetEnrichmentService`, `AssetHydrationProcessor`, `AssetVulnerabilityIndexer`, `VulnerabilityNamesCollectionService` (may remain in repo).
- Batch guard: row cap `1000`; NDJSON byte cap `50_000_000`.
- Byte calculation MUST be `Encoding.UTF8.GetByteCount(serializedJson) + 1` (the `+1` is the NDJSON newline). NOT string `.Length`.
- Single serialized row > 50MB → throw a clear `InvalidOperationException`.
- Adding a row that would exceed the byte cap → upload current batch first.
- Row count reaching 1000 → upload current batch.
- Every Defender VM `SaveUploadAndDeleteBatchAsync(...)` call MUST inspect the bool result and throw on `false`.
- Preserve existing assets-only `CollectAssetsAsync` behavior.
- Cortex collector MUST remain unchanged. Use its 50MB cap, not Cortex's 30MB cap.

## Parsers

- Add `defender-endpoint-assets-and-findings` to `DUAL_MODE_PARSERS` in `preparation.py`.
- Teach `DefenderEndpointAssetsAndFindings` to support `input_mode == "split"`.
- Reuse/extract Defender VM split pre-process (load assets lane, filter `type=="machine"`, unwrap `data`, normalize IPs, load findings lane, split by `sourceType`, reconcile deltas, enrich recommendation + exposure names, correlate machine `id` to vulnerability `deviceId`).
- Keep Endpoint parser's downstream `process()` output mapping as the contract; do NOT alias Endpoint output to Defender VM output.
- Existing Defender VM parser tests must continue to pass.

## Cross-cutting

- Shared enums/interfaces in `Cymulate.Agent.Application.Common` are wire/binary contracts — append only, never renumber.
- Follow `ReadMEs/coding-standards.md` for any C# changes.
- Read `ReadMEs/cybi-attack-flow.md` before modifying collector flow.
