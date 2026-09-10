namespace AILedger.Core.Contracts;

public sealed record GovernedTaskState
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required TaskId TaskId { get; init; }
    public required string Title { get; init; }
    public required string Goal { get; init; }
    public TaskStage Stage { get; init; } = TaskStage.Discovery;
    public long Version { get; init; }
    public IReadOnlyDictionary<ActorId, RoleAssignment> Roles { get; init; } = new Dictionary<ActorId, RoleAssignment>();
    public IReadOnlyDictionary<ClaimId, Claim> Claims { get; init; } = new Dictionary<ClaimId, Claim>();
    public IReadOnlyDictionary<EvidenceId, Evidence> Evidence { get; init; } = new Dictionary<EvidenceId, Evidence>();
    public IReadOnlyDictionary<DecisionId, Decision> Decisions { get; init; } = new Dictionary<DecisionId, Decision>();
    public IReadOnlyDictionary<ChallengeId, Challenge> Challenges { get; init; } = new Dictionary<ChallengeId, Challenge>();
    public IReadOnlyDictionary<WorkItemId, WorkItem> WorkItems { get; init; } = new Dictionary<WorkItemId, WorkItem>();
    public IReadOnlyDictionary<RunId, AgentRun> Runs { get; init; } = new Dictionary<RunId, AgentRun>();
    // Appended, never reordered: state.json is byte-compared against a fresh replay.
    public IReadOnlyDictionary<EscalationId, Escalation> Escalations { get; init; } = new Dictionary<EscalationId, Escalation>();
    public IReadOnlyDictionary<AlternativeId, Alternative> Alternatives { get; init; } = new Dictionary<AlternativeId, Alternative>();
    public IReadOnlyDictionary<ConstraintId, Constraint> Constraints { get; init; } = new Dictionary<ConstraintId, Constraint>();
    // Lessons are copied into the opening history of the next task, so its context remains
    // self-contained even if the source task is later moved or archived elsewhere.
    public IReadOnlyDictionary<LessonId, Lesson> Lessons { get; init; } = new Dictionary<LessonId, Lesson>();
    public IReadOnlyDictionary<LessonMarkId, LessonMark> LessonMarks { get; init; } = new Dictionary<LessonMarkId, LessonMark>();
    public IReadOnlyDictionary<ArtifactId, GovernedArtifact> Artifacts { get; init; } = new Dictionary<ArtifactId, GovernedArtifact>();
    // The tags this task was opened with, which recall selects lessons against. Appended here
    // rather than placed beside Goal to keep the property order above untouched, and left null
    // when absent so that the state.json of every task opened before tags existed is unchanged.
    public IReadOnlyList<string>? Tags { get; init; }
    // Which actors have been briefed on this task, and with what. Appended after Tags for the
    // reason Tags was appended after Artifacts: state.json is byte-compared against a fresh replay,
    // so a property may be added at the end and never moved. Empty for every task that has never
    // built context, which on the day this shipped was all of them.
    public IReadOnlyDictionary<ActorId, ContextBuild> ContextBuilds { get; init; } =
        new Dictionary<ActorId, ContextBuild>();
    // Which work items and which launches came through a door on the context gate, in the order the
    // doors were opened. Appended after ContextBuilds for the reason ContextBuilds was appended after
    // Tags: a property may be added at the end here and never moved. Empty for every task that opened
    // no door, which was all of them until the doors existed.
    public IReadOnlyList<ContextBriefWaiver> ContextBriefWaivers { get; init; } = [];
    internal ActorId? PendingOpeningActor { get; init; }
    // Transient replay state tying a waiver event to the immediately caused stage transition.
    // Internal properties are not projected into state.json; a completed command always consumes it.
    internal StagePrerequisiteWaiver? PendingStagePrerequisiteWaiver { get; init; }
    // The same shape for the context gate's doors: the waiver event is written immediately before the
    // work.added or run.started it lets through, and this holds it for exactly that one step so the
    // reducer can join the two. Cleared by whatever event comes next, so a waiver whose command
    // failed between the two appends cannot attach itself to a later item.
    internal PendingBriefWaiver? PendingContextBriefWaiver { get; init; }
}

internal sealed record StagePrerequisiteWaiver(EventId EventId, ActorId ActorId, TaskStage TargetStage);

internal sealed record PendingBriefWaiver(
    EventId EventId,
    ActorId ActorId,
    string? OperatorReason,
    EvidenceId? StaleBriefEvidenceId);

public sealed record CommandOutcome(
    GovernedTaskState State,
    IReadOnlyList<LedgerEvent> Events);

public interface ITaskReducer
{
    GovernedTaskState Apply(GovernedTaskState? state, LedgerEvent @event);
}

public interface ICommandHandler
{
    CommandOutcome Handle(GovernedTaskState? state, LedgerCommand command, DateTimeOffset now);
}
