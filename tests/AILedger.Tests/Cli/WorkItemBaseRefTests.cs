using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;
using System.Diagnostics;
using System.Text.Json;

namespace AILedger.Tests.Cli;

// `baseRef` is a skill contract before it is a kernel field: `prompt-contract-designer` captures
// `git rev-parse HEAD` at contract time and the verifier reads it to scope its diff. The kernel had
// nowhere to put it, so two verifier runs in a row on 2026-09-08_1428-run-cost fell back to comparing
// the change against the goal and the constraints and said so in their own reports.
//
// What these pin is the capture, not the rendering: the ref must come from the item's own scope
// directory and never from this process's, because a scope points into whichever repository holds the
// work and that is rarely this one.
public sealed class WorkItemBaseRefTests
{
    [Fact]
    public async Task AWorkItemCapturesTheHeadOfItsOwnScopeRatherThanTheProcessDirectory()
    {
        using var root = new TemporaryDirectory();
        using var repository = new TemporaryDirectory();
        var head = InitRepositoryWithOneCommit(repository.Path);
        Assert.NotNull(head);

        var state = await AddWorkItemAsync(root.Path, repository.Path);

        Assert.Equal(head, state.WorkItems[new WorkItemId("W1")].BaseRef);
    }

    // The absence is the measurement, exactly as it is for the manifest pair and the cost fields: a
    // scope outside a git work tree records nothing rather than borrowing a ref from somewhere else.
    [Fact]
    public async Task AScopeThatIsNotAGitWorkTreeRecordsNoBaseRef()
    {
        using var root = new TemporaryDirectory();
        using var plain = new TemporaryDirectory();

        var state = await AddWorkItemAsync(root.Path, plain.Path);

        Assert.Null(state.WorkItems[new WorkItemId("W1")].BaseRef);
    }

    // An explicit value wins over the probe, so a caller that knows the ref does not depend on git
    // being reachable from the scope at all.
    [Fact]
    public async Task AnExplicitBaseRefIsRecordedInsteadOfTheProbe()
    {
        using var root = new TemporaryDirectory();
        using var repository = new TemporaryDirectory();
        Assert.NotNull(InitRepositoryWithOneCommit(repository.Path));

        var state = await AddWorkItemAsync(root.Path, repository.Path, "deadbeef");

        Assert.Equal("deadbeef", state.WorkItems[new WorkItemId("W1")].BaseRef);
    }

    // Null is omitted from the projection, which is what keeps every task recorded before this field
    // existed byte-identical under the replay comparison.
    [Fact]
    public async Task StatusOmitsTheBaseRefWhenThereIsNone()
    {
        using var root = new TemporaryDirectory();
        using var plain = new TemporaryDirectory();
        var output = new StringWriter();
        var application = Create(output, new StringWriter());
        await AddWorkItemAsync(root.Path, plain.Path, application: application, output: output);

        var exit = await application.RunAsync(
            ["status", "--root", root.Path, "--task", "T1", "--actor", "operator"], CancellationToken.None);

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(output.ToString());
        var item = document.RootElement.GetProperty("workItems").GetProperty("W1");
        Assert.False(item.TryGetProperty("baseRef", out _));
    }

    private static async Task<GovernedTaskState> AddWorkItemAsync(
        string root,
        string scope,
        string? explicitRef = null,
        CliApplication? application = null,
        StringWriter? output = null)
    {
        output ??= new StringWriter();
        application ??= Create(output, new StringWriter());
        string[] common = ["--root", root, "--task", "T1", "--actor", "operator"];
        Assert.Equal(0, await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None));
        string[] add =
        [
            "work", "add", .. common, "--id", "W1", "--title", "Item", "--scope", scope,
            .. explicitRef is null ? Array.Empty<string>() : ["--base-ref", explicitRef]
        ];
        Assert.Equal(0, await application.RunAsync(add, CancellationToken.None));
        output.GetStringBuilder().Clear();
        var state = await Service(root).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        return state!;
    }

    // The fixture needs a real commit, because `rev-parse HEAD` answers nothing in a repository with
    // none. Asserted by every caller rather than skipped: C31 records that the two tests which fail
    // when a sandbox refuses `git commit` were read as a stable product baseline by three runs.
    private static string? InitRepositoryWithOneCommit(string path)
    {
        foreach (var arguments in new[]
                 {
                     "init --quiet",
                     "config user.email test@example.com",
                     "config user.name Test",
                     "commit --quiet --allow-empty -m seed"
                 })
        {
            if (Run(path, arguments) is null)
            {
                return null;
            }
        }

        return Run(path, "rev-parse HEAD");
    }

    private static string? Run(string workingDirectory, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException)
        {
            return null;
        }
    }

    private static CliApplication Create(TextWriter output, TextWriter error) => new(
        output,
        error,
        Service,
        _ => throw new InvalidOperationException("These tests launch no provider."),
        new ContextAssembler());

    private static IGovernedTaskService Service(string root)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(root, new CommandHandler(reducer, new AuthorizationPolicy()), reducer);
    }
}
