using System.Text.Json.Nodes;
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
                ["claim", "add", .. source, "--id", "C2", "--statement", "The research topic remains open"],
                CancellationToken.None),
            await application.RunAsync(
                ["alternative", "record", .. source, "--id", "ALT1", "--statement", "Skip bounded retries",
                 "--rejected-because", "The validated claim requires a bounded policy"], CancellationToken.None),
            await application.RunAsync(
                ["actor", "attach", .. source, "--target", "researcher", "--role", "researcher"],
                CancellationToken.None),
            await application.RunAsync(
                ["actor", "attach", .. source, "--target", "worker", "--role", "worker"],
                CancellationToken.None),
            await application.RunAsync(
                ["actor", "attach", .. source, "--target", "verifier", "--role", "verifier"],
                CancellationToken.None),
            await application.RunAsync(
                ["actor", "attach", .. source, "--target", "reviewer", "--role", "code-reviewer"],
                CancellationToken.None),
            await BriefAsync(source),
            await RecordArtifactAsync(
                application, source, "operator", "A-request", "user-request", null, null,
                "The governed request")
        };

        exits.Add(await application.RunAsync(
            ["stage", "transition", .. source, "--stage", "research"], CancellationToken.None));
        exits.Add(await application.RunAsync(
            ["run", "start", .. source, "--subject", "researcher", "--run", "R-research",
             "--provider", "codex", "--session", "research-session"], CancellationToken.None));
        exits.Add(await application.RunAsync(
            ["run", "complete", .. source, "--run", "R-research", "--status", "completed",
             "--session", "research-session"], CancellationToken.None));

        exits.Add(await application.RunAsync(
            ["stage", "transition", .. source, "--stage", "design"], CancellationToken.None));
        exits.Add(await application.RunAsync(
            ["run", "start", .. source, "--run", "R-artifacts", "--provider", "codex",
             "--session", "artifact-session"], CancellationToken.None));
        exits.Add(await RecordArtifactAsync(
            application, source, "operator", "A-contract", "prompt-contract", null, "R-artifacts",
            ArtifactCommands.Body));
        exits.Add(await RecordArtifactAsync(
            application, source, "operator", "A-plan", "orchestration-plan", null, "R-artifacts",
            ArtifactCommands.PlanBody));
        exits.Add(await application.RunAsync(
            ["run", "complete", .. source, "--run", "R-artifacts", "--status", "completed",
             "--session", "artifact-session"], CancellationToken.None));
        foreach (var stage in new[] { "scope", "ready" })
        {
            exits.Add(await application.RunAsync(
                ["stage", "transition", .. source, "--stage", stage], CancellationToken.None));
        }
        exits.Add(await application.RunAsync(
            ["work", "add", .. source, "--id", "W1", "--title", "Source work", "--owner", "worker"],
            CancellationToken.None));
        exits.Add(await application.RunAsync(
            ["stage", "transition", .. source, "--stage", "execution"], CancellationToken.None));
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
             "--provider", "claude", "--session", "verify-session"], CancellationToken.None));
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
            ["work", "complete", .. source, "--id", "W1"], CancellationToken.None));
        exits.Add(await application.RunAsync(
            ["stage", "transition", .. source, "--stage", "learn"], CancellationToken.None));
        exits.Add(await application.RunAsync(
            ["lesson", "mark", .. source, "--kind", "validated-claim", "--source", "C1",
             "--class", "untested", "--repo", "AILedger", "--tag", "retry", "--tag", "bounded",
             "--verify", "dotnet test --filter RetryTests.Bounded",
             "--do-not", "Do not assume retries are bounded without rerunning the test",
             "--lesson-actor", "verifier", "--verify-expects", "present",
             "--lesson-kind", "workflow", "--audience", "verifier"],
            CancellationToken.None));
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
        // The three routing options reach the minted lesson through the CLI, not only through the
        // command record: a flag the dispatcher drops would leave every other assertion here true.
        Assert.Equal(LessonKind.Workflow, minted.Kind);
        Assert.Equal([RoleKind.Verifier], minted.Audience);
        Assert.Equal(VerifyExpectation.Present, minted.VerifyExpects);
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

    // The launch path's half of the coordinating-role refusal. The rule lives in
    // RunDispatchRules.EnsurePermitted, which the command reaches and the pre-flight also runs —
    // but the CLI passed no work item to the pre-flight, so the rule saw none, did not fire, and the
    // launcher resolved an adapter and probed the provider's version before the command refused it.
    // The adapter factory is the assertion for exactly that reason: an exit code alone cannot tell a
    // refusal before the process from a refusal after one, and the cost this rule is placed early to
    // avoid is the process.
    [Theory]
    [InlineData("operator", RoleKind.Operator)]
    [InlineData("planning-lead", RoleKind.PlanningLead)]
    [InlineData("implementation-lead", RoleKind.ImplementationLead)]
    public async Task AProviderLaunchByACoordinatingSubjectAgainstAWorkItemStartsNoProcess(
        string roleOption, RoleKind role)
    {
        using var root = new TemporaryDirectory();
        using var providerRoot = new TemporaryDirectory();
        var solution = Directory.CreateDirectory(Path.Combine(providerRoot.Path, "solution")).FullName;
        Directory.CreateDirectory(Path.Combine(solution, ".git"));
        var project = Directory.CreateDirectory(Path.Combine(solution, "src", "AILedger.Core")).FullName;
        var factoryCalls = 0;
        var error = new StringWriter();
        var application = new CliApplication(
            TextWriter.Null, error, Service,
            _ =>
            {
                factoryCalls++;
                throw new InvalidOperationException("A refused launch must resolve no adapter.");
            },
            new ContextAssembler());
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);
        await application.RunAsync(
            ["actor", "attach", .. common, "--target", "coordinator", "--role", roleOption],
            CancellationToken.None);
        // The item carries a directory scope because grants are resolved before the pre-flight, and
        // an item with no scope is refused there — which would pass this test for the wrong reason.
        await application.RunAsync(
            ["work", "add", .. common, "--id", "W1", "--title", "One project", "--owner", "operator",
             "--scope", project], CancellationToken.None);

        var exit = await application.RunAsync(
            ["provider", "launch", .. common, "--subject", "coordinator",
             "--run", "R1", "--work", "W1", "--provider", "codex", "--executable", "/usr/bin/true",
             "--working-directory", solution, "--cognitive-root", FindCognitiveRoot()],
            CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(1, exit);
        Assert.Equal(0, factoryCalls);
        Assert.DoesNotContain(new RunId("R1"), state!.Runs.Keys);
        // Whole, because the refusal is the only instruction the refused dispatcher gets: it has to
        // name the roles that may be dispatched instead and the one run a coordinating role keeps.
        Assert.Contains(
            "A run against work item 'W1' cannot be held by a coordinating role, and " +
            $"'coordinator' is a {role}. Dispatch a Worker, Researcher, Verifier or CodeReviewer " +
            "against the item, or start this run without --work to file task-wide artifacts.",
            error.ToString(), StringComparison.Ordinal);
        // Decided before any command is submitted, so the service site of the refusal journal never
        // sees it; the launch site is the only record that the attempt happened at all.
        var journalled = Assert.Single(RefusalJournal(root.Path));
        Assert.Equal("provider-launch", journalled.GetProperty("site").GetString());
        Assert.Equal("provider launch", journalled.GetProperty("command").GetString());
        Assert.StartsWith(
            "A run against work item 'W1' cannot be held by a coordinating role",
            journalled.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    // The control for the test above. A zero adapter count proves a refusal arrived early only if
    // the same launch, with the same coordinating subject, still reaches the adapter when it names
    // no work item — otherwise the count could be zero because the launch is broken for a reason
    // that has nothing to do with the rule. It is also the case the rule deliberately preserves: a
    // lead's task-wide run is how the prompt contract and the orchestration plan are recorded.
    [Fact]
    public async Task AProviderLaunchByACoordinatingSubjectNamingNoWorkItemStartsTheProcess()
    {
        using var root = new TemporaryDirectory();
        using var providerRoot = new TemporaryDirectory();
        var solution = Directory.CreateDirectory(Path.Combine(providerRoot.Path, "solution")).FullName;
        Directory.CreateDirectory(Path.Combine(solution, ".git"));
        var factoryCalls = 0;
        var capture = new CapturingAdapter();
        var error = new StringWriter();
        var application = new CliApplication(
            TextWriter.Null, error, Service,
            _ =>
            {
                factoryCalls++;
                return capture;
            },
            new ContextAssembler());
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await ContextBrief.BuildAsync(root.Path, "T1");
        await application.RunAsync(
            ["actor", "attach", .. common, "--target", "coordinator", "--role", "implementation-lead"],
            CancellationToken.None);

        var exit = await application.RunAsync(
            ["provider", "launch", .. common, "--subject", "coordinator",
             "--run", "R1", "--provider", "codex", "--executable", "/usr/bin/true",
             "--working-directory", solution, "--cognitive-root", FindCognitiveRoot()],
            CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(0, exit);
        Assert.Equal(1, factoryCalls);
        Assert.Single(capture.Requests);
        Assert.Contains(new RunId("R1"), state!.Runs.Keys);
        Assert.Null(state.Runs[new RunId("R1")].WorkItemId);
        Assert.Empty(RefusalJournal(root.Path));
    }

    // Read as the rows are written, one JSON object per line, because the question these two tests
    // ask of the journal is whether the launch site wrote anything at all.
    private static IReadOnlyList<JsonElement> RefusalJournal(string root)
    {
        var path = Path.Combine(root, "T1", "refusals.jsonl");
        return File.Exists(path)
            ? File.ReadAllLines(path).Select(line => JsonDocument.Parse(line).RootElement).ToArray()
            : [];
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
        await ContextBrief.BuildAsync(root.Path, "T1");

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
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);
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
        await RecordExecutionArtifactsAsync(application, common);
        await CliStageFixture.ToExecutionAsync(application, root.Path);
        await CliStageFixture.ToVerificationAsync(application, root.Path);
        await application.RunAsync(
            ["run", "start", .. common, "--actor", "operator", "--subject", "verifier", "--run", "RV",
             "--work", "W1", "--provider", "codex", "--session", "verifier-session"], CancellationToken.None);
        await RecordArtifactAsync(
            application, common, "verifier", "A-RV", "verifier-output", "W1", "RV",
            ArtifactCommands.VerifierBody);
        await application.RunAsync(
            ["run", "complete", .. common, "--actor", "operator", "--run", "RV", "--status", "completed",
             "--session", "verifier-session"], CancellationToken.None);
        await CliStageFixture.ToReviewAsync(application, root.Path);

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
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);
        await application.RunAsync(
            ["work", "add", .. common, "--actor", "operator", "--id", "W1", "--title", "Review it",
             "--owner", "operator", "--scope", work], CancellationToken.None);
        await application.RunAsync(
            ["escalation", "raise", .. common, "--actor", "operator", "--id", "X1",
             "--kind", "business-decision", "--question", "Ship now or harden first?",
             "--option", "ship", "--option", "harden", "--recommend", "ship"], CancellationToken.None);

        // Undispatched means the operator holds the run itself, and a coordinating role may hold a
        // run only when it names no work item. So this launch is task-wide, which is also the only
        // shape an undispatched launch can now take. The escalation it asserts on is task-wide too,
        // so what the manifest carries is unchanged; W1 above stays, unclaimed, as the reviewer
        // test's counterpart.
        var exit = await application.RunAsync(
            ["provider", "launch", .. common, "--actor", "operator",
             "--run", "R1", "--provider", "codex", "--executable", "/usr/bin/true",
             "--working-directory", work, "--cognitive-root", FindCognitiveRoot()],
            CancellationToken.None);

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
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);
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
    //
    // The two halves differ in one more thing than the role, and they have to. A code reviewer
    // reviews a work item, so its run names one; an implementation lead may not hold a run that
    // names a work item at all, so its run is the task-wide kind. The launch token rule under test
    // is indifferent to that — it reads the run's token hash, not its scope — so the distinction
    // costs the theory nothing.
    [Theory]
    [InlineData("code-reviewer", true, "lacks capability", 3, AgentRunStatus.Failed)]
    [InlineData("implementation-lead", false, "closed by that launcher", 0, AgentRunStatus.Completed)]
    public async Task ADispatchedSubjectStillCannotCompleteItsOwnRun(
        string role,
        bool againstWorkItem,
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
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);
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
        await RecordExecutionArtifactsAsync(application, common);
        await CliStageFixture.ToExecutionAsync(application, root.Path);
        await CliStageFixture.ToVerificationAsync(application, root.Path);
        await application.RunAsync(
            ["run", "start", .. common, "--actor", "operator", "--subject", "verifier", "--run", "RV",
             "--work", "W1", "--provider", "codex", "--session", "verifier-session"], CancellationToken.None);
        await RecordArtifactAsync(
            application, common, "verifier", "A-RV", "verifier-output", "W1", "RV",
            ArtifactCommands.VerifierBody);
        await application.RunAsync(
            ["run", "complete", .. common, "--actor", "operator", "--run", "RV", "--status", "completed",
             "--session", "verifier-session"], CancellationToken.None);
        await CliStageFixture.ToReviewAsync(application, root.Path);

        // The code-reviewer half of this theory meets open question DX1 in ledger-artifacts: the
        // launcher's close of a reviewer run now requires a CodeReviewOutput this agent never
        // filed. The implementation-lead half is unaffected — the gate reaches the two inspecting
        // roles only.
        string[] scope = againstWorkItem
            ? ["--work", "W1"]
            : ["--working-directory", work];
        var exit = await application.RunAsync(
            ["provider", "launch", .. common, "--actor", "operator", "--subject", "subject",
             "--run", "R1", .. scope, "--provider", "codex", "--executable", "/usr/bin/true",
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
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);
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
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(Create(TextWriter.Null, error), root.Path);
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
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);
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
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);

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
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);
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
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);
        await application.RunAsync(
            ["work", "add", .. common, "--id", "W1", "--title", "Unverifiable work", "--owner", "operator",
             "--scope", area], CancellationToken.None);
        // The work itself was done, so the refusal below is about the verifier pass and nothing else.
        await application.RunAsync(
            ["actor", "attach", .. common, "--target", "worker", "--role", "worker"], CancellationToken.None);
        await CliStageFixture.ToExecutionAsync(application, root.Path);
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
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);
        await application.RunAsync(
            ["actor", "attach", .. common, "--target", "verifier", "--role", "verifier"],
            CancellationToken.None);
        await application.RunAsync(
            ["work", "add", .. common, "--id", "W1", "--title", "Verified work", "--owner", "operator",
             "--scope", area], CancellationToken.None);
        // The work itself was done, so the refusal below is about the verifier pass and nothing else.
        await application.RunAsync(
            ["actor", "attach", .. common, "--target", "worker", "--role", "worker"], CancellationToken.None);
        await RecordExecutionArtifactsAsync(application, common);
        await CliStageFixture.ToExecutionAsync(application, root.Path);
        await application.RunAsync(
            ["run", "start", .. common, "--subject", "worker", "--run", "RW", "--work", "W1",
             "--provider", "codex", "--session", "worker-session"], CancellationToken.None);
        await application.RunAsync(
            ["run", "complete", .. common, "--run", "RW", "--status", "completed",
             "--session", "worker-session"], CancellationToken.None);

        await CliStageFixture.ToVerificationAsync(application, root.Path);
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
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);

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
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);
        await application.RunAsync(
            ["work", "add", .. common, "--id", "W1", "--title", "One file", "--owner", "operator",
             "--scope", file], CancellationToken.None);
        // The launch names the work item, because the item's scope is what this test is about. That
        // makes the subject a worker: a coordinating role cannot hold a run against an item.
        await application.RunAsync(
            ["actor", "attach", .. common, "--target", "worker", "--role", "worker"],
            CancellationToken.None);
        await CliStageFixture.ToExecutionAsync(application, root.Path);

        var exit = await application.RunAsync(
            ["provider", "launch", .. common, "--subject", "worker",
             "--run", "R1", "--work", "W1", "--provider", "codex",
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

    // Constraint K8: the stage arms are the operator's to waive, and the waiver carries a reason
    // rather than being a flag. These five drive it through the CLI, because that is where the
    // option is parsed and where the per-command allow-list would otherwise refuse it unread.
    // Discovery -> Research is the only exit from Discovery and its arm needs an open claim, so a
    // task with no claims is a stage whose arm is unsatisfied without any setup.
    [Fact]
    public async Task WaivingStagePrerequisitesIsRefusedForAnActorWhoIsNotTheOperator()
    {
        using var root = new TemporaryDirectory();
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        // An implementation lead holds requestTransition, so what is refused below is the waiver and
        // not the authority to ask for a transition at all.
        await application.RunAsync(
            ["actor", "attach", .. common, "--target", "lead", "--role", "implementation-lead"],
            CancellationToken.None);

        var exit = await application.RunAsync(
            ["stage", "transition", "--root", root.Path, "--task", "T1", "--actor", "lead",
             "--stage", "research", "--without-prerequisites", "The lead read the arm as inapplicable"],
            CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(1, exit);
        Assert.Contains(
            "Only an operator can transition stages without prerequisites",
            error.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(TaskStage.Discovery, state!.Stage);
    }

    [Fact]
    public async Task WaivingStagePrerequisitesIsRefusedWhenTheReasonIsBlank()
    {
        using var root = new TemporaryDirectory();
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);

        // Whitespace rather than an empty string: the option consumes the following argument either
        // way, and a waiver whose reason records nothing is the one the log cannot be read from.
        var exit = await application.RunAsync(
            ["stage", "transition", .. common, "--stage", "research", "--without-prerequisites", "   "],
            CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(1, exit);
        Assert.Contains("a blank waiver records nothing", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(TaskStage.Discovery, state!.Stage);
    }

    [Fact]
    public async Task AnOperatorWaivesTheArmAndTheReasonLandsOnItsOwnEvent()
    {
        using var root = new TemporaryDirectory();
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);

        var exit = await application.RunAsync(
            ["stage", "transition", .. common, "--stage", "research",
             "--without-prerequisites", "Discovery closed with no open claim and the wave is scoped"],
            CancellationToken.None);

        var events = await HistoryAsync(root.Path);
        var waiver = events.Single(item => item.Data is StagePrerequisitesWaived);
        var transition = events.Single(item => item.Data is StageTransitioned);
        var waived = (StagePrerequisitesWaived)waiver.Data;
        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal("Discovery closed with no open claim and the wave is scoped", waived.Reason);
        Assert.Equal(TaskStage.Research, waived.TargetStage);
        // The reason is its own event, and the transition cites it, so a later reader sees which arm
        // was skipped and why rather than only that a transition happened.
        Assert.Equal(waiver.EventId, transition.CausationId);
    }

    [Fact]
    public async Task AWaivedTransitionReplaysThroughAFreshServiceWithItsReasonIntact()
    {
        using var root = new TemporaryDirectory();
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["stage", "transition", .. common, "--stage", "research",
             "--without-prerequisites", "Discovery closed with no open claim and the wave is scoped"],
            CancellationToken.None);

        // A fresh service holds no state, so reading it replays the whole log through the validator.
        // The waiver has to be legal at replay too, and its reason has to survive the round trip.
        var replayed = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        var waived = (StagePrerequisitesWaived)(await HistoryAsync(root.Path))
            .Single(item => item.Data is StagePrerequisitesWaived).Data;

        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal(TaskStage.Research, replayed!.Stage);
        Assert.Equal("Discovery closed with no open claim and the wave is scoped", waived.Reason);
    }

    [Fact]
    public async Task AStageTransitionWithNoWaiverStillRefusesWhenItsArmIsUnsatisfied()
    {
        using var root = new TemporaryDirectory();
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);

        // The same operator and the same transition as the accepted case above, without the waiver.
        // If this passed, the waiver would have become the default path rather than an override.
        var exit = await application.RunAsync(
            ["stage", "transition", .. common, "--stage", "research"], CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(1, exit);
        Assert.Contains(
            "Research requires at least one open claim", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(TaskStage.Discovery, state!.Stage);
    }

    // A move back through the pipeline says something was learned that invalidates work already
    // done, and --reason is where that sentence goes. These four drive it through the CLI because
    // that is the only level at which the option can be shown to exist: an option the parser does
    // not know is refused as a usage error before the kernel ever sees the command, so a rule
    // tested only in the kernel can be complete while the flag it reads is unreachable.
    //
    // The walk is Discovery -> Research -> Discovery. Discovery is the one stage with no arm of its
    // own, so the backward leg is refused for its reason and never for a prerequisite.
    [Fact]
    public async Task ABackwardStageTransitionCarriesItsReasonOntoTheRecordedTransition()
    {
        using var root = new TemporaryDirectory();
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["claim", "add", .. common, "--id", "C1", "--statement", "The arm is satisfiable"],
            CancellationToken.None);
        var forwardExit = await application.RunAsync(
            ["stage", "transition", .. common, "--stage", "research"], CancellationToken.None);

        var backwardExit = await application.RunAsync(
            ["stage", "transition", .. common, "--stage", "discovery",
             "--reason", "The open claim turned out to rest on an assumption nobody had recorded"],
            CancellationToken.None);

        // Read back off disk through a fresh service, so the reason has survived being written and
        // replayed rather than only having been accepted.
        var transitions = (await HistoryAsync(root.Path))
            .Select(item => item.Data)
            .OfType<StageTransitioned>()
            .ToArray();
        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(0, forwardExit);
        Assert.Equal(0, backwardExit);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal(TaskStage.Discovery, state!.Stage);
        Assert.Equal(2, transitions.Length);
        // The forward leg carries none. LedgerJson omits a null field entirely, so this is the half
        // that proves an absent reason deserialises as absent and not as something else.
        Assert.Equal(TaskStage.Research, transitions[0].Current);
        Assert.Null(transitions[0].Reason);
        Assert.Equal(TaskStage.Discovery, transitions[1].Current);
        Assert.Equal(
            "The open claim turned out to rest on an assumption nobody had recorded",
            transitions[1].Reason);
    }

    [Fact]
    public async Task ABackwardStageTransitionIsRefusedWithNoReason()
    {
        using var root = new TemporaryDirectory();
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["claim", "add", .. common, "--id", "C1", "--statement", "The arm is satisfiable"],
            CancellationToken.None);
        await application.RunAsync(
            ["stage", "transition", .. common, "--stage", "research"], CancellationToken.None);

        var exit = await application.RunAsync(
            ["stage", "transition", .. common, "--stage", "discovery"], CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(1, exit);
        Assert.Contains("goes back in the pipeline and needs a reason", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(TaskStage.Research, state!.Stage);
    }

    [Fact]
    public async Task ABackwardStageTransitionIsRefusedWhenTheReasonIsBlank()
    {
        using var root = new TemporaryDirectory();
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["claim", "add", .. common, "--id", "C1", "--statement", "The arm is satisfiable"],
            CancellationToken.None);
        await application.RunAsync(
            ["stage", "transition", .. common, "--stage", "research"], CancellationToken.None);

        // Whitespace rather than an empty string, as with the waiver above: the option consumes the
        // following argument either way, and a reason that records nothing is the one a later reader
        // cannot learn anything from.
        var exit = await application.RunAsync(
            ["stage", "transition", .. common, "--stage", "discovery", "--reason", "   "],
            CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(1, exit);
        Assert.Contains("a blank reason records nothing", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(TaskStage.Research, state!.Stage);
    }

    // The forward half, and the one test that pins the option onto the parser's allow-list. A
    // refusal from the kernel exits 1; an option the command does not declare exits 2 without the
    // kernel running at all. Asserting the code and the absence of the usage wording is what tells
    // "the rule refused it" from "the flag was never wired".
    [Fact]
    public async Task AForwardStageTransitionRefusesAReasonRatherThanIgnoringIt()
    {
        using var root = new TemporaryDirectory();
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["claim", "add", .. common, "--id", "C1", "--statement", "The arm is satisfiable"],
            CancellationToken.None);

        var exit = await application.RunAsync(
            ["stage", "transition", .. common, "--stage", "research",
             "--reason", "Discovery is finished and the wave is scoped"], CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(1, exit);
        Assert.DoesNotContain("Unknown option", error.ToString(), StringComparison.Ordinal);
        Assert.Contains(
            "goes forward and does not take a reason", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(TaskStage.Discovery, state!.Stage);
    }

    private static async Task<IReadOnlyList<LedgerEvent>> HistoryAsync(string root)
    {
        var events = new List<LedgerEvent>();
        await foreach (var @event in Service(root).GetHistoryAsync(new TaskId("T1"), CancellationToken.None))
        {
            events.Add(@event);
        }

        return events;
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

    // The three artifacts filed here are task-wide: a user request names no work item at all, and a
    // prompt contract and an orchestration plan govern the task rather than one item in it. The run
    // that produces them therefore names no work item either. It cannot: a coordinating role is the
    // only role allowed to file these artifacts, and a coordinating role may not hold a run against
    // a work item. Naming one here was refused outright, which is why this helper takes no item id.
    private static async Task<int[]> RecordExecutionArtifactsAsync(
        CliApplication application,
        IReadOnlyList<string> common)
    {
        var arguments = common.ToList();
        var root = common[arguments.IndexOf("--root") + 1];
        var task = common[arguments.IndexOf("--task") + 1];
        var originalStage = (await Service(root).GetStateAsync(new TaskId(task), CancellationToken.None))!.Stage;
        var exits = new List<int>
        {
            await RecordArtifactAsync(
                application, common, "operator", "A-request", "user-request", null, null,
                "The governed request")
        };
        switch (originalStage)
        {
            case TaskStage.Discovery:
                await CliStageFixture.AdvanceAsync(
                    application, root, task, TaskStage.Research, TaskStage.Design);
                break;
            case TaskStage.Ready:
                await CliStageFixture.BackAsync(application, root, TaskStage.Scope, task);
                await CliStageFixture.BackAsync(application, root, TaskStage.Design, task);
                break;
            case TaskStage.Execution:
                await CliStageFixture.BackAsync(application, root, TaskStage.Design, task);
                break;
            case not TaskStage.Design:
                throw new InvalidOperationException(
                    $"Artifact fixture cannot revise planning documents from stage '{originalStage}'.");
        }
        exits.Add(await application.RunAsync(
            ["run", "start", .. common, "--actor", "operator", "--subject", "operator",
             "--run", "R-artifacts", "--provider", "codex",
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
        if (originalStage is TaskStage.Ready or TaskStage.Execution)
        {
            await CliStageFixture.AdvanceAsync(
                application, root, task, TaskStage.Scope, TaskStage.Ready);
        }
        if (originalStage is TaskStage.Execution)
        {
            await CliStageFixture.ToExecutionAsync(application, root, task);
        }
        return [.. exits];
    }

    // The manifest reaches the agent as the CLI wrote it, so it is read back the same way.
    private static readonly JsonSerializerOptions ManifestJson = LedgerJson.CreateOptions();

    // LC3: the owed node had no test at its call site, so acceptance criteria 5 and 6 were
    // unproven — nothing pinned the node being absent on a clean task, and nothing pinned it
    // carrying counts and nothing else. VC1's partial-citation defect lived in exactly that gap.
    [Fact]
    public async Task StatusOmitsTheOwedNodeWhenTheTaskOwesNothing()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var application = new CliApplication(
            output, TextWriter.Null, Service,
            _ => new FixedResultAdapter(AgentRunStatus.Completed), new ContextAssembler());
        await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--title", "Task", "--goal", "Goal"], CancellationToken.None);

        // Every command writes to the same writer, so the setup calls have to be cleared or the
        // parse sees two documents.
        output.GetStringBuilder().Clear();
        await application.RunAsync(["status", "--root", root.Path, "--task", "T1"], CancellationToken.None);

        Assert.Null(JsonNode.Parse(output.ToString())!["owed"]);
    }

    [Fact]
    public async Task StatusReportsOwedCountsAndNoDerivedVerdict()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var application = new CliApplication(
            output, TextWriter.Null, Service,
            _ => new FixedResultAdapter(AgentRunStatus.Completed), new ContextAssembler());
        await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["claim", "add", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--id", "C1", "--statement", "Unearned assumption"], CancellationToken.None);

        output.GetStringBuilder().Clear();
        await application.RunAsync(["status", "--root", root.Path, "--task", "T1"], CancellationToken.None);

        var owed = JsonNode.Parse(output.ToString())!["owed"]!.AsObject();
        Assert.Equal(1, owed["openClaims"]!.GetValue<int>());
        // Counts, and one fact. No score, no colour, no health word, and no field whose value is
        // fixed by the condition under which the node is written. The retrospective debt is a
        // boolean because whether an archived task carries a retrospective is a yes or a no, and a
        // count that can only be zero or one would read as a measure of something.
        // Two facts joined the one: whether a coordinating session is open, because a waiver made
        // with none records origin 'manual' and a coordinator that never opens one cannot be told
        // from the operator; and which stage the task's own records imply, because the stage arms
        // fire only on a transition nothing asks for. Both are facts, neither is a judgement.
        // stageBehindActivity is present here because this fixture records a claim and a work item
        // while sitting in Discovery, which is the drift the field exists to name.
        // workItemsRunByNoWorkingRole is a count and reads zero on this fixture, which holds no work
        // item at all. What it says here is only that the field reaches the output; the value that
        // matters is pinned by StatusCountsAWorkItemWhoseOnlyRunDidNoWorkTheGateAccepts below.
        Assert.Equal(
            new[] { "coordinatorSessionOpen", "lessonsCited", "lessonsRecalled", "openClaims", "openClaimsWithSupportingEvidence", "retrospectiveOwed", "stageBehindActivity", "workItemsAwaitingVerification", "workItemsRunByNoWorkingRole" },
            owed.Select(pair => pair.Key).OrderBy(key => key, StringComparer.Ordinal).ToArray());
    }

    // The value, at the surface an operator reads. TaskDebtTests pins the projection; what no test
    // on the projection can show is that the number survives serialisation and that the task stops
    // reading clear because of it, which is the whole of what this field was added to do.
    //
    // The state is built with a verifier and no worker on purpose. The obvious way to make an item
    // "run by no working role" — give it a coordinating run — is no longer reachable: RunDispatchRules
    // refuses a run against a work item held by an Operator, PlanningLead or ImplementationLead
    // outright. Verifier and CodeReviewer are neither coordinating nor working, so they are the live
    // path, and a verifier is the one of the two that can start with no work behind it. So the field
    // is not a reader of old histories only: this sequence is four ordinary commands, each of which
    // the kernel accepts today, and it ends with an item 'work complete' refuses.
    [Fact]
    public async Task StatusCountsAWorkItemWhoseOnlyRunDidNoWorkTheGateAccepts()
    {
        using var root = new TemporaryDirectory();
        using var scopeRoot = new TemporaryDirectory();
        var area = Directory.CreateDirectory(Path.Combine(scopeRoot.Path, "area")).FullName;
        var output = new StringWriter();
        var error = new StringWriter();
        var application = Create(output, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);
        await application.RunAsync(
            ["actor", "attach", .. common, "--target", "verifier", "--role", "verifier"],
            CancellationToken.None);
        await application.RunAsync(
            ["work", "add", .. common, "--id", "W1", "--title", "Unworked work", "--owner", "operator",
             "--scope", area], CancellationToken.None);
        // A verifier output is refused without a current prompt contract behind it, so the task-wide
        // artifacts are filed first. They are filed by a coordinating run that names no work item,
        // which is the only kind such a role may hold, so they add nothing to either count below.
        await RecordExecutionArtifactsAsync(application, common);
        await CliStageFixture.ToExecutionAsync(application, root.Path);
        await CliStageFixture.ToVerificationAsync(application, root.Path);
        // No worker run precedes this one. The verifier has nothing to read, and the kernel does not
        // refuse it — that is the hole, not an artificial fixture.
        await application.RunAsync(
            ["run", "start", .. common, "--subject", "verifier", "--run", "RV", "--work", "W1",
             "--provider", "claude", "--session", "verifier-session"], CancellationToken.None);
        await RecordArtifactAsync(
            application, ["--root", root.Path, "--task", "T1"], "verifier", "A-RV", "verifier-output",
            "W1", "RV", ArtifactCommands.VerifierBody);
        await application.RunAsync(
            ["run", "complete", .. common, "--run", "RV", "--status", "completed",
             "--session", "verifier-session"], CancellationToken.None);
        Assert.Equal(string.Empty, error.ToString());

        output.GetStringBuilder().Clear();
        await application.RunAsync(["status", "--root", root.Path, "--task", "T1"], CancellationToken.None);

        var owed = JsonNode.Parse(output.ToString())!["owed"]!.AsObject();
        Assert.Equal(1, owed["workItemsRunByNoWorkingRole"]!.GetValue<int>());
        // Zero, and that is the point of the second count rather than a widening of the first: this
        // item has not been worked, so it is not awaiting verification. Before the field existed
        // both counts read zero here and the owed block said nothing about an item the kernel would
        // not complete.
        Assert.Equal(0, owed["workItemsAwaitingVerification"]!.GetValue<int>());

        // The half that makes the number mean something: the gate refuses the same item the debt
        // now names. A projection that disagreed with the gate is what this field was added to fix.
        var completeExit = await application.RunAsync(
            ["work", "complete", .. common, "--id", "W1"], CancellationToken.None);
        Assert.NotEqual(0, completeExit);
        Assert.Contains("no completed run", error.ToString(), StringComparison.Ordinal);
    }

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

    // Returns zero so it can sit in an exits list beside the commands it precedes; the helper
    // throws rather than returning a code when the brief itself is refused.
    private static async Task<int> BriefAsync(string[] common)
    {
        await ContextBrief.BuildAsync(common[1], common[3], common[5]);
        return 0;
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
