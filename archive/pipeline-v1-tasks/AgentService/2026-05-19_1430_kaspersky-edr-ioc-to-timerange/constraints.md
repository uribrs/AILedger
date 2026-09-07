# Constraints

- Only file modified: `Source/Application/Cymulate.Agent.Application.Actions/Actions/QueryIntegration/Logic/Clients/EDR/Kaspersky/KasperskyEdrApi.cs`.
- Do not touch: base classes (`QueryMakerBase`, `AdvancedQueryMakerBase`), factory, ApiInfo model, ConnectionResult model, auth, response parsing, error handling.
- Do not touch Cybereason or Humio in this PR (Humio already done on this branch).
- Endpoints unchanged: `Session.StartSession`, `EventProcessingFactory.CreateEventProcessing2`, `EventProcessing.GetRecordRange`.
- Request body change limited to removing `EVP_FTX_QUERY` from `pFilter`. Keep `KLEVP_EVENT_HOST_NETBIOSNAME`, `KLEVP_EVENT_RISE_TIME_LEAST`, `KLEVP_EVENT_RISE_TIME_GREATEST`, `vecFieldsToReturn`, `lifetimeSec`.
- Callback signature must match `QueryByTimeRangeCallback<KasperskyEdrApiInfoModel>` exactly — no lambdas / no closure captures.
- No dummy empty-string IOC threaded through old code path; remove the field.
- Delete `queryByIoc` once unused; no dead code.
- Match existing file style: camelCase private methods, `i`/`o` parameter prefixes, `c` private constant prefix.
- Codebase conventions (CLAUDE.md): explicit types, braces on all `if`, `IsNullOrWhiteSpace`, new params before `CancellationToken`.
- No new NuGet packages.
- `dotnet build AgentService.sln` must pass.
- No tests added (none exist for this integration).
