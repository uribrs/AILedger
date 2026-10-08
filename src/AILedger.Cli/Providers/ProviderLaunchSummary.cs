using AILedger.Cli.Dispatch;

namespace AILedger.Cli.Providers;

// Presentation only: provider outcomes, ledger acknowledgements and retention stay separate.
internal static class ProviderLaunchSummary
{
    internal static object Create(ProviderDispatchResult result) => new
    {
        SchemaVersion = 1,
        result.TaskId,
        result.RunId,
        result.Phase,
        LedgerStatus = LedgerStatus(result),
        ProviderStatus = result.ProviderResult?.Status,
        result.StartRecording,
        result.CompletionRecording,
        result.Retention,
        Failure = result.Failure is { } failure
            ? new { failure.Kind, failure.Code, Diagnostic = FirstLine(failure.Diagnostic) } : null,
        result.MissingRequiredOutput,
        result.CognitiveHandoffUnknown
    };

    internal static string FailureLine(ProviderDispatchResult result) =>
        $"Run {FirstLine(result.RunId.Value)}: ledger={LedgerStatus(result)}; " +
        $"{result.Failure!.Kind}: {FirstLine(result.Failure.Diagnostic)}";

    private static string LedgerStatus(ProviderDispatchResult result) =>
        result.Completed?.Status.ToString() ??
        (result.CompletionRecording is LedgerRecordingStatus.Unknown or LedgerRecordingStatus.Refused ||
         result.StartRecording == LedgerRecordingStatus.Unknown ? "Unconfirmed" :
         result.Started is not null ? "Active" : "NotStarted");

    private static string FirstLine(string text)
    {
        var line = text.Split(['\r', '\n'], 2)[0];
        line = new string(line.Where(character => !char.IsControl(character)).ToArray());
        return line.Length <= 512 ? line : line[..512] + "…";
    }
}
