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
public sealed class ProviderScopeCliTests
{
    [Fact]
    public async Task WorkCreationFreezesRelativeScopeAsCanonicalAbsolutePath()
    {
        using var root = new TemporaryDirectory();
        var application = Create(TextWriter.Null, TextWriter.Null);
        await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator", "--title", "Task", "--goal", "Goal"],
            CancellationToken.None);
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);

        var exit = await application.RunAsync(
            ["work", "add", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--id", "W1", "--title", "Work", "--owner", "operator", "--scope", "."],
            CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        var scope = Assert.Single(state!.WorkItems[new WorkItemId("W1")].ResourceScope);
        Assert.Equal(0, exit);
        Assert.True(Path.IsPathFullyQualified(scope));
        Assert.True(Directory.Exists(scope));
    }

    // Self-hosting: a governed agent has to record claims and escalations while it works, and a
    // workspace sandbox only lets it write inside its own workspace. A scope that contains the
    // Ledger root is therefore allowed; a tampered log is caught by replay, not by this check.
    [Fact]
    public async Task WorkScopeMayContainTheLedgerRootSoAGovernedAgentCanRecordTruth()
    {
        using var root = new TemporaryDirectory();
        var ledgerRoot = Directory.CreateDirectory(Path.Combine(root.Path, ".ailedger", "tasks")).FullName;
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        await application.RunAsync(
            ["task", "open", "--root", ledgerRoot, "--task", "T1", "--actor", "operator", "--title", "Task", "--goal", "Goal"],
            CancellationToken.None);
        await ContextBrief.BuildAsync(ledgerRoot, "T1");
        await CliStageFixture.ToReadyAsync(application, ledgerRoot);

        var exit = await application.RunAsync(
            ["work", "add", "--root", ledgerRoot, "--task", "T1", "--actor", "operator",
             "--id", "W1", "--title", "Work", "--owner", "operator", "--scope", root.Path],
            CancellationToken.None);

        var state = await Service(ledgerRoot).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, error.ToString());
        // The stored scope is canonicalised, which on macOS resolves /var to /private/var, so
        // identity is asserted by what the directory contains rather than by string equality.
        var scope = Assert.Single(state!.WorkItems[new WorkItemId("W1")].ResourceScope);
        await File.WriteAllTextAsync(Path.Combine(root.Path, "sentinel"), "same-directory");
        Assert.True(File.Exists(Path.Combine(scope, "sentinel")));
        Assert.True(Directory.Exists(Path.Combine(scope, ".ailedger", "tasks")));
    }

    [Theory]
    [InlineData("T1")]
    [InlineData("T2")]
    [InlineData("scratch/child")]
    public async Task WorkScopeCannotBeInsideAuthoritativeLedgerRoot(string relativeScope)
    {
        using var root = new TemporaryDirectory();
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator", "--title", "Task", "--goal", "Goal"],
            CancellationToken.None);
        if (relativeScope == "T2")
        {
            await application.RunAsync(
                ["task", "open", "--root", root.Path, "--task", "T2", "--actor", "operator", "--title", "Sibling", "--goal", "Goal"],
                CancellationToken.None);
        }

        var scope = Directory.CreateDirectory(Path.Combine(root.Path, relativeScope)).FullName;
        var exit = await application.RunAsync(
            ["work", "add", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--id", "W1", "--title", "Work", "--owner", "operator", "--scope", scope],
            CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(1, exit);
        Assert.Contains("inside the authoritative Ledger root", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(state!.WorkItems);
    }

    [Theory]
    [InlineData("")]
    [InlineData("T1")]
    [InlineData("T2")]
    [InlineData("scratch/child")]
    public async Task WorkScopeSymlinkCannotResolveInsideAuthoritativeLedgerRoot(string relativeTarget)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var root = new TemporaryDirectory();
        using var providerRoot = new TemporaryDirectory();
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator", "--title", "Task", "--goal", "Goal"],
            CancellationToken.None);
        if (relativeTarget == "T2")
        {
            await application.RunAsync(
                ["task", "open", "--root", root.Path, "--task", "T2", "--actor", "operator", "--title", "Sibling", "--goal", "Goal"],
                CancellationToken.None);
        }

        var target = string.IsNullOrEmpty(relativeTarget)
            ? root.Path
            : Directory.CreateDirectory(Path.Combine(root.Path, relativeTarget)).FullName;
        var link = Path.Combine(providerRoot.Path, "ledger-link");
        Directory.CreateSymbolicLink(link, target);

        var exit = await application.RunAsync(
            ["work", "add", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--id", "W1", "--title", "Work", "--owner", "operator", "--scope", link],
            CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(1, exit);
        Assert.Contains("authoritative Ledger root", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(state!.WorkItems);
    }

    [Fact]
    public async Task ScopedProviderLaunchRejectsPersistedLedgerDirectoryScope()
    {
        using var root = new TemporaryDirectory();
        var application = new CliApplication(
            TextWriter.Null,
            TextWriter.Null,
            Service,
            _ => new FixedResultAdapter(AgentRunStatus.Completed),
            new ContextAssembler());
        await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator", "--title", "Task", "--goal", "Goal"],
            CancellationToken.None);
        await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T2", "--actor", "operator", "--title", "Sibling", "--goal", "Goal"],
            CancellationToken.None);
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);
        await Service(root.Path).ExecuteAsync(new TaskId("T1"), new AddWorkItemCommand(
            new ActorId("operator"), null, "seed", new WorkItemId("W1"), "Legacy work",
            new ActorId("operator"), [], [Path.Combine(root.Path, "T2")],
            SkillsServedNow: await ContextBrief.RecordedAsync(Service(root.Path), "T1")),
            CancellationToken.None);

        var exit = await application.RunAsync(
            ["provider", "launch", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--run", "R1", "--work", "W1", "--provider", "codex", "--executable", "/usr/bin/true",
             "--cognitive-root", FindCognitiveRoot()], CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(1, exit);
        Assert.DoesNotContain(new RunId("R1"), state!.Runs.Keys);
    }

    [Fact]
    public async Task UnscopedProviderLaunchRejectsWorkingDirectoryInsideLedgerRoot()
    {
        using var root = new TemporaryDirectory();
        var application = new CliApplication(
            TextWriter.Null,
            TextWriter.Null,
            Service,
            _ => new FixedResultAdapter(AgentRunStatus.Completed),
            new ContextAssembler());
        await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator", "--title", "Task", "--goal", "Goal"],
            CancellationToken.None);

        var exit = await application.RunAsync(
            ["provider", "launch", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--run", "R1", "--provider", "codex", "--executable", "/usr/bin/true",
             "--working-directory", Path.Combine(root.Path, "T1"), "--cognitive-root", FindCognitiveRoot()],
            CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(1, exit);
        Assert.DoesNotContain(new RunId("R1"), state!.Runs.Keys);
    }

}
