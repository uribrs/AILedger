using AILedger.Core.Contracts;

namespace AILedger.Providers.Adapters;

internal static class ProviderRunOutput
{
    internal const string EnvironmentVariable = "AILEDGER_RUN_OUTPUT";

    internal static string? DirectoryFor(AgentLaunchRequest request) =>
        request.WorkItemId is not null && request.Assurance is null
            ? Path.Combine(request.WorkingDirectory, ".ailedger-output", "run-" + Uri.EscapeDataString(request.RunId.Value))
            : null;
}
