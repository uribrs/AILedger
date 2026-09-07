# Assumptions

- VALIDATED — `Job/AdapterTriggerParsing.cs` ≡ `Conducting/.../CollectorTriggerParsing.cs` logic-identical. (diff run 2026-07-08: namespace/class-name/doc-comments only.)
- VALIDATED — zero call sites for either trigger-parsing class inside src/. (rg usage sweep 2026-07-08.)
- VALIDATED — `IoaUploadRequest.cs` requires `IoaRuleRequestConverter` to compile (`[JsonConverter(typeof(...))]` + using Shared.Converters).
- VALIDATED — no unit tests exist in the adapters repo for the seven carried files (rg over UnitTests/ 2026-07-08) → nothing to mirror.
- VALIDATED — both Legacy executors are internal static classes referenced only by `AdapterBusFlowExecutor.cs` and `CollectorResumeRunner.cs` respectively (rg 2026-07-08); rename blast radius is those call sites + any docs.
- VALIDATED — `BaseFlowHandler`'s only dependencies are `Cymulate.Integration.Sdk.Models` + `Microsoft.Extensions.Logging` (+ carried `ITopicHandler`). Confirmed on carry: compiles clean.
- VALIDATED — Job.Tests covers the Conducting duplicate's cases AFTER porting: `TryParseBool("no"/"y")` rows and null-input `TryParseUtcDateTime` were the only gaps; both ported before deletion.
- OPEN — no consumer outside this repo references `Cymulate.IntegrationInfra.Conducting.Collectors.Triggers.CollectorTriggerParsing` (package is 1.0.0-preview.3; only known consumer experiment is the adapters repo, which still uses Shared). Treated as safe; surface in report if evidence contradicts.
