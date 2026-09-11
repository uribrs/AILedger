using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

// The asymmetry the retrospective feature rests on, and the mistake CLAUDE.md records this
// repository making twice. The entry condition on recording a retrospective — stage Archive, no
// live work item, no active run — is command-time only, because it keys on the task's aggregate
// state and every history ever written carries that state. An arm for it in TaskTransitionValidator
// would make a history that was legal when written unreadable the next time it is replayed.
//
// The table arm and the authority arm are in both copies, and they are safe on the other test: they
// key on GovernedArtifactKind.WorkflowRetrospective, which no earlier history can carry.
public sealed class WorkflowRetrospectiveReplayBoundaryTests
{
    [Fact]
    public void R4_EveryHistoryLegalBeforeThisChangeStillReplays()
    {
        // A history in the shape the command surface refuses outright: a retrospective recorded at
        // Discovery, while a work item is live and a run is active. Replay's job is to read what
        // happened, so it accepts all three. Add the entry condition to the validator and this is
        // the test that fails.
        var state = Opened("retrospective-replay-boundary", out var reducer, out var next, out var actor, out var recordedAt);
        state = reducer.Apply(state, next(state, actor, new WorkItemAdded(new WorkItem(
            new WorkItemId("W1"), "Still live", actor, WorkItemStatus.Proposed, [],
            [Path.GetFullPath("src")]))));
        state = reducer.Apply(state, next(state, actor, new RunStarted(new AgentRun(
            new RunId("R1"), actor, new WorkItemId("W1"), "codex", "session-r1", AgentRunStatus.Active,
            recordedAt, null, null, null, null, actor, RoleKind.Worker))));

        state = reducer.Apply(state, next(state, actor, new ArtifactRecorded(new GovernedArtifact(
            new ArtifactId("A-retro"), GovernedArtifactKind.WorkflowRetrospective,
            "How this task was governed", WorkflowRetrospectiveArtifactTests.Table(),
            null, null, null, new Provenance(actor, recordedAt, "artifact.record")))));

        Assert.Equal(TaskStage.Discovery, state.Stage);
        // Live in the sense ScopeOccupancyRules means: not Completed, Stale or Abandoned. Starting
        // the run moved the item from Proposed to Active, and both are live.
        Assert.Equal(WorkItemStatus.Active, state.WorkItems[new WorkItemId("W1")].Status);
        Assert.Equal(AgentRunStatus.Active, state.Runs[new RunId("R1")].Status);
        Assert.Equal(
            GovernedArtifactKind.WorkflowRetrospective,
            state.Artifacts[new ArtifactId("A-retro")].Kind);
    }

    // The boundary goes exactly that far and no further. The two arms the validator did gain still
    // refuse a forged event, so replay is loosened on the entry condition alone.
    [Fact]
    public void R4_ReplayStillRefusesAForgedRetrospective()
    {
        var state = Opened("retrospective-replay-forgery", out var reducer, out var next, out var actor, out var recordedAt);
        var worker = new ActorId("worker");
        state = reducer.Apply(state, next(state, actor, new RoleAssigned(new RoleAssignment(
            worker, RoleKind.Worker, [Capability.BuildContext, Capability.RecordArtifact],
            new Provenance(actor, recordedAt, "actor.assign-role")))));

        var byAWorker = next(state, worker, new ArtifactRecorded(new GovernedArtifact(
            new ArtifactId("A-worker"), GovernedArtifactKind.WorkflowRetrospective, "Scored itself",
            WorkflowRetrospectiveArtifactTests.Table(), null, null, null,
            new Provenance(worker, recordedAt, "artifact.record"))));
        var error = Assert.Throws<GovernanceException>(() => reducer.Apply(state, byAWorker));
        Assert.Contains("operator, planning lead, or implementation lead", error.Message,
            StringComparison.Ordinal);

        var withoutTheTable = next(state, actor, new ArtifactRecorded(new GovernedArtifact(
            new ArtifactId("A-untabled"), GovernedArtifactKind.WorkflowRetrospective, "No table",
            "# Retrospective\n\nIt went well.\n", null, null, null,
            new Provenance(actor, recordedAt, "artifact.record"))));
        var tableError = Assert.Throws<GovernanceException>(() => reducer.Apply(state, withoutTheTable));
        Assert.Contains("dimension | score | confidence | controllable | evidence", tableError.Message,
            StringComparison.Ordinal);
    }

    // A whole governed history the command surface produced today, replayed from nothing. The
    // retrospective is the last event on it, which is where it always is.
    [Fact]
    public void R4_AWholeArchivedHistoryCarryingARetrospectiveReplays()
    {
        var task = WorkflowRetrospectiveArtifactTests.Archived();
        task.Apply(WorkflowRetrospectiveArtifactTests.Retrospective(task, task.OperatorId, "A-retro"));

        var reducer = new TaskReducer();
        GovernedTaskState? replayed = null;
        foreach (var item in task.Events)
        {
            replayed = reducer.Apply(replayed, item);
        }

        Assert.NotNull(replayed);
        Assert.Equal(task.State.Version, replayed!.Version);
        Assert.Equal(TaskStage.Archive, replayed.Stage);
        Assert.True(replayed.Artifacts.ContainsKey(new ArtifactId("A-retro")));
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
