using System.Text.Json.Serialization;

namespace AILedger.Core.Contracts;

public sealed record LedgerEvent(
    int SchemaVersion,
    EventId EventId,
    TaskId TaskId,
    ActorId ActorId,
    DateTimeOffset RecordedAt,
    EventId? CausationId,
    string CorrelationId,
    LedgerEventData Data);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "eventType")]
[JsonDerivedType(typeof(TaskOpened), "task.opened")]
[JsonDerivedType(typeof(RoleAssigned), "actor.role-assigned")]
[JsonDerivedType(typeof(ClaimAdded), "claim.added")]
[JsonDerivedType(typeof(ClaimResolved), "claim.resolved")]
[JsonDerivedType(typeof(EvidenceAdded), "evidence.added")]
[JsonDerivedType(typeof(DecisionProposed), "decision.proposed")]
[JsonDerivedType(typeof(DecisionResolved), "decision.resolved")]
[JsonDerivedType(typeof(DecisionInvalidated), "decision.invalidated")]
[JsonDerivedType(typeof(ChallengeRaised), "challenge.raised")]
[JsonDerivedType(typeof(ChallengeDisposed), "challenge.disposed")]
[JsonDerivedType(typeof(WorkItemAdded), "work.added")]
[JsonDerivedType(typeof(WorkItemInvalidated), "work.invalidated")]
[JsonDerivedType(typeof(RunStarted), "run.started")]
[JsonDerivedType(typeof(RunCompleted), "run.completed")]
[JsonDerivedType(typeof(StageTransitioned), "stage.transitioned")]
public abstract record LedgerEventData;

public sealed record TaskOpened(string Title, string Goal) : LedgerEventData;
public sealed record RoleAssigned(RoleAssignment Assignment) : LedgerEventData;
public sealed record ClaimAdded(Claim Claim) : LedgerEventData;
public sealed record ClaimResolved(ClaimId ClaimId, ClaimStatus Status, IReadOnlyList<EvidenceId> EvidenceIds) : LedgerEventData;
public sealed record EvidenceAdded(Evidence Evidence) : LedgerEventData;
public sealed record DecisionProposed(Decision Decision) : LedgerEventData;
public sealed record DecisionResolved(DecisionId DecisionId, DecisionStatus Status) : LedgerEventData;
public sealed record DecisionInvalidated(DecisionId DecisionId, ClaimId RejectedClaimId) : LedgerEventData;
public sealed record ChallengeRaised(Challenge Challenge) : LedgerEventData;
public sealed record ChallengeDisposed(ChallengeId ChallengeId, ChallengeStatus Status) : LedgerEventData;
public sealed record WorkItemAdded(WorkItem WorkItem) : LedgerEventData;
public sealed record WorkItemInvalidated(WorkItemId WorkItemId, ClaimId RejectedClaimId, WorkItemStatus Status) : LedgerEventData;
public sealed record RunStarted(AgentRun Run) : LedgerEventData;
public sealed record RunCompleted(RunId RunId, AgentRunStatus Status, string? ProviderSessionId, DateTimeOffset EndedAt) : LedgerEventData;
public sealed record StageTransitioned(TaskStage Previous, TaskStage Current) : LedgerEventData;
