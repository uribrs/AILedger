using AILedger.Cli.Assurance;
using AILedger.Core.Assurance;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.Inspection;
using AILedger.Cli.Routing;
using AILedger.Providers.Assurance;
using System.Text.Json;
using static AILedger.Cli.Routing.CliInput;

namespace AILedger.Cli.Providers;

internal sealed partial class ProviderLauncher
{
    private static async Task<ContextArtifact?> PrepareAssuranceAsync(IGovernedTaskService service, CommandLine input,
        GovernedTaskState state, ProviderGrants grants, string ledgerRoot, CancellationToken token)
    {
        var configuration = AssurancePaths(service, input);
        if (configuration is null) return null;
        try
        {
            var subject = SubjectOrActor(input);
            var policy = await AssuranceHost.PrepareAsync(configuration.Value.Authority, configuration.Value.Store,
                subject.Value, state.Roles[subject].Role.ToString(),
                new[] { grants.WorkingDirectory }.Concat(grants.AdditionalDirectories), ledgerRoot, token).ConfigureAwait(false);
            return AssuranceDiscovery(AssuranceConfiguration.Areas(policy, subject.Value));
        }
        catch (AssuranceRefusal refusal) { throw new GovernanceException(refusal.Message); }
        catch (JsonException error) { throw new CliUsageException("Invalid --assurance-authority JSON: " + error.Message); }
    }

    private static (string Authority, string Store)? AssurancePaths(IGovernedTaskService service, CommandLine input)
    {
        var path = input.Optional("assurance-authority");
        var store = input.Optional("assurance-store");
        if (path is null && store is null) return null;
        if (path is null || store is null || service is not ITaskInspector)
            throw new CliUsageException("Supply both --assurance-authority and --assurance-store with the existing inspection service.");
        return (path, store);
    }

    private static ContextArtifact? AssuranceDiscovery(IReadOnlyList<AssuranceArea>? areas) => areas is null ? null :
        new(ContextArtifactKind.Rules, "configured-assurance-discovery",
            "Configured assurance discovery IDs (not acceptance or future readiness). Use inspect_assurance with schema_version:1 and an area_id below; " +
            "use only configured check IDs with run_assurance_checks when granted. " +
            JsonSerializer.Serialize(areas.Select(area => new { area_id = area.Id,
                check_ids = area.Criteria.Select(criterion => criterion.CheckId).Distinct().ToArray() })), []);

    private static async Task<AssuranceService?> OpenAssuranceAsync(IGovernedTaskService service, CommandLine input,
        RunId run, string provider, ProviderGrants grants, string ledgerRoot, CancellationToken token)
    {
        var configuration = AssurancePaths(service, input);
        if (configuration is null) return null;
        var (path, store) = configuration.Value;
        var inspector = (ITaskInspector)service;
        var subject = SubjectOrActor(input);
        var binding = new InspectionBinding(Task(input), subject, run, run.Value, AllowInspect: true);
        AILedger.Providers.Assurance.AssuranceService? assurance = null;
        async Task Authorize(CancellationToken ct)
        {
            AssuranceHost.EnsureProtectedPaths(path, store,
                new[] { grants.WorkingDirectory, ledgerRoot }.Concat(grants.AdditionalDirectories));
            var result = await inspector.InspectAsync(binding, new(), ct).ConfigureAwait(false);
            if (result.Status != "ok") throw new AssuranceRefusal("authorization_denied", result.Diagnostic?.Reason ?? "Existing task/run inspection admission failed.");
            if (assurance is not null)
            {
                AssuranceHost.EnsureGovernedRole(assurance.Role, result.Snapshot!.Role);
                AssuranceHost.EnsureGovernedContext(result.Snapshot.Role, assurance.ConfiguredAreas);
                AssuranceHost.EnsureInputGrants(assurance.AuthorizedInputPaths(),
                    new[] { grants.WorkingDirectory }.Concat(grants.AdditionalDirectories), ledgerRoot);
            }
        }
        try
        {
            // Re-read all mutable setup after run admission. No prospective session is reused.
            await Authorize(token).ConfigureAwait(false);
            var state = await RequireStateAsync(service, Task(input), token).ConfigureAwait(false);
            _ = await PrepareAssuranceAsync(service, input, state, grants, ledgerRoot, token).ConfigureAwait(false);
            assurance = await AssuranceHost.OpenAsync(path, store, new(subject.Value, run.Value, provider, input.Optional("model")), Authorize, token).ConfigureAwait(false);
            return assurance;
        }
        catch (AssuranceRefusal refusal) { throw new GovernanceException(refusal.Message); }
    }

    private static async Task ValidateAssuranceAtUseAsync(AssuranceService? assurance, CancellationToken token)
    {
        if (assurance is null) return;
        try { await assurance.ValidateConfigurationAsync(token).ConfigureAwait(false); }
        catch (AssuranceRefusal refusal) { throw new GovernanceException(refusal.Message); }
    }
}
