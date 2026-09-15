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
public sealed class ProviderRunRecordingCliTests
{
    [Fact]
    public async Task CancelledProviderLaunchPersistsAndOutputsLearnedSessionBeforeReturningCancellation()
    {
        using var root = new TemporaryDirectory();
        using var providerRoot = new TemporaryDirectory();
        var work = Directory.CreateDirectory(Path.Combine(providerRoot.Path, "provider-work")).FullName;
        using var cancellation = new CancellationTokenSource();
        var output = new StringWriter();
        var error = new StringWriter();
        var application = new CliApplication(
            output,
            error,
            Service,
            _ => new CancellingAdapter(cancellation),
            new ContextAssembler());
        await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator", "--title", "Task", "--goal", "Goal"],
            CancellationToken.None);
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);
        await application.RunAsync(
            ["work", "add", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--id", "W1", "--title", "Work", "--owner", "operator", "--scope", work],
            CancellationToken.None);
        // The run is against a work item, so its subject has to be a role that does work. The
        // operator dispatches it and remains the acting actor; only the subject moves.
        await application.RunAsync(
            ["actor", "attach", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--target", "worker", "--role", "worker"], CancellationToken.None);
        await CliStageFixture.ToExecutionAsync(application, root.Path);

        var exit = await application.RunAsync(
            ["provider", "launch", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--subject", "worker",
             "--run", "R1", "--work", "W1", "--provider", "codex", "--executable", "/usr/bin/true",
             "--working-directory", work, "--cognitive-root", FindCognitiveRoot()], cancellation.Token);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.True(exit == 130, $"Expected cancellation exit 130, got {exit}: {error}");
        Assert.Equal(AgentRunStatus.Cancelled, state?.Runs[new RunId("R1")].Status);
        Assert.Equal("cancelled-session", state?.Runs[new RunId("R1")].ProviderSessionId);
        Assert.Contains("\"providerSessionId\": \"cancelled-session\"", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellationAfterSuccessfulProviderReturnStillPersistsTerminalRun()
    {
        using var root = new TemporaryDirectory();
        using var providerRoot = new TemporaryDirectory();
        var work = Directory.CreateDirectory(Path.Combine(providerRoot.Path, "provider-work")).FullName;
        using var cancellation = new CancellationTokenSource();
        var application = new CliApplication(
            TextWriter.Null,
            TextWriter.Null,
            Service,
            _ => new SuccessfulCancellingAdapter(cancellation),
            new ContextAssembler());
        await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator", "--title", "Task", "--goal", "Goal"],
            CancellationToken.None);
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);
        await application.RunAsync(
            ["work", "add", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--id", "W1", "--title", "Work", "--owner", "operator", "--scope", work],
            CancellationToken.None);
        // As above: a run naming a work item is held by a worker, dispatched by the operator.
        await application.RunAsync(
            ["actor", "attach", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--target", "worker", "--role", "worker"], CancellationToken.None);
        await CliStageFixture.ToExecutionAsync(application, root.Path);

        var exit = await application.RunAsync(
            ["provider", "launch", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--subject", "worker",
             "--run", "R1", "--work", "W1", "--provider", "codex", "--executable", "/usr/bin/true",
             "--working-directory", work, "--cognitive-root", FindCognitiveRoot()], cancellation.Token);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(0, exit);
        Assert.Equal(AgentRunStatus.Completed, state?.Runs[new RunId("R1")].Status);
        // R5 (hidden-workitem-assertion): a provider exiting zero pauses its work item.
        // Completion is asserted through work.complete, never inferred from a process exit.
        Assert.Equal(WorkItemStatus.Paused, state?.WorkItems[new WorkItemId("W1")].Status);
    }

    [Fact]
    public async Task SuccessfulProviderResultRetriesInjectedLockFailureBeforeReturning()
    {
        using var root = new TemporaryDirectory();
        using var providerRoot = new TemporaryDirectory();
        var work = Directory.CreateDirectory(Path.Combine(providerRoot.Path, "provider-work")).FullName;
        var output = new StringWriter();
        var service = new CompletionFailureService(Service(root.Path), failuresBeforeSuccess: 1);
        var application = new CliApplication(
            output,
            TextWriter.Null,
            _ => service,
            _ => new FixedResultAdapter(AgentRunStatus.Completed),
            new ContextAssembler());
        await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator", "--title", "Task", "--goal", "Goal"],
            CancellationToken.None);
        await ContextBrief.BuildAsync(root.Path, "T1");
        output.GetStringBuilder().Clear();

        var exit = await application.RunAsync(
            ["provider", "launch", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--run", "R1", "--provider", "codex", "--executable", "/usr/bin/true",
             "--working-directory", work, "--cognitive-root", FindCognitiveRoot()], CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(0, exit);
        Assert.Equal(2, service.CompletionAttempts);
        Assert.Equal(AgentRunStatus.Completed, state?.Runs[new RunId("R1")].Status);
        Assert.Contains("\"providerSessionId\": \"session-1\"", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TerminalProviderResultIsOutputWhenPersistenceRetriesAreExhausted()
    {
        using var root = new TemporaryDirectory();
        using var providerRoot = new TemporaryDirectory();
        var work = Directory.CreateDirectory(Path.Combine(providerRoot.Path, "provider-work")).FullName;
        var output = new StringWriter();
        var error = new StringWriter();
        var service = new CompletionFailureService(Service(root.Path), failuresBeforeSuccess: int.MaxValue);
        var application = new CliApplication(
            output,
            error,
            _ => service,
            _ => new FixedResultAdapter(AgentRunStatus.Completed),
            new ContextAssembler());
        await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator", "--title", "Task", "--goal", "Goal"],
            CancellationToken.None);
        await ContextBrief.BuildAsync(root.Path, "T1");
        output.GetStringBuilder().Clear();

        var exit = await application.RunAsync(
            ["provider", "launch", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--run", "R1", "--provider", "codex", "--executable", "/usr/bin/true",
             "--working-directory", work, "--cognitive-root", FindCognitiveRoot()], CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(1, exit);
        Assert.Equal(3, service.CompletionAttempts);
        Assert.Equal(AgentRunStatus.Active, state?.Runs[new RunId("R1")].Status);
        Assert.Contains("\"providerSessionId\": \"session-1\"", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("written to standard output for recovery", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NumericUndefinedRoleIsRejectedWithoutPersistence()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var error = new StringWriter();
        var application = Create(output, error);
        await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator", "--title", "Task", "--goal", "Goal"],
            CancellationToken.None);

        var exit = await application.RunAsync(
            ["actor", "attach", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--target", "bad", "--role", "999", "--capability", "add-claim"], CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(2, exit);
        Assert.DoesNotContain(new ActorId("bad"), state!.Roles.Keys);
    }

    [Theory]
    [InlineData(AgentRunStatus.Failed)]
    [InlineData(AgentRunStatus.ProtocolError)]
    [InlineData(AgentRunStatus.Cancelled)]
    public async Task TerminalProviderFailureReturnsNonZeroAfterDurableClose(AgentRunStatus status)
    {
        using var root = new TemporaryDirectory();
        using var providerRoot = new TemporaryDirectory();
        var work = Directory.CreateDirectory(Path.Combine(providerRoot.Path, "provider-work")).FullName;
        var application = new CliApplication(
            TextWriter.Null,
            TextWriter.Null,
            Service,
            _ => new FixedResultAdapter(status),
            new ContextAssembler());
        await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator", "--title", "Task", "--goal", "Goal"],
            CancellationToken.None);
        await ContextBrief.BuildAsync(root.Path, "T1");

        var exit = await application.RunAsync(
            ["provider", "launch", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--run", "R1", "--provider", "codex", "--executable", "/usr/bin/true",
             "--working-directory", work, "--cognitive-root", FindCognitiveRoot()], CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(3, exit);
        Assert.Equal(status, state?.Runs[new RunId("R1")].Status);
    }

}
