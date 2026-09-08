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

public sealed record AddClaimCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    ClaimId ClaimId,
    string Statement,
    string? ConsequenceIfWrong,
    LessonId? FromLesson = null) : LedgerCommand(ActorId, CausationId, CorrelationId);

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
    DecisionId? Supersedes,
    LessonId? FromLesson = null) : LedgerCommand(ActorId, CausationId, CorrelationId);

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
    IReadOnlyList<string> ResourceScope,
    // The recorded alternative saying why more than one area was kept in a single work item.
    // Required only when the item claims more than one; a one-area item needs no defence.
    AlternativeId? NotSplitJustification = null) : LedgerCommand(ActorId, CausationId, CorrelationId);

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
    string? LaunchTokenHash = null,
    // The actor the run is for, when an operator dispatches on its behalf. ActorId stays the
    // authorising actor; the subject does the work and owns the run's provenance.
    ActorId? SubjectActorId = null) : LedgerCommand(ActorId, CausationId, CorrelationId);

public sealed record CompleteRunCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    RunId RunId,
    AgentRunStatus Status,
    string? ProviderSessionId,
    // Plaintext, held only in the launching process. Never persisted.
    string? LaunchToken = null,
    // The brief this run was handed, known to the launcher only after the run started.
    string? ManifestHash = null,
    int? ManifestArtifactCount = null,
    // What the run cost. All but the first-write time are read off the provider's terminal event by
    // RunCostReader; that one the launcher measures. Every one of them is known only once the run
    // has ended, which is why they arrive here rather than on StartRunCommand.
    int? Turns = null,
    long? OutputTokens = null,
    long? MillisecondsToFirstLedgerWrite = null,
    // The model the provider actually served, when the launcher could read it. Absent leaves the
    // model recorded at run.started standing.
    string? Model = null,
    // The three input buckets, mapped per provider by RunCostReader and never summed into one
    // total: C7 measured them billed at roughly 1x, 1.25x and 0.1x (D3, D4, C6). 64-bit for the
    // reason RunCost gives: token counters are already in the millions and the largest runs are the
    // ones a 32-bit counter would drop (RC2).
    long? TokensInUncached = null,
    long? TokensInCacheWrite = null,
    long? TokensInCacheRead = null) : LedgerCommand(ActorId, CausationId, CorrelationId);

public sealed record RequestStageTransitionCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    TaskStage TargetStage,
    // Why the operator is advancing without satisfying the target stage's command-time arm.
    // The handler emits this as its own event so the override remains visible in the log.
    string? WithoutPrerequisitesReason = null) : LedgerCommand(ActorId, CausationId, CorrelationId);

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
    DecisionId? ReplacedByDecisionId,
    LessonId? FromLesson = null) : LedgerCommand(ActorId, CausationId, CorrelationId);

public sealed record RecordArtifactCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    ArtifactId ArtifactId,
    GovernedArtifactKind Kind,
    string Title,
    string Content,
    WorkItemId? WorkItemId,
    RunId? ProducerRunId,
    ArtifactId? SupersedesArtifactId) : LedgerCommand(ActorId, CausationId, CorrelationId);

public sealed record MarkLessonBearingCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    LessonSourceKind SourceKind,
    string SourceRecordId,
    LessonId? SupersedesLessonId = null,
    LessonClass? Class = null,
    string? Repo = null,
    IReadOnlyList<string>? Tags = null,
    string? Verify = null,
    string? DoNot = null,
    LessonActor? Actor = null) : LedgerCommand(ActorId, CausationId, CorrelationId);

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
    WorkItemId WorkItemId,
    // Why this item is being completed with no verifier run behind it. Only an operator may pass
    // it, and passing it puts the waiver in the log rather than leaving it unrecorded.
    string? WithoutVerificationReason = null) : LedgerCommand(ActorId, CausationId, CorrelationId);

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

public sealed record AbandonWorkItemCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    WorkItemId WorkItemId,
    string Reason) : LedgerCommand(ActorId, CausationId, CorrelationId);
