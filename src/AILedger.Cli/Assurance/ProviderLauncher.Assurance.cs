using AILedger.Cli.Assurance;
using AILedger.Core.Assurance;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.Inspection;
using AILedger.Cli.Routing;
using static AILedger.Cli.Routing.CliInput;

namespace AILedger.Cli.Providers;

internal sealed partial class ProviderLauncher
{
    private static async Task<IAssuranceService?> OpenAssuranceAsync(IGovernedTaskService service, CommandLine input,
        RunId run, string provider, ProviderGrants grants, string ledgerRoot, CancellationToken token)
    {
        var path = input.Optional("assurance-authority"); var store = input.Optional("assurance-store");
        if (path is null && store is null) return null;
        if (path is null || store is null || service is not ITaskInspector inspector)
            throw new CliUsageException("Supply both --assurance-authority and --assurance-store with the existing inspection service.");
        AssuranceHost.EnsureProtectedPaths(path, store, new[] { grants.WorkingDirectory }.Concat(grants.AdditionalDirectories));
        var subject = SubjectOrActor(input);
        var binding = new InspectionBinding(Task(input), subject, run, run.Value, AllowInspect: true);
        AILedger.Providers.Assurance.AssuranceService? assurance = null;
        async Task Authorize(CancellationToken ct)
        {
            var result = await inspector.InspectAsync(binding, new(), ct).ConfigureAwait(false);
            if (result.Status != "ok") throw new AssuranceRefusal("authorization_denied", result.Diagnostic?.Reason ?? "Existing task/run inspection admission failed.");
            if (assurance is not null)
            {
                AssuranceHost.EnsureGovernedRole(assurance.Role, result.Snapshot!.Role);
                AssuranceHost.EnsureInputGrants(assurance.AuthorizedInputPaths(),
                    new[] { grants.WorkingDirectory }.Concat(grants.AdditionalDirectories), ledgerRoot);
            }
        }
        await Authorize(token).ConfigureAwait(false);
        assurance = await AssuranceHost.OpenAsync(path, store, new(subject.Value, run.Value, provider, input.Optional("model")), Authorize, token).ConfigureAwait(false);
        await Authorize(token).ConfigureAwait(false);
        return assurance;
    }
}
