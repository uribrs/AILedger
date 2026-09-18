using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Tests.Support;

namespace AILedger.Tests.Artifacts;

public sealed class TaskCloseoutEligibilityTests
{
    [Fact]
    public void AFullyClosedOutTaskIsEligible()
    {
        var report = TaskCloseoutEligibility.Evaluate(ClosedOut().State);

        Assert.True(report.Eligible);
        Assert.Empty(report.Unsatisfied);
        Assert.Equal("A-closeout", report.CloseoutSynthesisArtifactId);
        Assert.Equal("A-retro", report.WorkflowRetrospectiveArtifactId);
    }

    [Theory]
    [InlineData(false, true, "closeoutSynthesisFiled")]
    [InlineData(true, false, "workflowRetrospectiveFiled")]
    public void MissingCloseoutArtifactsFailTheirOwnNamedCheck(
        bool withSynthesis,
        bool withRetrospective,
        string expectedCheck)
    {
        var report = TaskCloseoutEligibility.Evaluate(
            ClosedOut(withSynthesis, withRetrospective).State);

        Assert.False(report.Eligible);
        Assert.Contains(report.Unsatisfied, check => check.Name == expectedCheck);
    }

    [Fact]
    public void BeforeArchiveTheStageCheckFails()
    {
        var task = new TestTask();
        task.ReachStage(TaskStage.Review);

        var report = TaskCloseoutEligibility.Evaluate(task.State);

        Assert.Contains(report.Unsatisfied, check => check.Name == "stageIsArchive");
    }

    [Fact]
    public void ALiveWorkItemFailsItsOwnCheckAndNamesTheItem()
    {
        var task = ClosedOut(withRetrospective: false, completeWork: false);

        var report = TaskCloseoutEligibility.Evaluate(task.State);

        var check = Assert.Single(report.Unsatisfied, item => item.Name == "noLiveWorkItems");
        Assert.Contains("W-stage", check.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public void AnActiveRunFailsItsOwnCheckAndNamesTheRun()
    {
        var task = ClosedOut();
        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R-late"), null, "codex",
            null, null, null, null, task.OperatorId));

        var report = TaskCloseoutEligibility.Evaluate(task.State);

        var check = Assert.Single(report.Unsatisfied, item => item.Name == "noActiveRuns");
        Assert.Contains("R-late", check.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOpenEscalationFailsItsOwnCheckAndNamesTheEscalation()
    {
        var task = ClosedOut();
        EscalationCommands.Raise(
            task,
            task.OperatorId,
            "X-late",
            EscalationKind.BusinessDecision,
            options: ["retain", "delete"],
            recommendation: "retain");

        var report = TaskCloseoutEligibility.Evaluate(task.State);

        var check = Assert.Single(report.Unsatisfied, item => item.Name == "noOpenEscalations");
        Assert.Contains("X-late", check.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOpenChallengeFailsItsOwnCheckAndNamesTheChallenge()
    {
        var task = ClosedOut();
        task.Apply(new RaiseChallengeCommand(
            task.OperatorId, null, task.NextCorrelation(), new ChallengeId("CH-late"),
            "claim", "C-topic", "The closeout evidence does not support this claim", []));

        var report = TaskCloseoutEligibility.Evaluate(task.State);

        var check = Assert.Single(report.Unsatisfied, item => item.Name == "noOpenChallenges");
        Assert.Contains("CH-late", check.Detail!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void LessonPublicationRequiresAMintedLessonOnlyWhenTheSynthesisHasFindings(
        bool withFindings,
        bool expectedSatisfied)
    {
        var task = ClosedOut();
        var synthesisId = new ArtifactId("A-closeout");
        var artifacts = task.State.Artifacts.ToDictionary(pair => pair.Key, pair => pair.Value);
        artifacts[synthesisId] = artifacts[synthesisId] with
        {
            Content = withFindings
                ? CloseoutSynthesisArtifactTests.Body(CloseoutSynthesisArtifactTests.Finding())
                : CloseoutSynthesisArtifactTests.Body()
        };
        var state = task.State with
        {
            Artifacts = artifacts,
            Lessons = new Dictionary<LessonId, Lesson>()
        };

        var check = Assert.Single(
            TaskCloseoutEligibility.Evaluate(state).Checks,
            item => item.Name == "lessonsMinted");

        Assert.Equal(expectedSatisfied, check.Satisfied);
    }

    [Fact]
    public void FindingsWhoseLessonsAreAllNoneOweNoPublication()
    {
        var task = ClosedOut();
        var synthesisId = new ArtifactId("A-closeout");
        var artifacts = task.State.Artifacts.ToDictionary(pair => pair.Key, pair => pair.Value);
        var finding = CloseoutSynthesisArtifactTests.FindingCells();
        finding[8] = "none";
        artifacts[synthesisId] = artifacts[synthesisId] with
        {
            Content = CloseoutSynthesisArtifactTests.Body(
                CloseoutSynthesisArtifactTests.Row(finding))
        };
        var state = task.State with
        {
            Artifacts = artifacts,
            Lessons = new Dictionary<LessonId, Lesson>()
        };

        var check = Assert.Single(
            TaskCloseoutEligibility.Evaluate(state).Checks,
            item => item.Name == "lessonsMinted");

        Assert.True(check.Satisfied);
    }

    [Fact]
    public void MissingSynthesisSaysLessonPublicationCannotBeEvaluated()
    {
        var state = ClosedOut(withSynthesis: false).State with
        {
            Lessons = new Dictionary<LessonId, Lesson>()
        };

        var check = Assert.Single(
            TaskCloseoutEligibility.Evaluate(state).Checks,
            item => item.Name == "lessonsMinted");

        Assert.False(check.Satisfied);
        Assert.Equal(
            "No current closeout synthesis is filed, so lesson publication cannot be evaluated.",
            check.Detail);
        Assert.DoesNotContain("recorded findings", check.Detail!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NumericGradeDoesNotGateCleanup()
    {
        var task = ClosedOut(retrospectiveBody: WorkflowRetrospectiveArtifactTests.Table("0"));

        Assert.True(TaskCloseoutEligibility.Evaluate(task.State).Eligible);
    }

    [Fact]
    public void EveryEligibilityConditionIsReportedIndependentlyInStableOrder()
    {
        var report = TaskCloseoutEligibility.Evaluate(ClosedOut().State);

        Assert.Equal(
            [
                "stageIsArchive", "noLiveWorkItems", "noActiveRuns", "noOpenEscalations",
                "noOpenChallenges", "closeoutSynthesisFiled", "workflowRetrospectiveFiled",
                "lessonsMinted"
            ],
            report.Checks.Select(check => check.Name).ToArray());
    }

    internal static TestTask ClosedOut(
        bool withSynthesis = true,
        bool withRetrospective = true,
        bool completeWork = true,
        string? retrospectiveBody = null)
    {
        var task = WorkflowRetrospectiveArtifactTests.Archived(completeWork);
        if (withSynthesis)
        {
            task.Apply(CloseoutSynthesisArtifactTests.Synthesis(
                task, task.OperatorId, "A-closeout"));
        }

        if (withRetrospective)
        {
            task.Apply(WorkflowRetrospectiveArtifactTests.Retrospective(
                task, task.OperatorId, "A-retro", body: retrospectiveBody));
        }

        return task;
    }
}
