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
[JsonDerivedType(typeof(StagePrerequisitesWaived), "stage.prerequisites-waived")]
[JsonDerivedType(typeof(StageTransitioned), "stage.transitioned")]
[JsonDerivedType(typeof(EscalationRaised), "escalation.raised")]
[JsonDerivedType(typeof(EscalationResolved), "escalation.resolved")]
[JsonDerivedType(typeof(AlternativeRecorded), "alternative.recorded")]
[JsonDerivedType(typeof(ConstraintAdded), "constraint.added")]
[JsonDerivedType(typeof(ConstraintSuperseded), "constraint.superseded")]
[JsonDerivedType(typeof(WorkItemCompleted), "work.completed")]
[JsonDerivedType(typeof(WorkItemBlocked), "work.blocked")]
[JsonDerivedType(typeof(WorkItemUnblocked), "work.unblocked")]
[JsonDerivedType(typeof(WorkItemAbandoned), "work.abandoned")]
[JsonDerivedType(typeof(ClaimDependenciesRepointed), "claim.dependencies-repointed")]
[JsonDerivedType(typeof(DecisionOverturned), "decision.overturned")]
[JsonDerivedType(typeof(LessonMinted), "lesson.minted")]
[JsonDerivedType(typeof(LessonRecalled), "lesson.recalled")]
[JsonDerivedType(typeof(LessonMarked), "lesson.marked")]
[JsonDerivedType(typeof(ArtifactRecorded), "artifact.recorded")]
[JsonDerivedType(typeof(ContextBuilt), "context.built")]
[JsonDerivedType(typeof(ContextBriefWaived), "context.brief-waived")]
[JsonDerivedType(typeof(SessionStarted), "session.started")]
[JsonDerivedType(typeof(SessionCompleted), "session.completed")]
public abstract record LedgerEventData;

// Tags are optional and trail the original two fields: every task opened before they existed
// carries none, and replay must keep reading those histories.
public sealed record TaskOpened(string Title, string Goal, IReadOnlyList<string>? Tags = null) : LedgerEventData;
public sealed record RoleAssigned(RoleAssignment Assignment) : LedgerEventData;
