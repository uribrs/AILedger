using AILedger.Cli.Verification;
using AILedger.Core.Authority;
using AILedger.Core.Contracts;
using AILedger.Storage;
namespace AILedger.Cli.Dispatch;

/// <summary>Trusted composition root. Give the driver only the returned service, never this
/// host's storage/configuration/factories. Provider children receive only bound MCP endpoints.</summary>
public static class ProviderDispatchHost
{
    public static IProviderDispatchService CreateRoutine(FileGovernedTaskService host,
        RoutineOrchestrationAuthority authority, DispatchHostOptions options,
        Func<string, IAgentAdapter> adapters, IContextAssembler context) =>
        Create(host.BindRoutineOrchestration(authority), host, options, adapters, context,
            authority, () => VerificationHost.Default);

    public static IProviderDispatchService CreateHuman(IGovernedTaskService host,
        DispatchHostOptions options, Func<string, IAgentAdapter> adapters, IContextAssembler context) =>
        Create(host, host, options, adapters, context, null, () => VerificationHost.Default);

    internal static IProviderDispatchService Create(IGovernedTaskService mutations, IGovernedTaskService host,
        DispatchHostOptions options, Func<string, IAgentAdapter> adapters, IContextAssembler context,
        RoutineOrchestrationAuthority? authority, Func<VerificationHost> verification)
    {
        var json = LedgerJson.CreateOptions(indented: true);
        return new ProviderDispatchService(adapters,
            new LaunchContextPreparation(context, new CognitiveArtifactLoader(), json, options),
            mutations, host, options, authority, json, new RefusalJournal(), new ProviderRunRecorder(json), verification);
    }
}
