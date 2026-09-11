using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

// Contract constraint 3, channel (c): a score never enters a lesson. A lesson is minted from a
// claim, a rejected alternative or a resolved escalation, and it carries that record's own words
// and its evidence citations. No artifact body is on that path, which is why an artifact carrying
// the dimension table can sit in the task's state while the mint runs and still reach no lesson.
public sealed class LessonMintTests
{
    // The header a retrospective's frozen table opens with. A minted lesson carrying it would mean
    // an artifact body had been wired into the mint, which is the failure this test exists for.
    private const string DimensionTableHeader =
        "| dimension | score | confidence | controllable | evidence |";

    [Fact]
    public void R1_NoMintedLessonBodyCarriesTheDimensionTable()
    {
        var task = new TestTask();
        task.ReachStage(TaskStage.Learn);

        // An artifact whose body carries a full dimension table, in state before the mint runs.
        // The mint reads the marked record and its evidence, so this must reach no lesson; wire an
        // artifact body into LessonRules.CreateLesson and this test is what fails.
        task.Apply(ArtifactCommands.Record(
            task, task.OperatorId, "A-request-2", GovernedArtifactKind.UserRequest,
            "# Request\n\n" + WorkflowRetrospectiveArtifactTests.Table(),
            supersedes: "A-request"));
        foreach (var item in task.State.WorkItems.Values
                     .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
                     .ToArray())
        {
            task.Apply(new CompleteWorkItemCommand(
                task.OperatorId, null, task.NextCorrelation(), item.Id));
        }

        task.Apply(new MarkLessonBearingCommand(
            task.OperatorId, null, task.NextCorrelation(), LessonSourceKind.RejectedAlternative, "ALT-stage",
            Class: LessonClass.Refuted, Repo: "AILedger", Tags: ["retrospective"],
            Verify: "ailedger status --task task-1",
            DoNot: "Do not walk the stages without recording what was considered",
            Actor: LessonActor.Verifier, Kind: LessonKind.Workflow,
            VerifyExpects: VerifyExpectation.Present));
        var outcome = task.Transition(TaskStage.Archive);

        var minted = outcome.Events.Select(item => item.Data).OfType<LessonMinted>()
            .Select(item => item.Lesson).ToArray();
        Assert.NotEmpty(minted);
        // The lesson carries the finding and its citations, which is what makes it worth recalling.
        Assert.All(minted, lesson => Assert.False(string.IsNullOrWhiteSpace(lesson.Statement)));
        Assert.All(minted, lesson =>
        {
            Assert.DoesNotContain(DimensionTableHeader, Text(lesson), StringComparison.Ordinal);
            // Half a header is still a score reaching a lesson.
            Assert.DoesNotContain("| dimension | score |", Text(lesson), StringComparison.Ordinal);
        });
    }

    // The other half, and the stronger statement: a retrospective can never be in state while the
    // mint runs. It is refused before Archive, the mint runs only on the transition into Archive,
    // and Archive has no outgoing transition — so no LessonMinted event can ever follow one.
    [Fact]
    public void R1_NoMintCanEverFollowAFiledRetrospective()
    {
        var task = WorkflowRetrospectiveArtifactTests.Archived();
        task.Apply(WorkflowRetrospectiveArtifactTests.Retrospective(task, task.OperatorId, "A-retro"));
        var mintedBefore = task.Events.Count(item => item.Data is LessonMinted);

        foreach (var stage in Enum.GetValues<TaskStage>())
        {
            Assert.Throws<GovernanceException>(() => task.Transition(stage));
        }

        Assert.Equal(mintedBefore, task.Events.Count(item => item.Data is LessonMinted));
        Assert.Contains(DimensionTableHeader, task.State.Artifacts[new ArtifactId("A-retro")].Content,
            StringComparison.Ordinal);
    }

    private static string Text(Lesson lesson) => string.Join(
        "\n",
        [
            lesson.Statement,
            lesson.Outcome,
            lesson.Verify ?? string.Empty,
            lesson.DoNot ?? string.Empty,
            .. lesson.Citations,
            .. lesson.Tags ?? []
        ]);
}
