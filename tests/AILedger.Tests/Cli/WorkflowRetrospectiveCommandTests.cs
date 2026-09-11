using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Cli;

[Collection(StandardInput.Collection)]
public sealed class WorkflowRetrospectiveCommandTests
{
    [Fact]
    public async Task R1_CommandReportsTheStageRefusalFromCore()
    {
        var task = new TestTask();
        var error = await RecordAsync(task);
        Assert.Equal(
            "error: A workflow retrospective is recorded only at stage 'Archive'; this task is at stage 'Discovery'.\n",
            error);
    }

    [Fact]
    public async Task R1_CommandReportsTheLiveWorkRefusalFromCore()
    {
        var task = Archived(completeWork: false);
        var error = await RecordAsync(task);
        Assert.Equal(
            "error: A workflow retrospective is recorded only when no work item is live; 'W-stage' is still " +
            "live in status 'Paused'.\n",
            error);
    }

    [Fact]
    public async Task R1_CommandReportsTheActiveRunRefusalFromCore()
    {
        var task = Archived();
        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R-late"), null, "codex",
            null, null, null, null, new ActorId("researcher")));
        var error = await RecordAsync(task);
        Assert.Equal(
            "error: A workflow retrospective is recorded only when no run is active; run 'R-late' is still active.\n",
            error);
    }

    [Fact]
    public async Task ABodyLargerThanTheGenericArtifactLimitReachesCoreUnchanged()
    {
        var task = Archived();
        var error = new StringWriter();
        var application = Create(task, error);
        var body = new string('x', 1024 * 1024 + 1) + "\n\n" + Table();
        int exit;
        using (new StandardInput(body))
        {
            exit = await application.RunAsync(Command(), CancellationToken.None);
        }

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, error.ToString());
        var artifact = task.State.Artifacts[new ArtifactId("A-retro")];
        Assert.Equal(GovernedArtifactKind.WorkflowRetrospective, artifact.Kind);
        Assert.Null(artifact.WorkItemId);
        Assert.Null(artifact.ProducerRunId);
        Assert.Equal(body, artifact.Content);
    }

    [Fact]
    public async Task RunIsNotACommandOption()
    {
        var task = Archived();
        var error = new StringWriter();
        var application = Create(task, error);
        var exit = await application.RunAsync([.. Command(), "--run", "R1"], CancellationToken.None);
        Assert.Equal(2, exit);
        Assert.Contains(
            "Unknown option(s) for command 'retrospective record': --run",
            error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HelpStatesTheEntryConditionsInCheckOrderAndExplainsWhyRunIsAbsent()
    {
        var output = new StringWriter();
        var application = new CliApplication(
            output, TextWriter.Null, _ => new TaskService(new TestTask()),
            _ => throw new InvalidOperationException(), new ContextAssembler());
        var exit = await application.RunAsync(["--help"], CancellationToken.None);
        Assert.Equal(0, exit);
        var help = output.ToString();
        var command = help.IndexOf("retrospective record", StringComparison.Ordinal);
        var archive = help.IndexOf("reaches Archive", command, StringComparison.Ordinal);
        var liveWork = help.IndexOf("no live work item", archive, StringComparison.Ordinal);
        var activeRun = help.IndexOf("no active run", liveWork, StringComparison.Ordinal);
        Assert.True(command >= 0 && command < archive && archive < liveWork && liveWork < activeRun);
        Assert.Contains("It takes no --run because it is filed after closeout", help, StringComparison.Ordinal);
    }

    private static async Task<string> RecordAsync(TestTask task)
    {
        var error = new StringWriter();
        var application = Create(task, error);
        using (new StandardInput(Table()))
        {
            var exit = await application.RunAsync(Command(), CancellationToken.None);
            Assert.Equal(1, exit);
        }
        return error.ToString();
    }

    private static CliApplication Create(TestTask task, TextWriter error) => new(
        TextWriter.Null, error, _ => new TaskService(task),
        _ => throw new InvalidOperationException(), new ContextAssembler());

    private static string[] Command() =>
    [
        "retrospective", "record", "--root", "/unused", "--task", "task-1",
        "--actor", "operator", "--id", "A-retro", "--title", "How governance performed",
        "--body-stdin"
    ];

    // Varied scores, and none of them '0', '5' or 'unmeasured'. Nothing in this file asserts
    // anything about a score, so nothing in it should fail when a score is mutated against — that
    // belongs to WorkflowRetrospectiveArtifactTests.NoScoreChangesTheOutcomeOfAnything alone.
    private static readonly string[] Scores = ["3", "1", "4", "2", "3", "1", "4", "2", "1", "3"];

    private static string Table()
    {
        var rows = string.Empty;
        for (var index = 1; index <= 10; index++)
        {
            rows += $"| D{index} | {Scores[index - 1]} | low | not-applicable | event E{index} |\n";
        }
        return "| dimension | score | confidence | controllable | evidence |\n" +
               "| --- | --- | --- | --- | --- |\n" + rows;
    }

    private static TestTask Archived(bool completeWork = true)
    {
        var task = new TestTask();
        task.ReachStage(TaskStage.Learn);
        if (completeWork)
        {
            foreach (var item in task.State.WorkItems.Values.ToArray())
            {
                task.Apply(new CompleteWorkItemCommand(
                    task.OperatorId, null, task.NextCorrelation(), item.Id));
            }
        }
        task.Apply(new MarkLessonBearingCommand(
            task.OperatorId, null, task.NextCorrelation(), LessonSourceKind.RejectedAlternative, "ALT-stage",
            Class: LessonClass.Refuted, Repo: "AILedger", Tags: ["retrospective"],
            Verify: "ailedger status --task task-1", DoNot: "Do not omit the evidence",
            Actor: LessonActor.Verifier, Kind: LessonKind.Workflow,
            VerifyExpects: VerifyExpectation.Present));
        task.Transition(TaskStage.Archive);
        return task;
    }

    private sealed class TaskService(TestTask task) : IGovernedTaskService
    {
        public Task<CommandOutcome> ExecuteAsync(
            TaskId taskId, LedgerCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(task.Apply(command));

        public Task<GovernedTaskState?> GetStateAsync(TaskId taskId, CancellationToken cancellationToken) =>
            Task.FromResult<GovernedTaskState?>(task.State);

        public async IAsyncEnumerable<LedgerEvent> GetHistoryAsync(
            TaskId taskId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var item in task.Events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }
            await Task.CompletedTask;
        }
    }
}
