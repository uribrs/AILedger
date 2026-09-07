# Cortex XDR Segmentation Research

## Decision
- Endpoint rows should be collected through `/public_api/v1/endpoints/get_endpoint` using `search_from`, `search_to`, and sort; checkpoint `nextSearchFrom`.
- XQL CVE rows should be read through the existing XQL query flow and published in adapter-owned chunks. Do not depend on vendor XQL result pagination because the official `get_query_results` API documents `limit` and stream handoff, but no result offset/page parameter.
- CVE-stage resume should re-run the XQL query with an explicit deterministic `sort` stage and skip already published rows/chunks based on adapter checkpoint state. This can duplicate vendor read cost, but avoids depending on undocumented query-offset behavior.

## Evidence
- Palo Alto `Get Endpoint` docs show request payloads with `search_from`, `search_to`, sort, and filters, and the response includes `total_count`, `result_count`, and `endpoints`. Source: https://docs-cortex.paloaltonetworks.com/r/Cortex-XDR-REST-API/Get-Endpoint
- Palo Alto `Get XQL Query Results` docs define `query_id`, `pending_flag`, `limit`, and `format`; when `limit` is omitted or larger than 1000 and results exceed 1000, a stream id is generated. No offset/page parameter is documented. Source: https://docs-cortex.paloaltonetworks.com/r/Cortex-XDR-REST-API/Get-XQL-Query-Results
- Palo Alto `Get XQL query results Stream` docs retrieve results by `stream_id` and describe chunked response behavior. Source: https://docs-cortex.paloaltonetworks.com/r/Cortex-XDR-Platform-APIs/Get-XQL-query-results-Stream
- Palo Alto XQL language docs state result sets are unsorted unless explicit stages define sort/filter behavior. Source: https://docs-cortex.paloaltonetworks.com/r/Cortex-XDR/Cortex-XDR-3.x-Documentation/XQL-Language-Structure
- Palo Alto XQL command reference documents `sort asc <field>` syntax. Source: https://docs-cortex.paloaltonetworks.com/r/Cortex-XQL-Command-Reference/Example-1-Single-field-sort-ascending

## Resolved Assumptions
- REJECTED: Cortex XDR XQL support for query-level segmentation is sufficient for resumable collection without re-reading the full result set. Public official API docs do not expose result offset paging.
- VALIDATED: If XQL cannot be safely segmented, re-running the XQL query with explicit sort and resuming from an adapter-owned output chunk/index is acceptable for CVE-stage recovery, with the explicit tradeoff that vendor rows before the resume index are re-read but not republished.
- VALIDATED: Endpoint collection in findings flow can use the same `get_endpoint` paging and `search_from` checkpoint shape as the existing assets flow.

## Implementation Guidance
- Keep endpoint stage similar to `CortexXdrAssetsFlow`.
- Keep CVE rows raw enough for upstream hydration; do not join them to endpoint rows.
- Use stable chunk sizes tied to configured page size so checkpoint state can store `nextCveIndex`, `findingsPage`, `assetsPage`, `nextSearchFrom`, and stage.
- Keep an explicit sort in the CVE XQL query before `limit`; resume-by-index is unsafe without deterministic ordering.
- Add tests that inspect request bodies for endpoint search offsets and output target paths for both `findings_*.json` and `assets_*.json`.
