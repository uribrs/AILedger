namespace AILedger.Core.Contracts;

public sealed record AssuranceWorkVersion(WorkItemId WorkItemId, RunId WorkingRunId);

public sealed record AssuranceBinding(
    int SchemaVersion,
    IReadOnlyList<WorkItemId> WorkItemIds,
    IReadOnlyList<AssuranceWorkVersion> WorkVersions,
    string CandidateId,
    RunId? VerifierRunId = null);
