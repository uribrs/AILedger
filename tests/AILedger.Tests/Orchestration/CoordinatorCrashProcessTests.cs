using System.Diagnostics;
using System.Text.Json;
using AILedger.Cli;
using AILedger.Cli.Orchestration;
using AILedger.Core.Contracts;
using AILedger.Storage;
using AILedger.Tests.Findings;
using AILedger.Tests.Support;
namespace AILedger.Tests.Orchestration;

public sealed class CoordinatorCrashProcessTests
{
    [Fact]
    public async Task BuiltCliCrashAndTakeoverDoNotLaunchAnUncertainProviderAgain()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var f = new FindingsFixture(); using var work = new TemporaryDirectory(); using var trusted = new TemporaryDirectory();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        Directory.CreateDirectory(Path.Combine(work.Path, ".git"));
        var intakePath = Path.Combine(trusted.Path, "request.json");
        await File.WriteAllTextAsync(intakePath, JsonSerializer.Serialize(new { schema_version = 1, request_id = "intake", title = "Crash fixture",
            original_request = "Investigate a bounded local fixture", constraints = new[] { "No real providers" }, tags = new[] { "fixture" } }));
        var intake = await RunAsync(["orchestrate", "intake", "--root", f.Root, "--lesson-root", Path.Combine(f.Root, "lessons"),
            "--task", f.TaskId.Value, "--actor", "operator", "--request-file", intakePath], timeout.Token);
        Assert.True(intake.Exit == 0, intake.Error);
        var profiles = Path.Combine(trusted.Path, "profiles.json");
        await File.WriteAllTextAsync(profiles, JsonSerializer.Serialize(new { schema_version = 1,
            agents = new[] { new { work = "discovery", subject = "lead", provider = "codex" } },
            preparation = new { roles = new[] { new { actor = "lead", role = "PlanningLead", capabilities = RoleDefaults.For(RoleKind.PlanningLead).Select(c => c.ToString()).ToArray() } }, work = Array.Empty<object>() } }));
        var executable = Path.Combine(trusted.Path, "fake-codex");
        var marker = Path.Combine(work.Path, "started");
        await File.WriteAllTextAsync(executable, "#!/bin/sh\nif [ \"$1\" = \"--version\" ]; then echo 'codex-cli 1.0.0'; exit 0; fi\nprintf 'started\\n' >> '" + marker + "'\nsleep 45\n");
        File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string[] args = ["orchestrate", "run", "--root", f.Root, "--lesson-root", Path.Combine(f.Root, "lessons"),
            "--task", f.TaskId.Value, "--actor", "operator", "--profiles", profiles, "--working-directory", work.Path,
            "--cognitive-root", ContextBrief.CognitiveRoot(), "--executable", executable, "--owner-lease-seconds", "3"];
        using var owner = Start(args);
        var stdout = owner.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = owner.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            while (!File.Exists(marker) && !owner.HasExited) await Task.Delay(50, timeout.Token);
            Assert.True(File.Exists(marker), owner.HasExited ? await stdout + await stderr : "Fixture did not start");
            var other = await RunAsync(args, timeout.Token);
            Assert.NotEqual(0, other.Exit);
            Assert.Contains("routing owner", other.Error);
            owner.Kill(entireProcessTree: true);
            await owner.WaitForExitAsync(timeout.Token);
            await stdout; await stderr;
            await Task.Delay(3500, timeout.Token);
            var takeover = await RunAsync(args, timeout.Token);
            Assert.True(takeover.Exit == 4, takeover.Output + takeover.Error);
            Assert.Contains("active_run", takeover.Output);
            Assert.Single(await File.ReadAllLinesAsync(marker));
            Assert.Equal(AgentRunStatus.Active, Assert.Single((await f.StateAsync()).Runs.Values).Status);
            var journal = JsonSerializer.Deserialize<CoordinationSnapshot>(await File.ReadAllTextAsync(Path.Combine(f.Directory, "coordination-v1.json")), LedgerJson.CreateOptions())!;
            Assert.True(journal.Epoch >= 2);
            Assert.Contains("runId", journal.Payload);
            var original = Assert.Single((await f.StateAsync()).Runs.Values);
            await f.ExecuteAsync(new AddEvidenceCommand(f.Actor, null, "fixture-termination", new("terminated"), "process-termination",
                $"{f.TaskId.Value}/{original.Id.Value}", "Test host killed the entire fake provider tree and awaited exit; no check was started.", [], []));
            await f.ExecuteAsync(new CompleteRunCommand(f.Actor, null, "fixture-orphan", original.Id, AgentRunStatus.Cancelled, null));
            var recovered = await RunAsync(["orchestrate", "reconcile-stopped", "--root", f.Root,
                "--task", f.TaskId.Value, "--actor", f.Actor.Value, "--run", original.Id.Value, "--termination-evidence", "terminated"], timeout.Token);
            Assert.True(recovered.Exit == 0, recovered.Output + recovered.Error);
            Assert.Contains("trusted_execution_stopped", recovered.Output);
            Assert.Single(await File.ReadAllLinesAsync(marker));
            Assert.Equal(AgentRunStatus.Cancelled, (await f.StateAsync()).Runs[original.Id].Status);
        }
        finally { if (!owner.HasExited) { owner.Kill(entireProcessTree: true); await owner.WaitForExitAsync(); } }
    }

    private static Process Start(IReadOnlyList<string> args)
    {
        var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var muxer = Path.GetFullPath(Path.Combine(runtime, "..", "..", "..", "dotnet"));
        var tests = typeof(CoordinatorCrashProcessTests).Assembly.Location;
        var start = new ProcessStartInfo(muxer) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "exec", "--runtimeconfig", Path.ChangeExtension(tests, "runtimeconfig.json"),
            "--depsfile", Path.ChangeExtension(tests, "deps.json"), typeof(CliApplication).Assembly.Location }.Concat(args)) start.ArgumentList.Add(arg);
        return Process.Start(start)!;
    }
    private static async Task<(int Exit, string Output, string Error)> RunAsync(IReadOnlyList<string> args, CancellationToken token)
    {
        using var process = Start(args);
        var output = process.StandardOutput.ReadToEndAsync(token); var error = process.StandardError.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); return (process.ExitCode, await output, await error); }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
    }
}
