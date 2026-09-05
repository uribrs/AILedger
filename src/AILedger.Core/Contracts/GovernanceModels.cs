namespace AILedger.Core.Contracts;

public enum TaskStage
{
    Discovery,
    Research,
    Design,
    Scope,
    Ready,
    Execution,
    Verification,
    Repair,
    Review,
    Learn,
    Archive
}

public enum RoleKind
{
    Operator,
    PlanningLead,
    ImplementationLead,
    Researcher,
    Worker,
    Verifier,
    CodeReviewer
}

public enum Capability
{
    ManageRoles,
    ManageScope,
    AddClaim,
    ResolveClaim,
    AddEvidence,
    ProposeDecision,
    ResolveDecision,
    RaiseChallenge,
    DisposeChallenge,
    ManageWork,
    ManageRuns,
    RequestTransition,
    BuildContext
}

public enum ClaimStatus
{
    Open,
    Validated,
    Rejected,
    Superseded
}

public enum DecisionStatus
{
    Proposed,
    Accepted,
    Superseded,
    Invalidated
}

public enum ChallengeStatus
{
    Open,
    Supported,
    Rejected,
    Withdrawn
}

public enum WorkItemStatus
{
    Proposed,
    Ready,
    Active,
    Paused,
    Blocked,
    Stale,
    Completed
}

public enum AgentRunStatus
{
    Pending,
    Active,
    Completed,
    Failed,
    Cancelled,
    ProtocolError
}

public sealed record Provenance(ActorId ActorId, DateTimeOffset RecordedAt, string? Source);

public sealed record RoleAssignment(
    ActorId ActorId,
    RoleKind Role,
    IReadOnlyList<Capability> Capabilities,
    Provenance AssignedBy);

public sealed record Claim(
    ClaimId Id,
    string Statement,
    ClaimStatus Status,
    IReadOnlyList<EvidenceId> EvidenceIds,
    string? ConsequenceIfWrong,
    Provenance Provenance);

public sealed record Evidence(
    EvidenceId Id,
    string SourceType,
    string Citation,
    string Summary,
    IReadOnlyList<ClaimId> Supports,
    IReadOnlyList<ClaimId> Refutes,
    Provenance Provenance);

public sealed record Decision(
    DecisionId Id,
    string Statement,
    DecisionStatus Status,
    string Rationale,
    IReadOnlyList<ClaimId> DependsOnClaims,
    DecisionId? Supersedes,
    Provenance Provenance);

public sealed record Challenge(
    ChallengeId Id,
    string TargetType,
    string TargetId,
    string Reason,
    ChallengeStatus Status,
    IReadOnlyList<EvidenceId> EvidenceIds,
    Provenance Provenance);

public sealed record WorkItem(
    WorkItemId Id,
    string Title,
    ActorId? Owner,
    WorkItemStatus Status,
    IReadOnlyList<ClaimId> DependsOnClaims,
    IReadOnlyList<string> ResourceScope);

public sealed record AgentRun(
    RunId Id,
    ActorId ActorId,
    WorkItemId? WorkItemId,
    string Provider,
    string? ProviderSessionId,
    AgentRunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt);
