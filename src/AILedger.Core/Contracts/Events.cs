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
public abstract record LedgerEventData;

// Tags are optional and trail the original two fields: every task opened before they existed
// carries none, and replay must keep reading those histories.
public sealed record TaskOpened(string Title, string Goal, IReadOnlyList<string>? Tags = null) : LedgerEventData;
public sealed record RoleAssigned(RoleAssignment Assignment) : LedgerEventData;
public sealed record ClaimAdded(Claim Claim) : LedgerEventData;
public sealed record ClaimResolved(ClaimId ClaimId, ClaimStatus Status, IReadOnlyList<EvidenceId> EvidenceIds, ClaimId? SupersededByClaimId = null, SupersessionOutcome? Outcome = null) : LedgerEventData;
public sealed record EvidenceAdded(Evidence Evidence) : LedgerEventData;
public sealed record DecisionProposed(Decision Decision) : LedgerEventData;
public sealed record DecisionResolved(DecisionId DecisionId, DecisionStatus Status) : LedgerEventData;
public sealed record DecisionInvalidated(DecisionId DecisionId, ClaimId RejectedClaimId) : LedgerEventData;
public sealed record ChallengeRaised(Challenge Challenge) : LedgerEventData;
public sealed record ChallengeDisposed(ChallengeId ChallengeId, ChallengeStatus Status) : LedgerEventData;
public sealed record WorkItemAdded(WorkItem WorkItem) : LedgerEventData;
public sealed record WorkItemInvalidated(WorkItemId WorkItemId, ClaimId RejectedClaimId, WorkItemStatus Status) : LedgerEventData;
public sealed record RunStarted(AgentRun Run) : LedgerEventData;
public sealed record RunCompleted(RunId RunId, AgentRunStatus Status, string? ProviderSessionId, DateTimeOffset EndedAt, bool LauncherAuthorized = false) : LedgerEventData;
public sealed record StageTransitioned(TaskStage Previous, TaskStage Current) : LedgerEventData;
public sealed record EscalationRaised(Escalation Escalation) : LedgerEventData;
public sealed record EscalationResolved(EscalationId EscalationId, EscalationStatus Status, string? Resolution, ActorId ResolvedBy) : LedgerEventData;
public sealed record AlternativeRecorded(Alternative Alternative) : LedgerEventData;
public sealed record ConstraintAdded(Constraint Constraint) : LedgerEventData;
public sealed record ConstraintSuperseded(ConstraintId ConstraintId) : LedgerEventData;
public sealed record WorkItemCompleted(WorkItemId WorkItemId, string? WithoutVerificationReason = null) : LedgerEventData;
public sealed record WorkItemBlocked(WorkItemId WorkItemId, string Reason, EscalationId? EscalationId) : LedgerEventData;
public sealed record WorkItemUnblocked(WorkItemId WorkItemId) : LedgerEventData;
public sealed record WorkItemAbandoned(WorkItemId WorkItemId, string Reason) : LedgerEventData;
public sealed record ClaimDependenciesRepointed(ClaimId SupersededClaimId, ClaimId ReplacementClaimId) : LedgerEventData;
public sealed record DecisionOverturned(DecisionId DecisionId, ChallengeId ChallengeId) : LedgerEventData;
public sealed record LessonMinted(Lesson Lesson) : LedgerEventData;
public sealed record LessonRecalled(Lesson Lesson) : LedgerEventData;
public sealed record LessonMarked(LessonMark Mark) : LedgerEventData;
public sealed record ArtifactRecorded(GovernedArtifact Artifact) : LedgerEventData;
