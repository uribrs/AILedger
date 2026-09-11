using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Providers.Adapters;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Cli;

// The other half of RunDispatchRecordTests: that file pins what the kernel does with the limit and
// the reason, and this one pins that the launcher supplies them. Both halves are needed for the same
// reason IC4 records — six cost fields once shipped with nothing populating them, so every real run
// recorded null while the kernel's own tests passed.
//
// These run through the production launch path, because that is the only place the two fields are
// filled: the launcher is what holds the request it built and the result the adapter returned.
[Collection(StandardInput.Collection)]
public sealed class RunDispatchLaunchTests
{
    // The default limit, recorded without anybody asking for it. A run that records no limit cannot
    // be told from one that ended inside a limit nobody set, so the ordinary launch has to carry it.
    [Fact]
    public async Task ALaunchRecordsTheLimitItWasGiven()
    {
        using var root = new TemporaryDirectory();

        var run = await LaunchAsync(root.Path, new ResultAdapter());

        Assert.Equal(1800, run.LaunchTimeoutSeconds);
    }

    [Fact]
    public async Task ALaunchRecordsTheLimitTheOperatorNamed()
    {
        using var root = new TemporaryDirectory();

        var run = await LaunchAsync(root.Path, new ResultAdapter(), timeoutSeconds: "60");

        Assert.Equal(60, run.LaunchTimeoutSeconds);
    }

    // A run that completed on its own terms reports no reason. That absence is what makes a non-null
    // reason readable as a failure at all, and it is what keeps a clean verification out of measure
    // 12 whatever the agent concluded.
    [Fact]
    public async Task ALaunchThatCompletedRecordsNoReason()
    {
        using var root = new TemporaryDirectory();

        var run = await LaunchAsync(root.Path, new ResultAdapter());

        Assert.Equal(AgentRunStatus.Completed, run.Status);
        Assert.Null(run.TerminalFailureReason);
    }

    // The provider's own words, relayed unaltered. The launcher reads them off the same result object
    // the sidecar beside the run is written from, so the run record and its sidecar make one statement
    // rather than two that can disagree — which is the disagreement C8 measured on run LR4.
    [Fact]
    public async Task ALaunchWhoseProviderReportedAFailureRecordsThatReasonVerbatim()
    {
        using var root = new TemporaryDirectory();
        var adapter = new ResultAdapter
        {
            Status = AgentRunStatus.Cancelled,
            Failure = "Provider run timed out."
        };

        var run = await LaunchAsync(root.Path, adapter, expectedExit: null);

        Assert.Equal("Provider run timed out.", run.TerminalFailureReason);
        Assert.Equal(1800, run.LaunchTimeoutSeconds);
    }

    // C8's own shape, at the moment the record is written. The adapter completed with no failure and
    // the launcher closed the run as failed anyway, because a verifier that files no output artifact
    // is refused — so the status says failed while the provider reported nothing. The reason stays
    // absent, which is what stops measure 11 counting a run whose agent did its job.
    [Fact]
    public async Task ARunTheLauncherReclassifiedAsFailedRecordsNoProviderReason()
    {
        using var root = new TemporaryDirectory();
        var adapter = new ResultAdapter();
        var application = Application(adapter);
        await OpenTaskAsync(application, root.Path);
        // The work item's scope has to be a real, fully qualified directory outside the ledger root,
        // and the run's working directory has to fall inside it — a scoped launch is refused
        // otherwise, before any run exists.
        var scope = WorkingDirectory();
        await StageVerifierWorkAsync(application, root.Path, scope);

        var exit = await application.RunAsync(
            ["provider", "launch", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--subject", "verifier", "--run", "RV", "--work", "W1",
             "--provider", adapter.Provider, "--executable", "/usr/bin/true",
             "--working-directory", scope, "--cognitive-root", ContextBrief.CognitiveRoot()],
            CancellationToken.None);

        Assert.NotEqual(0, exit);
        var run = await RunAsync(root.Path, "RV");
        Assert.Equal(AgentRunStatus.Failed, run.Status);
        Assert.Null(run.TerminalFailureReason);
        Assert.Equal(1800, run.LaunchTimeoutSeconds);
    }

    // A launch that died before the adapter returned has no provider reason to relay, so what ended
    // it is the launcher's own fault and that is what is recorded. A run that failed before its
    // provider spoke is exactly the one a reader cannot otherwise account for.
    [Fact]
    public async Task ALaunchWhoseAdapterThrewRecordsTheLauncherSOwnReason()
    {
        using var root = new TemporaryDirectory();
        var adapter = new ResultAdapter { Throw = "The provider binary vanished mid-launch." };

        var run = await LaunchAsync(root.Path, adapter, expectedExit: null);

        Assert.Equal(AgentRunStatus.Failed, run.Status);
        Assert.Equal("The provider binary vanished mid-launch.", run.TerminalFailureReason);
        // The limit was parsed before the adapter was called, so it is on the record even though the
        // adapter never returned.
        Assert.Equal(1800, run.LaunchTimeoutSeconds);
    }

    // A limit this process cannot parse leaves the field absent, which says no limit was given rather
    // than letting a default stand in for one. The run still closes, which is the invariant that
    // matters more: a launch that throws must never leave a run active.
    [Fact]
    public async Task ALaunchWhoseLimitCouldNotBeParsedRecordsNoLimitAndStillClosesItsRun()
    {
        using var root = new TemporaryDirectory();

        var run = await LaunchAsync(
            root.Path, new ResultAdapter(), expectedExit: null, timeoutSeconds: "not-a-number");

        Assert.NotEqual(AgentRunStatus.Active, run.Status);
        Assert.Null(run.LaunchTimeoutSeconds);
        Assert.NotNull(run.TerminalFailureReason);
    }

    // The relay of what ended the run, and the four tests below are one set, one per closing path the
    // launcher has. The observation exists only at the moment of termination: the process runner
    // reports its own expired deadline as a distinct exception, the adapter turns that into
    // AgentRunResult.EndedAtTheLaunchTimeout, and this launcher is the only thing that can put it on
    // the run record. It shipped on both ends with nothing carrying it between them, so every real
    // launch recorded null and measure 11 left timeouts the runner had actually seen unjudged
    // (WC2, WE20) — which is IC4's failure again and why the relay is pinned at the launch path
    // rather than only at the kernel.
    //
    // The first: the runner ended the provider at the deadline this launch set, and said so.
    [Fact]
    public async Task ALaunchTheRunnerEndedAtItsDeadlineRecordsThatObservation()
    {
        using var root = new TemporaryDirectory();
        // The shape AgentAdapterBase produces for its ProviderProcessTimeoutException arm: cancelled,
        // with the provider's own words beside the observation.
        var adapter = new ResultAdapter
        {
            Status = AgentRunStatus.Cancelled,
            Failure = "Provider run timed out.",
            EndedAtTheLaunchTimeout = true
        };

        var run = await LaunchAsync(root.Path, adapter, expectedExit: null, timeoutSeconds: "60");

        Assert.True(run.EndedAtTheLaunchTimeout);
        Assert.Equal(60, run.LaunchTimeoutSeconds);
    }

    // The second, and the one the contract asks for by name: an ordinary run driven to completion
    // through the production path records that it did not end at its deadline. False here is a
    // statement and not a default — the launcher held a result and read it — and it is what lets a
    // reader tell a run that ended inside its limit from one nobody watched.
    [Fact]
    public async Task ALaunchThatCompletedRecordsThatItDidNotEndAtItsDeadline()
    {
        using var root = new TemporaryDirectory();

        var run = await LaunchAsync(root.Path, new ResultAdapter());

        Assert.Equal(AgentRunStatus.Completed, run.Status);
        Assert.False(run.EndedAtTheLaunchTimeout);
    }

    // The third: no result reached the launcher, so nobody observed the termination and the record
    // says so by holding nothing. Absent rather than false, because false would be the launcher
    // stating it watched this run end some other way, and it watched nothing.
    [Fact]
    public async Task ALaunchWhoseAdapterNeverReturnedRecordsNoObservation()
    {
        using var root = new TemporaryDirectory();
        var adapter = new ResultAdapter { Throw = "The provider binary vanished mid-launch." };

        var run = await LaunchAsync(root.Path, adapter, expectedExit: null);

        Assert.Null(run.EndedAtTheLaunchTimeout);
    }

    // The fourth path, which is the launcher closing a run as failed on its own decision after the
    // adapter completed. The observation is the adapter's and survives that reclassification
    // untouched: what ended the provider is a fact somebody watched, while the failed status here is
    // this process's ruling about a missing artifact. Overwriting the one with the other would report
    // a termination nobody saw — the same separation C8 keeps between the status and the reason.
    [Fact]
    public async Task ARunTheLauncherReclassifiedAsFailedKeepsTheAdapterSObservation()
    {
        using var root = new TemporaryDirectory();
        var adapter = new ResultAdapter();
        var application = Application(adapter);
        await OpenTaskAsync(application, root.Path);
        var scope = WorkingDirectory();
        await StageVerifierWorkAsync(application, root.Path, scope);

        var exit = await application.RunAsync(
            ["provider", "launch", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--subject", "verifier", "--run", "RV", "--work", "W1",
             "--provider", adapter.Provider, "--executable", "/usr/bin/true",
             "--working-directory", scope, "--cognitive-root", ContextBrief.CognitiveRoot()],
            CancellationToken.None);

        Assert.NotEqual(0, exit);
        var run = await RunAsync(root.Path, "RV");
        Assert.Equal(AgentRunStatus.Failed, run.Status);
        Assert.False(run.EndedAtTheLaunchTimeout);
    }

    // MC1: the causal link needs no new field and no new option. --cause is already accepted on a
    // launch and already reaches the event, and ME2 is that no dispatch in this ledger has ever
    // passed it. This is the launch path asserting the link end to end.
    [Fact]
    public async Task ALaunchNamingItsCauseRecordsItOnRunStarted()
    {
        using var root = new TemporaryDirectory();
        var adapter = new ResultAdapter();
        var application = Application(adapter);
        await OpenTaskAsync(application, root.Path);
        var service = Service(root.Path);
        var claim = await service.ExecuteAsync(
            new TaskId("T1"),
            new AddClaimCommand(
                new ActorId("operator"), null, "c-claim", new ClaimId("C1"), "The premise", null),
            CancellationToken.None);
        var cause = claim.Events[^1].EventId;

        await application.RunAsync(
            ["provider", "launch", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--run", "R1", "--provider", adapter.Provider, "--executable", "/usr/bin/true",
             "--cause", cause.Value,
             "--working-directory", WorkingDirectory(), "--cognitive-root", ContextBrief.CognitiveRoot()],
            CancellationToken.None);

        var history = new List<LedgerEvent>();
        await foreach (var @event in Service(root.Path)
                           .GetHistoryAsync(new TaskId("T1"), CancellationToken.None))
        {
            history.Add(@event);
        }

        var started = Assert.Single(history, @event => @event.Data is RunStarted);
        Assert.Equal(cause, started.CausationId);
    }

    private static async Task<AgentRun> LaunchAsync(
        string root,
        ResultAdapter adapter,
        int? expectedExit = 0,
        string? timeoutSeconds = null)
    {
        var application = Application(adapter);
        await OpenTaskAsync(application, root);
        string[] command =
        [
            "provider", "launch", "--root", root, "--task", "T1", "--actor", "operator",
            "--run", "R1", "--provider", adapter.Provider, "--executable", "/usr/bin/true",
            "--working-directory", WorkingDirectory(), "--cognitive-root", ContextBrief.CognitiveRoot(),
            .. timeoutSeconds is null ? Array.Empty<string>() : ["--timeout-seconds", timeoutSeconds]
        ];
        var exit = await application.RunAsync(command, CancellationToken.None);
        if (expectedExit is { } expected)
        {
            Assert.Equal(expected, exit);
        }

        return await RunAsync(root, "R1");
    }

    private static async Task<AgentRun> RunAsync(string root, string runId)
    {
        var state = await Service(root).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        return state!.Runs[new RunId(runId)];
    }

    private static async Task OpenTaskAsync(CliApplication application, string root)
    {
        await application.RunAsync(
            ["task", "open", "--root", root, "--task", "T1", "--actor", "operator",
             "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        // A provider launch is refused until the dispatching actor has been briefed.
        await ContextBrief.BuildAsync(root, "T1");
    }

    // A verifier subject and a work item for it to run against, which is what makes the launcher's
    // own reclassification reachable: completing a verifier run requires its VerifierOutput artifact,
    // and this launch files none.
    private static async Task StageVerifierWorkAsync(
        CliApplication application,
        string root,
        string scope)
    {
        string[] common = ["--root", root, "--task", "T1", "--actor", "operator"];
        Assert.Equal(0, await application.RunAsync(
            ["actor", "attach", .. common, "--target", "verifier", "--role", "verifier"],
            CancellationToken.None));
        Assert.Equal(0, await application.RunAsync(
            ["work", "add", .. common, "--id", "W1", "--title", "Work", "--scope", scope,
             "--cognitive-root", ContextBrief.CognitiveRoot()],
            CancellationToken.None));
    }

    private static CliApplication Application(ResultAdapter adapter) => new(
        TextWriter.Null, TextWriter.Null, Service, _ => adapter, new ContextAssembler());

    private static IGovernedTaskService Service(string root)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(root, new CommandHandler(reducer, new AuthorizationPolicy()), reducer);
    }

    // Outside the ledger root, which the launcher requires of a provider working directory.
    private static string WorkingDirectory() =>
        Directory.CreateDirectory(Path.Combine(
                Path.GetTempPath(), "ailedger-run-dispatch", Guid.NewGuid().ToString("N")))
            .FullName;

    // An adapter that returns exactly the result a test asks for, including its Failure — which is
    // the field under test here and the one the production doubles in this suite hardcode to null.
    private sealed class ResultAdapter : IAgentAdapter
    {
        public string Provider => "claude";

        public AgentRunStatus Status { get; init; } = AgentRunStatus.Completed;

        public string? Failure { get; init; }

        // The launch path that has no result at all, which is the one the launcher's own reason is
        // recorded on.
        public string? Throw { get; init; }

        // What the runner observed at the moment it ended the process, which the production adapters
        // set true only from a runner that named its own expired deadline.
        public bool EndedAtTheLaunchTimeout { get; init; }

        public Task<string> ProbeVersionAsync(string executablePath, CancellationToken cancellationToken) =>
            Task.FromResult("test");

        public Task<AgentRunResult> RunAsync(AgentLaunchRequest request, CancellationToken cancellationToken)
        {
            if (Throw is { } message)
            {
                throw new AgentAdapterException(message);
            }

            return Task.FromResult(new AgentRunResult(
                request.RunId, Provider, "session-1", Status,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(1),
                Failure is null ? 0 : 1, null, [], string.Empty, "test", [], false, Failure,
                EndedAtTheLaunchTimeout: EndedAtTheLaunchTimeout));
        }
    }
}
