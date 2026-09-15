using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Cli;

public sealed class EntryActionCliTests
{
    [Fact]
    public async Task WorkAddRefusalNamesTheActionCurrentStageAndAdmittingStage()
    {
        using var root = new TemporaryDirectory();
        using var scope = new TemporaryDirectory();
        var error = new StringWriter();
        var application = Create(error);
        await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await ContextBrief.BuildAsync(root.Path, "T1");

        var exit = await application.RunAsync(
            ["work", "add", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--id", "W1", "--title", "Work", "--scope", scope.Path], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains(
            "Action 'work add' is not allowed at stage 'Discovery'; admitting stage is: 'Ready'.",
            error.ToString(), StringComparison.Ordinal);
        Assert.Empty((await Service(root.Path).GetStateAsync(
            new("T1"), CancellationToken.None))!.WorkItems);
    }

    [Fact]
    public async Task WorkerRunRefusalNamesBothExecutionStages()
    {
        using var root = new TemporaryDirectory();
        using var scope = new TemporaryDirectory();
        var error = new StringWriter();
        var application = Create(error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await ContextBrief.BuildAsync(root.Path, "T1");
        await application.RunAsync(
            ["actor", "attach", .. common, "--target", "worker", "--role", "worker"],
            CancellationToken.None);
        await CliStageFixture.ToReadyAsync(application, root.Path);
        await application.RunAsync(
            ["work", "add", .. common, "--id", "W1", "--title", "Work", "--scope", scope.Path],
            CancellationToken.None);
        error.GetStringBuilder().Clear();

        var exit = await application.RunAsync(
            ["run", "start", .. common, "--subject", "worker", "--run", "R1", "--work", "W1",
             "--provider", "codex", "--session", "session"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains(
            "Action 'run start for Worker' is not allowed at stage 'Ready'; admitting stages are: " +
            "'Execution', 'Repair'.",
            error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SerialBecauseOptionReachesTheKernelAsAnAlternativeId()
    {
        using var root = new TemporaryDirectory();
        var error = new StringWriter();
        var application = Create(error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await CliStageFixture.ToReadyAsync(application, root.Path);
        await CliStageFixture.ToExecutionAsync(application, root.Path);

        var exit = await application.RunAsync(
            ["stage", "transition", .. common, "--stage", "verification", "--serial-because", "MISSING"],
            CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.DoesNotContain("Unknown option", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("Unknown alternative 'MISSING'", error.ToString(), StringComparison.Ordinal);
    }

    private static CliApplication Create(TextWriter error) => new(
        TextWriter.Null,
        error,
        Service,
        _ => throw new InvalidOperationException("Provider adapter is not used by this test."),
        new ContextAssembler());

    private static IGovernedTaskService Service(string root)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(
            root, new CommandHandler(reducer, new AuthorizationPolicy()), reducer);
    }
}
