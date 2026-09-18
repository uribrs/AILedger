using AILedger.Core.Contracts;

namespace AILedger.Storage;

public sealed record TaskRetentionEntry(
    string Path,
    string Sha256,
    long Length,
    TaskRetentionDecision Decision,
    string Reason,
    string? Replacement = null,
    string? DiagnosticsLost = null,
    string? Evidence = null);

public enum TaskRetentionDecision
{
    Retain,
    Delete
}

public sealed record TaskRetentionInputs(
    long TaskVersion,
    string EventsSha256,
    long EventsLength,
    string? RefusalsSha256,
    long? RefusalsLength,
    string CloseoutSynthesisArtifactId,
    string CloseoutSynthesisSha256,
    string WorkflowRetrospectiveArtifactId);

public sealed record TaskRetentionTotals(
    int FilesRetained,
    int FilesToDelete,
    long BytesRetained,
    long BytesToDelete);

public sealed record TaskRetentionPlan(
    int SchemaVersion,
    string PlanId,
    TaskId Task,
    DateTimeOffset PlannedAtUtc,
    TaskRetentionInputs Inputs,
    IReadOnlyList<TaskRetentionEntry> Entries,
    TaskRetentionTotals Totals)
{
    public const int CurrentSchemaVersion = 1;
}

public enum TaskRetentionOutcome
{
    Removed,
    RemovedWithoutReceipt,
    AlreadyRemoved
}

public sealed record TaskRetentionReceipt(
    string Path,
    string Sha256,
    long Length,
    TaskRetentionOutcome Outcome,
    DateTimeOffset RecordedAtUtc);

public sealed record TaskRetentionResult(
    string PlanId,
    TaskId Task,
    ActorId Actor,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    int Removed,
    int RemovedWithoutReceipt,
    int AlreadyRemoved,
    long BytesReclaimed,
    IReadOnlyList<TaskRetentionReceipt> Receipts);
