using System.Text.Json.Nodes;
using AILedger.Cli;
using AILedger.Cli.Tasks;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.TaskClosure;
using AILedger.Tests.Core;
using AILedger.Tests.Findings;
using AILedger.Storage;

namespace AILedger.Tests.Cli;

public sealed class HumanTaskClosureCliTests
{
    [Theory]
    [InlineData(false, "Reason", false)]
    [InlineData(true, " ", false)]
    [InlineData(true, "Reason", true)]
    public async Task MissingIntentOrReasonAndDeniedConfirmationLeaveTaskUnchanged(bool flag, string reason, bool denied)
    {
        using var f = new FindingsFixture();
        await f.OpenAsync();
        var initial = await File.ReadAllTextAsync(f.EventsPath);
        var confirmation = new Confirmation { Deny = denied };
        var error = new StringWriter();
        var app = App(f, TextWriter.Null, error, confirmation);
        var arguments = Args(f, reason).ToList();
        if (!flag) arguments.Remove("--human-override");
        Assert.NotEqual(0, await app.RunAsync(arguments, default));
        Assert.Equal(initial, await File.ReadAllTextAsync(f.EventsPath));
        Assert.Equal(denied ? 1 : 0, confirmation.Calls);
    }

    [Fact]
    public async Task ConfirmedClosureIsReportedAsSuccessAndStatusRetainsUnresolvedFindings()
    {
        using var f = new FindingsFixture();
        await f.OpenAsync();
        await f.ExecuteAsync(new AddClaimCommand(f.Actor, null, "claim", new("C1"), "Unknown", null));
        var confirmation = new Confirmation();
        var output = new StringWriter();
        var app = App(f, output, TextWriter.Null, confirmation);
        Assert.Equal(0, await app.RunAsync(Args(f, "End the detour"), default));
        var result = JsonNode.Parse(output.ToString())!;
        Assert.Equal("succeeded_by_human_override", result["humanClosure"]!["outcome"]!.GetValue<string>());
        Assert.Equal("End the detour", result["humanClosure"]!["reason"]!.GetValue<string>());
        Assert.Equal(1, confirmation.Calls);
        output.GetStringBuilder().Clear();
        Assert.Equal(0, await app.RunAsync(["status", "--root", f.Root, "--task", f.TaskId.Value], default));
        var status = JsonNode.Parse(output.ToString())!;
        Assert.Null(status["owed"]);
        Assert.NotNull(status["unresolvedAtHumanClosure"]);
        Assert.NotNull(status["claims"]!["C1"]);
        Assert.NotEqual(0, await app.RunAsync(Args(f, "Close again"), default));
        Assert.Equal(1, confirmation.Calls);
    }

    [Fact]
    public async Task AgentWritesDuringHumanPromptCannotPreventWholeTaskClosure()
    {
        using var f = new FindingsFixture();
        await f.OpenAsync();
        var reviewed = await f.StateAsync();
        var confirmation = new Confirmation
        {
            DuringPrompt = () => f.ExecuteAsync(new AddClaimCommand(f.Actor, null, "concurrent", new("C1"), "New fact", null))
        };
        Assert.Equal(0, await App(f, TextWriter.Null, TextWriter.Null, confirmation).RunAsync(Args(f, "Reason"), default));
        var closed = await f.StateAsync();
        Assert.NotNull(closed.HumanClosure);
        Assert.Equal(reviewed.Version, closed.HumanClosure.ReviewedVersion);
        Assert.Equal(reviewed.Version + 1, closed.HumanClosure.BasisVersion);
        Assert.Single(closed.Claims);
    }

    [Fact]
    public async Task NonOperatorCannotEvenRequestConfirmation()
    {
        using var f = new FindingsFixture();
        await f.OpenAsync();
        await f.ExecuteAsync(new AssignRoleCommand(f.Actor, null, "assign", new("worker"), RoleKind.Worker, [Capability.AddClaim]));
        var confirmation = new Confirmation();
        var args = Args(f, "Reason").Select(value => value == "operator" ? "worker" : value).ToArray();
        Assert.NotEqual(0, await App(f, TextWriter.Null, TextWriter.Null, confirmation).RunAsync(args, default));
        Assert.Equal(0, confirmation.Calls);
    }

    [Fact]
    public async Task ClosureCancelsExecutionAndPreventsLaterProviderStart()
    {
        using var f = new FindingsFixture();
        await f.OpenAsync();
        await using var monitor = await HumanClosureMonitor.StartAsync(f.Service(), f.TaskId, default);
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = monitor.Token.Register(() => observed.SetResult());
        await f.ExecuteAsync(HumanTaskClosureTests.Confirm(await f.StateAsync()));
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(monitor.Token.IsCancellationRequested);
        await Assert.ThrowsAsync<GovernanceException>(() => HumanClosureMonitor.StartAsync(f.Service(), f.TaskId, default));
    }

    [Fact]
    public async Task CallerCancellationInterruptsMonitorWaitingForTaskLock()
    {
        using var f = new FindingsFixture();
        await f.OpenAsync();
        using var cancellation = new CancellationTokenSource();
        var monitor = await HumanClosureMonitor.StartAsync(f.Service(), f.TaskId, cancellation.Token);
        await using var held = await new TaskMutationLock().AcquireAsync(
            Path.Combine(f.Directory, new TaskWorkspaceLayout().LockFileName), default);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            await cancellation.CancelAsync();
            Assert.True(monitor.Token.IsCancellationRequested);
        }
        finally { await monitor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthoritativeReadFailureStillCancelsExecution(bool governanceFailure)
    {
        using var f = new FindingsFixture();
        await f.OpenAsync();
        var service = new FailingReadService(await f.StateAsync(), governanceFailure);
        await using var monitor = await HumanClosureMonitor.StartAsync(service, f.TaskId, default);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = monitor.Token.Register(() => cancelled.TrySetResult());
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(monitor.Token.IsCancellationRequested);
    }

    private sealed class FailingReadService(GovernedTaskState state, bool governanceFailure) : IGovernedTaskService
    {
        private bool _started;
        public Task<GovernedTaskState?> GetStateAsync(TaskId taskId, CancellationToken cancellationToken)
        {
            if (_started) throw governanceFailure ? new GovernanceException("History lost") : new IOException("Read failed");
            _started = true;
            return Task.FromResult<GovernedTaskState?>(state);
        }
        public Task<CommandOutcome> ExecuteAsync(TaskId taskId, LedgerCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<LedgerEvent> GetHistoryAsync(TaskId taskId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static string[] Args(FindingsFixture f, string reason) =>
        ["task", "close", "--root", f.Root, "--task", f.TaskId.Value, "--actor", "operator", "--human-override", "--reason", reason];

    private static CliApplication App(FindingsFixture f, TextWriter output, TextWriter error, Confirmation confirmation) =>
        new(output, error, _ => f.Service(), _ => throw new InvalidOperationException("No provider may be launched."), new ContextAssembler())
        { HumanClosureConfirmation = confirmation };

    private sealed class Confirmation : IHumanClosureConfirmation
    {
        internal int Calls { get; private set; }
        internal bool Deny { get; init; }
        internal Func<Task>? DuringPrompt { get; init; }
        public async Task<HumanClosureAdmission> ConfirmAsync(GovernedTaskState state, ActorId actor, string reason,
            string ledgerRoot, CancellationToken token)
        {
            Calls++;
            if (Deny) throw new GovernanceException("Human declined.");
            if (DuringPrompt is not null) await DuringPrompt();
            return HumanTaskClosureTests.Confirm(state, reason).Admission!;
        }
    }
}
