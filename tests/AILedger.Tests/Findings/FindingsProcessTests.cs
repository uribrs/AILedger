using System.Diagnostics;
using System.Text.Json;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.Findings;
using AILedger.Storage;
using AILedger.Storage.Findings;

namespace AILedger.Tests.Findings;

internal static class FindingsProcessProbe
{
    internal const string Argument = "--findings-probe";
    internal static async Task<int> RunAsync(string[] args)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!File.Exists(args[2])) await Task.Delay(10, deadline.Token);
        var binding = new FindingsBinding(new("findings"), new("operator"), null, "stable",
            AllowRecordFindings: true, AllowRunless: true);
        var service = new FileGovernedTaskService(args[1], new CommandHandler(), new TaskReducer());
        if (args[3].StartsWith("cli-", StringComparison.Ordinal))
        {
            await service.ExecuteAsync(binding.TaskId, new AddClaimCommand(binding.ActorId, null, "cli",
                new(args[3]), "Separate CLI writer", null), deadline.Token);
            return 0;
        }
        if (args[3].StartsWith("crash-", StringComparison.Ordinal))
        {
            service = new FileGovernedTaskService(args[1], new CommandHandler(), new TaskReducer(),
                new FindingsStorageFaults(Write: async (stream, bytes, token) =>
                {
                    var count = args[3] == "crash-prefix" ? Array.IndexOf(bytes.ToArray(), (byte)'\n') + 1 : bytes.Length;
                    await stream.WriteAsync(bytes[..count], token);
                    await stream.FlushAsync(token); // drain managed buffering, before the durability flush
                    // Terminate the process with no managed disposal, receipt response or journal.
                    Environment.Exit(23);
                }));
        }
        var result = await service.RecordAsync(binding, FindingsFixture.Request(), deadline.Token);
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(result));
        return result.Error is null ? 0 : 1;
    }
}

public sealed class FindingsProcessTests
{
    [Fact]
    public async Task IndependentProcessesShareTheCliLockAndCommitSameKeyOnce()
    {
        using var f = new FindingsFixture();
        await f.OpenAsync();
        var marker = Path.Combine(f.Root, "go");
        var processes = new[] { "findings", "findings", "findings", "cli-one", "cli-two" }
            .Select(mode => Start(f.Root, marker, mode)).ToArray();
        try
        {
            await File.WriteAllTextAsync(marker, "go");
            var outputs = await Task.WhenAll(processes.Select(p => FinishAsync(p, 0)));
            var results = outputs.Take(3).Select(s => JsonSerializer.Deserialize<FindingsResult>(s)!).ToArray();
            Assert.Single(results.Where(r => !r.Replayed));
            Assert.Single(results.Select(r => r.Receipt!.TransactionId).Distinct());
            var state = await f.StateAsync();
            Assert.Equal(6, state.Version);
            Assert.Equal(3, state.Claims.Count);
            Assert.Single(state.Evidence);
        }
        finally { DisposeAll(processes); }
    }

    [Theory]
    [InlineData("crash-prefix", false)]
    [InlineData("crash-complete", true)]
    public async Task ProcessExitDuringAppendRecoversFromTheCanonicalLog(string mode, bool replayed)
    {
        using var f = new FindingsFixture();
        await f.OpenAsync();
        var marker = Path.Combine(f.Root, "go");
        await File.WriteAllTextAsync(marker, "go");
        var process = Start(f.Root, marker, mode);
        try { await FinishAsync(process, 23); }
        finally { DisposeAll([process]); }
        Assert.False(File.Exists(Path.Combine(f.Directory, "findings-attempts.jsonl")));
        var result = await f.RecordAsync();
        Assert.Null(result.Error);
        Assert.Equal(replayed, result.Replayed);
        Assert.Equal(4, (await f.StateAsync()).Version);
        Assert.Single((await f.StateAsync()).Claims);
        Assert.Single((await f.StateAsync()).Evidence);
    }

    private static Process Start(string root, string marker, string mode)
    {
        var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var muxer = Path.GetFullPath(Path.Combine(runtime, "..", "..", "..",
            OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        var start = new ProcessStartInfo(muxer)
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "exec", typeof(Program).Assembly.Location,
                     FindingsProcessProbe.Argument, root, marker, mode }) start.ArgumentList.Add(argument);
        return Process.Start(start)!;
    }

    private static async Task<string> FinishAsync(Process process, int expectedCode)
    {
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await process.WaitForExitAsync(timeout.Token);
        var text = await output;
        Assert.True(process.ExitCode == expectedCode, $"Exit {process.ExitCode}: {await error} {text}");
        return text;
    }

    private static void DisposeAll(IEnumerable<Process> processes)
    {
        foreach (var process in processes)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.Dispose();
        }
    }
}
