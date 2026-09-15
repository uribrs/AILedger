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
[JsonDerivedType(typeof(AbandonWorkItemCommand), "work.abandon")]
[JsonDerivedType(typeof(MarkLessonBearingCommand), "lesson.mark")]
[JsonDerivedType(typeof(RecordArtifactCommand), "artifact.record")]
[JsonDerivedType(typeof(RecordContextBuiltCommand), "context.build")]
[JsonDerivedType(typeof(StartCoordinatorSessionCommand), "session.start")]
[JsonDerivedType(typeof(CompleteCoordinatorSessionCommand), "session.complete")]
public abstract record LedgerCommand(ActorId ActorId, EventId? CausationId, string CorrelationId);

public sealed record OpenTaskCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    TaskId TaskId,
    string Title,
    string Goal,
    // Populated by the durable service from archived sibling tasks. Callers opening an isolated
    // aggregate can omit it and preserve the original command contract.
    IReadOnlyList<Lesson>? RecalledLessons = null,
    // The tags recall selects against, in the order given. Optional: a task opened without them
    // recalls exactly as it did before they existed.
    IReadOnlyList<string>? Tags = null) : LedgerCommand(ActorId, CausationId, CorrelationId);

public sealed record AssignRoleCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    ActorId TargetActorId,
    RoleKind Role,
    IReadOnlyList<Capability> Capabilities) : LedgerCommand(ActorId, CausationId, CorrelationId);
