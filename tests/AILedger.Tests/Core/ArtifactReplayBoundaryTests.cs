using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Tests.Core;

// The most important file in the artifact wave, and the mistake this repository has already made
// twice. Every task on disk was written before artifacts existed: its stage transitions, its
// verifier runs and its completed work items carry no artifact and never can. Command-time rules
// may tighten; replay-time rules may not. So the replay validator learns to validate the new event
// and nothing else, and these tests fail if a later refactor "completes the pair" by teaching it the
// two gates as well.
public sealed class ArtifactReplayBoundaryTests
{
    [Fact]
    public void ReplayAcceptsAStageTransitionIntoExecutionRecordedBeforeArtifactsExisted()
    {
        var state = Opened("legacy-execution", out var reducer, out var next, out var actor, out _);
        state = reducer.Apply(state, next(state, actor, new WorkItemAdded(new WorkItem(
            new WorkItemId("W1"), "Legacy work", actor, WorkItemStatus.Proposed, [],
            [Path.GetFullPath("src")]))));

        foreach (var stage in new[]
                 {
                     (TaskStage.Discovery, TaskStage.Research),
                     (TaskStage.Research, TaskStage.Design),
                     (TaskStage.Design, TaskStage.Scope),
                     (TaskStage.Scope, TaskStage.Ready),
                     (TaskStage.Ready, TaskStage.Execution)
                 })
        {
            state = reducer.Apply(state, next(state, actor, new StageTransitioned(stage.Item1, stage.Item2)));
        }

        Assert.Equal(TaskStage.Execution, state.Stage);
        Assert.Empty(state.Artifacts);
    }

    [Fact]
    public void ReplayAcceptsAVerifierRunCompletedBeforeArtifactsExisted()
    {
        var state = Opened("legacy-verifier-run", out var reducer, out var next, out var actor, out var recordedAt);
        var verifier = new ActorId("verifier");
        state = reducer.Apply(state, next(state, actor, new RoleAssigned(new RoleAssignment(
            verifier, RoleKind.Verifier, [Capability.BuildContext],
            new Provenance(actor, recordedAt, "actor.assign-role")))));
        state = reducer.Apply(state, next(state, actor, new WorkItemAdded(new WorkItem(
            new WorkItemId("W1"), "Verified work", actor, WorkItemStatus.Proposed, [],
            [Path.GetFullPath("src")]))));
        state = reducer.Apply(state, next(state, actor, new RunStarted(new AgentRun(
            new RunId("RV"), verifier, new WorkItemId("W1"), "claude", "session-rv", AgentRunStatus.Active,
            recordedAt, null, null, null, null, actor, RoleKind.Verifier))));

        // No VerifierOutput exists and none can be written retroactively. The pass closed years of
        // events ago; replay's only job is to read what happened.
        state = reducer.Apply(state, next(state, actor, new RunCompleted(
            new RunId("RV"), AgentRunStatus.Completed, "session-rv", recordedAt)));

        Assert.Equal(AgentRunStatus.Completed, state.Runs[new RunId("RV")].Status);
        Assert.Empty(state.Artifacts);
    }

    [Fact]
    public void ReplayAcceptsAWorkItemCompletedBeforeArtifactsExisted()
    {
        var state = Opened("legacy-completion", out var reducer, out var next, out var actor, out _);
        state = reducer.Apply(state, next(state, actor, new WorkItemAdded(new WorkItem(
            new WorkItemId("W1"), "Finished long ago", actor, WorkItemStatus.Proposed, [],
            [Path.GetFullPath("src")]))));

        state = reducer.Apply(state, next(state, actor, new WorkItemCompleted(new WorkItemId("W1"))));

        Assert.Equal(WorkItemStatus.Completed, state.WorkItems[new WorkItemId("W1")].Status);
    }

    // The other half of the boundary: the new event is new, so no history on disk carries one, and
    // every rule about it is safe to write twice. A forged log must not get past replay.
    [Fact]
    public void ReplayValidatesTheArtifactEventItself()
    {
        var state = Opened("artifact-replay", out var reducer, out var next, out var actor, out var recordedAt);
        var worker = new ActorId("worker");
        state = reducer.Apply(state, next(state, actor, new RoleAssigned(new RoleAssignment(
            worker, RoleKind.Worker, [Capability.BuildContext, Capability.RecordArtifact],
            new Provenance(actor, recordedAt, "actor.assign-role")))));

        // The honest event replays.
        state = reducer.Apply(state, next(state, actor, new ArtifactRecorded(Artifact(
            "A1", GovernedArtifactKind.UserRequest, actor, recordedAt))));
        Assert.Equal("A body", state.Artifacts[new ArtifactId("A1")].Content);

        // The same identifier twice: one of the two bodies would be lost silently.
        var duplicate = next(state, actor, new ArtifactRecorded(Artifact(
            "A1", GovernedArtifactKind.UserRequest, actor, recordedAt)));
        Assert.Throws<GovernanceException>(() => reducer.Apply(state, duplicate));

        // A request the operator never made, written by an actor that is not the operator.
        var forgedRequest = next(state, worker, new ArtifactRecorded(Artifact(
            "A2", GovernedArtifactKind.UserRequest, worker, recordedAt)));
        var error = Assert.Throws<GovernanceException>(() => reducer.Apply(state, forgedRequest));
        Assert.Contains("operator", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    // Growing the Capability enum is the one change that reaches every task ever opened, because
    // replay used to require an opening assignment to equal the current enum exactly. It now
    // requires the legacy baseline instead, so an opening event written before RecordArtifact
    // existed still replays — and command-time opening keeps granting everything.
    [Fact]
    public void ReplayAcceptsAnOpeningAssignmentCarryingOnlyTheCapabilitiesThatExistedWhenItWasWritten()
    {
        var reducer = new TaskReducer();
        var taskId = new TaskId("legacy-opening");
        var actor = new ActorId("operator");
        var recordedAt = new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        LedgerEvent Next(GovernedTaskState? current, LedgerEventData data) => new(
            GovernedTaskState.CurrentSchemaVersion,
            new EventId($"{taskId.Value}:{(current?.Version ?? 0) + 1:D10}"),
            taskId, actor, recordedAt, null, "replay", data);

        var state = reducer.Apply(null, Next(null, new TaskOpened("Legacy", "Goal")));
        var legacyCapabilities = Enum.GetValues<Capability>()
            .Where(capability => capability != Capability.RecordArtifact)
            .ToArray();

        state = reducer.Apply(state, Next(state, new RoleAssigned(new RoleAssignment(
            actor, RoleKind.Operator, legacyCapabilities, new Provenance(actor, recordedAt, "task.open")))));

        Assert.Equal(RoleKind.Operator, state.Roles[actor].Role);
        Assert.DoesNotContain(Capability.RecordArtifact, state.Roles[actor].Capabilities);
    }

    // The loosening goes exactly as far as the baseline and no further. An opening assignment
    // missing a capability that did exist when it was written is still a forgery.
    [Fact]
    public void ReplayStillRefusesAnOpeningAssignmentBelowTheLegacyBaseline()
    {
        var reducer = new TaskReducer();
        var taskId = new TaskId("narrow-opening");
        var actor = new ActorId("operator");
        var recordedAt = new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        LedgerEvent Next(GovernedTaskState? current, LedgerEventData data) => new(
            GovernedTaskState.CurrentSchemaVersion,
            new EventId($"{taskId.Value}:{(current?.Version ?? 0) + 1:D10}"),
            taskId, actor, recordedAt, null, "replay", data);

        var state = reducer.Apply(null, Next(null, new TaskOpened("Narrow", "Goal")));
        var narrowed = Enum.GetValues<Capability>()
            .Where(capability => capability is not (Capability.RecordArtifact or Capability.ManageWork))
            .ToArray();

        var forged = Next(state, new RoleAssigned(new RoleAssignment(
            actor, RoleKind.Operator, narrowed, new Provenance(actor, recordedAt, "task.open"))));

        Assert.Throws<GovernanceException>(() => reducer.Apply(state, forged));
    }

    private static GovernedArtifact Artifact(
        string artifactId,
        GovernedArtifactKind kind,
        ActorId actor,
        DateTimeOffset recordedAt) =>
        new(
            new ArtifactId(artifactId),
            kind,
            "Governed document",
            "A body",
            null,
            null,
            null,
            new Provenance(actor, recordedAt, "artifact.record"));

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
