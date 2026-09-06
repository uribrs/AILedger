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
    BuildContext,
    RaiseEscalation,
    ResolveEscalation,
    RecordAlternative,
    ManageConstraints
}

// Only two things may interrupt the operator: a tradeoff no amount of research
// settles, and a question the code and the sources cannot answer.
public enum EscalationKind
{
    BusinessDecision,
    TrueUnknown
}

public enum EscalationStatus
{
    Open,
    Resolved,
    Withdrawn
}

public enum ConstraintStatus
{
    Active,
    Superseded
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
    Active,
    Paused,
    Blocked,
    Stale,
    Completed
}

public enum AgentRunStatus
{
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

// Superseding is not one act. A refinement sharpens a claim and its dependents stay
// valid; a correction narrows or contradicts it and they do not. The kernel derives
// which from state, never from a flag set by the actor doing the superseding.
public enum SupersessionOutcome
{
    Correction,
    Refinement
}

public sealed record Claim(
    ClaimId Id,
    string Statement,
    ClaimStatus Status,
    IReadOnlyList<EvidenceId> EvidenceIds,
    string? ConsequenceIfWrong,
    Provenance Provenance,
    ClaimId? SupersededByClaimId = null);

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
    IReadOnlyList<string> ResourceScope,
    string? BlockReason = null);

public sealed record Escalation(
    EscalationId Id,
    EscalationKind Kind,
    string Question,
    EscalationStatus Status,
    WorkItemId? WorkItemId,
    IReadOnlyList<string> Options,
    string? Recommendation,
    IReadOnlyList<EvidenceId> AttemptEvidenceIds,
    string? Resolution,
    ActorId? ResolvedBy,
    Provenance Provenance);

public sealed record Alternative(
    AlternativeId Id,
    string Statement,
    string RejectionRationale,
    DecisionId? ReplacedByDecisionId,
    Provenance Provenance);

public sealed record Constraint(
    ConstraintId Id,
    string Statement,
    string Source,
    IReadOnlyList<string> Scope,
    ConstraintStatus Status,
    Provenance Provenance);

public sealed record AgentRun(
    RunId Id,
    ActorId ActorId,
    WorkItemId? WorkItemId,
    string Provider,
    string? ProviderSessionId,
    AgentRunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt);
