using System.Text.Json;
using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.Runs.Batch;
using AILedger.Storage;
using AILedger.Tests.Support;
using static AILedger.Tests.Cli.CliApplicationTestSupport;

namespace AILedger.Tests.Cli;

[Collection(StandardInput.Collection)]
public sealed class BatchPreflightCliTests
{
    [Fact]
    public async Task ANullMemberReturnsIndexedUsageErrorInsteadOfEscapingNullReferenceException()
    {
        const string body = """
            {"members":[null]}
            """;
        using var input = new StandardInput(body);
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var error = new StringWriter();
        var application = new CliApplication(
            output, error, Service,
            _ => throw new InvalidOperationException("Invalid input must not resolve an adapter."),
            new ContextAssembler());
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await ContextBrief.BuildAsync(root.Path, "T1");
        output.GetStringBuilder().Clear();

        var exit = await application.RunAsync(
            ["preflight", "batch", .. common, "--body-stdin"], CancellationToken.None);

        Assert.Equal(2, exit);
        Assert.Equal(
            "error: Batch preflight member at index 0 cannot be null." + Environment.NewLine,
            error.ToString());
        Assert.DoesNotContain(nameof(NullReferenceException), error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MixedBatchReportsOneVersionMembersAndGroupedCausesWithoutMutatingTheTask()
    {
        const string body = """
            {"members":[
              {"id":"planned-work","workItem":{"workItemId":"W1","title":"Planned","resourceScope":[]}},
              {"id":"review-a","providerLaunch":{"subjectActorId":"reviewer"}},
              {"id":"review-b","providerLaunch":{"subjectActorId":"reviewer"}}
            ]}
            """;
        // CliApplication captures process stdin when its type is initialized. Install the test
        // reader before constructing it so this new read surface receives the request body.
        using var input = new StandardInput(body);
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var error = new StringWriter();
        var factoryCalls = 0;
        var application = new CliApplication(
            output, error, Service,
            _ =>
            {
                factoryCalls++;
                throw new InvalidOperationException("Read-only preflight must not resolve an adapter.");
            },
            new ContextAssembler());
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);
        await application.RunAsync(
            ["actor", "attach", .. common, "--target", "reviewer", "--role", "code-reviewer"],
            CancellationToken.None);
        var before = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        output.GetStringBuilder().Clear();

        var exit = await application.RunAsync(
            ["preflight", "batch", .. common, "--body-stdin"], CancellationToken.None);

        var after = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.True(exit == 0, error.ToString());
        Assert.Equal(0, factoryCalls);
        Assert.Equal(before!.Version, after!.Version);
        Assert.Empty(after.WorkItems);
        Assert.Empty(after.Runs);

        using var json = JsonDocument.Parse(output.ToString());
        var result = json.RootElement;
        Assert.Equal(before.Version, result.GetProperty("checkedTaskVersion").GetInt64());
        Assert.False(result.GetProperty("admissible").GetBoolean());
        Assert.Equal(3, result.GetProperty("members").GetArrayLength());
        var cause = Assert.Single(result.GetProperty("causes").EnumerateArray());
        Assert.Equal(["review-a", "review-b"],
            cause.GetProperty("affectedMembers").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Contains(nameof(RoleKind.CodeReviewer), cause.GetProperty("refusal").GetString(),
            StringComparison.Ordinal);
    }

    // R8 (batch-report-overstates-admissibility): planned work is admitted by the same stage rule
    // as work add. A read in Discovery may describe the refusal, but may not call the plan ready.
    [Fact]
    public async Task AStageInadmissibleMemberMakesTheBatchNonAdmissible()
    {
        const string body = """
            {"members":[
              {"id":"too-early","workItem":{"workItemId":"W1","title":"Planned","resourceScope":[]}}
            ]}
            """;
        using var input = new StandardInput(body);
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var application = new CliApplication(
            output, TextWriter.Null, Service,
            _ => throw new InvalidOperationException("Read-only preflight must not resolve an adapter."),
            new ContextAssembler());
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await ContextBrief.BuildAsync(root.Path, "T1");
        output.GetStringBuilder().Clear();

        var exit = await application.RunAsync(
            ["preflight", "batch", .. common, "--body-stdin"], CancellationToken.None);

        Assert.Equal(0, exit);
        using var json = JsonDocument.Parse(output.ToString());
        var result = json.RootElement;
        Assert.False(result.GetProperty("admissible").GetBoolean());
        var member = Assert.Single(result.GetProperty("members").EnumerateArray().ToArray());
        Assert.False(member.GetProperty("admissible").GetBoolean());
        Assert.Contains(nameof(TaskStage.Ready), member.GetProperty("refusal").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnInvalidScopeRefusesOnlyItsMemberAndDoesNotHideSiblings()
    {
        using var root = new TemporaryDirectory();
        var missing = Path.Combine(root.Path, "does-not-exist");
        var body = $$$"""
            {"members":[
              {"id":"invalid-scope","workItem":{"workItemId":"W1","title":"Invalid","resourceScope":["{{{missing}}}"]}},
              {"id":"valid-work","workItem":{"workItemId":"W2","title":"Valid","resourceScope":[]}}
            ]}
            """;
        using var input = new StandardInput(body);
        var output = new StringWriter();
        var application = new CliApplication(
            output, TextWriter.Null, Service,
            _ => throw new InvalidOperationException("Read-only preflight must not resolve an adapter."),
            new ContextAssembler());
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);
        output.GetStringBuilder().Clear();

        var exit = await application.RunAsync(
            ["batch", "preflight", .. common, "--body-stdin"], CancellationToken.None);

        Assert.Equal(0, exit);
        using var json = JsonDocument.Parse(output.ToString());
        var result = json.RootElement;
        Assert.False(result.GetProperty("admissible").GetBoolean());
        var members = result.GetProperty("members").EnumerateArray()
            .ToDictionary(item => item.GetProperty("id").GetString()!);
        Assert.False(members["invalid-scope"].GetProperty("admissible").GetBoolean());
        Assert.True(members["valid-work"].GetProperty("admissible").GetBoolean());
        Assert.Single(result.GetProperty("causes").EnumerateArray());
    }

    // R4 (stale-batch-approval-used-as-authority): the result describes only its checked version.
    // A later execution still enters the live command rule and is refused after the item changes.
    [Fact]
    public void R4_ExecutionRechecksAfterTheReportedVersionChanges()
    {
        var task = new TestTask();
        var item = new WorkItemId("W1");
        task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), item, "Work", task.OperatorId, [], []));
        var worker = new ActorId("worker");
        task.Assign(worker, RoleKind.Worker, Capability.BuildContext);
        var served = task.State.ContextBuilds[task.OperatorId].Skills;
        var result = BatchPreflight.Evaluate(task.State, new BatchPreflightRequest(
        [
            new BatchPreflightMember("launch", ProviderLaunch: new ProviderLaunchPreflightRequest(
                task.OperatorId, worker, item, served))
        ]));

        Assert.True(result.Admissible);
        Assert.Equal(task.State.Version, result.CheckedTaskVersion);

        task.Apply(new AbandonWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), item, "Plan changed after preflight"));
        Assert.True(task.State.Version > result.CheckedTaskVersion);

        var refusal = Assert.Throws<GovernanceException>(() => task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), item, "codex",
            null, null, null, null, worker)));
        Assert.Contains(nameof(WorkItemStatus.Abandoned), refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(new RunId("R1"), task.State.Runs.Keys);
    }
}
