using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Storage;
using AILedger.Tests.Artifacts;
using AILedger.Tests.Support;
using System.Text;
using System.Text.Json;

namespace AILedger.Tests.Cli;

public sealed class TaskCleanupCliTests
{
    [Fact]
    public async Task CloseoutEvidenceDistinguishesExactTrimmedAndMissingArtifactMatches()
    {
        using var root = new TemporaryDirectory();
        var task = WithFiledVerifierReport();
        var taskDirectory = new TaskWorkspacePathResolver(root.Path).Resolve(task.TaskId);
        var reviewDirectory = Path.Combine(taskDirectory, "review");
        Directory.CreateDirectory(reviewDirectory);
        var filedBody = task.State.Artifacts[new ArtifactId("A-verifier")].Content;
        await File.WriteAllTextAsync(
            Path.Combine(reviewDirectory, "exact.md"), filedBody, new UTF8Encoding(false));
        await File.WriteAllTextAsync(
            Path.Combine(reviewDirectory, "trimmed.md"), $"  {filedBody}\n", new UTF8Encoding(false));
        await File.WriteAllTextAsync(
            Path.Combine(reviewDirectory, "none.md"), "not a filed artifact", new UTF8Encoding(false));

        using var document = await RunCloseoutEvidence(root.Path, task);
        var reports = document.RootElement.GetProperty("looseReports")
            .EnumerateArray()
            .ToDictionary(report => report.GetProperty("path").GetString()!);

        AssertMatch(reports["review/exact.md"], "exact", matched: true, "A-verifier");
        AssertMatch(reports["review/trimmed.md"], "trimmed", matched: false, "A-verifier");
        AssertMatch(reports["review/none.md"], "none", matched: false);
    }

    [Fact]
    public async Task CloseoutStatusAgreesThatAFindingWithNoLessonOwesNoPublication()
    {
        using var root = new TemporaryDirectory();
        using var lessonRoot = new TemporaryDirectory();
        var task = TaskCloseoutEligibilityTests.ClosedOut();
        var synthesisId = new ArtifactId("A-closeout");
        var artifacts = task.State.Artifacts.ToDictionary(pair => pair.Key, pair => pair.Value);
        var finding = CloseoutSynthesisArtifactTests.FindingCells();
        finding[8] = "none";
        artifacts[synthesisId] = artifacts[synthesisId] with
        {
            Content = CloseoutSynthesisArtifactTests.Body(CloseoutSynthesisArtifactTests.Row(finding))
        };
        var state = task.State with
        {
            Artifacts = artifacts,
            Lessons = new Dictionary<LessonId, Lesson>()
        };

        using var document = await RunCloseoutStatus(root.Path, lessonRoot.Path, state, task.Events);

        AssertLessonPublicationAgreesWithEligibility(document, expectedSatisfied: true);
    }

    [Fact]
    public async Task CloseoutStatusAgreesThatAnUnpublishedFindingOwesPublication()
    {
        using var root = new TemporaryDirectory();
        using var lessonRoot = new TemporaryDirectory();
        var task = TaskCloseoutEligibilityTests.ClosedOut();
        var state = task.State with { Lessons = new Dictionary<LessonId, Lesson>() };

        using var document = await RunCloseoutStatus(root.Path, lessonRoot.Path, state, task.Events);

        AssertLessonPublicationAgreesWithEligibility(document, expectedSatisfied: false);
    }

    [Fact]
    public async Task PlanRefusesAnOutputInsideTheTaskOutsideCleanup()
    {
        using var root = new TemporaryDirectory();
        var task = TaskCloseoutEligibilityTests.ClosedOut();
        var taskDirectory = new TaskWorkspacePathResolver(root.Path).Resolve(task.TaskId);
        Directory.CreateDirectory(taskDirectory);
        await File.WriteAllTextAsync(Path.Combine(taskDirectory, "events.jsonl"), "event\n");
        var outputPath = Path.Combine(taskDirectory, "plan.json");
        var error = new StringWriter();
        var application = new CliApplication(
            TextWriter.Null,
            error,
            _ => new TaskService(task),
            _ => throw new InvalidOperationException("Provider adapter is not used by this test."),
            new ContextAssembler());

        var exit = await application.RunAsync(
            ["task", "cleanup", "plan", "--root", root.Path, "--task", task.TaskId.Value,
             "--actor", task.OperatorId.Value, "--output", outputPath],
            CancellationToken.None);

        Assert.Equal(2, exit);
        Assert.Contains("must be under its 'cleanup' directory", error.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(outputPath));
    }

    private static async Task<JsonDocument> RunCloseoutStatus(
        string root,
        string lessonRoot,
        GovernedTaskState state,
        IReadOnlyList<LedgerEvent> events)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var application = new CliApplication(
            output,
            error,
            _ => new TaskService(state, events),
            _ => throw new InvalidOperationException("Provider adapter is not used by this test."),
            new ContextAssembler());

        var exit = await application.RunAsync(
            ["closeout", "status", "--root", root, "--lesson-root", lessonRoot,
             "--task", state.TaskId.Value, "--actor", "operator"],
            CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, error.ToString());
        return JsonDocument.Parse(output.ToString());
    }

    private static async Task<JsonDocument> RunCloseoutEvidence(string root, TestTask task)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var application = new CliApplication(
            output,
            error,
            _ => new TaskService(task),
            _ => throw new InvalidOperationException("Provider adapter is not used by this test."),
            new ContextAssembler());

        var exit = await application.RunAsync(
            ["closeout", "evidence", "--root", root, "--task", task.TaskId.Value,
             "--actor", task.OperatorId.Value],
            CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, error.ToString());
        return JsonDocument.Parse(output.ToString());
    }

    private static TestTask WithFiledVerifierReport()
    {
        var task = new TestTask();
        task.ReachStage(TaskStage.Verification);
        var workItem = task.State.WorkItems.Values.Single().Id;
        ArtifactCommands.StartVerifierRun(task, workItem, "R-verifier", out var run);
        task.Apply(ArtifactCommands.Record(
            task, new ActorId("verifier"), "A-verifier", GovernedArtifactKind.VerifierOutput,
            ArtifactCommands.VerifierBody.Trim(), "Verifier", workItem, run));
        task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), run,
            AgentRunStatus.Completed, "session-verifier"));
        return task;
    }

    private static void AssertMatch(
        JsonElement report,
        string expectedKind,
        bool matched,
        params string[] artifactIds)
    {
        Assert.Equal(expectedKind, report.GetProperty("matchKind").GetString());
        Assert.Equal(matched, report.GetProperty("matched").GetBoolean());
        Assert.Equal(
            artifactIds,
            report.GetProperty("matchesFiledArtifacts")
                .EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray());
    }

    private static void AssertLessonPublicationAgreesWithEligibility(
        JsonDocument document,
        bool expectedSatisfied)
    {
        var root = document.RootElement;
        var lessonsMinted = root.GetProperty("checks")
            .EnumerateArray()
            .Single(check => check.GetProperty("name").GetString() == "lessonsMinted")
            .GetProperty("satisfied")
            .GetBoolean();
        var lessonPublication = root.GetProperty("lessonPublication")
            .GetProperty("satisfied")
            .GetBoolean();

        Assert.Equal(expectedSatisfied, lessonsMinted);
        Assert.Equal(expectedSatisfied, lessonPublication);
        Assert.Equal(lessonsMinted, lessonPublication);
    }

    private sealed class TaskService : IGovernedTaskService
    {
        private readonly GovernedTaskState _state;
        private readonly IReadOnlyList<LedgerEvent> _events;

        public TaskService(TestTask task) : this(task.State, task.Events)
        {
        }

        public TaskService(GovernedTaskState state, IReadOnlyList<LedgerEvent> events)
        {
            _state = state;
            _events = events;
        }

        public Task<CommandOutcome> ExecuteAsync(
            TaskId taskId, LedgerCommand command, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("This read-only test service does not execute commands.");

        public Task<GovernedTaskState?> GetStateAsync(
            TaskId taskId, CancellationToken cancellationToken) =>
            Task.FromResult<GovernedTaskState?>(_state);

        public async IAsyncEnumerable<LedgerEvent> GetHistoryAsync(
            TaskId taskId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var item in _events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }

            await Task.CompletedTask;
        }
    }
}
