using AILedger.Core.Contracts;
using AILedger.Providers.Process;
using System.ComponentModel;

namespace AILedger.Tests.Providers;

public sealed class SystemProcessRunnerTests
{
    [Fact]
    public async Task RealProcessReceivesOnlyAllowlistedAmbientAndExplicitEnvironment()
    {
        if (!File.Exists("/usr/bin/env"))
        {
            return;
        }

        var ambientName = $"AILEDGER_TEST_SECRET_{Guid.NewGuid():N}";
        var ambientValue = $"ambient-{Guid.NewGuid():N}";
        var lines = new List<string>();
        Environment.SetEnvironmentVariable(ambientName, ambientValue);
        try
        {
            var invocation = new ProcessInvocation(
                "/usr/bin/env",
                Path.GetTempPath(),
                [],
                string.Empty,
                new Dictionary<string, string> { ["AILEDGER_EXPLICIT"] = "explicit-value" },
                TimeSpan.FromSeconds(10));

            await new SystemProcessRunner().RunAsync(
                invocation,
                (line, _) =>
                {
                    lines.Add(line);
                    return ValueTask.CompletedTask;
                },
                static (_, _) => ValueTask.CompletedTask,
                CancellationToken.None);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ambientName, null);
        }

        Assert.DoesNotContain(lines, line => line.StartsWith($"{ambientName}=", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains(ambientValue, StringComparison.Ordinal));
        Assert.Contains("AILEDGER_EXPLICIT=explicit-value", lines);
        if (Environment.GetEnvironmentVariable("PATH") is not null)
        {
            Assert.Contains(lines, line => line.StartsWith("PATH=", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task RealProcessOutputLimitTerminatesUnboundedProducer()
    {
        if (!File.Exists("/usr/bin/yes"))
        {
            return;
        }

        var invocation = new ProcessInvocation(
            "/usr/bin/yes",
            Path.GetTempPath(),
            ["provider-output"],
            string.Empty,
            new Dictionary<string, string>(),
            TimeSpan.FromSeconds(10));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new SystemProcessRunner().RunAsync(
                invocation,
                static (_, _) => ValueTask.CompletedTask,
                static (_, _) => ValueTask.CompletedTask,
                CancellationToken.None));

        Assert.Contains("output exceeded", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReceiverFailureReapsProcessBeforeReturning()
    {
        if (!File.Exists("/usr/bin/yes"))
        {
            return;
        }

        var callbackCount = 0;
        var invocation = new ProcessInvocation(
            "/usr/bin/yes", Path.GetTempPath(), ["callback"], string.Empty,
            new Dictionary<string, string>(), TimeSpan.FromSeconds(10));

        await Assert.ThrowsAsync<InvalidOperationException>(() => new SystemProcessRunner().RunAsync(
            invocation,
            (_, _) =>
            {
                Interlocked.Increment(ref callbackCount);
                throw new InvalidOperationException("Injected receiver failure.");
            },
            static (_, _) => ValueTask.CompletedTask,
            CancellationToken.None));
        var countAfterReturn = Volatile.Read(ref callbackCount);
        await Task.Delay(100);

        Assert.Equal(countAfterReturn, Volatile.Read(ref callbackCount));
    }

    [Fact]
    public async Task KillFailureStillAttemptsReapAndPreservesOriginalWhenProcessExits()
    {
        var original = new InvalidDataException("original provider failure");
        var process = new FakeCleanupTarget
        {
            KillFailure = new Win32Exception("injected kill failure"),
            ExitWhenWaited = true
        };

        var thrown = await Assert.ThrowsAsync<InvalidDataException>(() =>
            SystemProcessRunner.RethrowAfterCleanupAsync(
                original, process, [Task.CompletedTask], TimeSpan.FromSeconds(1)));

        Assert.Same(original, thrown);
        Assert.True(process.KillAttempted);
        Assert.True(process.WaitAttempted);
    }

    [Fact]
    public async Task ProcessRemainingAliveProducesExplicitCleanupFailure()
    {
        var original = new InvalidDataException("original provider failure");
        var process = new FakeCleanupTarget
        {
            KillFailure = new Win32Exception("injected kill failure"),
            ExitWhenWaited = false
        };

        var thrown = await Assert.ThrowsAsync<ProviderProcessCleanupException>(() =>
            SystemProcessRunner.RethrowAfterCleanupAsync(
                original, process, [Task.CompletedTask], TimeSpan.FromMilliseconds(50)));

        Assert.Same(original, thrown.InnerException);
        Assert.Contains("cleanup failed", thrown.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("remained alive", thrown.CleanupFailure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(process.KillAttempted);
        Assert.True(process.WaitAttempted);
    }

    private sealed class FakeCleanupTarget : IProcessCleanupTarget
    {
        public Exception? KillFailure { get; init; }
        public bool ExitWhenWaited { get; init; }
        public bool KillAttempted { get; private set; }
        public bool WaitAttempted { get; private set; }
        public bool HasExited { get; private set; }

        public void Kill()
        {
            KillAttempted = true;
            if (KillFailure is not null)
            {
                throw KillFailure;
            }

            HasExited = true;
        }

        public async Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            WaitAttempted = true;
            if (HasExited || ExitWhenWaited)
            {
                HasExited = true;
                return;
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
