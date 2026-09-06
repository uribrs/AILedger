using System.Text.Json.Serialization;

namespace AILedger.Core.Contracts;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "commandType")]
[JsonDerivedType(typeof(OpenTaskCommand), "task.open")]
[JsonDerivedType(typeof(AssignRoleCommand), "actor.assign-role")]
[JsonDerivedType(typeof(AddClaimCommand), "claim.add")]
[JsonDerivedType(typeof(ResolveClaimCommand), "claim.resolve")]
[JsonDerivedType(typeof(AddEvidenceCommand), "evidence.add")]
[JsonDerivedType(typeof(ProposeDecisionCommand), "decision.propose")]
[JsonDerivedType(typeof(ResolveDecisionCommand), "decision.resolve")]
[JsonDerivedType(typeof(RaiseChallengeCommand), "challenge.raise")]
[JsonDerivedType(typeof(DisposeChallengeCommand), "challenge.dispose")]
[JsonDerivedType(typeof(AddWorkItemCommand), "work.add")]
[JsonDerivedType(typeof(StartRunCommand), "run.start")]
[JsonDerivedType(typeof(CompleteRunCommand), "run.complete")]
[JsonDerivedType(typeof(RequestStageTransitionCommand), "stage.transition")]
[JsonDerivedType(typeof(RaiseEscalationCommand), "escalation.raise")]
[JsonDerivedType(typeof(ResolveEscalationCommand), "escalation.resolve")]
[JsonDerivedType(typeof(RecordAlternativeCommand), "alternative.record")]
[JsonDerivedType(typeof(AddConstraintCommand), "constraint.add")]
[JsonDerivedType(typeof(SupersedeConstraintCommand), "constraint.supersede")]
[JsonDerivedType(typeof(CompleteWorkItemCommand), "work.complete")]
[JsonDerivedType(typeof(BlockWorkItemCommand), "work.block")]
[JsonDerivedType(typeof(UnblockWorkItemCommand), "work.unblock")]
public abstract record LedgerCommand(ActorId ActorId, EventId? CausationId, string CorrelationId);

public sealed record OpenTaskCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    TaskId TaskId,
    string Title,
    string Goal) : LedgerCommand(ActorId, CausationId, CorrelationId);

public sealed record AssignRoleCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    ActorId TargetActorId,
    RoleKind Role,
    IReadOnlyList<Capability> Capabilities) : LedgerCommand(ActorId, CausationId, CorrelationId);

public sealed record AddClaimCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    ClaimId ClaimId,
    string Statement,
    string? ConsequenceIfWrong) : LedgerCommand(ActorId, CausationId, CorrelationId);

public sealed record ResolveClaimCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    ClaimId ClaimId,
    ClaimStatus Status,
    IReadOnlyList<EvidenceId> EvidenceIds,
    ClaimId? SupersededByClaimId = null) : LedgerCommand(ActorId, CausationId, CorrelationId);

public sealed record AddEvidenceCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    EvidenceId EvidenceId,
    string SourceType,
    string Citation,
    string Summary,
    IReadOnlyList<ClaimId> Supports,
    IReadOnlyList<ClaimId> Refutes) : LedgerCommand(ActorId, CausationId, CorrelationId);

public sealed record ProposeDecisionCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    DecisionId DecisionId,
    string Statement,
    string Rationale,
    IReadOnlyList<ClaimId> DependsOnClaims,
    DecisionId? Supersedes) : LedgerCommand(ActorId, CausationId, CorrelationId);

public sealed record ResolveDecisionCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    DecisionId DecisionId,
    DecisionStatus Status) : LedgerCommand(ActorId, CausationId, CorrelationId);

public sealed record RaiseChallengeCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    ChallengeId ChallengeId,
    string TargetType,
    string TargetId,
    string Reason,
    IReadOnlyList<EvidenceId> EvidenceIds) : LedgerCommand(ActorId, CausationId, CorrelationId);

public sealed record DisposeChallengeCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    ChallengeId ChallengeId,
    ChallengeStatus Status) : LedgerCommand(ActorId, CausationId, CorrelationId);

public sealed record AddWorkItemCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    WorkItemId WorkItemId,
    string Title,
    ActorId? Owner,
    IReadOnlyList<ClaimId> DependsOnClaims,
    IReadOnlyList<string> ResourceScope) : LedgerCommand(ActorId, CausationId, CorrelationId);

public sealed record StartRunCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    RunId RunId,
    WorkItemId? WorkItemId,
    string Provider,
    string? ProviderSessionId,
    string? Model = null,
    string? ProviderVersion = null,
    string? LaunchTokenHash = null) : LedgerCommand(ActorId, CausationId, CorrelationId);

public sealed record CompleteRunCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    RunId RunId,
    AgentRunStatus Status,
    string? ProviderSessionId,
    // Plaintext, held only in the launching process. Never persisted.
    string? LaunchToken = null) : LedgerCommand(ActorId, CausationId, CorrelationId);

public sealed record RequestStageTransitionCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    TaskStage TargetStage) : LedgerCommand(ActorId, CausationId, CorrelationId);

public sealed record RaiseEscalationCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    EscalationId EscalationId,
    EscalationKind Kind,
    string Question,
    WorkItemId? WorkItemId,
    IReadOnlyList<string> Options,
    string? Recommendation,
    IReadOnlyList<EvidenceId> AttemptEvidenceIds) : LedgerCommand(ActorId, CausationId, CorrelationId);

public sealed record ResolveEscalationCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    EscalationId EscalationId,
    EscalationStatus Status,
    string? Resolution) : LedgerCommand(ActorId, CausationId, CorrelationId);

public sealed record RecordAlternativeCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    AlternativeId AlternativeId,
    string Statement,
    string RejectionRationale,
    DecisionId? ReplacedByDecisionId) : LedgerCommand(ActorId, CausationId, CorrelationId);

public sealed record AddConstraintCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    ConstraintId ConstraintId,
    string Statement,
    string Source,
    IReadOnlyList<string> Scope) : LedgerCommand(ActorId, CausationId, CorrelationId);

public sealed record SupersedeConstraintCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    ConstraintId ConstraintId) : LedgerCommand(ActorId, CausationId, CorrelationId);

public sealed record CompleteWorkItemCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    WorkItemId WorkItemId) : LedgerCommand(ActorId, CausationId, CorrelationId);

public sealed record BlockWorkItemCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    WorkItemId WorkItemId,
    string Reason,
    EscalationId? EscalationId) : LedgerCommand(ActorId, CausationId, CorrelationId);

public sealed record UnblockWorkItemCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    WorkItemId WorkItemId) : LedgerCommand(ActorId, CausationId, CorrelationId);
