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
    IReadOnlyList<EvidenceId> EvidenceIds) : LedgerCommand(ActorId, CausationId, CorrelationId);

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
    string? ProviderSessionId) : LedgerCommand(ActorId, CausationId, CorrelationId);

public sealed record CompleteRunCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    RunId RunId,
    AgentRunStatus Status,
    string? ProviderSessionId) : LedgerCommand(ActorId, CausationId, CorrelationId);

public sealed record RequestStageTransitionCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    TaskStage TargetStage) : LedgerCommand(ActorId, CausationId, CorrelationId);
