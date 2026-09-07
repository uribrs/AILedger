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
    Completed,
    // Work is released as well as finished. A dead end that can never be completed used to hold its
    // directory area forever, so nothing else could claim it.
    Abandoned
}

public enum AgentRunStatus
{
    Active,
    Completed,
    Failed,
    Cancelled,
    ProtocolError
}

public enum LessonSourceKind
{
    ValidatedClaim,
    // A belief that was disproved, which is a different record from an approach that was
    // discarded. Only a rejected claim carries the evidence that refuted it, so it is the
    // record a Refuted lesson is minted from; RejectedAlternative carries a rationale for not
    // taking a path and no evidence at all. Conflating them loses the counter-evidence.
    RejectedClaim,
    RejectedAlternative,
    ResolvedEscalation
}

public enum LessonClass
{
    Refuted,
    Untested,
    Drifted
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
    string? BlockReason = null,
    string? AbandonReason = null,
    // Carried onto the item so the justification for holding several areas lands in the log. Kept
    // only on the command, the decision the kernel demanded would be checked and then thrown away.
    AlternativeId? NotSplitJustification = null);

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

public sealed record Lesson(
    LessonId Id,
    TaskId SourceTaskId,
    LessonSourceKind SourceKind,
    string SourceRecordId,
    string Statement,
    string Outcome,
    IReadOnlyList<string> Citations,
    Provenance Provenance,
    LessonId? SupersedesLessonId = null,
    // Nullable only so histories written before lesson classification can still replay. New marks
    // require all three metadata fields at command time and therefore mint populated lessons.
    LessonClass? Class = null,
    string? Repo = null,
    IReadOnlyList<string>? Tags = null);

public sealed record LessonMark(
    LessonMarkId Id,
    LessonSourceKind SourceKind,
    string SourceRecordId,
    LessonId? SupersedesLessonId,
    Provenance Provenance,
    LessonClass? Class = null,
    string? Repo = null,
    IReadOnlyList<string>? Tags = null);

public sealed record AgentRun(
    RunId Id,
    ActorId ActorId,
    WorkItemId? WorkItemId,
    string Provider,
    string? ProviderSessionId,
    AgentRunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    // Which cognition actually ran. A score or a lesson earned against one model and CLI version
    // says nothing about another, and the adapter knew both and used to discard them.
    string? Model = null,
    string? ProviderVersion = null,
    // A run launched by the kernel is closed by the launcher, not by the agent inside it. The
    // launcher holds a secret for the run's lifetime and only its hash is recorded, so the agent
    // — which shares the run's actor identity — cannot authorise its own completion.
    string? LaunchTokenHash = null,
    // Who authorised this run, when that is not the actor doing the work. A role that must not hold
    // run authority — verifier, code reviewer — can only run if someone else dispatches for it, and
    // the ledger has to say who. Null is the ordinary case: the actor started its own run.
    ActorId? LaunchedBy = null,
    // The role the subject held when the run started. A role assignment can change afterwards, so
    // asking "was this work verified" of the current assignment answers a different question than
    // the one being asked. Null on every run recorded before this field existed.
    RoleKind? SubjectRole = null);
