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
    EvidenceId? StaleBriefEvidenceId = null,
    // The coordinating session dispatching this run, so the session owns its children (D1). Optional
    // and last: a run started outside a session, or before sessions existed, names none, and the
    // kernel refuses no run for its absence.
    CoordinatorSessionId? CoordinatorSessionId = null) : LedgerCommand(ActorId, CausationId, CorrelationId);

// The bracket a coordinator puts around its own work. Two commands and nothing else: the session
// records no cost, no volume and no self-assessment, because every measure over it is derived from
// the log rather than reported by the thing being measured (D2).
public sealed record StartCoordinatorSessionCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    CoordinatorSessionId SessionId,
    // Which harness hosts the coordinator — `claude-code`, `codex-cli`. Required: a session that
    // does not say what it ran in cannot have its usage record looked for, and "no record" and
    // "never asked" are different absences (D6).
    string Harness,
    // The harness's own identity for this conversation, when it exposes one. It is what a transcript
    // is identity-checked against, and a session carrying none reports the usage read as an absence
    // rather than trusting a path (R4, PD2).
    string? HarnessSessionId = null) : LedgerCommand(ActorId, CausationId, CorrelationId);

public sealed record CompleteCoordinatorSessionCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    CoordinatorSessionId SessionId) : LedgerCommand(ActorId, CausationId, CorrelationId);

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
    long? TokensInCacheRead = null,
    // How many provider lines the drain cut to the per-line cap, read off the adapter's result by
    // the launcher. A hand-issued 'run complete' leaves it absent, which is correct: nobody was
    // watching that stream.
    int? TruncatedLines = null,
    // The limit this launch was given, and the provider's own reason for a run that ended other than
    // by completing. Both are the launcher's to record and both arrive here rather than on
    // StartRunCommand, because the launcher builds its request and reads its result after the kernel
    // has recorded the run. A hand-issued 'run complete' leaves both absent, which is correct: no
    // limit was given and no provider reported anything.
    //
    // The reason is relayed from the adapter and never composed from Status. C8 is the constraint and
    // AgentRun.TerminalFailureReason carries it: a run whose agent returned an adverse verdict is a
    // run that did its job, and a measure that read the status would count it as a failed dispatch.
    int? LaunchTimeoutSeconds = null,
    string? TerminalFailureReason = null,
    // Which condition ended the run, taken from AgentRunResult.EndedAtTheLaunchTimeout, which the
    // process runner sets by observing which cancellation source fired rather than by reading a
    // clock. A hand-issued 'run complete' leaves it absent, and so does a launch whose adapter never
    // returned: in both, nobody observed the termination, and measure 11 must leave the row unjudged
    // rather than infer it (VC4, VC6).
    bool? EndedAtTheLaunchTimeout = null) : LedgerCommand(ActorId, CausationId, CorrelationId);

public sealed record RequestStageTransitionCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    TaskStage TargetStage,
    // Why the operator is advancing without satisfying the target stage's command-time arm.
    // The handler emits this as its own event so the override remains visible in the log.
    string? WithoutPrerequisitesReason = null,
    // Why the stage moved at all, carried onto StageTransitioned.
    //
    // Hazard: this record ends in two nullable strings that mean opposite things — one excuses a
    // refusal, the other narrates a move that was allowed. A positional call passing one string
    // silently binds it to WithoutPrerequisitesReason. Pass either of them by name.
    string? Reason = null,
    // Which recorded alternative says why an execution that could have been dispatched concurrently
    // was run serially instead. Carried onto StageTransitioned.
    //
    // This third trailing parameter is outside the hazard above, and its type is why. A caller
    // cannot bind a string to it or an AlternativeId to either string: the compiler refuses both
    // directions, so no positional call can put this value in a reason's slot or a reason's text in
    // this one. A third nullable string would have widened the hazard; a distinct type closes it.
    AlternativeId? SerialJustification = null) : LedgerCommand(ActorId, CausationId, CorrelationId);

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
