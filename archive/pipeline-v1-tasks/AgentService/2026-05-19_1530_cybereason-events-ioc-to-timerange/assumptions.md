# Assumptions

- A1 [OPEN — pending research]: Cybereason VisualSearch `/rest/visualsearch/query/simple` accepts a `Between` filter on a `File` `requestedType` node using `createdTime` (or `modifiedTime`) as the `facetName`. Resolution path: `research/cybereason-file-time-filter.md`. If research returns LOW confidence, this becomes a blocker.

- A2 [VALIDATED]: The Alert path (`generateCacheByTimeRange(Alert, …, queryByTimeRange, …)`) is already in place and uses `QueryByTimeRangeGroupModel` end-to-end. Source: `CybereasonApi.cs:148-154` and `CybereasonApi.cs:284-320`.

- A3 [VALIDATED]: The Event-IOC pipeline does not share helpers with the Advanced-query path. `tryFetchEventsByAdvancedQueryAsync` (line 635) takes a raw JSON query string from `NormalizedQueryModel.Query` and parses it directly; it does not call `buildEventQuery` / `buildEventMachineSearchQuery` / `buildEventFileSearchQuery`. Therefore modifying those three private helpers is safe.

- A4 [VALIDATED]: Existing tests in `CybereasonApiTests.cs` will pass unchanged after the refactor. Verified by inspection of mock setup at lines 497-500, 661-669, 1198-1201, 1367-1370, 2399-2402, 2810-2812, 3271-3274: each call to `rFakeConfigurer.ConfigureHttpServiceToReturn` keys mocks on `iExpectedUrl` plus `iExpectedPartialContent = "\"requestedType\":\"Machine\""` or `"\"requestedType\":\"File\""`. The search keyword does not appear in any `iExpectedPartialContent`. Test assertions verify response evidences (which are mocked, not derived from request body content).

- A5 [VALIDATED]: `QueryByTimeRangeGroupModel` carries `Hostname`, `DateTimeRange`, `PrivateIps`, `QueryResultType`, `QueryIds`, `IsCustomQuery`, `ResultsLimit` — sufficient for both the Machine and File event queries.

- A6 [VALIDATED]: A single `generateCacheByTimeRange` call with the combined bitmask `eQueryResultTypes.Alert | eQueryResultTypes.Event` is preferred over two separate calls. Justification: the existing `queryByTimeRange` callback already uses a `HasFlag` dispatch pattern (line 296). Adding an `else if` branch for Event mirrors how Humio's converted callback handles both result types in one method.

- A7 [VALIDATED]: Removing `EVP`-style keyword filters from Cybereason VisualSearch by simply omitting the `facetName: elementDisplayName, filterType: ContainsIgnoreCase` filter is sufficient — Cybereason treats absent filters as unconstrained. (Standard VisualSearch semantics: filters are conjunctive; absent filter = no constraint on that facet.)
