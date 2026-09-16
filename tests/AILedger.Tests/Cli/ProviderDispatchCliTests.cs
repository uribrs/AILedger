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
public sealed class ProviderDispatchCliTests
{
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

    [Fact]
    public async Task ATaskWideCodeReviewerLaunchIsRefusedBeforeAnyProviderProcessStarts()
    {
        using var root = new TemporaryDirectory();
        using var providerRoot = new TemporaryDirectory();
        var solution = Directory.CreateDirectory(Path.Combine(providerRoot.Path, "solution")).FullName;
        Directory.CreateDirectory(Path.Combine(solution, ".git"));
        var factoryCalls = 0;
        var error = new StringWriter();
        var application = new CliApplication(
            TextWriter.Null, error, Service,
            _ =>
            {
                factoryCalls++;
                throw new InvalidOperationException("A refused reviewer launch must resolve no adapter.");
            },
            new ContextAssembler());
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await ContextBrief.BuildAsync(root.Path, "T1");
        await application.RunAsync(
            ["actor", "attach", .. common, "--target", "reviewer", "--role", "code-reviewer"],
            CancellationToken.None);

        var exit = await application.RunAsync(
            ["provider", "launch", .. common, "--subject", "reviewer", "--run", "R1",
             "--provider", "codex", "--executable", "/usr/bin/true",
             "--working-directory", solution, "--cognitive-root", FindCognitiveRoot()],
            CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(1, exit);
        Assert.Equal(0, factoryCalls);
        Assert.DoesNotContain(new RunId("R1"), state!.Runs.Keys);
        Assert.Contains(nameof(RoleKind.CodeReviewer), error.ToString(), StringComparison.Ordinal);
        Assert.Contains("work item", error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Single(RefusalJournal(root.Path));
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

}
