using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

public sealed class CommandAndLifecycleTests
{
    [Fact]
    public void OpeningTaskEstablishesOperatorWithEveryCapabilityAndCausalEvents()
    {
        var handler = new CommandHandler();
        var actor = new ActorId("operator");
        var now = new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

        var outcome = handler.Handle(
            null,
            new OpenTaskCommand(actor, null, "open-1", new TaskId("T1"), " Title ", " Goal "),
            now);

        Assert.Equal(2, outcome.Events.Count);
        Assert.IsType<TaskOpened>(outcome.Events[0].Data);
        Assert.IsType<RoleAssigned>(outcome.Events[1].Data);
        Assert.Equal(outcome.Events[0].EventId, outcome.Events[1].CausationId);
        Assert.Equal("Title", outcome.State.Title);
        Assert.Equal("Goal", outcome.State.Goal);
        Assert.Equal(Enum.GetValues<Capability>().OrderBy(value => value),
            outcome.State.Roles[actor].Capabilities.OrderBy(value => value));
    }

    [Fact]
    public void LifecycleAllowsForwardPipelineAndGovernedBackwardRepair()
    {
        var task = new TestTask();
        task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Build",
            null, [], [Path.GetFullPath("src")]));

        // Every stage now requires state that only engagement with it produces. The arms are pinned
        // one at a time in StagePrerequisiteTests; here the walk stages all of them, because the
        // subject is the shape of the lifecycle rather than any one gate.
        task.ReachStage(TaskStage.Verification);
        // Repair asks what there is to repair. The verifier's findings are that record.
        task.RecordVerifierPass(task.StageWorkItem());
        Transition(task, TaskStage.Repair);
        Transition(task, TaskStage.Execution);

        Assert.Equal(TaskStage.Execution, task.State.Stage);
    }

    [Fact]
    public void LifecycleRejectsSkippedStage()
    {
        var task = new TestTask();

        var exception = Assert.Throws<GovernanceException>(() => Transition(task, TaskStage.Ready));

        Assert.Contains("not legal", exception.Message, StringComparison.Ordinal);
        Assert.Equal(TaskStage.Discovery, task.State.Stage);
    }

    [Fact]
    public void ArchiveRejectsOpenChallenge()
    {
        var task = new TestTask();
        task.Apply(new AddClaimCommand(task.OperatorId, null, task.NextCorrelation(), new ClaimId("C1"), "Claim", null));
        task.Apply(new RaiseChallengeCommand(
            task.OperatorId, null, task.NextCorrelation(), new ChallengeId("CH1"), "claim", "C1", "Uncertain", []));
        task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Build", null, [], [Path.GetFullPath("src")]));
        task.ReachStage(TaskStage.Learn);
        // Archive also asks for something worth carrying out of the task, so the challenge is the
        // only thing left for it to refuse.
        task.Apply(new MarkLessonBearingCommand(
            task.OperatorId, null, task.NextCorrelation(), LessonSourceKind.RejectedAlternative, "ALT-stage",
            Class: LessonClass.Refuted, Repo: "AILedger", Tags: ["stages"],
            Verify: "dotnet test --filter StagePrerequisiteTests",
            DoNot: "Do not bypass the governed stage walk", Actor: LessonActor.Verifier,
            VerifyExpects: VerifyExpectation.Present));

        var exception = Assert.Throws<GovernanceException>(() => Transition(task, TaskStage.Archive));

        Assert.Contains("open challenges", exception.Message, StringComparison.Ordinal);
    }

    // An illegal transition used to name only what was refused, so the cheapest way to find the
    // legal target was to try every stage in enum order. Callers did exactly that: 84 refused
    // transitions in 12 bursts, one of them 19 commands walking out from 'Discovery'. The graph is
    // unchanged; the refusal now reads it out.
    [Fact]
    public void AnIllegalTransitionNamesTheStagesThatAreLegalFromHere()
    {
        var task = new TestTask();

        var error = Assert.Throws<GovernanceException>(() => Transition(task, TaskStage.Archive));

        Assert.Contains("Transition from 'Discovery' to 'Archive' is not legal", error.Message, StringComparison.Ordinal);
        Assert.Contains("legal from 'Discovery': 'Research'", error.Message, StringComparison.Ordinal);
        // Naming the set is not the same as widening it: the one legal target is still the only one.
        Assert.Equal([TaskStage.Research], StageTransitionPolicy.LegalTargets(TaskStage.Discovery));
    }

    // Every stage reports its own set, and Archive reports that it has none rather than an empty
    // list a reader has to interpret.
    [Fact]
    public void EveryStageCanNameItsLegalTargetsAndArchiveSaysItIsTerminal()
    {
        foreach (var stage in Enum.GetValues<TaskStage>())
        {
            var legal = StageTransitionPolicy.LegalTargets(stage);
            Assert.All(legal, target => Assert.True(StageTransitionPolicy.CanTransition(stage, target)));
            // The list is what the policy will accept, and nothing else is.
            Assert.All(
                Enum.GetValues<TaskStage>().Where(target => !legal.Contains(target)),
                target => Assert.False(StageTransitionPolicy.CanTransition(stage, target)));
            // Ordered by the pipeline order the enum declares, so two readers agree.
            Assert.Equal(legal.OrderBy(target => target), legal);
        }

        Assert.Empty(StageTransitionPolicy.LegalTargets(TaskStage.Archive));
        var terminal = Assert.Throws<GovernanceException>(
            () => StageTransitionPolicy.EnsureAllowed(TaskStage.Archive, TaskStage.Learn));
        Assert.Contains("'Archive' is terminal", terminal.Message, StringComparison.Ordinal);
    }

    private static void Transition(TestTask task, TaskStage stage) =>
        task.Apply(new RequestStageTransitionCommand(task.OperatorId, null, task.NextCorrelation(), stage));
}
