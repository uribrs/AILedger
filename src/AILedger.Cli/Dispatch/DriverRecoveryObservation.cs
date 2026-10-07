using AILedger.Core.Contracts;
namespace AILedger.Cli.Dispatch;

// Diagnostic evidence only. Neither message text nor an explanatory action is executable.
public sealed record DriverRecoveryObservation(TaskStage Stage, long Version, string AttemptedAction,
    string Code, string Diagnostic, RunId? DispatchRun);
