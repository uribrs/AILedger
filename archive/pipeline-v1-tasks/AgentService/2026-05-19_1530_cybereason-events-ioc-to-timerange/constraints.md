# Constraints

- Source file: `Source/.../EDR/Cybereason/CybereasonApi.cs` (always modified).
- Test file: `Tests/.../EDR/CybereasonApiTests.cs` (only if a real test failure requires alignment; do NOT pre-emptively edit).
- Out of scope: base classes, factories, ApiInfo, ConnectionResult, all Models in `Cybereason/Models/`, parse* methods, auth flow, advanced-query path.
- Endpoints unchanged: `/login.html`, `/rest/detection/inbox`, `/rest/detection/details`, `/rest/visualsearch/query/simple`.
- Single combined `generateCacheByTimeRange(Alert | Event, …)` call. No second IOC call.
- `queryByTimeRange` callback must match `QueryByTimeRangeCallback<CybereasonApiInfoModel>` exactly — no lambda, no closure captures.
- No dummy empty-string keyword threaded through old code. Drop `iKeyword` from builder signatures entirely.
- File-node Event query MUST get a time-range filter (otherwise removing the keyword yields unbounded results). Facet identified by research.
- Delete `queryByIoc` and `createEventsCacheByIocAsync` (or rename the latter) once unreferenced. No dead code.
- Naming: legacy camelCase private methods, `i`/`o` parameter prefixes, `c` for private constants.
- CLAUDE.md: explicit types, braces on every `if`, `IsNullOrWhiteSpace`, no NEW `#region` blocks, new params before `CancellationToken`.
- No new NuGet packages.
- `dotnet build` Actions project: 0 errors.
- `dotnet test --filter FullyQualifiedName~CybereasonApi`: all currently-passing tests still pass after refactor.
