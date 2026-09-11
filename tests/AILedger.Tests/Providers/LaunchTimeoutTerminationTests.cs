using AILedger.Core.Contracts;
using AILedger.Providers.Adapters;
using AILedger.Providers.Process;
using AILedger.Tests.Support;

namespace AILedger.Tests.Providers;

// Where the fact measure 11 attributes on is established, and the only place it can be.
//
// It used to be inferred downstream: the projection compared the run's recorded elapsed time against
// the limit the launch was given, and called anything at or over the limit a timeout. That interval
// is the kernel's, run.started to run.completed, and it brackets manifest construction before the
// adapter is called and result persistence after it returns — so it is strictly wider than the
// provider's own run, and a provider that failed on its own terms just short of the limit crossed it
// on the kernel's clock. The coordinator was then scored for a decision the record did not establish
// (UC2, UE6).
//
// So the condition is observed where it happens instead. The runner holds the deadline and knows
// which cancellation source fired; the adapter carries what the runner said out on its result; and
// nothing between them reads a clock. These tests pin each link of that chain, and the last one pins
// the case that matters most: a cancellation nobody attributed stays unattributed.
public sealed class LaunchTimeoutTerminationTests
{
    // The runner's half, against a real process that outlives its deadline.
    [Fact]
    public async Task TheRunnerReportsItsOwnExpiredDeadlineAsATimeout()
    {
        if (!File.Exists("/bin/sleep"))
        {
            return;
        }

        var invocation = Sleeping(TimeSpan.FromSeconds(1));

        var exception = await Assert.ThrowsAsync<ProviderProcessTimeoutException>(
            () => new SystemProcessRunner().RunAsync(
                invocation,
                static (_, _) => ValueTask.CompletedTask,
                static (_, _) => ValueTask.CompletedTask,
                null,
                CancellationToken.None));

        Assert.Equal(invocation.Timeout, exception.Timeout);
    }

    // The other half of the runner's rule, and the reason the type is not simply "cancelled": a caller
    // that asked this to stop got what it asked for, and calling that a timeout would attribute to the
    // launch's limit a termination the limit did not cause.
    [Fact]
    public async Task TheRunnerDoesNotCallACallerSCancellationATimeout()
    {
        if (!File.Exists("/bin/sleep"))
        {
            return;
        }

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new SystemProcessRunner().RunAsync(
                // A deadline far beyond the run, so the only thing that can end it is the caller.
                Sleeping(TimeSpan.FromMinutes(5)),
                static (_, _) => ValueTask.CompletedTask,
                static (_, _) => ValueTask.CompletedTask,
                null,
                cancellation.Token));

        Assert.IsNotType<ProviderProcessTimeoutException>(exception);
    }

    // The adapter's half: what the runner established leaves on the result, structurally, beside the
    // text. The text alone is what the old rule could not use — a measure that graded the wording
    // would break the first time a provider reworded it, which is why the flag exists.
    [Fact]
    public async Task AnAdapterWhoseRunnerTimedOutRecordsThatTheRunEndedAtItsLaunchTimeout()
    {
        var result = await RunCodexAsync(_ => throw new ProviderProcessTimeoutException(TimeSpan.FromSeconds(30)));

        Assert.True(result.EndedAtTheLaunchTimeout);
        Assert.Equal(AgentRunStatus.Cancelled, result.Status);
        Assert.Equal("Provider run timed out.", result.Failure);
    }

    // The case the whole change exists for. This provider failed on its own terms, and the record says
    // so rather than leaving the reader to weigh a duration against a limit.
    [Fact]
    public async Task AnAdapterWhoseProviderFailedOnItsOwnTermsRecordsNoLaunchTimeout()
    {
        var result = await RunCodexAsync(_ => new ScriptedProcessResult(1, [], ["provider fell over"]));

        Assert.False(result.EndedAtTheLaunchTimeout);
        Assert.Equal(AgentRunStatus.Failed, result.Status);
    }

    [Fact]
    public async Task AnAdapterWhoseRunCompletedRecordsNoLaunchTimeout()
    {
        var result = await RunCodexAsync(_ => new ScriptedProcessResult(0,
            ["{\"type\":\"thread.started\",\"thread_id\":\"session-1\"}",
             "{\"type\":\"turn.completed\"}"],
            []));

        Assert.False(result.EndedAtTheLaunchTimeout);
        Assert.Equal(AgentRunStatus.Completed, result.Status);
    }

    [Fact]
    public async Task AnAdapterCancelledByItsCallerRecordsNoLaunchTimeout()
    {
        using var cancellation = new CancellationTokenSource();

        var result = await RunCodexAsync(
            _ =>
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            },
            cancellation.Token);

        Assert.False(result.EndedAtTheLaunchTimeout);
        Assert.Equal("Provider run was cancelled.", result.Failure);
    }

    // The deliberate gap, stated as a test so that nobody closes it by accident. A runner that cancels
    // without naming its own deadline gets the likeliest account of what happened — the wording is
    // kept, because a reader is better served by it than by nothing — and the flag stays false,
    // because a likely account is not an observation. Measure 11 then leaves that row unjudged, which
    // is the correct answer and the one an inference would have replaced with a plausible verdict.
    [Fact]
    public async Task AnAdapterCancelledByARunnerThatDidNotNameItsDeadlineRecordsNoLaunchTimeout()
    {
        var result = await RunCodexAsync(_ => throw new OperationCanceledException());

        Assert.Equal("Provider run timed out.", result.Failure);
        Assert.False(result.EndedAtTheLaunchTimeout);
    }

    private static ProcessInvocation Sleeping(TimeSpan timeout) => new(
        "/bin/sleep",
        Path.GetTempPath(),
        ["30"],
        string.Empty,
        new Dictionary<string, string>(),
        timeout);

    // The two probes the adapter runs before the launch, then the scripted launch itself.
    private static async Task<AgentRunResult> RunCodexAsync(
        Func<ProcessInvocation, ScriptedProcessResult> run,
        CancellationToken cancellationToken = default)
    {
        var runner = new ScriptedProcessRunner()
            .Enqueue(0, ["codex-cli 1.2.3"])
            .Enqueue(0, ["--strict-config --sandbox --cd --add-dir --output-schema --json"])
            .Enqueue(0, ["SESSION_ID --json"])
            .Enqueue(run);

        return await new CodexAgentAdapter(runner).RunAsync(
            ProviderProtocolTests.Request("codex", AgentLaunchMode.Resume, "session-1"),
            cancellationToken);
    }
}
