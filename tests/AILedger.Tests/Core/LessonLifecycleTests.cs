using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

public sealed class LessonLifecycleTests
{
    [Fact]
    public void ArchiveMintsLessonsFromEachGovernedSourceKind()
    {
        var task = new TestTask("source-task");
        var claimId = new ClaimId("C1");
        var evidenceId = new EvidenceId("E1");
        task.Apply(new AddClaimCommand(
            task.OperatorId, null, task.NextCorrelation(), claimId, "The retry is bounded", null));
        task.Apply(new AddEvidenceCommand(
            task.OperatorId, null, task.NextCorrelation(), evidenceId, "test-run", "RetryTests.Bounded",
            "The production policy stopped after three attempts", [claimId], []));
        task.Apply(new ResolveClaimCommand(
            task.OperatorId, null, task.NextCorrelation(), claimId, ClaimStatus.Validated, [evidenceId]));
        task.Apply(new MarkLessonBearingCommand(
            task.OperatorId, null, task.NextCorrelation(), LessonSourceKind.ValidatedClaim, claimId.Value));
        task.Apply(new RecordAlternativeCommand(
            task.OperatorId, null, task.NextCorrelation(), new AlternativeId("ALT1"),
            "Retry forever", "It prevents terminal failure", null));
        task.Apply(new MarkLessonBearingCommand(
            task.OperatorId, null, task.NextCorrelation(), LessonSourceKind.RejectedAlternative, "ALT1"));
        task.Apply(new RaiseEscalationCommand(
            task.OperatorId, null, task.NextCorrelation(), new EscalationId("X1"),
            EscalationKind.BusinessDecision, "Ship narrow or broad?", null,
            ["narrow", "broad"], "narrow", []));
        task.Apply(new ResolveEscalationCommand(
            task.OperatorId, null, task.NextCorrelation(), new EscalationId("X1"),
            EscalationStatus.Resolved, "narrow"));
        task.Apply(new MarkLessonBearingCommand(
            task.OperatorId, null, task.NextCorrelation(), LessonSourceKind.ResolvedEscalation, "X1"));
        AddWorkAndReachLearn(task);

        var outcome = task.Apply(new RequestStageTransitionCommand(
            task.OperatorId, null, task.NextCorrelation(), TaskStage.Archive));

        Assert.Equal(TaskStage.Archive, outcome.State.Stage);
        Assert.Equal(3, outcome.Events.Count(item => item.Data is LessonMinted));
        Assert.IsType<StageTransitioned>(outcome.Events[^1].Data);
        Assert.Collection(
            outcome.State.Lessons.Values.OrderBy(item => item.SourceKind),
            lesson =>
            {
                Assert.Equal(LessonSourceKind.ValidatedClaim, lesson.SourceKind);
                Assert.Equal(["RetryTests.Bounded"], lesson.Citations);
            },
            lesson => Assert.Equal(LessonSourceKind.RejectedAlternative, lesson.SourceKind),
            lesson => Assert.Equal(LessonSourceKind.ResolvedEscalation, lesson.SourceKind));
    }

    [Fact]
    public void ArchiveMintsOnlySourcesExplicitlyMarkedLessonBearing()
    {
        var task = new TestTask();
        task.Apply(new RecordAlternativeCommand(
            task.OperatorId, null, task.NextCorrelation(), new AlternativeId("ALT1"),
            "Keep the first local workaround", "It does not generalise", null));
        task.Apply(new RecordAlternativeCommand(
            task.OperatorId, null, task.NextCorrelation(), new AlternativeId("ALT2"),
            "Repeat the cross-task mistake", "Future tasks need this warning", null));
        task.Apply(new MarkLessonBearingCommand(
            task.OperatorId, null, task.NextCorrelation(), LessonSourceKind.RejectedAlternative, "ALT2"));
        AddWorkAndReachLearn(task);

        var outcome = task.Apply(new RequestStageTransitionCommand(
            task.OperatorId, null, task.NextCorrelation(), TaskStage.Archive));

        var minted = Assert.Single(outcome.Events, item => item.Data is LessonMinted);
        Assert.Equal("ALT2", Assert.IsType<LessonMinted>(minted.Data).Lesson.SourceRecordId);
        Assert.DoesNotContain(task.State.Lessons.Values, lesson => lesson.SourceRecordId == "ALT1");
    }

    [Fact]
    public void WorkerCannotMarkASourceLessonBearing()
    {
        var task = new TestTask();
        var worker = new ActorId("worker");
        task.Assign(worker, RoleKind.Worker, Capability.AddClaim);
        task.Apply(new RecordAlternativeCommand(
            task.OperatorId, null, task.NextCorrelation(), new AlternativeId("ALT1"),
            "Carry this into every later task", "Only a lead can make that scope decision", null));

        var error = Assert.Throws<GovernanceException>(() => task.Apply(new MarkLessonBearingCommand(
            worker, null, task.NextCorrelation(), LessonSourceKind.RejectedAlternative, "ALT1")));

        Assert.Contains("operator or lead", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(task.State.LessonMarks);
    }

    [Fact]
    public void ArchiveIsRefusedWhenTheTaskHasNothingLessonBearing()
    {
        var task = new TestTask();
        AddWorkAndReachLearn(task);

        var error = Assert.Throws<GovernanceException>(() => task.Apply(
            new RequestStageTransitionCommand(
                task.OperatorId, null, task.NextCorrelation(), TaskStage.Archive)));

        // The message names the mark, not the lesson: after LD6 a lesson exists only
        // because a source was marked lesson-bearing, so the mark is what is missing.
        Assert.Contains("lesson-bearing mark", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TaskStage.Learn, task.State.Stage);
    }

    [Fact]
    public void ReplayAcceptsLegacyArchiveTransitionWithoutLessonEvents()
    {
        var task = new TestTask();
        AddWorkAndReachLearn(task);
        var reducer = new TaskReducer();
        var legacyArchive = new LedgerEvent(
            GovernedTaskState.CurrentSchemaVersion,
            new EventId("legacy-archive"),
            task.TaskId,
            task.OperatorId,
            new DateTimeOffset(2026, 9, 6, 14, 0, 0, TimeSpan.Zero),
            null,
            "legacy",
            new StageTransitioned(TaskStage.Learn, TaskStage.Archive));

        var replayed = reducer.Apply(task.State, legacyArchive);

        Assert.Equal(TaskStage.Archive, replayed.Stage);
        Assert.Empty(replayed.Lessons);
    }

    internal static void AddWorkAndReachLearn(TestTask task)
    {
        task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Build", null, [], []));
        foreach (var stage in new[]
                 {
                     TaskStage.Research, TaskStage.Design, TaskStage.Scope, TaskStage.Ready,
                     TaskStage.Execution, TaskStage.Verification, TaskStage.Review, TaskStage.Learn
                 })
        {
            task.Apply(new RequestStageTransitionCommand(
                task.OperatorId, null, task.NextCorrelation(), stage));
        }
    }
}
