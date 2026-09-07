* Scope product code changes to `Source/Application/Cymulate.Agent.Application.Actions/Actions/QueryIntegration/Logic/Clients/SIEM/IBM/QRadar`.
* Do not modify QueryIntegration base classes or factory registrations.
* Remove the hardcoded event AQL `LIMIT 100` behavior.
* Use QRadar result pagination through the `Range` request header on `/ariel/searches/{search_id}/results`.
* Preserve existing QRadar authentication, alert querying, advanced custom query behavior, and cache-writing contracts unless directly required for event result pagination.
* Follow existing QRadarApi style and .NET conventions.
* Pass `CancellationToken` through all I/O.
* Add or update focused tests for paged QRadar event result retrieval.
