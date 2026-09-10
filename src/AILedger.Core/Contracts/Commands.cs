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
    AlternativeId? NotSplitJustification = null,
    // The commit this item's work starts from. Captured by the CLI in the item's first scope
    // directory; null when that is not a git work tree.
    string? BaseRef = null,
    // What the cognitive layer would serve this actor right now, read by the caller because the
    // kernel has no filesystem. The gate needs it to tell a current brief from a stale one; null
    // means the caller could not read the layer, which is a refusal rather than a pass — a brief
    // that cannot be checked is not a current brief (VC1). Commands are not persisted, so this
    // reaches no event and no replay.
    IReadOnlyList<ContextSkill>? SkillsServedNow = null,
    // The operator door. An operator's decision that this command proceeds with no brief at all,
    // and the reason it was right. Operator-only, blank is refused, and it is recorded as its own
    // event — the shape and the spirit of --without-verification.
    string? WithoutBriefReason = null,
    // The conditional door. An evidence record on this task saying why proceeding on a brief that
    // is no longer current is correct. The kernel checks that the record exists and that a brief
    // exists to be stale, never whether the reason is a good one — the same contract as
    // NotSplitJustification above.
    EvidenceId? StaleBriefEvidenceId = null) : LedgerCommand(ActorId, CausationId, CorrelationId);

// 'context build' is a read that conditionally records audit evidence. It is never gated, it
// cannot be refused for anything but an unknown task or an actor that may not build context, and a
// repeat of a brief already recorded appends nothing — but a new or changed skill set appends this
// event, so the command can move the task on and is not classified read-only in the CLI (SC2).
// The trace is the point: whether an actor was briefed — and with which skills, saying what — is a
// query against the events rather than an assumption.
//
// WorkItemId is what this one invocation was for. It stays on the event, which records an
// invocation truthfully; the ContextBuild projection drops it, because the projection is keyed on
// the actor and the skill set and would otherwise name whichever work item appended first (MD2).
public sealed record RecordContextBuiltCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    WorkItemId? WorkItemId,
    IReadOnlyList<ContextSkill> Skills) : LedgerCommand(ActorId, CausationId, CorrelationId);

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
    ActorId? SubjectActorId = null,
    // As on AddWorkItemCommand: what the cognitive layer would serve the acting actor now. Read by
    // the gate on a provider launch only — a launch is the moment the dispatcher decomposes the
    // work, and LaunchTokenHash above is what tells a launch from a manually started run (IC3).
    IReadOnlyList<ContextSkill>? SkillsServedNow = null,
    // The two doors, as on AddWorkItemCommand, and read on a provider launch for the same reason
    // SkillsServedNow is: they open the gate this command is subject to, and a manually started run
    // is not subject to it.
    string? WithoutBriefReason = null,
    EvidenceId? StaleBriefEvidenceId = null) : LedgerCommand(ActorId, CausationId, CorrelationId);

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
    LessonActor? Actor = null,
    // Optional on the command because absent is a meaningful answer to both: a lesson about the
    // software is Domain, and a lesson everyone should read has no audience to narrow to.
    LessonKind? Kind = null,
    IReadOnlyList<RoleKind>? Audience = null,
    // Required at command time and nullable on the record. New marks must state the direction their
    // verify has to come out; the lessons already minted carry none and replay must keep reading
    // them. This is the twin-rule asymmetry: CommandHandler may tighten, TaskTransitionValidator
    // may not.
    VerifyExpectation? VerifyExpects = null) : LedgerCommand(ActorId, CausationId, CorrelationId);

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
