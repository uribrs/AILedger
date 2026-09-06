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
    internal ActorId? PendingOpeningActor { get; init; }
}

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
