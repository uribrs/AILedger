# Research: Cybereason VisualSearch — time-range filter on File node

## Question
Does Cybereason VisualSearch accept a `Between` filter on a `requestedType: "File"` node
using `createdTime` (or `modifiedTime`) as the `facetName`? If not, what is the correct
facet name to filter Files by creation/modification time?

## Sources consulted

Reachable / authoritative:
- msticpy 2.17.2 docs (Microsoft, stable): https://msticpy.readthedocs.io/en/stable/data_acquisition/DataProv-Cybereason.html
- msticpy RST source (verbatim): https://msticpy.readthedocs.io/en/stable/_sources/data_acquisition/DataProv-Cybereason.rst.txt (downloaded; 428 lines)
- demisto/content Cybereason integration (production Cortex XSOAR connector): https://github.com/shaniacht1/content/blob/master/integration-Cybereason.yml (raw downloaded; 1962 lines) — contains the `query_file(filters)` function calling `POST /rest/visualsearch/query/simple` with `requestedType: 'File'`.
- Cybereason 23.2 docs — "Analyze Query Results" / Timeline filter section: https://docs.cybereason.com/en/latest/Hunt_Investigate/analyzeResults.html (only via search-engine snippet; direct fetch blocked from the research sandbox).
- Cybereason 23.2 docs — "Automate Your Hunting" (API examples): https://docs.cybereason.com/en/latest/Hunt_Investigate/automateHunting.html
- Cybereason 23.2 docs — "Build a Query" (filter operator reference): https://docs.cybereason.com/en/latest/Hunt_Investigate/creatingQueries.html
- Cybereason 23.2 docs — "Elements and Features" (schema reference): https://docs.cybereason.com/en/latest/Hunt_Investigate/elements_features.html (only via search snippets; direct fetch blocked).
- Third-party PowerShell wrapper (tobor88/CybereasonAPI) reviewed for File time filters — none found.

Behind auth (could not read):
- Official Cybereason API reference: https://api-doc.cybereason.com/en/latest/APIReference/QueryAPI/queryElementFeatures.html
- https://api-doc.cybereason.com/en/latest/usecaseExamples/fileSearch.html
- Cybereason customer support portal (nest.cybereason.com).

In-repo precedent (for sibling node, not new evidence on File facet):
- `/Users/user/Dev/AgentService/Source/Application/Cymulate.Agent.Application.Actions/Actions/QueryIntegration/Logic/Clients/EDR/Cybereason/CybereasonApi.cs` — `buildEventProcessSearchQuery` already filters the **Process** node with `facetName: "creationTime"` + `filterType: "Between"` (line ~1700). The sibling `buildEventFileSearchQuery` (line ~1744) currently has no time filter on the File node.

## Findings

### Confirmed

1. **`createdTime` and `modifiedTime` exist on the File element.**
   The demisto/content production integration treats them as first-class File features, both as `customFields` to retrieve and as values returned in `simpleValues`. See `integration-Cybereason.yml` lines 1547–1548, 1594, and 1612:
   ```python
   query_fields = ['md5String', 'ownerMachine', 'avRemediationStatus', 'isSigned',
                   'signatureVerified', 'sha1String', 'maliciousClassificationType',
                   'createdTime', 'modifiedTime', 'size', 'correctedPath', ...,
                   'elementDisplayName']
   path = [ { 'requestedType': 'File', 'filters': filters, 'isResult': True } ]
   ```
   and
   ```python
   'CreationTime': timestamp_to_datestring(simple_values['createdTime']['values'][0]),
   'ModifiedTime': timestamp_to_datestring(simple_values['modifiedTime']['values'][0]),
   ```
   This proves the facet names exist on the File schema as readable features. It does **not** prove they are queryable as filter `facetName`s — Cybereason features can be result-only.

2. **`Between` is a documented `filterType`** in Cybereason's VisualSearch DSL. Used pervasively in msticpy and the official 23.2 docs, always paired with millisecond POSIX timestamps in a 2-element `values` array. Example (msticpy stable docs, applied to `Process.creationTime`):
   ```json
   { "facetName": "creationTime", "filterType": "Between",
     "values": [1642752424307, 1643443624308] }
   ```

3. **The Cybereason UI "Timeline" filter explicitly enumerates which Elements it applies to**, and **File is NOT in that list.** From the search-engine snippet of `docs.cybereason.com/.../analyzeResults.html`:
   > "The Timeline filter applies to all Elements in the query that have time-based components, including the **Connection, LogonSession, MalopDetectionEvents, MalopProcess, and Process** Elements."
   This is the strongest hard signal we have. It implies that File is not treated by Cybereason as a "time-based" Element in the same way as Process — its lifetime is "as last reported by the sensor," not a bounded interval. Time facets on File may therefore be filterable only as raw simple-value timestamps (i.e., as `GreaterThan` / `LessThan` / `Between` on the literal long), not as part of the UI Timeline semantics.

4. **No example anywhere in the public surface filters a File node directly by `createdTime`, `modifiedTime`, `createTime`, or `lastModified` with `Between`.** Reviewed:
   - msticpy 2.17.2 (all File examples filter by `elementDisplayName`, `md5String`, `sha1String`, or use `guidList` — never by time).
   - demisto/content (1962 lines) — `query_file()` is only ever called from `query_file_command()` with hash filters; no time filter.
   - tobor88/CybereasonAPI — no File time filters.

### Inferred (NOT confirmed)

- **`createdTime` + `Between` on File is probably accepted by the API.** The DSL is generic: `facetName` + `filterType` + `values` is applied uniformly across element types in every example we found. The same pattern works on `MalopProcess.malopLastUpdateTime`, `MalopProcess.creationTime`, `Process.creationTime`. `createdTime` is a documented feature of File. There is no documented reason it would be filter-rejected.
- However, this is **inference, not confirmation**. Two specific risks:
  1. Cybereason may classify some features as "result-only" (returnable in `customFields`, not usable as `facetName` in a filter). We have no whitelist.
  2. **The facet name on File is almost certainly `createdTime` / `modifiedTime`, NOT `creationTime`.** Note the subtle naming inconsistency in the Cybereason schema:
     - Process → `creationTime` (confirmed by msticpy, official docs, and our existing code).
     - File → `createdTime` (confirmed by the demisto integration as the simple-value key).
     This is a real schema quirk. Using `creationTime` on a File node will likely return zero results or an error.

- **Other candidate names (`createTime`, `lastModified`) are unlikely.** Neither appears anywhere in the public surface for File. They look like guesses based on other vendors' schemas (e.g., Crowdstrike Falcon / SentinelOne). I would not try them first.

## Recommendation

- **Use facet name: `createdTime`** (and, if needed for a "modified within window" variant, `modifiedTime`).
- **Use filterType: `Between`** with `[fromPosixMillis, toPosixMillis]`.
- **Do NOT use `creationTime` on the File node** — that's the Process facet name; on File the field is `createdTime`.
- **Confidence: MEDIUM.** High confidence on the schema name (`createdTime` not `creationTime`) and on the `Between` operator syntax. Medium confidence that the Cybereason server actually permits filtering Files on this facet, because no public example does so and the official "Timeline" doc lists Process/Connection/LogonSession/Malop* but not File as time-based Elements.

## Fallback plan if the facet is rejected

Implement a fail-fast probe + graceful degradation, in priority order:

1. **First attempt: filter on the File node.**
   ```json
   { "facetName": "createdTime", "filterType": "Between",
     "values": [fromPosixMs, toPosixMs] }
   ```
   inside the existing `requestedType: "File"` block.

2. **If the API returns a 4xx, a `status != "SUCCESS"`, or rejects the filter, fall back to filtering on the connected Process or Connection node.** The File element is rarely queried in isolation in real hunts — it is typically reached via `imageFile` from a Process. The cleanest fallback is to invert the join: make the **Process** node the time-filtered result root and pull the File via `imageFile`, mirroring the pattern in `buildEventProcessSearchQuery` (CybereasonApi.cs:1686-1710). That query already uses `Process.creationTime Between [from, to]` successfully, and `imageFile.*` features expose all the File data we already request.

3. **Last-resort post-filter on the client.** Keep the existing keyword-only File query but discard results whose `simpleValues.createdTime.values[0]` (or `modifiedTime`) falls outside the window. This is what the integration currently does implicitly by ignoring time — we'd just make it explicit. Cost: more bandwidth + slower; benefit: zero risk of an API rejection.

4. **Pragmatic recommendation:** ship option (1) wrapped in a try/catch that downgrades to option (3) on the first observed rejection, and log a single warning so we can flip the default if the field gets confirmed rejected in production. Option (2) is a larger refactor and should not block this change.
