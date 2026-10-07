using AILedger.Core.Contracts;
using AILedger.Providers.Adapters;
using AILedger.Providers.Process;

namespace AILedger.Tests.Providers;

public sealed class ProviderStartupTests
{
    [Fact]
    public async Task EarlyChildExitRetainsStderrAndExitDespiteLargeInput()
    {
        var request = ProviderProtocolTests.Request("probe", AgentLaunchMode.New, null) with
        {
            StandardInput = new string('x', 4 * 1024 * 1024),
            Environment = new Dictionary<string, string> { ["TEST_SECRET"] = "startup-secret-value" }
        };
        var result = await new StartupAdapter().RunAsync(request, default);
        Assert.Equal(AgentRunStatus.Failed, result.Status);
        Assert.Equal(37, result.ExitCode);
        Assert.Contains("startup refused", result.StandardError);
        Assert.DoesNotContain("startup-secret-value", result.StandardError);
        Assert.Contains("input", result.Failure, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ProviderProcessFailureKind.InputWrite, result.ProcessFailure!.Kind);
        Assert.True(result.ProcessFailure.ExitedBeforeCleanup);
        Assert.True(result.ProcessFailure.CleanupConfirmed);
        Assert.True(result.ProcessFailure.OutputComplete);
        Assert.Equal(37, result.ProcessFailure.ObservedExitCode);
        Assert.False(result.EndedAtTheLaunchTimeout);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ClosedStdinWithLiveChildKeepsCancellationAndTimeoutDistinct(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        var invocation = new ProcessInvocation("/bin/sh", Path.GetTempPath(),
            ["-c", "exec 0<&-; echo input-closed >&2; exec /bin/sleep 30"], new string('x', 4 * 1024 * 1024),
            new Dictionary<string, string>(), cancel ? TimeSpan.FromSeconds(10) : TimeSpan.FromMilliseconds(250));
        var errors = new List<string>();
        var result = await new SystemProcessRunner().RunAsync(invocation, (_, _) => ValueTask.CompletedTask,
            (line, _) => { errors.Add(line); if (cancel) cancellation.CancelAfter(50); return ValueTask.CompletedTask; }, null, cancellation.Token);
        Assert.Contains("input-closed", errors);
        Assert.NotNull(result.Failure);
        Assert.False(result.Failure.ExitedBeforeCleanup);
        Assert.True(result.Failure.CleanupConfirmed);
        Assert.False(result.Failure.OutputComplete);
        Assert.Equal(cancel, result.Failure.CallerCancelled);
        Assert.Equal(!cancel, result.Failure.LaunchTimedOut);
    }

    [Fact]
    public async Task ChildClosingInputWithoutExitingHasBoundedDiagnosticWaitAndCleanup()
    {
        var invocation = new ProcessInvocation("/bin/sh", Path.GetTempPath(),
            ["-c", "exec 0<&-; echo input-closed >&2; exec /bin/sleep 30"], new string('x', 4 * 1024 * 1024),
            new Dictionary<string, string>(), TimeSpan.FromSeconds(20));
        var result = await new SystemProcessRunner().RunAsync(invocation, (_, _) => ValueTask.CompletedTask,
            (_, _) => ValueTask.CompletedTask, null, default);
        Assert.True(result.EndedAt - result.StartedAt < TimeSpan.FromSeconds(10));
        Assert.False(result.Failure!.ExitedBeforeCleanup);
        Assert.True(result.Failure.CleanupConfirmed);
        Assert.False(result.Failure.LaunchTimedOut);
    }

    [Fact]
    public async Task InputFailurePreservesAConcurrentOutputLimitFailureSeparately()
    {
        var tally = new TruncatedLineTally();
        var invocation = new ProcessInvocation("/bin/sh", Path.GetTempPath(),
            ["-c", "exec 0<&-; exec /usr/bin/head -c 9437184 /dev/zero >&2"], new string('x', 4 * 1024 * 1024),
            new Dictionary<string, string>(), TimeSpan.FromSeconds(20));
        var result = await new SystemProcessRunner().RunAsync(invocation, (_, _) => ValueTask.CompletedTask,
            (_, _) => ValueTask.CompletedTask, tally, default);
        Assert.Equal(ProviderProcessFailureKind.InputWrite, result.Failure!.Kind);
        Assert.Contains("pipe", result.Failure.Diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("output exceeded", result.Failure.DrainDiagnostic);
        Assert.True(result.Failure.CleanupConfirmed);
        Assert.False(result.Failure.OutputComplete);
        Assert.Equal(1, result.TruncatedLines);
    }

    private sealed class StartupAdapter() : AgentAdapterBase(new StartupRunner())
    {
        public override string Provider => "probe";
        protected override IReadOnlyList<CapabilityProbe> CapabilityProbes => [];
        protected override IReadOnlyList<string> BuildArguments(AgentLaunchRequest request, ref string? sessionId) =>
            ["-c", "echo startup refused: $TEST_SECRET >&2; exit 37"];
        protected override ProviderEvent ParseEvent(long sequence, string json) => ProviderProtocol.ParseCodex(sequence, json);
        protected override string? ReadFinalOutput(ProviderEvent providerEvent) => null;
    }

    private sealed class StartupRunner : IProcessRunner
    {
        public async Task<ProcessExit> RunAsync(ProcessInvocation invocation,
            Func<string, CancellationToken, ValueTask> stdout, Func<string, CancellationToken, ValueTask> stderr,
            TruncatedLineTally? tally, CancellationToken token)
        {
            if (invocation.Arguments is ["--version"])
            {
                await stdout("controlled-provider", token);
                return new(0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            }
            return await new SystemProcessRunner().RunAsync(invocation with { ExecutablePath = "/bin/sh" }, stdout, stderr, tally, token);
        }
    }
}
