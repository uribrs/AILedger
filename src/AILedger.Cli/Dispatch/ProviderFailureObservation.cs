using AILedger.Core.Contracts;

namespace AILedger.Cli.Dispatch;

internal static class ProviderFailureObservation
{
    internal static DispatchFailureKind Classify(AgentRunResult result, bool missingOutput = false) => result switch
    {
        { ProcessFailure.CleanupConfirmed: false } => DispatchFailureKind.ProcessCleanupUnconfirmed,
        { Status: AgentRunStatus.Cancelled } => DispatchFailureKind.Cancelled,
        { ProcessFailure: not null } => DispatchFailureKind.ProviderTransport,
        { Events.Count: 0 } when !missingOutput => DispatchFailureKind.ProviderStartup,
        _ => DispatchFailureKind.ProviderOutcome
    };

    internal static string Diagnostic(AgentRunResult result)
    {
        // Adapter output is already redacted. Bound the copy into the driver journal.
        var stderr = result.StandardError.Length <= 2048 ? result.StandardError : result.StandardError[..2048] + " [truncated; see retained result]";
        return $"Provider run '{result.RunId}' ended with status '{result.Status}'. {result.Failure}" +
            (stderr.Length == 0 ? " No stderr was captured." : $" Stderr: {stderr}");
    }
}
