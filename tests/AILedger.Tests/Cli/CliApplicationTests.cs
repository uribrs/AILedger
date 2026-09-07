using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;
using System.Text;
using System.Text.Json;

namespace AILedger.Tests.Cli;

// This class records artifacts through the CLI, which reads the body from Console.In, so it shares
// the standard-input collection with every other class that does.
[Collection(StandardInput.Collection)]
public sealed class CliApplicationTests
{
    [Fact]
    public async Task LessonMarkArchiveAndRecallFlowWorksThroughCli()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var error = new StringWriter();
        var application = Create(output, error);
        var source = new[]
        {
            "--root", root.Path, "--task", "2026-09-01_1200-source", "--actor", "operator"
        };
        var exits = new List<int>
        {
            await application.RunAsync(
                ["task", "open", .. source, "--title", "Source", "--goal", "Learn"], CancellationToken.None),
            await application.RunAsync(
                ["claim", "add", .. source, "--id", "C1", "--statement", "Retries stop after three attempts"],
                CancellationToken.None),
            await application.RunAsync(
                ["evidence", "add", .. source, "--id", "E1", "--source-type", "test-run",
                 "--citation", "RetryTests.Bounded", "--summary", "The retry policy stopped after three attempts",
                 "--supports", "C1"], CancellationToken.None),
            await application.RunAsync(
                ["claim", "resolve", .. source, "--id", "C1", "--status", "validated", "--evidence", "E1"],
                CancellationToken.None),
            await application.RunAsync(
                ["lesson", "mark", .. source, "--kind", "validated-claim", "--source", "C1",
                 "--class", "untested", "--repo", "AILedger", "--tag", "retry", "--tag", "bounded",
                 "--verify", "dotnet test --filter RetryTests.Bounded",
                 "--do-not", "Do not assume retries are bounded without rerunning the test",
                 "--lesson-actor", "verifier"],
                CancellationToken.None),
            await application.RunAsync(
                ["claim", "add", .. source, "--id", "C2", "--statement", "The research topic remains open"],
                CancellationToken.None),
            await application.RunAsync(
                ["alternative", "record", .. source, "--id", "ALT1", "--statement", "Skip bounded retries",
                 "--rejected-because", "The validated claim requires a bounded policy"], CancellationToken.None),
            await application.RunAsync(
                ["actor", "attach", .. source, "--target", "researcher", "--role", "researcher"],
                CancellationToken.None),
            await application.RunAsync(
                ["actor", "attach", .. source, "--target", "verifier", "--role", "verifier"],
                CancellationToken.None),
            await application.RunAsync(
                ["actor", "attach", .. source, "--target", "reviewer", "--role", "code-reviewer"],
                CancellationToken.None),
            await application.RunAsync(
                ["work", "add", .. source, "--id", "W1", "--title", "Source work", "--owner", "operator"],
                CancellationToken.None)
        };
        // The walk to Archive passes through Execution, which cannot be entered without the three
        // task-wide workflow artifacts. They are recorded here the way an operator records them.
        exits.AddRange(await RecordExecutionArtifactsAsync(application, source, "W1"));

        exits.Add(await application.RunAsync(
            ["stage", "transition", .. source, "--stage", "research"], CancellationToken.None));
        exits.Add(await application.RunAsync(
            ["run", "start", .. source, "--subject", "researcher", "--run", "R-research",
             "--provider", "codex", "--session", "research-session"], CancellationToken.None));
        exits.Add(await application.RunAsync(
            ["run", "complete", .. source, "--run", "R-research", "--status", "completed",
             "--session", "research-session"], CancellationToken.None));

        foreach (var stage in new[] { "design", "scope", "ready", "execution" })
        {
            exits.Add(await application.RunAsync(
                ["stage", "transition", .. source, "--stage", stage], CancellationToken.None));
        }

        // Verification asks for the pass that did the work, and the two roles that means are worker
        // and implementation lead. The researcher pass above is not one of them.
        exits.Add(await application.RunAsync(
            ["actor", "attach", .. source, "--target", "worker", "--role", "worker"],
            CancellationToken.None));
        exits.Add(await application.RunAsync(
            ["run", "start", .. source, "--subject", "worker", "--run", "R-work", "--work", "W1",
             "--provider", "codex", "--session", "work-session"], CancellationToken.None));
        exits.Add(await application.RunAsync(
            ["run", "complete", .. source, "--run", "R-work", "--status", "completed",
             "--session", "work-session"], CancellationToken.None));
        exits.Add(await application.RunAsync(
            ["stage", "transition", .. source, "--stage", "verification"], CancellationToken.None));

        exits.Add(await application.RunAsync(
            ["run", "start", .. source, "--subject", "verifier", "--run", "R-verify", "--work", "W1",
             "--provider", "codex", "--session", "verify-session"], CancellationToken.None));
        exits.Add(await RecordArtifactAsync(
            application, source, "verifier", "A-verifier", "verifier-output", "W1", "R-verify",
            ArtifactCommands.VerifierBody));
        exits.Add(await application.RunAsync(
            ["run", "complete", .. source, "--run", "R-verify", "--status", "completed",
             "--session", "verify-session"], CancellationToken.None));
        exits.Add(await application.RunAsync(
            ["stage", "transition", .. source, "--stage", "review"], CancellationToken.None));
        exits.Add(await application.RunAsync(
            ["run", "start", .. source, "--subject", "reviewer", "--run", "R-review", "--work", "W1",
             "--provider", "codex", "--session", "review-session"], CancellationToken.None));
        exits.Add(await RecordArtifactAsync(
            application, source, "reviewer", "A-review", "code-review-output", "W1", "R-review",
            "Review findings"));
        exits.Add(await application.RunAsync(
            ["run", "complete", .. source, "--run", "R-review", "--status", "completed",
             "--session", "review-session"], CancellationToken.None));
        exits.Add(await application.RunAsync(
            ["stage", "transition", .. source, "--stage", "learn"], CancellationToken.None));
        exits.Add(await application.RunAsync(
            ["stage", "transition", .. source, "--stage", "archive"], CancellationToken.None));

        exits.Add(await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "2026-09-02_1200-target", "--actor", "operator",
             "--title", "Target", "--goal", "Recall"], CancellationToken.None));

        Assert.All(exits, exit => Assert.Equal(0, exit));
        Assert.Equal(string.Empty, error.ToString());
        var service = Service(root.Path);
        var sourceHistory = new List<LedgerEvent>();
        await foreach (var @event in service.GetHistoryAsync(
                           new TaskId("2026-09-01_1200-source"), CancellationToken.None))
        {
            sourceHistory.Add(@event);
        }

        var minted = Assert.IsType<LessonMinted>(
            Assert.Single(sourceHistory, item => item.Data is LessonMinted).Data).Lesson;
        Assert.Equal(LessonSourceKind.ValidatedClaim, minted.SourceKind);
        Assert.Equal("C1", minted.SourceRecordId);
        Assert.Equal(LessonClass.Untested, minted.Class);
        Assert.Equal("AILedger", minted.Repo);
        Assert.Equal(["retry", "bounded"], minted.Tags);
        var targetHistory = new List<LedgerEvent>();
        await foreach (var @event in service.GetHistoryAsync(
                           new TaskId("2026-09-02_1200-target"), CancellationToken.None))
        {
            targetHistory.Add(@event);
        }

        var recalledLesson = Assert.IsType<LessonRecalled>(
            Assert.Single(targetHistory, item => item.Data is LessonRecalled).Data).Lesson;
        Assert.Equal(minted.Id, recalledLesson.Id);
        Assert.Equal(minted.Statement, recalledLesson.Statement);
        Assert.Equal(minted.Citations, recalledLesson.Citations);
    }

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
    public async Task WorkCreationFreezesRelativeScopeAsCanonicalAbsolutePath()
    {
        using var root = new TemporaryDirectory();
        var application = Create(TextWriter.Null, TextWriter.Null);
        await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator", "--title", "Task", "--goal", "Goal"],
            CancellationToken.None);

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
        await Service(root.Path).ExecuteAsync(new TaskId("T1"), new AddWorkItemCommand(
            new ActorId("operator"), null, "seed", new WorkItemId("W1"), "Legacy work",
            new ActorId("operator"), [], [Path.Combine(root.Path, "T2")]), CancellationToken.None);

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
        await application.RunAsync(
            ["work", "add", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--id", "W1", "--title", "Work", "--owner", "operator", "--scope", work],
            CancellationToken.None);

        var exit = await application.RunAsync(
            ["provider", "launch", "--root", root.Path, "--task", "T1", "--actor", "operator",
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
        await application.RunAsync(
            ["work", "add", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--id", "W1", "--title", "Work", "--owner", "operator", "--scope", work],
            CancellationToken.None);

        var exit = await application.RunAsync(
            ["provider", "launch", "--root", root.Path, "--task", "T1", "--actor", "operator",
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

        var exit = await application.RunAsync(
            ["provider", "launch", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--run", "R1", "--provider", "codex", "--executable", "/usr/bin/true",
             "--working-directory", work, "--cognitive-root", FindCognitiveRoot()], CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(3, exit);
        Assert.Equal(status, state?.Runs[new RunId("R1")].Status);
    }

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
             "--target", "lead", "--role", "implementation-lead"], CancellationToken.None);
        await application.RunAsync(
            ["work", "add", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--id", "W1", "--title", "Work", "--owner", "lead", "--scope", allowed], CancellationToken.None);

        var exit = await application.RunAsync(
            ["provider", "launch", "--root", root.Path, "--task", "T1", "--actor", "lead",
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
             "--target", "lead", "--role", "implementation-lead"], CancellationToken.None);
        await application.RunAsync(
            ["work", "add", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--id", "W1", "--title", "Work", "--owner", "lead", "--scope", allowed], CancellationToken.None);

        var exit = await application.RunAsync(
            ["provider", "launch", "--root", root.Path, "--task", "T1", "--actor", "lead",
             "--run", "R1", "--work", "W1", "--provider", "codex", "--executable", "/usr/bin/true",
             "--working-directory", link, "--cognitive-root", FindCognitiveRoot()], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.DoesNotContain(new RunId("R1"),
            (await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None))!.Runs.Keys);
    }

    [Fact]
    public async Task ProviderLaunchCorrelatesAndCausallyLinksItsRunEvents()
    {
        using var root = new TemporaryDirectory();
        using var providerRoot = new TemporaryDirectory();
        var work = Directory.CreateDirectory(Path.Combine(providerRoot.Path, "provider-work")).FullName;
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
             "--working-directory", work, "--cognitive-root", FindCognitiveRoot()], CancellationToken.None);

        var events = new List<LedgerEvent>();
        await foreach (var @event in Service(root.Path).GetHistoryAsync(new TaskId("T1"), CancellationToken.None))
        {
            events.Add(@event);
        }

        var started = events.Single(item => item.Data is RunStarted);
        var completed = events.Single(item => item.Data is RunCompleted);
        Assert.Equal(0, exit);
        // One CLI invocation is one logical operation: shared correlation, and the
        // completion cites the start it closes.
        Assert.Equal(started.CorrelationId, completed.CorrelationId);
        Assert.Equal(started.EventId, completed.CausationId);
        Assert.NotEqual(events[0].CorrelationId, started.CorrelationId);
    }

    // A code reviewer holds no run authority, so before dispatch existed it could not be launched
    // at all. The operator authorises the run; the reviewer receives it, and receives a manifest
    // filtered by its own role rather than the operator's.
    [Fact]
    public async Task OperatorDispatchesACodeReviewerAndTheManifestStaysTheReviewers()
    {
        using var root = new TemporaryDirectory();
        using var providerRoot = new TemporaryDirectory();
        var work = Directory.CreateDirectory(Path.Combine(providerRoot.Path, "provider-work")).FullName;
        var capture = new CapturingAdapter();
        var application = new CliApplication(
            TextWriter.Null, TextWriter.Null, Service, _ => capture, new ContextAssembler());
        string[] common = ["--root", root.Path, "--task", "T1"];
        await application.RunAsync(
            ["task", "open", .. common, "--actor", "operator", "--title", "Task", "--goal", "Goal"],
            CancellationToken.None);
        await application.RunAsync(
            ["actor", "attach", .. common, "--actor", "operator", "--target", "reviewer",
             "--role", "code-reviewer"], CancellationToken.None);
        await application.RunAsync(
            ["work", "add", .. common, "--actor", "operator", "--id", "W1", "--title", "Review it",
             "--owner", "operator", "--scope", work], CancellationToken.None);
        // An open escalation carries the team's recommendation, so a blind review must not see it.
        await application.RunAsync(
            ["escalation", "raise", .. common, "--actor", "operator", "--id", "X1",
             "--kind", "business-decision", "--question", "Ship now or harden first?",
             "--option", "ship", "--option", "harden", "--recommend", "ship"], CancellationToken.None);
        // A reviewer cannot start on a work item a verifier has not finished with, so the pass is
        // recorded first. What this test is about begins at the launch below.
        await application.RunAsync(
            ["actor", "attach", .. common, "--actor", "operator", "--target", "verifier",
             "--role", "verifier"], CancellationToken.None);
        await RecordExecutionArtifactsAsync(application, common, "W1");
        await application.RunAsync(
            ["run", "start", .. common, "--actor", "operator", "--subject", "verifier", "--run", "RV",
             "--work", "W1", "--provider", "codex", "--session", "verifier-session"], CancellationToken.None);
        await RecordArtifactAsync(
            application, common, "verifier", "A-RV", "verifier-output", "W1", "RV",
            ArtifactCommands.VerifierBody);
        await application.RunAsync(
            ["run", "complete", .. common, "--actor", "operator", "--run", "RV", "--status", "completed",
             "--session", "verifier-session"], CancellationToken.None);

        // This agent files no review output. The launcher therefore records the run as failed
        // instead of leaving the work item occupied by a run that can no longer be resumed.
        var exit = await application.RunAsync(
            ["provider", "launch", .. common, "--actor", "operator", "--subject", "reviewer",
             "--run", "R1", "--work", "W1", "--provider", "codex", "--executable", "/usr/bin/true",
             "--cognitive-root", FindCognitiveRoot()], CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        var run = state!.Runs[new RunId("R1")];
        Assert.Equal(3, exit);
        Assert.Equal(AgentRunStatus.Failed, run.Status);
        Assert.Equal(new ActorId("reviewer"), run.ActorId);
        Assert.Equal(new ActorId("operator"), run.LaunchedBy);
        Assert.DoesNotContain(Capability.ManageRuns, state.Roles[new ActorId("reviewer")].Capabilities);

        var request = Assert.Single(capture.Requests);
        Assert.Equal(new ActorId("reviewer"), request.ActorId);
        var manifest = JsonSerializer.Deserialize<ContextManifest>(
            request.StandardInput, ManifestJson)!;
        Assert.Equal(RoleKind.CodeReviewer, manifest.Role);
        ContextArtifactKind[] forbidden =
        [
            ContextArtifactKind.UserRequest,
            ContextArtifactKind.PromptContract,
            ContextArtifactKind.OrchestrationPlan,
            ContextArtifactKind.VerifierOutput,
            ContextArtifactKind.Escalation
        ];
        Assert.DoesNotContain(manifest.Artifacts, artifact => forbidden.Contains(artifact.Kind));
        Assert.Contains(manifest.Artifacts, artifact =>
            artifact.Kind == ContextArtifactKind.Skill && artifact.Id.Contains("code-reviewer", StringComparison.Ordinal));
        Assert.DoesNotContain(manifest.Artifacts, artifact =>
            artifact.Kind == ContextArtifactKind.Skill && artifact.Id.Contains("workflow-coordinator", StringComparison.Ordinal));
    }

    // The control for the test above: the same task launched without a subject does carry the open
    // escalation, so the reviewer's manifest is short because of its role, not because the task is.
    [Fact]
    public async Task AnUndispatchedLaunchStillCarriesTheOpenEscalation()
    {
        using var root = new TemporaryDirectory();
        using var providerRoot = new TemporaryDirectory();
        var work = Directory.CreateDirectory(Path.Combine(providerRoot.Path, "provider-work")).FullName;
        var capture = new CapturingAdapter();
        var application = new CliApplication(
            TextWriter.Null, TextWriter.Null, Service, _ => capture, new ContextAssembler());
        string[] common = ["--root", root.Path, "--task", "T1"];
        await application.RunAsync(
            ["task", "open", .. common, "--actor", "operator", "--title", "Task", "--goal", "Goal"],
            CancellationToken.None);
        await application.RunAsync(
            ["work", "add", .. common, "--actor", "operator", "--id", "W1", "--title", "Review it",
             "--owner", "operator", "--scope", work], CancellationToken.None);
        await application.RunAsync(
            ["escalation", "raise", .. common, "--actor", "operator", "--id", "X1",
             "--kind", "business-decision", "--question", "Ship now or harden first?",
             "--option", "ship", "--option", "harden", "--recommend", "ship"], CancellationToken.None);

        var exit = await application.RunAsync(
            ["provider", "launch", .. common, "--actor", "operator",
             "--run", "R1", "--work", "W1", "--provider", "codex", "--executable", "/usr/bin/true",
             "--cognitive-root", FindCognitiveRoot()], CancellationToken.None);

        var manifest = JsonSerializer.Deserialize<ContextManifest>(
            Assert.Single(capture.Requests).StandardInput, ManifestJson)!;
        Assert.Equal(0, exit);
        Assert.Contains(manifest.Artifacts, artifact => artifact.Kind == ContextArtifactKind.Escalation);
    }

    [Fact]
    public async Task ANonOperatorCannotDispatchAProviderRunForAnotherActor()
    {
        using var root = new TemporaryDirectory();
        using var providerRoot = new TemporaryDirectory();
        var work = Directory.CreateDirectory(Path.Combine(providerRoot.Path, "provider-work")).FullName;
        var capture = new CapturingAdapter();
        var error = new StringWriter();
        var application = new CliApplication(
            TextWriter.Null, error, Service, _ => capture, new ContextAssembler());
        string[] common = ["--root", root.Path, "--task", "T1"];
        await application.RunAsync(
            ["task", "open", .. common, "--actor", "operator", "--title", "Task", "--goal", "Goal"],
            CancellationToken.None);
        await application.RunAsync(
            ["actor", "attach", .. common, "--actor", "operator", "--target", "lead",
             "--role", "implementation-lead"], CancellationToken.None);
        await application.RunAsync(
            ["actor", "attach", .. common, "--actor", "operator", "--target", "reviewer",
             "--role", "code-reviewer"], CancellationToken.None);
        await application.RunAsync(
            ["work", "add", .. common, "--actor", "operator", "--id", "W1", "--title", "Work",
             "--owner", "lead", "--scope", work], CancellationToken.None);

        var exit = await application.RunAsync(
            ["provider", "launch", .. common, "--actor", "lead", "--subject", "reviewer",
             "--run", "R1", "--work", "W1", "--provider", "codex", "--executable", "/usr/bin/true",
             "--cognitive-root", FindCognitiveRoot()], CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(1, exit);
        Assert.DoesNotContain(new RunId("R1"), state!.Runs.Keys);
        Assert.Empty(capture.Requests);
        Assert.Contains("on another actor's behalf", error.ToString(), StringComparison.Ordinal);
    }

    // Dispatch must not loosen the launcher rule. A dispatched subject shares the run's actor
    // identity, so it is stopped twice over: a role with no run authority cannot even reach the
    // command, and a role that has run authority is still refused because it holds no launch token.
    [Theory]
    [InlineData("code-reviewer", "lacks capability", 3, AgentRunStatus.Failed)]
    [InlineData("implementation-lead", "closed by that launcher", 0, AgentRunStatus.Completed)]
    public async Task ADispatchedSubjectStillCannotCompleteItsOwnRun(
        string role,
        string expectedRefusal,
        int expectedExit,
        AgentRunStatus expectedStatus)
    {
        using var root = new TemporaryDirectory();
        using var providerRoot = new TemporaryDirectory();
        var work = Directory.CreateDirectory(Path.Combine(providerRoot.Path, "provider-work")).FullName;
        var application = new CliApplication(
            TextWriter.Null, TextWriter.Null, Service, _ => new SelfCompletingAdapter(root.Path),
            new ContextAssembler());
        string[] common = ["--root", root.Path, "--task", "T1"];
        await application.RunAsync(
            ["task", "open", .. common, "--actor", "operator", "--title", "Task", "--goal", "Goal"],
            CancellationToken.None);
        await application.RunAsync(
            ["actor", "attach", .. common, "--actor", "operator", "--target", "subject", "--role", role],
            CancellationToken.None);
        await application.RunAsync(
            ["work", "add", .. common, "--actor", "operator", "--id", "W1", "--title", "Do it",
             "--owner", "operator", "--scope", work], CancellationToken.None);
        // One of the two roles under test is the code reviewer, which cannot start until a verifier
        // has finished with the item. The pass is recorded for both so the theory stays symmetric.
        await application.RunAsync(
            ["actor", "attach", .. common, "--actor", "operator", "--target", "verifier",
             "--role", "verifier"], CancellationToken.None);
        await RecordExecutionArtifactsAsync(application, common, "W1");
        await application.RunAsync(
            ["run", "start", .. common, "--actor", "operator", "--subject", "verifier", "--run", "RV",
             "--work", "W1", "--provider", "codex", "--session", "verifier-session"], CancellationToken.None);
        await RecordArtifactAsync(
            application, common, "verifier", "A-RV", "verifier-output", "W1", "RV",
            ArtifactCommands.VerifierBody);
        await application.RunAsync(
            ["run", "complete", .. common, "--actor", "operator", "--run", "RV", "--status", "completed",
             "--session", "verifier-session"], CancellationToken.None);

        // The code-reviewer half of this theory meets open question DX1 in ledger-artifacts: the
        // launcher's close of a reviewer run now requires a CodeReviewOutput this agent never
        // filed. The implementation-lead half is unaffected — the gate reaches the two inspecting
        // roles only.
        var exit = await application.RunAsync(
            ["provider", "launch", .. common, "--actor", "operator", "--subject", "subject",
             "--run", "R1", "--work", "W1", "--provider", "codex", "--executable", "/usr/bin/true",
             "--cognitive-root", FindCognitiveRoot()], CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        var run = state!.Runs[new RunId("R1")];
        // The agent's attempt from inside the run was refused; the launcher's close is the one that
        // landed, and it carries the session identity the agent's attempt would have overwritten.
        Assert.Equal(expectedExit, exit);
        Assert.Equal(expectedStatus, run.Status);
        Assert.Equal("session-1", run.ProviderSessionId);
        Assert.Equal(new ActorId("subject"), run.ActorId);
        Assert.NotNull(SelfCompletingAdapter.LastAttemptError);
        Assert.Contains(expectedRefusal, SelfCompletingAdapter.LastAttemptError!, StringComparison.Ordinal);
    }

    // F2 at the surface the operator actually types. Releasing an area is only useful if the next
    // work item can take it, so the test asserts the reuse rather than the status alone.
    [Fact]
    public async Task WorkAbandonReleasesTheAreaForABetterSplit()
    {
        using var root = new TemporaryDirectory();
        using var scopeRoot = new TemporaryDirectory();
        var area = Directory.CreateDirectory(Path.Combine(scopeRoot.Path, "area")).FullName;
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["work", "add", .. common, "--id", "W1", "--title", "Wrong split", "--owner", "operator",
             "--scope", area], CancellationToken.None);

        var abandonExit = await application.RunAsync(
            ["work", "abandon", .. common, "--id", "W1", "--reason", "The split was wrong"],
            CancellationToken.None);
        var reuseExit = await application.RunAsync(
            ["work", "add", .. common, "--id", "W2", "--title", "Better split", "--owner", "operator",
             "--scope", area], CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(0, abandonExit);
        Assert.Equal(0, reuseExit);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal(WorkItemStatus.Abandoned, state!.WorkItems[new WorkItemId("W1")].Status);
        Assert.Equal("The split was wrong", state.WorkItems[new WorkItemId("W1")].AbandonReason);
        Assert.Equal(WorkItemStatus.Proposed, state.WorkItems[new WorkItemId("W2")].Status);
    }

    // The statuses that release a work item's area are written out twice with no shared predicate:
    // once in the occupancy check behind `work add`, once in the `who` projection's occupied query.
    // If the two lists ever drift, `who` calls an area free that `work add` refuses, or — the way
    // round that costs real work — `who` calls an area held after `work add` has already handed the
    // same directory to a second agent. Asserting either fact alone still passes while they
    // disagree, so both are asserted here, in one test, against one abandonment.
    [Fact]
    public async Task AbandoningReleasesAnAreaInBothTheWhoProjectionAndTheOccupancyCheck()
    {
        using var root = new TemporaryDirectory();
        using var scopeRoot = new TemporaryDirectory();
        var area = Directory.CreateDirectory(Path.Combine(scopeRoot.Path, "area")).FullName;
        var error = new StringWriter();
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await Create(TextWriter.Null, error).RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await Create(TextWriter.Null, error).RunAsync(
            ["work", "add", .. common, "--id", "W1", "--title", "Wrong split", "--owner", "operator",
             "--scope", area], CancellationToken.None);
        // The stored scope is canonicalised — on macOS /var resolves to /private/var — so what is
        // matched against the projection is what the ledger holds, not what was typed.
        var added = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        var canonicalArea = Assert.Single(added!.WorkItems[new WorkItemId("W1")].ResourceScope);

        var whileHeld = new StringWriter();
        await Create(whileHeld, error).RunAsync(["who", .. common], CancellationToken.None);
        await Create(TextWriter.Null, error).RunAsync(
            ["work", "abandon", .. common, "--id", "W1", "--reason", "The split was wrong"],
            CancellationToken.None);
        var afterRelease = new StringWriter();
        await Create(afterRelease, error).RunAsync(["who", .. common], CancellationToken.None);

        // Only now is the area claimed again, so the projection above was read while it was free.
        var reuseExit = await Create(TextWriter.Null, error).RunAsync(
            ["work", "add", .. common, "--id", "W2", "--title", "Better split", "--owner", "operator",
             "--scope", area], CancellationToken.None);

        // The control. Without it, an absence proves nothing: a `who` that never named areas at all
        // would satisfy the second assertion on its own.
        Assert.Contains(canonicalArea, whileHeld.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(canonicalArea, afterRelease.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, reuseExit);
        Assert.Equal(string.Empty, error.ToString());
    }

    // The whole defect, end to end, at the surface the operator uses. An abandoned item releases
    // its area; a second item takes it; then rejecting the claim the first depended on moves it
    // from Abandoned to Stale — and blocking a stale item used to be accepted, leaving two live
    // work items holding one directory. The exit code is only half the evidence: the area has to
    // still read as held by exactly one item, in `who` and in what a third `work add` is told.
    [Fact]
    public async Task AnAreaReleasedByAbandonmentIsNotRetakenWhenTheItemGoesStale()
    {
        using var root = new TemporaryDirectory();
        using var scopeRoot = new TemporaryDirectory();
        var area = Directory.CreateDirectory(Path.Combine(scopeRoot.Path, "area")).FullName;
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["claim", "add", .. common, "--id", "C1", "--statement", "The API is stable"],
            CancellationToken.None);
        await application.RunAsync(
            ["work", "add", .. common, "--id", "W1", "--title", "Wrong split", "--owner", "operator",
             "--depends-on", "C1", "--scope", area], CancellationToken.None);
        await application.RunAsync(
            ["work", "abandon", .. common, "--id", "W1", "--reason", "The split was wrong"],
            CancellationToken.None);
        var reuseExit = await application.RunAsync(
            ["work", "add", .. common, "--id", "W2", "--title", "Better split", "--owner", "operator",
             "--scope", area], CancellationToken.None);

        // The claim W1 was built on is refuted, which invalidates W1 and moves it to Stale.
        await application.RunAsync(
            ["evidence", "add", .. common, "--id", "E1", "--source-type", "probe", "--citation", "cite",
             "--summary", "refutes C1", "--refutes", "C1"], CancellationToken.None);
        await application.RunAsync(
            ["claim", "resolve", .. common, "--id", "C1", "--status", "rejected", "--evidence", "E1"],
            CancellationToken.None);
        var staleState = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        var canonicalArea = Assert.Single(staleState!.WorkItems[new WorkItemId("W2")].ResourceScope);

        var blockExit = await application.RunAsync(
            ["work", "block", .. common, "--id", "W1", "--reason", "Reopening it"], CancellationToken.None);
        // The other way back in, and the one that would also rewrite the record: abandoning again
        // would replace the reason the operator actually acted on with a later, different one.
        var abandonAgainExit = await application.RunAsync(
            ["work", "abandon", .. common, "--id", "W1", "--reason", "A different reason"],
            CancellationToken.None);

        var who = new StringWriter();
        await Create(who, TextWriter.Null).RunAsync(["who", .. common], CancellationToken.None);
        var thirdAdd = new StringWriter();
        var thirdExit = await Create(TextWriter.Null, thirdAdd).RunAsync(
            ["work", "add", .. common, "--id", "W3", "--title", "A third claimant", "--owner", "operator",
             "--scope", area], CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(0, reuseExit);
        Assert.Equal(1, blockExit);
        Assert.Equal(1, abandonAgainExit);
        Assert.Equal("The split was wrong", state!.WorkItems[new WorkItemId("W1")].AbandonReason);
        Assert.Equal(WorkItemStatus.Stale, state.WorkItems[new WorkItemId("W1")].Status);
        Assert.Equal(WorkItemStatus.Proposed, state.WorkItems[new WorkItemId("W2")].Status);
        // One holder, not two: the area is named once in `who`, and the item turned away is told
        // which single work item holds it.
        Assert.Equal(1, Occurrences(who.ToString(), canonicalArea));
        Assert.Equal(1, thirdExit);
        Assert.Contains("already held by work item 'W2'", thirdAdd.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("'W1'", thirdAdd.ToString(), StringComparison.Ordinal);
    }

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    // F3b at the operator's surface. The refusal has to arrive as an exit code and a sentence that
    // says what to do next, because the operator's next move is to write the alternative down.
    [Fact]
    public async Task WorkAddRefusesASecondAreaUntilAnAlternativeExplainsTheNonSplit()
    {
        using var root = new TemporaryDirectory();
        using var scopeRoot = new TemporaryDirectory();
        var first = Directory.CreateDirectory(Path.Combine(scopeRoot.Path, "first")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(scopeRoot.Path, "second")).FullName;
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);

        var refusedExit = await application.RunAsync(
            ["work", "add", .. common, "--id", "W1", "--title", "Two areas", "--owner", "operator",
             "--scope", first, "--scope", second], CancellationToken.None);

        await application.RunAsync(
            ["alternative", "record", .. common, "--id", "ALT1",
             "--statement", "Split the two areas into separate work items",
             "--rejected-because", "The two areas only ever change together"], CancellationToken.None);
        var acceptedExit = await application.RunAsync(
            ["work", "add", .. common, "--id", "W1", "--title", "Two areas", "--owner", "operator",
             "--scope", first, "--scope", second, "--not-split-because", "ALT1"], CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(1, refusedExit);
        Assert.Contains("more than one area", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, acceptedExit);
        Assert.Equal(new AlternativeId("ALT1"), state!.WorkItems[new WorkItemId("W1")].NotSplitJustification);
    }

    // F3a at the same surface, and the gate the operator hits first: nothing has run at all.
    [Fact]
    public async Task WorkCompleteIsRefusedUntilSomeoneHasActuallyWorkedOnTheItem()
    {
        using var root = new TemporaryDirectory();
        using var scopeRoot = new TemporaryDirectory();
        var area = Directory.CreateDirectory(Path.Combine(scopeRoot.Path, "area")).FullName;
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["work", "add", .. common, "--id", "W1", "--title", "Untouched work", "--owner", "operator",
             "--scope", area], CancellationToken.None);

        var exit = await application.RunAsync(
            ["work", "complete", .. common, "--id", "W1"], CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(1, exit);
        Assert.Contains("no completed run", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(WorkItemStatus.Proposed, state!.WorkItems[new WorkItemId("W1")].Status);
    }

    // F1 at the same surface: the gate is refused with an exit code and a message the operator can
    // act on, and the waiver is the only way past it without a verifier.
    [Fact]
    public async Task WorkCompleteIsRefusedWithoutAVerifierRunUntilTheOperatorWaivesIt()
    {
        using var root = new TemporaryDirectory();
        using var scopeRoot = new TemporaryDirectory();
        var area = Directory.CreateDirectory(Path.Combine(scopeRoot.Path, "area")).FullName;
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["work", "add", .. common, "--id", "W1", "--title", "Unverifiable work", "--owner", "operator",
             "--scope", area], CancellationToken.None);
        // The work itself was done, so the refusal below is about the verifier pass and nothing else.
        await application.RunAsync(
            ["actor", "attach", .. common, "--target", "worker", "--role", "worker"], CancellationToken.None);
        await application.RunAsync(
            ["run", "start", .. common, "--subject", "worker", "--run", "RW", "--work", "W1",
             "--provider", "codex", "--session", "worker-session"], CancellationToken.None);
        await application.RunAsync(
            ["run", "complete", .. common, "--run", "RW", "--status", "completed",
             "--session", "worker-session"], CancellationToken.None);

        var refusedExit = await application.RunAsync(
            ["work", "complete", .. common, "--id", "W1"], CancellationToken.None);
        var waivedExit = await application.RunAsync(
            ["work", "complete", .. common, "--id", "W1",
             "--without-verification", "No verifier is attached to this task"], CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(1, refusedExit);
        Assert.Contains("verifier run", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, waivedExit);
        Assert.Equal(WorkItemStatus.Completed, state!.WorkItems[new WorkItemId("W1")].Status);
    }

    // The path the pipeline is meant to take, driven end to end: an operator dispatches a run to a
    // verifier, closes it, and only then does the completion go through.
    [Fact]
    public async Task AVerifierRunRecordedThroughTheCliUnlocksCompletion()
    {
        using var root = new TemporaryDirectory();
        using var scopeRoot = new TemporaryDirectory();
        var area = Directory.CreateDirectory(Path.Combine(scopeRoot.Path, "area")).FullName;
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["actor", "attach", .. common, "--target", "verifier", "--role", "verifier"],
            CancellationToken.None);
        await application.RunAsync(
            ["work", "add", .. common, "--id", "W1", "--title", "Verified work", "--owner", "operator",
             "--scope", area], CancellationToken.None);
        // The work itself was done, so the refusal below is about the verifier pass and nothing else.
        await application.RunAsync(
            ["actor", "attach", .. common, "--target", "worker", "--role", "worker"], CancellationToken.None);
        await application.RunAsync(
            ["run", "start", .. common, "--subject", "worker", "--run", "RW", "--work", "W1",
             "--provider", "codex", "--session", "worker-session"], CancellationToken.None);
        await application.RunAsync(
            ["run", "complete", .. common, "--run", "RW", "--status", "completed",
             "--session", "worker-session"], CancellationToken.None);

        await RecordExecutionArtifactsAsync(application, common, "W1");
        await application.RunAsync(
            ["run", "start", .. common, "--subject", "verifier", "--run", "RV", "--work", "W1",
             "--provider", "claude", "--session", "verifier-session"], CancellationToken.None);
        // The pass files its findings before it closes: a verifier run cannot be recorded as
        // completed without the VerifierOutput artifact that run produced.
        await RecordArtifactAsync(
            application, ["--root", root.Path, "--task", "T1"], "verifier", "A-RV", "verifier-output",
            "W1", "RV", ArtifactCommands.VerifierBody);
        await application.RunAsync(
            ["run", "complete", .. common, "--run", "RV", "--status", "completed",
             "--session", "verifier-session"], CancellationToken.None);
        var completeExit = await application.RunAsync(
            ["work", "complete", .. common, "--id", "W1"], CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(0, completeExit);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal(RoleKind.Verifier, state!.Runs[new RunId("RV")].SubjectRole);
        Assert.Equal(WorkItemStatus.Completed, state.WorkItems[new WorkItemId("W1")].Status);
    }

    // Accepted decision D1: an area may be a single file, so two agents can hold two files in one
    // directory. The kernel accepted that from the beginning; the check that held every scope to a
    // directory was here, in the operator surface, which is why these two tests are at this level.
    [Fact]
    public async Task WorkCreationAcceptsAFileAsAnAreaAndStoresTheFileItself()
    {
        using var root = new TemporaryDirectory();
        using var scopeRoot = new TemporaryDirectory();
        var area = Directory.CreateDirectory(Path.Combine(scopeRoot.Path, "area")).FullName;
        var file = Path.Combine(area, "ClaimRules.cs");
        await File.WriteAllTextAsync(file, "// the area itself");
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);

        var exit = await application.RunAsync(
            ["work", "add", .. common, "--id", "W1", "--title", "One file", "--owner", "operator",
             "--scope", file], CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        var scope = Assert.Single(state!.WorkItems[new WorkItemId("W1")].ResourceScope);
        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, error.ToString());
        // The stored scope is the file, not the directory around it. A scope widened to the parent
        // here would hand one agent every other file beside it and read in `who` as the whole area.
        Assert.True(Path.IsPathFullyQualified(scope));
        Assert.True(File.Exists(scope));
        Assert.Equal("ClaimRules.cs", Path.GetFileName(scope));
    }

    // A process cannot start inside a file, so a file-scoped item resolves its provider's working
    // directory to the file's parent. That widening is the launch's alone: the recorded scope stays
    // the file, and occupancy keeps reading it as one.
    [Fact]
    public async Task AFileScopedWorkItemLaunchesItsProviderInTheFilesDirectory()
    {
        using var root = new TemporaryDirectory();
        using var scopeRoot = new TemporaryDirectory();
        var area = Directory.CreateDirectory(Path.Combine(scopeRoot.Path, "area")).FullName;
        var file = Path.Combine(area, "ClaimRules.cs");
        await File.WriteAllTextAsync(file, "// the area itself");
        var capture = new CapturingAdapter();
        var application = new CliApplication(
            TextWriter.Null, TextWriter.Null, Service, _ => capture, new ContextAssembler());
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["work", "add", .. common, "--id", "W1", "--title", "One file", "--owner", "operator",
             "--scope", file], CancellationToken.None);

        var exit = await application.RunAsync(
            ["provider", "launch", .. common, "--run", "R1", "--work", "W1", "--provider", "codex",
             "--executable", "/usr/bin/true", "--cognitive-root", FindCognitiveRoot()],
            CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        var scope = Assert.Single(state!.WorkItems[new WorkItemId("W1")].ResourceScope);
        var request = Assert.Single(capture.Requests);
        Assert.Equal(0, exit);
        // Both sides are canonical already — the stored scope was canonicalised when it was
        // recorded — so the parent is compared as a path rather than through a sentinel file.
        Assert.Equal(Path.GetDirectoryName(scope), request.WorkingDirectory);
        Assert.EndsWith("ClaimRules.cs", scope, StringComparison.Ordinal);
    }

    // The body arrives on standard input rather than as an option value, because the parser takes
    // any value opening with two dashes as the next option name and a real workflow document starts
    // with a horizontal rule or YAML front matter. That is validated claim C10, and it is why every
    // artifact recorded through the CLI in this file goes through here.
    private static async Task<int> RecordArtifactAsync(
        CliApplication application,
        IReadOnlyList<string> common,
        string actor,
        string artifactId,
        string kind,
        string? work,
        string? run,
        string body)
    {
        string[] scope = work is null ? [] : ["--work", work];
        string[] producer = run is null ? [] : ["--run", run];
        using var input = new StandardInput(body);
        return await application.RunAsync(
            ["artifact", "record", .. common, "--actor", actor, "--id", artifactId, "--kind", kind,
             "--title", "Governed document", "--body-stdin", .. scope, .. producer],
            CancellationToken.None);
    }

    private static async Task<int[]> RecordExecutionArtifactsAsync(
        CliApplication application,
        IReadOnlyList<string> common,
        string workItemId)
    {
        var exits = new List<int>
        {
            await RecordArtifactAsync(
                application, common, "operator", "A-request", "user-request", null, null,
                "The governed request")
        };
        exits.Add(await application.RunAsync(
            ["run", "start", .. common, "--actor", "operator", "--subject", "operator",
             "--run", "R-artifacts", "--work", workItemId, "--provider", "codex",
             "--session", "artifact-session"], CancellationToken.None));
        exits.Add(await RecordArtifactAsync(
            application, common, "operator", "A-contract", "prompt-contract", null, "R-artifacts",
            ArtifactCommands.Body));
        exits.Add(await RecordArtifactAsync(
            application, common, "operator", "A-plan", "orchestration-plan", null, "R-artifacts",
            ArtifactCommands.PlanBody));
        exits.Add(await application.RunAsync(
            ["run", "complete", .. common, "--actor", "operator", "--run", "R-artifacts",
             "--status", "completed", "--session", "artifact-session"], CancellationToken.None));
        return [.. exits];
    }

    // The manifest reaches the agent as the CLI wrote it, so it is read back the same way.
    private static readonly JsonSerializerOptions ManifestJson = LedgerJson.CreateOptions();

    private sealed class CapturingAdapter : IAgentAdapter
    {
        public List<AgentLaunchRequest> Requests { get; } = [];

        public string Provider => "codex";

        public Task<string> ProbeVersionAsync(string executablePath, CancellationToken cancellationToken) =>
            Task.FromResult("test");

        public Task<AgentRunResult> RunAsync(AgentLaunchRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new AgentRunResult(
                request.RunId, Provider, "session-1", AgentRunStatus.Completed,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(1), 0, "done", [], string.Empty,
                "test", [], false, null));
        }
    }

    // Stands in for the live agents that closed their own runs despite being told not to: it tries
    // the completion from inside the run, using the subject identity it was launched under.
    private sealed class SelfCompletingAdapter(string ledgerRoot) : IAgentAdapter
    {
        public static string? LastAttemptError { get; private set; }

        public string Provider => "codex";

        public Task<string> ProbeVersionAsync(string executablePath, CancellationToken cancellationToken) =>
            Task.FromResult("test");

        public async Task<AgentRunResult> RunAsync(AgentLaunchRequest request, CancellationToken cancellationToken)
        {
            LastAttemptError = null;
            try
            {
                await Service(ledgerRoot).ExecuteAsync(
                    request.TaskId,
                    new CompleteRunCommand(
                        request.ActorId, null, "agent-inside-run", request.RunId,
                        AgentRunStatus.Completed, "agent-session"),
                    cancellationToken);
            }
            catch (GovernanceException exception)
            {
                LastAttemptError = exception.Message;
            }

            return new AgentRunResult(
                request.RunId, Provider, "session-1", AgentRunStatus.Completed,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(1), 0, "done", [], string.Empty,
                "test", [], false, null);
        }
    }

    private static CliApplication Create(TextWriter output, TextWriter error) => new(
        output,
        error,
        Service,
        _ => throw new InvalidOperationException("Provider adapter is not used by this test."),
        new ContextAssembler());

    private static async Task WaitUntilAsync(Func<bool> condition, string failureMessage)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail(failureMessage);
    }

    private sealed class ThreadSafeStringWriter : TextWriter
    {
        private readonly StringBuilder _buffer = new();
        private readonly object _gate = new();

        public override Encoding Encoding => Encoding.UTF8;

        public override Task WriteLineAsync(string? value)
        {
            lock (_gate)
            {
                _buffer.AppendLine(value);
            }

            return Task.CompletedTask;
        }

        public string GetText()
        {
            lock (_gate)
            {
                return _buffer.ToString();
            }
        }
    }

    private static IGovernedTaskService Service(string root)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(root, new CommandHandler(reducer, new AuthorizationPolicy()), reducer);
    }

    private static string FindCognitiveRoot()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "cognitive");
            if (File.Exists(Path.Combine(candidate, "manifest.json")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Test could not locate the cognitive root.");
    }

    private sealed class CancellingAdapter(CancellationTokenSource cancellation) : IAgentAdapter
    {
        public string Provider => "codex";

        public Task<string> ProbeVersionAsync(string executablePath, CancellationToken cancellationToken) =>
            Task.FromResult("test");

        public Task<AgentRunResult> RunAsync(AgentLaunchRequest request, CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            return Task.FromResult(new AgentRunResult(
                request.RunId, Provider, "cancelled-session", AgentRunStatus.Cancelled,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(1), -1, null, [], string.Empty,
                "test", [], false, "Provider run was cancelled."));
        }
    }

    private sealed class SuccessfulCancellingAdapter(CancellationTokenSource cancellation) : IAgentAdapter
    {
        public string Provider => "codex";

        public Task<string> ProbeVersionAsync(string executablePath, CancellationToken cancellationToken) =>
            Task.FromResult("test");

        public Task<AgentRunResult> RunAsync(AgentLaunchRequest request, CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            return Task.FromResult(new AgentRunResult(
                request.RunId, Provider, "session-1", AgentRunStatus.Completed,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(1), 0, "done", [], string.Empty,
                "test", [], false, null));
        }
    }

    private sealed class FixedResultAdapter(AgentRunStatus status) : IAgentAdapter
    {
        public string Provider => "codex";

        public Task<string> ProbeVersionAsync(string executablePath, CancellationToken cancellationToken) =>
            Task.FromResult("test");

        public Task<AgentRunResult> RunAsync(AgentLaunchRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new AgentRunResult(
                request.RunId, Provider, "session-1", status,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(1),
                status == AgentRunStatus.Failed ? 1 : 0, null, [], string.Empty, "test", [], false, "failed"));
    }

    private sealed class CompletionFailureService(
        IGovernedTaskService inner,
        int failuresBeforeSuccess) : IGovernedTaskService
    {
        public int CompletionAttempts { get; private set; }

        public Task<CommandOutcome> ExecuteAsync(
            TaskId taskId,
            LedgerCommand command,
            CancellationToken cancellationToken)
        {
            if (command is CompleteRunCommand && ++CompletionAttempts <= failuresBeforeSuccess)
            {
                throw new IOException("Injected pre-commit task mutation lock failure.");
            }

            return inner.ExecuteAsync(taskId, command, cancellationToken);
        }

        public Task<GovernedTaskState?> GetStateAsync(TaskId taskId, CancellationToken cancellationToken) =>
            inner.GetStateAsync(taskId, cancellationToken);

        public IAsyncEnumerable<LedgerEvent> GetHistoryAsync(
            TaskId taskId,
            CancellationToken cancellationToken) =>
            inner.GetHistoryAsync(taskId, cancellationToken);
    }
}
