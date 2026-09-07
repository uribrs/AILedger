using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Tests.Core;

// R1 (replay-gate-leak). The stage arms are methodology, and the operator is still changing it.
// Command-time rules may tighten; replay-time rules may not, because every task already on disk
// transitioned before the arms existed and carries none of the state they ask for. Adding an arm to
// the replay validator has twice made a live task permanently unreadable, so these tests fail if a
// later refactor "completes the pair" by teaching replay the arms as well.
public sealed class StagePrerequisiteReplayBoundaryTests
{
    // Every legal edge in the pipeline, walked in one history: the eleven stages forwards, plus the
    // return to Discovery, the loops back from Execution, the repair loop, and the return from Learn.
    private static readonly TaskStage[] EveryLegalEdge =
    [
        TaskStage.Research, TaskStage.Discovery, TaskStage.Research,
        TaskStage.Design, TaskStage.Research, TaskStage.Design,
        TaskStage.Scope, TaskStage.Design, TaskStage.Scope,
        TaskStage.Ready, TaskStage.Scope, TaskStage.Ready,
        TaskStage.Execution, TaskStage.Research, TaskStage.Design, TaskStage.Scope, TaskStage.Ready,
        TaskStage.Execution, TaskStage.Design, TaskStage.Scope, TaskStage.Ready,
        TaskStage.Execution, TaskStage.Scope, TaskStage.Ready,
        TaskStage.Execution, TaskStage.Verification, TaskStage.Execution,
        TaskStage.Verification, TaskStage.Repair, TaskStage.Execution,
        TaskStage.Verification, TaskStage.Repair, TaskStage.Verification,
        TaskStage.Review, TaskStage.Repair, TaskStage.Verification,
        TaskStage.Review, TaskStage.Learn, TaskStage.Review, TaskStage.Learn,
        TaskStage.Archive
    ];

    [Fact]
    public void R1_ReplayAcceptsEveryLegacyStageTransitionWithoutEngagementState()
    {
        var state = Opened("legacy-stage-walk", out var reducer, out var next, out var actor, out _);
        state = reducer.Apply(state, next(state, actor, new WorkItemAdded(new WorkItem(
            new WorkItemId("W1"), "Legacy work", actor, WorkItemStatus.Proposed, [],
            [Path.GetFullPath("src")]))));

        // Not one of these events carries a claim, a run, an artifact, a discarded approach or a
        // lesson mark, because none of those records existed when a history like this was written.
        foreach (var target in EveryLegalEdge)
        {
            state = reducer.Apply(state, next(state, actor, new StageTransitioned(state.Stage, target)));
        }

        Assert.Equal(TaskStage.Archive, state.Stage);
        Assert.Empty(state.Claims);
        Assert.Empty(state.Runs);
        Assert.Empty(state.Artifacts);
        Assert.Empty(state.Alternatives);
        Assert.Empty(state.Decisions);
        Assert.Empty(state.LessonMarks);
        Assert.Empty(state.Lessons);
    }

    // The boundary is a boundary, not an absence of rules. The two structural gates that predate the
    // arms key on state every history has always carried, so replay keeps refusing them and a forged
    // log does not get past.
    [Fact]
    public void ReplayStillRefusesExecutionOnATaskThatHasNoWorkItem()
    {
        var state = Opened("legacy-no-work", out var reducer, out var next, out var actor, out _);
        foreach (var target in new[] { TaskStage.Research, TaskStage.Design, TaskStage.Scope, TaskStage.Ready })
        {
            state = reducer.Apply(state, next(state, actor, new StageTransitioned(state.Stage, target)));
        }

        var forged = next(state, actor, new StageTransitioned(TaskStage.Ready, TaskStage.Execution));

        var refusal = Assert.Throws<GovernanceException>(() => reducer.Apply(state, forged));
        Assert.Contains("work item", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TaskStage.Ready, state.Stage);
    }

    [Fact]
    public void ReplayStillRefusesAnArchiveWhileARunIsActive()
    {
        var state = Learn("legacy-active-run", out var reducer, out var next, out var actor, out var recordedAt);
        state = reducer.Apply(state, next(state, actor, new RunStarted(new AgentRun(
            new RunId("R1"), actor, new WorkItemId("W1"), "codex", "session-r1", AgentRunStatus.Active,
            recordedAt, null, null, null, null, null, RoleKind.Operator))));

        var forged = next(state, actor, new StageTransitioned(TaskStage.Learn, TaskStage.Archive));

        var refusal = Assert.Throws<GovernanceException>(() => reducer.Apply(state, forged));
        Assert.Contains("active runs", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TaskStage.Learn, state.Stage);
    }

    [Fact]
    public void ReplayStillRefusesAnArchiveWhileAChallengeIsOpen()
    {
        var state = Learn("legacy-open-challenge", out var reducer, out var next, out var actor, out var recordedAt);
        state = reducer.Apply(state, next(state, actor, new ClaimAdded(new Claim(
            new ClaimId("C1"), "The area was never verified", ClaimStatus.Open, [], null,
            new Provenance(actor, recordedAt, "claim.add")))));
        state = reducer.Apply(state, next(state, actor, new ChallengeRaised(new Challenge(
            new ChallengeId("CH1"), "claim", "C1", "The evidence does not support it",
            ChallengeStatus.Open, [], new Provenance(actor, recordedAt, "challenge.raise")))));

        var forged = next(state, actor, new StageTransitioned(TaskStage.Learn, TaskStage.Archive));

        var refusal = Assert.Throws<GovernanceException>(() => reducer.Apply(state, forged));
        Assert.Contains("open challenges", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TaskStage.Learn, state.Stage);
    }

    // A legacy history at Learn: one work item, and not one record any arm asks for.
    private static GovernedTaskState Learn(
        string taskId,
        out TaskReducer reducer,
        out Func<GovernedTaskState, ActorId, LedgerEventData, LedgerEvent> next,
        out ActorId actor,
        out DateTimeOffset recordedAt)
    {
        var state = Opened(taskId, out reducer, out next, out actor, out recordedAt);
        state = reducer.Apply(state, next(state, actor, new WorkItemAdded(new WorkItem(
            new WorkItemId("W1"), "Legacy work", actor, WorkItemStatus.Proposed, [],
            [Path.GetFullPath("src")]))));

        foreach (var target in new[]
                 {
                     TaskStage.Research, TaskStage.Design, TaskStage.Scope, TaskStage.Ready,
                     TaskStage.Execution, TaskStage.Verification, TaskStage.Review, TaskStage.Learn
                 })
        {
            state = reducer.Apply(state, next(state, actor, new StageTransitioned(state.Stage, target)));
        }

        return state;
    }

    // An opened task with one operator, ready for hand-built events. The reducer re-validates every
    // event, so a history assembled here is held to the replay rules and nothing else.
    private static GovernedTaskState Opened(
        string taskId,
        out TaskReducer reducer,
        out Func<GovernedTaskState, ActorId, LedgerEventData, LedgerEvent> next,
        out ActorId actor,
        out DateTimeOffset recordedAt)
    {
        var id = new TaskId(taskId);
        var localActor = new ActorId("operator");
        var localRecordedAt = new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        var localReducer = new TaskReducer();
        actor = localActor;
        recordedAt = localRecordedAt;
        reducer = localReducer;
        next = (current, eventActor, data) => new LedgerEvent(
            GovernedTaskState.CurrentSchemaVersion,
            new EventId($"{id.Value}:{(current?.Version ?? 0) + 1:D10}"),
            id, eventActor, localRecordedAt, null, "replay", data);

        var state = localReducer.Apply(null, next(null!, localActor, new TaskOpened("Legacy", "Goal")));
        return localReducer.Apply(state, next(state, localActor, new RoleAssigned(new RoleAssignment(
            localActor, RoleKind.Operator, Enum.GetValues<Capability>(),
            new Provenance(localActor, localRecordedAt, "task.open")))));
    }
}
