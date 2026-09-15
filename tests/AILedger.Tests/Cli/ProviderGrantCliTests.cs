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
public sealed class ProviderGrantCliTests
{
    [Fact]
    public async Task RunManagerCannotGrantProviderDirectoryOutsideGovernedScope()
    {
        using var root = new TemporaryDirectory();
        using var providerRoot = new TemporaryDirectory();
        var allowed = Directory.CreateDirectory(Path.Combine(providerRoot.Path, "work")).FullName;
        var sibling = Directory.CreateDirectory(Path.Combine(providerRoot.Path, "work-other")).FullName;
        var application = Create(TextWriter.Null, TextWriter.Null);
        await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator", "--title", "Task", "--goal", "Goal"],
            CancellationToken.None);
        await application.RunAsync(
            ["actor", "attach", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--target", "worker", "--role", "worker"], CancellationToken.None);
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);
        await application.RunAsync(
            ["work", "add", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--id", "W1", "--title", "Work", "--owner", "operator", "--scope", allowed], CancellationToken.None);
        await CliStageFixture.ToExecutionAsync(application, root.Path);

        var exit = await application.RunAsync(
            ["provider", "launch", "--root", root.Path, "--task", "T1", "--actor", "operator", "--subject", "worker",
             "--run", "R1", "--work", "W1", "--provider", "codex", "--executable", "/usr/bin/true",
             "--working-directory", sibling, "--cognitive-root", FindCognitiveRoot()], CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(1, exit);
        Assert.DoesNotContain(new RunId("R1"), state!.Runs.Keys);
    }

    [Fact]
    public async Task SymlinkCannotEscapeProviderWorkScope()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var root = new TemporaryDirectory();
        using var providerRoot = new TemporaryDirectory();
        var allowed = Directory.CreateDirectory(Path.Combine(providerRoot.Path, "allowed")).FullName;
        var outside = Directory.CreateDirectory(Path.Combine(providerRoot.Path, "outside")).FullName;
        var link = Path.Combine(allowed, "escape-link");
        Directory.CreateSymbolicLink(link, outside);
        var application = Create(TextWriter.Null, TextWriter.Null);
        await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator", "--title", "Task", "--goal", "Goal"],
            CancellationToken.None);
        await application.RunAsync(
            ["actor", "attach", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--target", "worker", "--role", "worker"], CancellationToken.None);
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);
        await application.RunAsync(
            ["work", "add", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--id", "W1", "--title", "Work", "--owner", "operator", "--scope", allowed], CancellationToken.None);
        await CliStageFixture.ToExecutionAsync(application, root.Path);

        var exit = await application.RunAsync(
            ["provider", "launch", "--root", root.Path, "--task", "T1", "--actor", "operator", "--subject", "worker",
             "--run", "R1", "--work", "W1", "--provider", "codex", "--executable", "/usr/bin/true",
             "--working-directory", link, "--cognitive-root", FindCognitiveRoot()], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.DoesNotContain(new RunId("R1"),
            (await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None))!.Runs.Keys);
    }

    // The positive counterpart to the two refusals above, and the distinction between them is the
    // whole rule: scope is the file list a worker owns, not the directory it runs in. A working
    // directory that CONTAINS the scope is accepted, because a worker owning one project inside a
    // solution still has to run where the solution builds. The two tests above stay refused because
    // their directory is neither ancestor nor descendant of the scope — a sibling is not the
    // solution root, it is somebody else's work.
    //
    // The subject is a worker, not the operator. A coordinating role cannot hold a run that names a
    // work item, and this launch gets past the grant check that the two refusals above stop at, so
    // it would otherwise be refused a step later for a reason that has nothing to do with scope.
    [Fact]
    public async Task AProviderWorkingDirectoryMayBeAnAncestorOfTheWorkItemScope()
    {
        using var root = new TemporaryDirectory();
        using var providerRoot = new TemporaryDirectory();
        var solution = Directory.CreateDirectory(Path.Combine(providerRoot.Path, "solution")).FullName;
        // The ancestor is accepted because it is the repository the scope lives in. Marking the
        // repository is what makes 'solution' the solution root rather than an arbitrary parent, and
        // it is the same marker CodexAgentAdapter already requires before it will launch at all.
        Directory.CreateDirectory(Path.Combine(solution, ".git"));
        var project = Directory.CreateDirectory(Path.Combine(solution, "src", "AILedger.Core")).FullName;
        var siblingProject = Directory.CreateDirectory(Path.Combine(solution, "tests", "AILedger.Tests")).FullName;
        var capture = new CapturingAdapter();
        var application = new CliApplication(
            TextWriter.Null, TextWriter.Null, Service, _ => capture, new ContextAssembler());
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);
        await application.RunAsync(
            ["actor", "attach", .. common, "--target", "worker", "--role", "worker"],
            CancellationToken.None);
        await application.RunAsync(
            ["work", "add", .. common, "--id", "W1", "--title", "One project", "--owner", "operator",
             "--scope", project], CancellationToken.None);
        await application.RunAsync(
            ["work", "add", .. common, "--id", "W2", "--title", "Sibling project", "--owner", "operator",
             "--scope", siblingProject], CancellationToken.None);
        await CliStageFixture.ToExecutionAsync(application, root.Path);

        var exit = await application.RunAsync(
            ["provider", "launch", .. common, "--subject", "worker",
             "--run", "R1", "--work", "W1", "--provider", "codex", "--executable", "/usr/bin/true",
             "--working-directory", solution, "--cognitive-root", FindCognitiveRoot()],
            CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        var request = Assert.Single(capture.Requests);
        var scope = Assert.Single(state!.WorkItems[new WorkItemId("W1")].ResourceScope);
        Assert.Equal(0, exit);
        Assert.Contains(new RunId("R1"), state.Runs.Keys);
        Assert.Equal(2, state.WorkItems.Count);
        Assert.NotEqual(
            Assert.Single(state.WorkItems[new WorkItemId("W1")].ResourceScope),
            Assert.Single(state.WorkItems[new WorkItemId("W2")].ResourceScope));
        // Both the stored scope and the granted directory are canonical, and on macOS the temporary
        // root canonicalises (/var is a link to /private/var), so the expected ancestor is derived
        // from the stored scope rather than from the path the test composed.
        Assert.EndsWith(Path.Combine("solution", "src", "AILedger.Core"), scope, StringComparison.Ordinal);
        // The granted directory is the ancestor the launch asked for, two levels above the scope,
        // not the scope narrowed back down to the project. The recorded scope stays the project,
        // which is what occupancy reads.
        Assert.Equal(Path.GetDirectoryName(Path.GetDirectoryName(scope)), request.WorkingDirectory);
    }

    // The ceiling on the test above. An ancestor is accepted up to the repository holding the scope
    // and no further, because the reason ancestors are accepted at all — a worker runs where the
    // solution builds — stops there. Without this the clause had no upper bound: the grant is the
    // agent's write boundary, so every directory between the repository and the filesystem root was
    // writable by any run that asked for it, and the refusal it never reached still said "outside
    // scope", which is not what is wrong with an ancestor that is merely too high.
    //
    // The subject is a worker for the same reason as the test above: a coordinating role is refused
    // a run naming a work item a step later, and that would pass this test for the wrong reason.
    [Fact]
    public async Task R5_ProviderGrantStopsAtRepositoryRoot()
    {
        using var root = new TemporaryDirectory();
        using var providerRoot = new TemporaryDirectory();
        var repository = Directory.CreateDirectory(Path.Combine(providerRoot.Path, "solution")).FullName;
        Directory.CreateDirectory(Path.Combine(repository, ".git"));
        var project = Directory.CreateDirectory(Path.Combine(repository, "src", "AILedger.Core")).FullName;
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);
        await application.RunAsync(
            ["actor", "attach", .. common, "--target", "worker", "--role", "worker"], CancellationToken.None);
        await application.RunAsync(
            ["work", "add", .. common, "--id", "W1", "--title", "One project", "--owner", "operator",
             "--scope", project], CancellationToken.None);
        await CliStageFixture.ToExecutionAsync(application, root.Path);

        var exit = await application.RunAsync(
            ["provider", "launch", .. common, "--subject", "worker",
             "--run", "R1", "--work", "W1", "--provider", "codex", "--executable", "/usr/bin/true",
             "--working-directory", providerRoot.Path, "--cognitive-root", FindCognitiveRoot()],
            CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        var scope = Assert.Single(state!.WorkItems[new WorkItemId("W1")].ResourceScope);
        Assert.Equal(1, exit);
        Assert.DoesNotContain(new RunId("R1"), state.Runs.Keys);
        // The refusal names the highest directory that would have been accepted, so the caller does
        // not have to guess it. The expected path is derived from the stored scope because both are
        // canonical and on macOS the temporary root resolves /var to /private/var.
        var ceiling = Path.GetDirectoryName(Path.GetDirectoryName(scope));
        Assert.Contains("is above the highest directory work item 'W1' may be granted", error.ToString());
        Assert.Contains($"'{ceiling}'", error.ToString());
    }

    // '--add-dir' is resolved by the same loop as '--working-directory' and nothing pinned it, so an
    // unbounded ancestor could be taken through the second door while the first was closed.
    [Fact]
    public async Task AnAdditionalProviderDirectoryAboveTheRepositoryOfTheScopeIsRefused()
    {
        using var root = new TemporaryDirectory();
        using var providerRoot = new TemporaryDirectory();
        var repository = Directory.CreateDirectory(Path.Combine(providerRoot.Path, "solution")).FullName;
        Directory.CreateDirectory(Path.Combine(repository, ".git"));
        var project = Directory.CreateDirectory(Path.Combine(repository, "src", "AILedger.Core")).FullName;
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);
        await application.RunAsync(
            ["actor", "attach", .. common, "--target", "worker", "--role", "worker"], CancellationToken.None);
        await application.RunAsync(
            ["work", "add", .. common, "--id", "W1", "--title", "One project", "--owner", "operator",
             "--scope", project], CancellationToken.None);
        await CliStageFixture.ToExecutionAsync(application, root.Path);

        // The working directory is the repository root, which is accepted. Only the extra directory
        // is above the ceiling, so nothing but '--add-dir' can be what refuses this launch.
        var exit = await application.RunAsync(
            ["provider", "launch", .. common, "--subject", "worker",
             "--run", "R1", "--work", "W1", "--provider", "codex", "--executable", "/usr/bin/true",
             "--working-directory", repository, "--add-dir", providerRoot.Path,
             "--cognitive-root", FindCognitiveRoot()],
            CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        var scope = Assert.Single(state!.WorkItems[new WorkItemId("W1")].ResourceScope);
        var ceiling = Path.GetDirectoryName(Path.GetDirectoryName(scope));
        Assert.Equal(1, exit);
        Assert.DoesNotContain(new RunId("R1"), state.Runs.Keys);
        Assert.Contains("is above the highest directory work item 'W1' may be granted", error.ToString());
        Assert.Contains($"'{ceiling}'", error.ToString());
    }

}
