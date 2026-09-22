using AILedger.Cli.Routing;
using AILedger.Core.Contracts;
using AILedger.Providers.Adapters;

namespace AILedger.Cli.Providers;

internal static class ProviderCliCommands
{
    public static IEnumerable<CliCommandRegistration> Registrations(ProviderLauncher launcher)
    {
        yield return Registration("provider launch", AgentLaunchMode.New, launcher);
        yield return Registration("provider resume", AgentLaunchMode.Resume, launcher);
    }

    private static CliCommandRegistration Registration(
        string name,
        AgentLaunchMode mode,
        ProviderLauncher launcher) => new(
        [name],
        CliCommandOptions.Set(
            "root", "task", "actor", "subject", "run", "work", "provider", "session", "executable",
            "also-work", "candidate", "verifier-run", "working-directory", "model", "timeout-seconds", "add-dir", "cognitive-root", "output-schema",
            "without-brief", "with-stale-brief", "coordinator-session", "cause", "correlation", "max-context-bytes"),
        isReadOnly: false,
        (invocation, cancellationToken) => launcher.LaunchAsync(
            invocation.Service, invocation.Input, mode, invocation.LedgerRoot, cancellationToken));
}
