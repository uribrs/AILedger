using System.Text.Json;
using System.Text.Json.Nodes;
using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;
using static AILedger.Tests.Cli.CliApplicationTestSupport;

namespace AILedger.Tests.Cli;

[Collection(StandardInput.Collection)]
public sealed class CliApplicationBoundaryTests
{
    [Fact]
    public async Task HistoryFollowStreamsOnlyEventsAfterSinceUntilCancelled()
    {
        using var root = new TemporaryDirectory();
        var common = new[] { "--root", root.Path, "--task", "T1", "--actor", "observer" };
        await Create(TextWriter.Null, TextWriter.Null).RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--title", "Task", "--goal", "Goal"], CancellationToken.None);

        var output = new ThreadSafeStringWriter();
        var error = new StringWriter();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var follow = Create(output, error).RunAsync(
            ["history", .. common, "--follow", "--since", "2"], cancellation.Token);

        await Create(TextWriter.Null, TextWriter.Null).RunAsync(
            ["claim", "add", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--id", "C1", "--statement", "Landed while history was following"], CancellationToken.None);
        await WaitUntilAsync(
            () => output.GetText().Contains("claim.added", StringComparison.Ordinal),
            "history --follow did not print the newly appended event");

        cancellation.Cancel();
        var exit = await follow;
        var lines = output.GetText().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var line = Assert.Single(lines);
        using var document = JsonDocument.Parse(line);
        Assert.Equal("claim.added", document.RootElement.GetProperty("data").GetProperty("eventType").GetString());
        Assert.DoesNotContain("task.opened", output.GetText(), StringComparison.Ordinal);
        Assert.DoesNotContain("actor.role-assigned", output.GetText(), StringComparison.Ordinal);
        Assert.Equal(130, exit);
        Assert.Equal("Cancelled." + Environment.NewLine, error.ToString());
    }

    [Fact]
    public async Task OpenStatusAndHistoryUseDurableServiceBoundary()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var error = new StringWriter();
        var application = Create(output, error);
        var common = new[] { "--root", root.Path, "--task", "T1", "--actor", "operator" };

        var openExit = await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal", "--correlation", "open"],
            CancellationToken.None);
        var statusExit = await application.RunAsync(["status", .. common], CancellationToken.None);
        var historyExit = await application.RunAsync(["history", .. common], CancellationToken.None);

        Assert.Equal(0, openExit);
        Assert.Equal(0, statusExit);
        Assert.Equal(0, historyExit);
        Assert.Contains("\"taskId\": \"T1\"", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("task.opened", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("actor.role-assigned", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task AttachUsesSafeRoleDefaultsAndAgentCanAddClaim()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var error = new StringWriter();
        var application = Create(output, error);
        await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator", "--title", "Task", "--goal", "Goal"],
            CancellationToken.None);

        var attachExit = await application.RunAsync(
            ["actor", "attach", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--target", "lead", "--role", "planning-lead"], CancellationToken.None);
        var claimExit = await application.RunAsync(
            ["claim", "add", "--root", root.Path, "--task", "T1", "--actor", "lead",
             "--id", "C1", "--statement", "Shared truth"], CancellationToken.None);

        Assert.Equal(0, attachExit);
        Assert.Equal(0, claimExit);
        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.NotNull(state);
        Assert.Contains(Capability.BuildContext, state.Roles[new ActorId("lead")].Capabilities);
        Assert.Equal("Shared truth", state.Claims[new ClaimId("C1")].Statement);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task UsageFailureReturnsTwoAndWritesConciseError()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = await Create(output, error).RunAsync(["unknown"], CancellationToken.None);

        Assert.Equal(2, exit);
        Assert.StartsWith("error: Unknown command", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("launch")]
    [InlineData("resume")]
    public async Task MisspelledProviderWorkOptionIsRejectedBeforeRunStarts(string mode)
    {
        using var root = new TemporaryDirectory();
        using var providerRoot = new TemporaryDirectory();
        var work = Directory.CreateDirectory(Path.Combine(providerRoot.Path, "provider-work")).FullName;
        var error = new StringWriter();
        var application = new CliApplication(
            TextWriter.Null,
            error,
            Service,
            _ => new FixedResultAdapter(AgentRunStatus.Completed),
            new ContextAssembler());
        await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator", "--title", "Task", "--goal", "Goal"],
            CancellationToken.None);
        await application.RunAsync(
            ["work", "add", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--id", "W1", "--title", "Work", "--owner", "operator", "--scope", work],
            CancellationToken.None);

        var arguments = new List<string>
        {
            "provider", mode, "--root", root.Path, "--task", "T1", "--actor", "operator",
            "--run", "R1", "--wrok", "W1", "--provider", "codex", "--executable", "/usr/bin/true",
            "--working-directory", work, "--cognitive-root", FindCognitiveRoot()
        };
        if (mode == "resume")
        {
            arguments.AddRange(["--session", "exact-session"]);
        }

        var exit = await application.RunAsync(arguments, CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(2, exit);
        Assert.Contains("--wrok", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(new RunId("R1"), state!.Runs.Keys);
    }

    [Fact]
    public async Task UnknownOptionOnTaskOpenIsRejectedBeforeTaskIsCreated()
    {
        using var root = new TemporaryDirectory();
        var error = new StringWriter();

        var exit = await Create(TextWriter.Null, error).RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--title", "Task", "--goal", "Goal", "--gaol", "Typo"],
            CancellationToken.None);

        Assert.Equal(2, exit);
        Assert.Contains("--gaol", error.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(root.Path, "T1")));
    }

    [Fact]
    public async Task RepeatedListOptionsRemainAccepted()
    {
        using var root = new TemporaryDirectory();
        using var providerRoot = new TemporaryDirectory();
        var firstScope = Directory.CreateDirectory(Path.Combine(providerRoot.Path, "first")).FullName;
        var secondScope = Directory.CreateDirectory(Path.Combine(providerRoot.Path, "second")).FullName;
        var application = Create(TextWriter.Null, TextWriter.Null);
        await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator", "--title", "Task", "--goal", "Goal"],
            CancellationToken.None);
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);

        // A work item claiming two areas now has to name the alternative explaining why it was not
        // split in two. That is a separate rule; this test is still about the parser accepting a
        // repeated option, so the alternative is recorded and the two --scope values stay.
        await application.RunAsync(
            ["alternative", "record", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--id", "ALT1", "--statement", "Split the two areas into separate work items",
             "--rejected-because", "The two areas only ever change together"],
            CancellationToken.None);

        var exit = await application.RunAsync(
            ["work", "add", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--id", "W1", "--title", "Work", "--scope", firstScope, "--scope", secondScope,
             "--not-split-because", "ALT1"],
            CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        var scopes = state!.WorkItems[new WorkItemId("W1")].ResourceScope;
        Assert.Equal(0, exit);
        Assert.Equal(2, scopes.Count);
        Assert.Contains(scopes, scope => Path.GetFileName(scope) == Path.GetFileName(firstScope));
        Assert.Contains(scopes, scope => Path.GetFileName(scope) == Path.GetFileName(secondScope));
    }

    [Fact]
    public async Task StatusOfMissingTaskReturnsTwoWithoutCreatingTaskDirectory()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = await Create(output, error).RunAsync(
            ["status", "--root", root.Path, "--task", "missing", "--actor", "operator"],
            CancellationToken.None);

        Assert.Equal(2, exit);
        Assert.Contains("was not found", error.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(root.Path, "missing")));
    }

    [Fact]
    public async Task VerifierContextLoadsGoverningRulesAndTaskOrchestratorProtocol()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var error = new StringWriter();
        var application = Create(output, error);
        await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator", "--title", "Task", "--goal", "Goal"],
            CancellationToken.None);
        await application.RunAsync(
            ["actor", "attach", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--target", "verifier", "--role", "verifier"], CancellationToken.None);
        output.GetStringBuilder().Clear();

        var exit = await application.RunAsync(
            ["context", "build", "--root", root.Path, "--task", "T1", "--actor", "verifier",
             "--cognitive-root", FindCognitiveRoot()], CancellationToken.None);

        using var document = JsonDocument.Parse(output.ToString());
        var artifacts = document.RootElement.GetProperty("artifacts").EnumerateArray().ToArray();
        Assert.Equal(0, exit);
        Assert.Contains(artifacts, artifact =>
            artifact.GetProperty("kind").GetString() == "rules" &&
            artifact.GetProperty("id").GetString() == "governing-rules");
        Assert.Contains(artifacts, artifact =>
            artifact.GetProperty("kind").GetString() == "skill" &&
            artifact.GetProperty("id").GetString() == "task-orchestrator");
        Assert.Equal(string.Empty, error.ToString());
    }

}
