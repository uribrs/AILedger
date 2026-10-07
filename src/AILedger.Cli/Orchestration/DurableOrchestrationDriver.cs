using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
namespace AILedger.Cli.Orchestration;

internal sealed class DurableOrchestrationDriver(FileGovernedTaskService host, DriverHostOptions options,
    Func<string, IAgentAdapter> adapters, IContextAssembler context) : IOrchestrationDriver
{
    public async Task<DriverResult> DriveAsync(DriverRequest request, CancellationToken token)
    {
        if (options.DeploymentProfile != "macos-confined-cognitive-coordinator-v1" || !OperatingSystem.IsMacOS())
            return new(DriverStatus.Unsupported, "coordinator_deployment_unsupported", "This host supports macOS confined cognitive coordinators only; an owner-level conversational shell is not confined.", null, null, [], [], []);
        var lease = await host.AcquireCoordinationAsync(options.TaskId, Guid.NewGuid().ToString("N"), options.OwnerLifetime, token).ConfigureAwait(false);
        var continuity = new DriverContinuity(host, lease);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var heartbeat = HeartbeatAsync(continuity, lifetime);
        try
        {
            await continuity.LoadAsync(token).ConfigureAwait(false);
            var bound = host.BindCoordination(lease);
            var state = await bound.GetStateAsync(options.TaskId, token).ConfigureAwait(false) ?? throw new InvalidDataException("Task unavailable.");
            var authority = new AILedger.Core.Authority.RoutineOrchestrationAuthority(state, options.ActorId, options.ExpiresAt);
            foreach (var intent in continuity.Journal.Intents.Where(i => i.Completion is not null))
            {
                if (state.Runs.TryGetValue(intent.Request.RunId, out var run) && run.Status == AgentRunStatus.Active)
                {
                    var receipt = await AILedger.Cli.Dispatch.DispatchRecovery.ReconcileAsync(bound.BindRoutineOrchestration(authority),
                        intent.Request, intent.Receipt, intent.Completion, token).ConfigureAwait(false);
                    if (receipt is not null) await continuity.SaveAsync(intent.Request, receipt, intent.Completion, token).ConfigureAwait(false);
                }
            }
            var driver = await OrchestrationHost.ComposeAsync(bound, options, adapters, context, continuity, token).ConfigureAwait(false);
            var result = await driver.DriveAsync(request, lifetime.Token).ConfigureAwait(false);
            await continuity.StopAsync(result, CancellationToken.None).ConfigureAwait(false);
            return result;
        }
        catch (Exception error) when (error is GovernanceException or InvalidDataException or IOException)
        {
            return new(DriverStatus.UnknownOutcome, "coordination_unresolved", error.Message, null, null, [], [], []);
        }
        finally
        {
            await lifetime.CancelAsync().ConfigureAwait(false);
            await heartbeat.ConfigureAwait(false);
            try { await host.ReleaseCoordinationAsync(lease, CancellationToken.None).ConfigureAwait(false); }
            catch (GovernanceException) { /* A replacement epoch must never be released by this owner. */ }
        }
    }

    private async Task HeartbeatAsync(DriverContinuity continuity, CancellationTokenSource lifetime)
    {
        try
        {
            while (true)
            {
                await Task.Delay(options.OwnerLifetime / 3, lifetime.Token).ConfigureAwait(false);
                await continuity.RenewAsync(options.OwnerLifetime, lifetime.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is GovernanceException or IOException)
        { await lifetime.CancelAsync().ConfigureAwait(false); }
    }
}
