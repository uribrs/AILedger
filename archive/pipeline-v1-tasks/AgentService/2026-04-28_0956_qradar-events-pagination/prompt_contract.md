Role:
You are a .NET backend engineer and vendor API integration specialist.

Goal:
Replace IBM QRadar event query truncation caused by a hardcoded `LIMIT 100` with QRadar REST result pagination.

Context:
The QRadar SIEM client lives under `Source/Application/Cymulate.Agent.Application.Actions/Actions/QueryIntegration/Logic/Clients/SIEM/IBM/QRadar`. `QRadarApi` currently builds standard event searches with an AQL `LIMIT 100` and fetches `/ariel/searches/{search_id}/results` once. IBM QRadar documents result paging with a zero-based `Range: items=x-y` header on supported GET endpoints, and `/ariel/searches/{search_id}/results` supports that header.

Constraints:

* Scope product code changes to the QRadar SIEM vendor directory unless tests require changes under `Tests`.
* Do not change QueryIntegration base classes, factory wiring, authentication flow, alert response parsing, or advanced custom query semantics beyond shared paged result retrieval.
* Remove the hardcoded standard event AQL `LIMIT 100`.
* Fetch QRadar search results page by page using `Range` headers.
* Use `Content-Range` when available to stop after the final page; fall back safely when `Content-Range` is absent.
* Preserve cancellation behavior and asynchronous I/O.
* Propagate QRadar result-fetch exceptions to the standard time-range cache writer so `tryWriteCacheDetailError` records failed pagination instead of a successful empty/no-results cache.
* Follow existing QRadarApi style and keep methods small.
* Add or update focused tests for QRadar event pagination and query construction.

Success Criteria:

* Standard QRadar event queries no longer include `LIMIT 100`.
* Event result retrieval can return more than one page of results from `/ariel/searches/{search_id}/results`.
* Pagination stops when QRadar indicates the last page or when a partial/empty page is returned.
* Mid-pagination failures are reported through the existing cache-error path and are not written as successful empty/no-results caches.
* Existing QRadar alert and advanced custom query tests remain compatible.
* Relevant QRadar tests pass, or any test blocker is reported with exact command output.
* Task state and execution notes are updated after implementation.

Execution Rules:

* Do not assume missing data.
* Respect constraints strictly.
* Use official or official-adjacent QRadar docs for pagination behavior.
* Keep implementation localized and avoid unrelated refactors.
* Run a final verifier against the original request and produced changes.

Output Format:
Final response must include changed files, QRadar pagination behavior implemented, verification result, and any residual risk.

Stop Conditions:

* When the goal is achieved.
* When required data is missing.
* When task state conflicts with the contract.
* When sandbox or approval restrictions block required verification.
