using AILedger.Cli.Dispatch;
using AILedger.Core.Authority;
using AILedger.Core.Contracts;
using AILedger.Storage;
namespace AILedger.Cli.Orchestration;

public static class OrchestrationHost
{
    public static Task<IOrchestrationDriver> CreateAsync(FileGovernedTaskService trustedHost,
        DriverHostOptions options, Func<string, IAgentAdapter> adapters, IContextAssembler context, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        options = options with
        {
            Agents = new Dictionary<CognitiveWorkKind, DriverAgentProfile>(options.Agents),
            Preparation = options.Preparation is { } policy ? new(policy.Roles.Select(r => r with { Capabilities = r.Capabilities.ToArray() }).ToArray(),
                policy.Work.Select(w => w with { Scope = w.Scope.ToArray() }).ToArray()) : null
        };
        return Task.FromResult<IOrchestrationDriver>(new DurableOrchestrationDriver(trustedHost, options, adapters, context));
    }

    internal static async Task<IOrchestrationDriver> ComposeAsync(FileGovernedTaskService trustedHost,
        DriverHostOptions options, Func<string, IAgentAdapter> adapters, IContextAssembler context,
        DriverContinuity continuity, CancellationToken token)
    {
        var state = await trustedHost.GetStateAsync(options.TaskId, token).ConfigureAwait(false)
            ?? throw new InvalidDataException("The driver requires an explicitly prepared task.");
        options = options with { Agents = new Dictionary<CognitiveWorkKind, DriverAgentProfile>(options.Agents) };
        var authority = new RoutineOrchestrationAuthority(state, options.ActorId, options.ExpiresAt);
        var protectedPaths = options.Dispatch.ProtectedPaths.Concat(new[]
            { options.Dispatch.AssuranceAuthority, options.Dispatch.AssuranceStore,
              options.GovernedReviewDispatch?.AssuranceAuthority, options.GovernedReviewDispatch?.AssuranceStore }
            .OfType<string>()).Distinct(StringComparer.Ordinal).ToArray();
        options = options with { Dispatch = options.Dispatch with { ProtectedPaths = protectedPaths, Checkpoint = continuity, PreparationProfiles = options.Preparation is null ? null : System.Text.Json.JsonSerializer.Serialize(options.Preparation.Work, LedgerJson.CreateOptions()) } };
        var plain = options.Dispatch with { AssuranceAuthority = null, AssuranceStore = null };
        // These are separate *contexts*, using the same shared dispatch implementation. Blind
        // review has no requirements-aware tools; a separate authorized context supplies that assurance.
        var standard = ProviderDispatchHost.CreateRoutine(trustedHost, authority, plain, adapters, context);
        var verifier = ProviderDispatchHost.CreateRoutine(trustedHost, authority, options.Dispatch, adapters, context);
        var reviewer = ProviderDispatchHost.CreateRoutine(trustedHost, authority, options.GovernedReviewDispatch is { } reviewOptions ? reviewOptions with { ProtectedPaths = protectedPaths, Checkpoint = continuity, PreparationProfiles = options.Preparation is null ? null : System.Text.Json.JsonSerializer.Serialize(options.Preparation.Work, LedgerJson.CreateOptions()) } : plain, adapters, context);
        var completionHost = options.AcceptancePrincipal is { } acceptor && options.Dispatch.AssuranceAuthority is { } policyPath && options.Dispatch.AssuranceStore is { } storePath
            ? trustedHost.BindCompletionAdmission(new AILedger.Cli.Assurance.GovernedCompletionHost(policyPath, storePath, acceptor, options.Dispatch.LedgerRoot)) : trustedHost;
        var restricted = completionHost.BindRoutineOrchestration(authority);
        var preparation = options.Preparation is null ? null : new DriverPreparation(trustedHost, authority, options, context);
        return new OrchestrationDriver(new(restricted, options, context), options,
            new DurableDispatch(standard, restricted, continuity), new DurableDispatch(verifier, restricted, continuity),
            new DurableDispatch(reviewer, restricted, continuity), preparation);
    }
}
