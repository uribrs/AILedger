using AILedger.Core.Contracts;
using AILedger.Tests.Support;

namespace AILedger.Tests.CoordinatorSessions;

// Provenance attributes a waiver; it never changes whether the waiver is accepted. These tests
// drive the same three successful commands with and without a coordinator bracket and compare only
// what was recorded.
public sealed class WaiverProvenanceTests
{
    [Fact]
    public void AllThreeWaiversCarryTheOpenCoordinatorSession()
    {
        var task = new TestTask { AutoBuildContext = false, AutoServeSkills = false };
        var sessionId = new CoordinatorSessionId("S1");
        task.Apply(new StartCoordinatorSessionCommand(
            task.OperatorId,
            null,
            task.NextCorrelation(),
            sessionId,
            "codex-cli",
            "harness-session-1"));

        var stage = task.Apply(new RequestStageTransitionCommand(
            task.OperatorId,
            null,
            task.NextCorrelation(),
            TaskStage.Research,
            "Discovery was completed outside this fixture"));
        var work = task.Apply(new AddWorkItemCommand(
            task.OperatorId,
            null,
            task.NextCorrelation(),
            new WorkItemId("W1"),
            "Work",
            null,
            [],
            [],
            WithoutBriefReason: "The fixture deliberately has no cognitive layer"));
        var completion = task.Apply(new CompleteWorkItemCommand(
            task.OperatorId,
            null,
            task.NextCorrelation(),
            new WorkItemId("W1"),
            "The fixture does not dispatch providers"));

        AssertSessionProvenance(
            Assert.Single(stage.Events.Select(item => item.Data).OfType<StagePrerequisitesWaived>()).Provenance,
            sessionId);
        AssertSessionProvenance(
            Assert.Single(work.Events.Select(item => item.Data).OfType<ContextBriefWaived>()).Provenance,
            sessionId);
        AssertSessionProvenance(
            Assert.Single(completion.Events.Select(item => item.Data).OfType<WorkItemCompleted>())
                .Provenance,
            sessionId);

        var projected = Assert.Single(task.State.ContextBriefWaivers);
        AssertSessionProvenance(projected.Provenance, sessionId);
    }

    [Fact]
    public void AllThreeWaiversAreManualWhenTheActorHasNoOpenSession()
    {
        var task = new TestTask { AutoBuildContext = false, AutoServeSkills = false };

        var stage = task.Apply(new RequestStageTransitionCommand(
            task.OperatorId,
            null,
            task.NextCorrelation(),
            TaskStage.Research,
            "Discovery was completed outside this fixture"));
        var work = task.Apply(new AddWorkItemCommand(
            task.OperatorId,
            null,
            task.NextCorrelation(),
            new WorkItemId("W1"),
            "Work",
            null,
            [],
            [],
            WithoutBriefReason: "The fixture deliberately has no cognitive layer"));
        var completion = task.Apply(new CompleteWorkItemCommand(
            task.OperatorId,
            null,
            task.NextCorrelation(),
            new WorkItemId("W1"),
            "The fixture does not dispatch providers"));

        AssertManual(
            Assert.Single(stage.Events.Select(item => item.Data).OfType<StagePrerequisitesWaived>()).Provenance);
        AssertManual(
            Assert.Single(work.Events.Select(item => item.Data).OfType<ContextBriefWaived>()).Provenance);
        AssertManual(
            Assert.Single(completion.Events.Select(item => item.Data).OfType<WorkItemCompleted>())
                .Provenance);
    }

    private static void AssertSessionProvenance(
        WaiverProvenance? provenance,
        CoordinatorSessionId sessionId)
    {
        var recorded = Assert.IsType<WaiverProvenance>(provenance);
        Assert.Equal(WaiverOrigin.CoordinatorSession, recorded.Origin);
        Assert.Equal(sessionId, recorded.CoordinatorSessionId);
        Assert.Equal("codex-cli", recorded.Harness);
        Assert.Equal("harness-session-1", recorded.HarnessSessionId);
    }

    private static void AssertManual(WaiverProvenance? provenance)
    {
        var recorded = Assert.IsType<WaiverProvenance>(provenance);
        Assert.Equal(WaiverOrigin.Manual, recorded.Origin);
        Assert.Null(recorded.CoordinatorSessionId);
        Assert.Null(recorded.Harness);
        Assert.Null(recorded.HarnessSessionId);
    }
}
