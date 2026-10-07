using System.Text.Json;
using AILedger.Cli.Findings;
using AILedger.Core.Contracts;
using AILedger.Providers.Adapters;
namespace AILedger.Cli.Dispatch;
internal sealed partial class ProviderDispatchService
{
    private async Task<AgentRunResult> InvokeProviderAsync(ActiveDispatch active, DispatchProgress receipt, CancellationToken token)
    {
        var input = active.Input;
        var grants = active.Prepared.Grants;
        var assurance = await OpenAssuranceAsync(_hostService, input, active.Start.RunId,
            active.Prepared.Provider, grants, _options.LedgerRoot, token).ConfigureAwait(false);
        if (assurance is not null) receipt.AssurancePreparation = AssurancePreparationStatus.Opened;
        // Subject role and actual admitted run select the brief, never the dispatcher's view.
        var manifest = await _contextPreparation.CreateAsync(_hostService, input, input.Subject, token,
            active.Start.RunId, AssuranceDiscovery(assurance?.ConfiguredAreas)).ConfigureAwait(false);
        var manifestJson = JsonSerializer.Serialize(manifest, _json);
        if (input.TimeoutSeconds <= 0) throw new CliUsageException("Timeout must be a positive integer.");
        receipt.TimeoutSeconds = input.TimeoutSeconds;
        await ValidateAssuranceAtUseAsync(assurance, token).ConfigureAwait(false);
        var findings = ProviderFindingsSession.Start(_hostService, _options.LedgerRoot, input.TaskId,
            input.Subject, active.Start.RunId, input.Cause, active.Prepared.Provider,
            active.Started.State.Roles[input.Subject], DateTimeOffset.UtcNow.AddSeconds(input.TimeoutSeconds),
            CognitiveArtifactLoader.ResolveRoot(_options.CognitiveRoot), assurance, input.CognitiveWork);
        try
        {
            var request = new AgentLaunchRequest(active.Start.RunId, input.TaskId, input.Subject, input.WorkItemId,
                input.Mode, active.Prepared.Provider, active.Executable, grants.WorkingDirectory,
                _options.LedgerRoot, ResolveLedgerCommandLine(active.Start.RunId), manifestJson, input.SessionId,
                PermissionProfile.WorkspaceGoverned, input.Model, _options.OutputSchema, grants.AdditionalDirectories,
                new Dictionary<string, string>(), TimeSpan.FromSeconds(input.TimeoutSeconds),
                manifest.CoveredWorkItemIds, active.Started.State.Runs[active.Start.RunId].Assurance,
                typeof(CliApplication).Assembly.Location, grants.NavigationDirectories, findings.Endpoint, active.Isolation);
            // Record delivery only after all request preparation succeeds, over exactly these bytes.
            receipt.ManifestHash = _runRecorder.HashManifest(manifestJson);
            receipt.ManifestArtifactCount = manifest.Artifacts.Count;
            receipt.Phase = DispatchPhase.ProviderExecution;
            var result = await active.Adapter.RunAsync(request, token).ConfigureAwait(false);
            if (result.RunId != active.Start.RunId)
                throw new AgentAdapterException($"Provider '{active.Prepared.Provider}' returned a result for run '{result.RunId}' while run " +
                    $"'{active.Start.RunId}' was launched. The provider result was not kept and the run was closed as failed.");
            return result;
        }
        finally
        {
            await findings.DisposeAsync().ConfigureAwait(false);
            receipt.CognitiveHandoffUnknown = findings.CognitiveHandoffUnknown;
            receipt.CognitiveReceipts = findings.CognitiveReceipts;
        }
    }
}
