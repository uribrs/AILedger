using System.Reflection;
using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Cli;

// The installed tool and the built solution are separate artifacts and the failure between them is
// silent: a feature written, built and tested still records nothing if provider launch went through
// a launcher packed before it. This is the reading half — install.sh already embeds the stamp.
public sealed class KernelVersionTests
{
    [Fact]
    public async Task VersionPrintsWhatTheBuildWasMadeFrom()
    {
        var output = new StringWriter();
        var application = Application(output, TextWriter.Null);

        var exit = await application.RunAsync(["version"], CancellationToken.None);

        var printed = output.ToString().Trim();
        Assert.Equal(0, exit);
        Assert.False(string.IsNullOrWhiteSpace(printed));
        // The assembly's informational version is the only source; nothing here computes a version.
        var informational = typeof(CliApplication).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var version = informational.Split('+')[0];
        Assert.StartsWith(version, printed, StringComparison.Ordinal);
        // The build metadata can hold several dot-separated segments — install.sh's short sha plus
        // the SDK's source revision — and only the first is this build's stamp. Taking all of it
        // printed the commit twice.
        if (printed.Contains("from ", StringComparison.Ordinal))
        {
            var sha = printed[(printed.IndexOf("from ", StringComparison.Ordinal) + 5)..].Trim();
            Assert.DoesNotContain(".", sha, StringComparison.Ordinal);
        }
    }

    // A stale tool still reads a ledger correctly, and a warning on every status is a warning nobody
    // reads. So the reads stay silent whatever the comparison would say.
    [Theory]
    [InlineData("version")]
    [InlineData("status")]
    [InlineData("who")]
    [InlineData("history")]
    public async Task AReadCommandNeverWarnsAboutStaleness(string command)
    {
        using var root = new TemporaryDirectory();
        var error = new StringWriter();
        var application = Application(TextWriter.Null, error);
        if (command != "version")
        {
            await application.RunAsync(
                ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator",
                 "--title", "Task", "--goal", "Goal"], CancellationToken.None);
            error.GetStringBuilder().Clear();
            await application.RunAsync([command, "--root", root.Path, "--task", "T1"], CancellationToken.None);
        }
        else
        {
            await application.RunAsync([command], CancellationToken.None);
        }

        Assert.DoesNotContain("built from", error.ToString(), StringComparison.Ordinal);
    }

    // Nothing to compare means nothing to say. Each of these returns null rather than guessing, and
    // a null from git is deliberately not read as a clean tree.
    [Fact]
    public void NoLedgerHomeMeansNoWarning() =>
        Assert.Null(InvokeStalenessWarning(null));

    [Fact]
    public void ADirectoryThatIsNotAGitTreeMeansNoWarning()
    {
        using var root = new TemporaryDirectory();

        Assert.Null(InvokeStalenessWarning(root.Path));
    }

    // The repository this suite runs in. On a clean tree the embedded sha and HEAD are the same
    // commit and there is no warning; on a dirty tree there is no commit to compare and there is
    // also no warning. Either way the check must not fire against its own source tree, which is the
    // regression that broke nineteen tests when the comparison demanded equal sha lengths.
    [Fact]
    public void TheKernelDoesNotWarnAboutTheTreeItWasBuiltFrom() =>
        Assert.Null(InvokeStalenessWarning(RepositoryRoot()));

    // The comparison itself, not the environment. TheKernelDoesNotWarnAboutTheTreeItWasBuiltFrom
    // passes on a dirty tree for the dirty-tree reason and never reaches this predicate, so it
    // cannot stand as its test — removing the prefix logic left that test green.
    //
    // install.sh embeds a short sha; a plain `dotnet build` embeds the full one the SDK derives from
    // the source revision. Both name the same commit, and demanding equal length made every dev
    // build warn about its own tree.
    [Theory]
    [InlineData("e2dd37d", "e2dd37dcafe0000000000000000000000000000a", true)]
    [InlineData("e2dd37dcafe0000000000000000000000000000a", "e2dd37d", true)]
    [InlineData("e2dd37d", "e2dd37d", true)]
    [InlineData("e2dd37d", "b088254", false)]
    [InlineData("e2dd37dcafe0000000000000000000000000000a", "b0882540000000000000000000000000000000ff", false)]
    // Too short to identify a commit: a coincidence must not read as a match.
    [InlineData("e2dd", "e2dd37dcafe0000000000000000000000000000a", false)]
    public void ShortAndFullShaOfTheSameCommitCompareEqual(string first, string second, bool expected)
    {
        var type = typeof(CliApplication).Assembly.GetType("AILedger.Cli.KernelVersion")!;
        var method = type.GetMethod("SameCommit", BindingFlags.Static | BindingFlags.NonPublic)!;

        Assert.Equal(expected, (bool)method.Invoke(null, [first, second])!);
    }

    // Every test above asserts that no warning appears, and each of them would still pass if the
    // warning could never appear at all. That is the same defect as LC4 one level up: silence proved
    // against this machine's own repository is not evidence that the fire path works. These four
    // drive the decision directly, with both git answers and the build's own stamp supplied.
    [Theory]
    // A build with no sha in its metadata has nothing to compare, whatever the home is at.
    [InlineData("unknown", false, true, "b088254")]
    // A build packed from a dirty tree was not made from any commit.
    [InlineData("e2dd37d", true, true, "b088254")]
    // KC1: a ledger home that is not this kernel's own source tree. Comparing would name a foreign
    // commit and point at a script that is not there.
    [InlineData("e2dd37d", false, false, "b088254")]
    // Git could not answer the HEAD question.
    [InlineData("e2dd37d", false, true, null)]
    // The same commit, named short by install.sh and full by the SDK.
    [InlineData("e2dd37d", false, true, "e2dd37dcafe0000000000000000000000000000a")]
    public void NothingToCompareMeansNoWarning(string buildSha, bool buildIsDirty, bool isKernelTree, string? homeSha) =>
        Assert.Null(InvokeWarning(buildSha, buildIsDirty, isKernelTree, homeSha));

    // KC2: edits on top of the home tree do not make a stale build any less stale.
    [Fact]
    public void ADirtyHomeTreeAtADifferentCommitStillWarns() =>
        Assert.NotNull(InvokeWarning("e2dd37d", false, true, "b0882540000000000000000000000000000000ff"));

    // The one case that fires. Both commits are named because the operator's next move depends on
    // which direction the two artifacts have separated in.
    [Fact]
    public void AMovedLedgerHomeWarnsAndNamesBothCommits()
    {
        var warning = InvokeWarning("e2dd37d", false, true, "b0882540000000000000000000000000000000ff");

        Assert.NotNull(warning);
        Assert.Contains("e2dd37d", warning, StringComparison.Ordinal);
        Assert.Contains("b088254", warning, StringComparison.Ordinal);
        Assert.Contains("scripts/install.sh", warning, StringComparison.Ordinal);
    }

    // The HEAD lookup is a second git invocation, so it is taken lazily: on a dirty tree, or from a
    // build with no sha, it is never run. The comparison is a courtesy and must not become the
    // slowest thing a mutation does.
    [Fact]
    public void TheHeadLookupIsNotRunWhenThereIsNothingToCompare()
    {
        var lookups = 0;

        Assert.Null(InvokeWarning("e2dd37d", buildIsDirty: true, isKernelTree: true, () =>
        {
            lookups++;
            return "b088254";
        }));
        Assert.Equal(0, lookups);
    }

    // A git command that succeeds with nothing to print has answered. This is the distinction the
    // whole feature turned on and the one no other test could reach: `status --porcelain` prints
    // nothing on a clean tree, so folding empty output into "git could not answer" left the warning
    // unreachable on exactly the tree it exists to compare. A live probe read HEAD b088254 and the
    // build's own e2dd37d and still produced no warning, and the suite was green.
    [Fact]
    public void AGitCommandThatPrintsNothingHasStillAnswered()
    {
        var answer = InvokeGit(RepositoryRoot(), "status --porcelain -- no-such-path-in-this-tree");

        Assert.NotNull(answer);
        Assert.Equal(string.Empty, answer);
    }

    // The other half of the same distinction: a command that fails has not answered, and null is
    // reserved for that.
    [Fact]
    public void AGitCommandThatFailsHasNotAnswered() =>
        Assert.Null(InvokeGit(RepositoryRoot(), "rev-parse --verify no-such-ref-in-this-tree"));

    private static string RepositoryRoot()
    {
        var repository = new DirectoryInfo(Environment.CurrentDirectory);
        while (repository is not null
            && !Directory.Exists(Path.Combine(repository.FullName, ".git"))
            && !File.Exists(Path.Combine(repository.FullName, ".git")))
        {
            repository = repository.Parent;
        }

        Assert.NotNull(repository);
        return repository!.FullName;
    }

    private static string? InvokeGit(string workingDirectory, string arguments)
    {
        var type = typeof(CliApplication).Assembly.GetType("AILedger.Cli.KernelVersion")!;
        var method = type.GetMethod("Git", BindingFlags.Static | BindingFlags.NonPublic)!;
        return (string?)method.Invoke(null, [workingDirectory, arguments]);
    }

    private static string? InvokeWarning(string buildSha, bool buildIsDirty, bool isKernelTree, string? homeSha) =>
        InvokeWarning(buildSha, buildIsDirty, isKernelTree, () => homeSha);

    private static string? InvokeWarning(
        string buildSha,
        bool buildIsDirty,
        bool isKernelTree,
        Func<string?> homeSha)
    {
        var type = typeof(CliApplication).Assembly.GetType("AILedger.Cli.KernelVersion")!;
        var method = type.GetMethod("Warning", BindingFlags.Static | BindingFlags.NonPublic)!;
        return (string?)method.Invoke(null, [buildSha, buildIsDirty, isKernelTree, homeSha]);
    }

    // VC1: the end-to-end read test above cannot prove read exemption, because a test process
    // discovers this dirty source checkout as its ledger home and staleness is suppressed before any
    // command is classified. So the classification is asserted directly, and the warning is proved
    // against a clean repository built for the purpose.
    //
    // Listed positively in production so a command added later warns by default. Asserted from both
    // sides here: the reads are exempt, and the paths that brief an agent or write an event are not.
    [Theory]
    [InlineData("version", true)]
    [InlineData("status", true)]
    [InlineData("who", true)]
    [InlineData("history", true)]
    [InlineData("context build", true)]
    [InlineData("artifact list", true)]
    [InlineData("claim add", false)]
    [InlineData("provider launch", false)]
    [InlineData("stage transition", false)]
    [InlineData("work complete", false)]
    public void OnlyReadCommandsAreExemptFromTheStalenessWarning(string command, bool exempt)
    {
        var field = typeof(CliApplication)
            .GetField("ReadOnlyCommands", BindingFlags.Static | BindingFlags.NonPublic)!;
        var reads = (IReadOnlySet<string>)field.GetValue(null)!;

        Assert.Equal(exempt, reads.Contains(command));
    }

    // The case LC6 showed was never covered: a clean tree at a different commit. `git status
    // --porcelain` answers a clean tree with empty output and exit zero, and treating that as no
    // answer meant the warning could never fire in the only situation it exists for.
    [Fact]
    public void ACleanTreeAtADifferentCommitWarns()
    {
        using var home = new TemporaryDirectory();
        // KC4: this used to return without asserting when git setup failed, so the only test that
        // proves the warning can fire was also the only one that could pass while proving nothing.
        // The setup is now part of what is asserted.
        Assert.True(InitRepositoryWithOneCommit(home.Path), "git could not create the fixture repository");
        // KC1: the comparison only runs when the ledger home is this kernel's own source tree, so
        // the fixture has to look like one.
        File.WriteAllText(Path.Combine(home.Path, "AILedger.sln"), string.Empty);

        var warning = InvokeStalenessWarning(home.Path);

        Assert.NotNull(warning);
        Assert.Contains("was built from", warning, StringComparison.Ordinal);
        Assert.Contains("the ledger home is at", warning, StringComparison.Ordinal);
    }

    // KC1: a ledger opened in any other repository must not be compared against that repository's
    // commit, or the operator is warned with a foreign sha and told to run a script that is not there.
    [Fact]
    public void ACleanRepositoryThatIsNotTheKernelsOwnTreeDoesNotWarn()
    {
        using var home = new TemporaryDirectory();
        Assert.True(InitRepositoryWithOneCommit(home.Path), "git could not create the fixture repository");

        Assert.Null(InvokeStalenessWarning(home.Path));
    }

    // KC2: the working tree's cleanliness is a different question. A build carrying a real commit
    // against a home at another commit is stale whether or not there are edits on top, and gating on
    // dirt suppressed exactly the true positives the warning exists for.
    [Fact]
    public void ADirtyTreeAtADifferentCommitStillWarns()
    {
        using var home = new TemporaryDirectory();
        Assert.True(InitRepositoryWithOneCommit(home.Path), "git could not create the fixture repository");
        File.WriteAllText(Path.Combine(home.Path, "AILedger.sln"), string.Empty);
        File.WriteAllText(Path.Combine(home.Path, "uncommitted.txt"), "edits on top");

        Assert.NotNull(InvokeStalenessWarning(home.Path));
    }

    private static bool InitRepositoryWithOneCommit(string path)
    {
        foreach (var arguments in new[]
                 {
                     "init --quiet",
                     "config user.email test@example.com",
                     "config user.name Test",
                     "commit --quiet --allow-empty -m seed"
                 })
        {
            using var process = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("git", arguments)
                {
                    WorkingDirectory = path,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                });
            if (process is null)
            {
                return false;
            }

            process.WaitForExit(10_000);
            if (process.ExitCode != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static string? InvokeStalenessWarning(string? ledgerHome)
    {
        var type = typeof(CliApplication).Assembly.GetType("AILedger.Cli.KernelVersion")!;
        var method = type.GetMethod("StalenessWarning", BindingFlags.Static | BindingFlags.NonPublic)!;
        return (string?)method.Invoke(null, [ledgerHome]);
    }

    private static IGovernedTaskService Service(string root)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(root, new CommandHandler(reducer, new AuthorizationPolicy()), reducer);
    }

    private static CliApplication Application(TextWriter output, TextWriter error) =>
        new(output, error,
            Service,
            _ => throw new NotSupportedException("No provider is launched in these tests."),
            new ContextAssembler());
}
