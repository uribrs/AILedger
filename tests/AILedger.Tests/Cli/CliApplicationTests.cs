using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;
using System.Text.Json;

namespace AILedger.Tests.Cli;

public sealed class CliApplicationTests
{
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

        var exit = await application.RunAsync(
            ["work", "add", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--id", "W1", "--title", "Work", "--scope", firstScope, "--scope", secondScope],
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

    private static CliApplication Create(TextWriter output, TextWriter error) => new(
        output,
        error,
        Service,
        _ => throw new InvalidOperationException("Provider adapter is not used by this test."),
        new ContextAssembler());

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
