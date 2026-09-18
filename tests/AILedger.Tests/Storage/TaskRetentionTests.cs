using System.Text;
using System.Text.Json;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Artifacts;
using AILedger.Tests.Support;

namespace AILedger.Tests.Storage;

[CollectionDefinition(EnvironmentVariableCollection.Name, DisableParallelization = true)]
public sealed class EnvironmentVariableCollection
{
    public const string Name = "Environment variables";
}

[Collection(EnvironmentVariableCollection.Name)]
public sealed class TaskRetentionTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);
    private readonly TemporaryDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void APlanDeletesOnlyPathsNamedDeleteByTheSynthesis()
    {
        var task = Fixture(Delete("runs/R1.json", "raw stream", "F1"));

        var plan = Plan(task);

        var deletion = Assert.Single(plan.Entries.Where(
            entry => entry.Decision == TaskRetentionDecision.Delete));
        Assert.Equal("runs/R1.json", deletion.Path);
        Assert.Equal("raw stream", deletion.Reason);
        Assert.Equal("F1", deletion.Evidence);
        Assert.All(plan.Entries.Where(entry => entry.Path != deletion.Path),
            entry => Assert.Equal(TaskRetentionDecision.Retain, entry.Decision));
    }

    [Fact]
    public void WithoutDeleteRowsEverythingIsRetained()
    {
        var plan = Plan(Fixture());

        Assert.Empty(plan.Entries.Where(entry => entry.Decision == TaskRetentionDecision.Delete));
        Assert.Equal(0, plan.Totals.BytesToDelete);
        Assert.Contains("No retention row names this path",
            plan.Entries.Single(entry => entry.Path == "runs/R1.json").Reason,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MatchingArtifactBytesAreReportedAsAReplacementFactOnly()
    {
        var task = Fixture(Delete("review/copy.md", "body already filed", "F1"));
        File.WriteAllText(
            Path.Combine(TaskDirectory, "review", "copy.md"),
            task.State.Artifacts[new ArtifactId("A-closeout")].Content,
            new UTF8Encoding(false));

        var entry = Plan(task).Entries.Single(item => item.Path == "review/copy.md");

        Assert.Equal("A-closeout", entry.Replacement);
        Assert.Contains("exact content remains", entry.DiagnosticsLost!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("events.jsonl")]
    [InlineData("refusals.jsonl")]
    [InlineData(".writer.lock")]
    [InlineData("state.json")]
    [InlineData("cleanup/old.intent.json")]
    public void ProtectedPathsCannotBePlannedForDeletion(string path)
    {
        var task = Fixture(Delete(path, "looks disposable", "F1"));

        var error = Assert.Throws<GovernanceException>(() => Plan(task));

        Assert.Contains("may", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deleted", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ADeleteRowForAPathNotOnDiskIsRefused()
    {
        var task = Fixture(Delete("runs/absent.json", "assumed present", "F1"));

        var error = Assert.Throws<GovernanceException>(() => Plan(task));

        Assert.Contains("no such file is in the task directory", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PlanningAnIneligibleTaskNamesEachFailedCheck()
    {
        var task = TaskCloseoutEligibilityTests.ClosedOut(
            withSynthesis: false, withRetrospective: false, completeWork: false);
        WriteWorkspace();

        var error = Assert.Throws<GovernanceException>(() => TaskRetentionPlanner.Plan(
            TaskDirectory, task.State, TaskCloseoutEligibility.Evaluate(task.State), Now));

        Assert.Contains("noLiveWorkItems", error.Message, StringComparison.Ordinal);
        Assert.Contains("closeoutSynthesisFiled", error.Message, StringComparison.Ordinal);
        Assert.Contains("workflowRetrospectiveFiled", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ALinkAnywhereBelowTheTaskRefusesTheScan()
    {
        var task = Fixture();
        var outside = Path.Combine(_directory.Path, "outside.txt");
        File.WriteAllText(outside, "outside");
        File.CreateSymbolicLink(Path.Combine(TaskDirectory, "runs", "linked.json"), outside);

        var error = Assert.Throws<GovernanceException>(() => Plan(task));

        Assert.Contains("symbolic link or reparse point", error.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public async Task ASymbolicLinkAboveTheTaskRootIsToleratedAndApplySucceeds()
    {
        var physicalParent = Path.Combine(_directory.Path, "physical-parent");
        var linkedParent = Path.Combine(_directory.Path, "linked-parent");
        Directory.CreateDirectory(physicalParent);
        Directory.CreateSymbolicLink(linkedParent, physicalParent);
        var taskDirectory = Path.Combine(linkedParent, "task-1");
        var task = FixtureAt(taskDirectory, Delete("runs/R1.json", "raw stream", "F1"));
        var plan = Plan(task, taskDirectory);

        var result = await ApplyAsync(task, plan, taskDirectory);

        Assert.Equal(1, result.Removed);
        Assert.False(File.Exists(Path.Combine(taskDirectory, "runs", "R1.json")));
        Assert.True(File.Exists(Path.Combine(taskDirectory, "runs", "R2.json")));
    }

    [Fact]
    public void ASymbolicLinkAtTheTaskRootIsRefused()
    {
        var physicalTask = Path.Combine(_directory.Path, "physical-task");
        var linkedTask = Path.Combine(_directory.Path, "linked-task");
        var task = FixtureAt(physicalTask);
        Directory.CreateSymbolicLink(linkedTask, physicalTask);

        var error = Assert.Throws<GovernanceException>(() => Plan(task, linkedTask));

        Assert.Contains("symbolic link or reparse point", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyValidatesPlanEntriesAgainstTheTaskRootNotTheTemporaryDirectory()
    {
        var task = Fixture(Delete("runs/R1.json", "raw stream", "F1"));
        var plan = Plan(task);
        var physicalTemporaryDirectory = Path.Combine(_directory.Path, "physical-temp");
        var linkedTemporaryDirectory = Path.Combine(_directory.Path, "linked-temp");
        Directory.CreateDirectory(physicalTemporaryDirectory);
        Directory.CreateSymbolicLink(linkedTemporaryDirectory, physicalTemporaryDirectory);
        var originalTemporaryDirectory = Environment.GetEnvironmentVariable("TMPDIR");

        try
        {
            Environment.SetEnvironmentVariable("TMPDIR", linkedTemporaryDirectory);
            Assert.Equal(
                Path.GetFullPath(linkedTemporaryDirectory),
                Path.TrimEndingDirectorySeparator(Path.GetTempPath()));

            var result = await ApplyAsync(task, plan);

            Assert.Equal(1, result.Removed);
            Assert.False(File.Exists(Path.Combine(TaskDirectory, "runs", "R1.json")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TMPDIR", originalTemporaryDirectory);
        }
    }

    [Theory]
    [InlineData("../escape.json")]
    [InlineData("runs/../../escape.json")]
    [InlineData("/tmp/escape.json")]
    public void TheResolverRefusesPathsOutsideTheTask(string path)
    {
        Directory.CreateDirectory(TaskDirectory);

        var error = Assert.Throws<GovernanceException>(() =>
            TaskRetentionScan.ResolveContained(TaskDirectory, path));

        Assert.Contains("relative to the task directory", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyRemovesExactlyTheNamedFileAndWritesAReceipt()
    {
        var task = Fixture(Delete("runs/R1.json", "raw stream", "F1"));
        var plan = Plan(task);

        var result = await ApplyAsync(task, plan);

        Assert.Equal(1, result.Removed);
        Assert.Equal(0, result.RemovedWithoutReceipt);
        Assert.Equal(0, result.AlreadyRemoved);
        Assert.False(File.Exists(Path.Combine(TaskDirectory, "runs", "R1.json")));
        Assert.True(File.Exists(Path.Combine(TaskDirectory, "runs", "R2.json")));
        Assert.True(File.Exists(EventsPath));
        Assert.Single(await File.ReadAllLinesAsync(
            TaskRetentionApplier.ReceiptsPath(CleanupDirectory, plan.PlanId)));
    }

    [Fact]
    public async Task ApplyRecordsARealStartAndCompletionRatherThanAZeroDuration()
    {
        var task = Fixture(Delete("runs/R1.json", "raw stream", "F1"));
        var plan = Plan(task);
        var completedAt = Now.AddMinutes(7);

        var result = await ApplyAsync(task, plan, timeProvider: new FixedTimeProvider(completedAt));

        Assert.Equal(Now, result.StartedAtUtc);
        Assert.Equal(completedAt, result.CompletedAtUtc);
    }

    [Fact]
    public async Task RepeatingApplyIsIdempotent()
    {
        var task = Fixture(Delete("runs/R1.json", "raw stream", "F1"));
        var plan = Plan(task);
        await ApplyAsync(task, plan);

        var repeated = await ApplyAsync(task, plan);

        Assert.Equal(0, repeated.Removed);
        Assert.Equal(1, repeated.AlreadyRemoved);
        Assert.Equal(0, repeated.BytesReclaimed);
    }

    [Fact]
    public async Task AnInterruptedRemovalWithPreExistingIntentIsRecovered()
    {
        var task = Fixture(Delete("runs/R1.json", "raw stream", "F1"));
        var plan = Plan(task);
        await ApplyAsync(task, plan);
        File.Delete(TaskRetentionApplier.ReceiptsPath(CleanupDirectory, plan.PlanId));

        var recovered = await ApplyAsync(task, plan);

        Assert.Equal(0, recovered.Removed);
        Assert.Equal(1, recovered.RemovedWithoutReceipt);
        Assert.Equal(TaskRetentionOutcome.RemovedWithoutReceipt,
            Assert.Single(recovered.Receipts).Outcome);
    }

    [Fact]
    public async Task AnAbsentFileWithoutPreExistingIntentIsNotCountedAsDeleted()
    {
        var task = Fixture(Delete("runs/R1.json", "raw stream", "F1"));
        var plan = Plan(task);
        File.Delete(Path.Combine(TaskDirectory, "runs", "R1.json"));

        var error = await Assert.ThrowsAsync<GovernanceException>(() => ApplyAsync(task, plan));

        Assert.Contains("unexplained absence", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplyRefusesAChangedTaskVersion()
    {
        var task = Fixture(Delete("runs/R1.json", "raw stream", "F1"));
        var plan = Plan(task);
        var moved = task.State with { Version = task.State.Version + 1 };

        var error = await Assert.ThrowsAsync<GovernanceException>(() =>
            TaskRetentionApplier.ApplyAsync(
                TaskDirectory, moved, TaskCloseoutEligibility.Evaluate(moved), plan,
                task.OperatorId, Now));

        Assert.Contains("task version", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(TaskDirectory, "runs", "R1.json")));
    }

    [Fact]
    public async Task ApplyRefusesAChangedCanonicalLog()
    {
        var task = Fixture(Delete("runs/R1.json", "raw stream", "F1"));
        var plan = Plan(task);
        await File.AppendAllTextAsync(EventsPath, "{\"appended\":true}\n");

        var error = await Assert.ThrowsAsync<GovernanceException>(() => ApplyAsync(task, plan));

        Assert.Contains("events.jsonl", error.Message, StringComparison.Ordinal);
        Assert.Contains("has changed", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARefusalAppendedAfterPlanningDoesNotBlockApply()
    {
        var task = Fixture(Delete("runs/R1.json", "raw stream", "F1"));
        var plan = Plan(task);
        await File.AppendAllTextAsync(
            Path.Combine(TaskDirectory, "refusals.jsonl"),
            "{\"actorId\":\"operator\",\"reason\":\"ordinary refusal\"}\n");

        var result = await ApplyAsync(task, plan);

        Assert.Equal(1, result.Removed);
        Assert.False(File.Exists(Path.Combine(TaskDirectory, "runs", "R1.json")));
    }

    [Fact]
    public async Task ApplyRefusesAChangedPlannedFile()
    {
        var task = Fixture(Delete("runs/R1.json", "raw stream", "F1"));
        var plan = Plan(task);
        await File.WriteAllTextAsync(Path.Combine(TaskDirectory, "runs", "R1.json"), "changed");

        var error = await Assert.ThrowsAsync<GovernanceException>(() => ApplyAsync(task, plan));

        Assert.Contains("has changed since the plan was written", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyRefusesAFileThePlanNeverEnumerated()
    {
        var task = Fixture(Delete("runs/R1.json", "raw stream", "F1"));
        var plan = Plan(task);
        await File.WriteAllTextAsync(Path.Combine(TaskDirectory, "runs", "R3.json"), "{}");

        var error = await Assert.ThrowsAsync<GovernanceException>(() => ApplyAsync(task, plan));

        Assert.Contains("plan does not account for", error.Message, StringComparison.Ordinal);
        Assert.Contains("runs/R3.json", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyRefusesAPlanWithAPathOutsideTheTask()
    {
        var task = Fixture(Delete("runs/R1.json", "raw stream", "F1"));
        var plan = Plan(task);
        var entry = plan.Entries.Single(item => item.Path == "runs/R1.json") with
        {
            Path = "../outside.json"
        };
        var tampered = plan with
        {
            Entries = plan.Entries.Select(item =>
                item.Path == "runs/R1.json" ? entry : item).ToArray()
        };

        var error = await Assert.ThrowsAsync<GovernanceException>(() => ApplyAsync(task, tampered));

        Assert.Contains("relative to the task directory", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyRefusesANullPlanEntryAsGovernanceInput()
    {
        var task = Fixture(Delete("runs/R1.json", "raw stream", "F1"));
        var plan = Plan(task);
        var tampered = plan with
        {
            Entries = plan.Entries.Append((TaskRetentionEntry)null!).ToArray()
        };

        var error = await Assert.ThrowsAsync<GovernanceException>(() => ApplyAsync(task, tampered));

        Assert.Contains("entries may not be null", error.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(TaskDirectory, "runs", "R1.json")));
    }

    [Fact]
    public async Task ApplyRefusesADeletionTheCurrentSynthesisDoesNotAuthorise()
    {
        var task = Fixture();
        var plan = Plan(task);
        var entry = plan.Entries.Single(item => item.Path == "review/verifier-1.md") with
        {
            Decision = TaskRetentionDecision.Delete,
            Reason = "tampered plan",
            Evidence = "none",
            DiagnosticsLost = "task projection"
        };
        var entries = plan.Entries.Select(item =>
            item.Path == entry.Path ? entry : item).ToArray();
        var tampered = plan with
        {
            Entries = entries,
            Totals = Totals(entries)
        };

        var error = await Assert.ThrowsAsync<GovernanceException>(() => ApplyAsync(task, tampered));

        Assert.Contains(
            "is not the deletion authorised by the current closeout synthesis",
            error.Message,
            StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(TaskDirectory, "review", "verifier-1.md")));
    }

    [Fact]
    public async Task ApplyRefusesAnActorWithNoTaskAssignmentBeforeDeletingAnything()
    {
        var task = Fixture(Delete("runs/R1.json", "raw stream", "F1"));
        var plan = Plan(task);
        var actor = new ActorId("unassigned-worker");

        var error = await Assert.ThrowsAsync<GovernanceException>(() =>
            TaskRetentionApplier.ApplyAsync(
                TaskDirectory, task.State, TaskCloseoutEligibility.Evaluate(task.State), plan,
                actor, Now));

        Assert.Contains("unassigned-worker", error.Message, StringComparison.Ordinal);
        Assert.Contains("no assigned role", error.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(TaskDirectory, "runs", "R1.json")));
    }

    [Fact]
    public async Task ApplyRefusesAnAssignedWorkerBeforeDeletingAnything()
    {
        var task = Fixture(Delete("runs/R1.json", "raw stream", "F1"));
        var actor = new ActorId("assigned-worker");
        task.Assign(actor, RoleKind.Worker, Capability.BuildContext);
        var plan = Plan(task);

        var error = await Assert.ThrowsAsync<GovernanceException>(() =>
            TaskRetentionApplier.ApplyAsync(
                TaskDirectory, task.State, TaskCloseoutEligibility.Evaluate(task.State), plan,
                actor, Now));

        Assert.Contains("assigned-worker", error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(RoleKind.Worker), error.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(TaskDirectory, "runs", "R1.json")));
    }

    [Fact]
    public async Task CleanupRecordsDoNotInvalidateTheirOwnPlan()
    {
        var task = Fixture(Delete("runs/R1.json", "raw stream", "F1"));
        var plan = Plan(task);
        await ApplyAsync(task, plan);

        Assert.DoesNotContain(plan.Entries,
            entry => entry.Path.StartsWith("cleanup/", StringComparison.Ordinal));
        Assert.True(File.Exists(TaskRetentionApplier.IntentPath(CleanupDirectory, plan.PlanId)));
        Assert.True(File.Exists(TaskRetentionApplier.ResultPath(CleanupDirectory, plan.PlanId)));
        Assert.Equal(1, (await ApplyAsync(task, plan)).AlreadyRemoved);
    }

    [Fact]
    public void ThePlanBindsCanonicalInputsAndEveryFileFact()
    {
        var task = Fixture(Delete("runs/R1.json", "raw stream", "F1"));

        var plan = Plan(task);

        Assert.Equal("A-closeout", plan.Inputs.CloseoutSynthesisArtifactId);
        Assert.Equal("A-retro", plan.Inputs.WorkflowRetrospectiveArtifactId);
        Assert.Equal(task.State.Version, plan.Inputs.TaskVersion);
        Assert.NotNull(plan.Inputs.EventsSha256);
        Assert.All(plan.Entries, entry =>
        {
            Assert.Equal(64, entry.Sha256.Length);
            Assert.True(entry.Length >= 0);
            Assert.False(string.IsNullOrWhiteSpace(entry.Reason));
        });
    }

    [Theory]
    [InlineData("canonical.events")]
    [InlineData(".canonical.lock")]
    [InlineData("canonical.state")]
    public void ANonDefaultLayoutProtectsItsCanonicalFilesDuringPlanning(string protectedPath)
    {
        var layout = new TaskWorkspaceLayout(
            EventsFileName: "canonical.events",
            LockFileName: ".canonical.lock",
            StateFileName: "canonical.state");
        var taskDirectory = Path.Combine(_directory.Path, "plan-" + protectedPath.Replace('.', '-'));
        var task = FixtureAt(
            taskDirectory,
            layout,
            Delete(protectedPath, "looks disposable", "F1"));

        var error = Assert.Throws<GovernanceException>(() => Plan(task, taskDirectory, layout));

        Assert.Contains("canonical logs and the task mutation lock may never be deleted", error.Message,
            StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(taskDirectory, protectedPath)));
    }

    [Theory]
    [InlineData("canonical.events")]
    [InlineData(".canonical.lock")]
    public async Task ANonDefaultLayoutProtectsItsCanonicalLogAndLockDuringApply(string protectedPath)
    {
        var layout = new TaskWorkspaceLayout(
            EventsFileName: "canonical.events",
            LockFileName: ".canonical.lock");
        var taskDirectory = Path.Combine(_directory.Path, "apply-custom-layout");
        var task = FixtureAt(taskDirectory, layout);
        var plan = Plan(task, taskDirectory, layout);
        var entries = plan.Entries.Select(entry =>
            entry.Path == layout.EventsFileName
                ? entry with
                {
                    Path = protectedPath,
                    Decision = TaskRetentionDecision.Delete,
                    Evidence = "F1",
                    DiagnosticsLost = "canonical log would be lost"
                }
                : entry).ToArray();
        var forged = plan with { Entries = entries, Totals = Totals(entries) };

        var error = await Assert.ThrowsAsync<GovernanceException>(() =>
            ApplyAsync(task, forged, taskDirectory, layout));

        Assert.Contains($"protected path '{protectedPath}'", error.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(taskDirectory, protectedPath)));
    }

    private string TaskDirectory => Path.Combine(_directory.Path, "task-1");
    private string CleanupDirectory => Path.Combine(TaskDirectory, "cleanup");
    private string EventsPath => Path.Combine(TaskDirectory, "events.jsonl");

    private TaskRetentionPlan Plan(
        TestTask task,
        string? taskDirectory = null,
        TaskWorkspaceLayout? layout = null) =>
        TaskRetentionPlanner.Plan(
            taskDirectory ?? TaskDirectory,
            task.State,
            TaskCloseoutEligibility.Evaluate(task.State),
            Now,
            layout);

    private Task<TaskRetentionResult> ApplyAsync(
        TestTask task,
        TaskRetentionPlan plan,
        string? taskDirectory = null,
        TaskWorkspaceLayout? layout = null,
        TimeProvider? timeProvider = null) =>
        TaskRetentionApplier.ApplyAsync(
            taskDirectory ?? TaskDirectory,
            task.State,
            TaskCloseoutEligibility.Evaluate(task.State),
            plan,
            task.OperatorId, Now,
            layout: layout,
            timeProvider: timeProvider);

    private static string Delete(string path, string reason, string evidence) =>
        CloseoutSynthesisArtifactTests.RetentionRow(path, "delete", reason, evidence);

    private TestTask Fixture(params string[] retentionRows)
        => FixtureAt(TaskDirectory, retentionRows);

    private TestTask FixtureAt(string taskDirectory, params string[] retentionRows)
        => FixtureAt(taskDirectory, new TaskWorkspaceLayout(), retentionRows);

    private TestTask FixtureAt(
        string taskDirectory,
        TaskWorkspaceLayout layout,
        params string[] retentionRows)
    {
        var task = WorkflowRetrospectiveArtifactTests.Archived();
        task.Apply(CloseoutSynthesisArtifactTests.Synthesis(
            task, task.OperatorId, "A-closeout",
            body: CloseoutSynthesisArtifactTests.Body(CloseoutSynthesisArtifactTests.Finding()) +
                string.Concat(retentionRows)));
        task.Apply(WorkflowRetrospectiveArtifactTests.Retrospective(
            task, task.OperatorId, "A-retro"));
        WriteWorkspace(taskDirectory, layout);
        return task;
    }

    private void WriteWorkspace(
        string? taskDirectory = null,
        TaskWorkspaceLayout? layout = null)
    {
        layout ??= new TaskWorkspaceLayout();
        var root = taskDirectory ?? TaskDirectory;
        Directory.CreateDirectory(Path.Combine(root, "runs"));
        Directory.CreateDirectory(Path.Combine(root, "review"));
        File.WriteAllText(Path.Combine(root, layout.EventsFileName), "{\"eventId\":\"task-1:0000000001\"}\n");
        File.WriteAllText(Path.Combine(root, "refusals.jsonl"), "{\"actorId\":\"operator\"}\n");
        File.WriteAllText(Path.Combine(root, layout.LockFileName), string.Empty);
        File.WriteAllText(Path.Combine(root, layout.StateFileName), "{}");
        File.WriteAllText(Path.Combine(root, "runs", "R1.json"), Json(2048));
        File.WriteAllText(Path.Combine(root, "runs", "R2.json"), Json(1024));
        File.WriteAllText(Path.Combine(root, "review", "verifier-1.md"), "# Verifier\n\nFAIL\n");
    }

    private static string Json(int padding) =>
        JsonSerializer.Serialize(new { events = new string('e', padding) });

    private static TaskRetentionTotals Totals(IReadOnlyList<TaskRetentionEntry> entries) =>
        new(
            entries.Count(entry => entry.Decision == TaskRetentionDecision.Retain),
            entries.Count(entry => entry.Decision == TaskRetentionDecision.Delete),
            entries.Where(entry => entry.Decision == TaskRetentionDecision.Retain).Sum(entry => entry.Length),
            entries.Where(entry => entry.Decision == TaskRetentionDecision.Delete).Sum(entry => entry.Length));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

}
