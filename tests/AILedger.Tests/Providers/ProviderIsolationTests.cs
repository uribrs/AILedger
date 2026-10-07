using System.Text;
using AILedger.Cli;
using AILedger.Cli.Providers;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Providers.Process;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Providers;

public sealed class ProviderIsolationTests
{
    [Fact]
    public async Task CodexWorktreeIsWritableWhileOperatorConfigurationRemainsProtected()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var root = new TemporaryDirectory();
        var home = Directory.CreateDirectory(Path.Combine(root.Path, ".codex")).FullName;
        var work = Directory.CreateDirectory(Path.Combine(home, "worktrees", "checkout")).FullName;
        var config = Path.Combine(home, "config.toml");
        await File.WriteAllTextAsync(config, "trusted");
        var script = $"set -eu; echo code > source.txt; if echo forged > {Quote(config)}; then exit 10; fi";
        var result = await RunAsync(new("/bin/sh", work, ["-c", script], "", new Dictionary<string, string>(),
            TimeSpan.FromSeconds(10), new([work], [], ProviderIsolationPreparation.CodexStatePaths(home).ToArray())));
        Assert.True(result.Exit == 0, result.Error);
        Assert.Equal("code\n", await File.ReadAllTextAsync(Path.Combine(work, "source.txt")));
        Assert.Equal("trusted", await File.ReadAllTextAsync(config));
    }

    [Fact]
    public async Task RealDescendantsCannotUseShellCliSymlinksOrConfigurationToRewriteAuthority()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Throws<ArgumentException>(ProviderProcessIsolation.EnsureSupported);
            return;
        }
        using var root = new TemporaryDirectory();
        var work = Directory.CreateDirectory(Path.Combine(root.Path, "work")).FullName;
        // The ledger is deliberately inside the otherwise writable root (self-hosting).
        var ledger = Path.Combine(work, "host", "tasks");
        var service = new FileGovernedTaskService(ledger, new CommandHandler(), new TaskReducer());
        var task = new TaskId("T1");
        await service.ExecuteAsync(task, new OpenTaskCommand(new("operator"), null, "open", task, "Task", "Goal"), default);
        var events = Path.Combine(ledger, "T1", "events.jsonl");
        var original = await File.ReadAllTextAsync(events);
        var authority = Path.Combine(work, "authority.json");
        await File.WriteAllTextAsync(authority, "trusted authority");
        File.CreateSymbolicLink(Path.Combine(work, "alias"), events);
        var cli = typeof(CliApplication).Assembly.Location;
        var dotnet = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "../../..", "dotnet"));
        var command = $"{Quote(dotnet)} {Quote(cli)} claim add --root {Quote(ledger)} --task T1 --actor operator --id forged --statement forged";
        var script = $"""
            set -eu
            printf allowed > allowed.txt
            if cat {Quote(events)}; then exit 11; fi
            if printf forged > {Quote(events)}; then exit 12; fi
            if printf forged > alias; then exit 13; fi
            if cat alias; then exit 14; fi
            if ln {Quote(events)} hardlink; then exit 21; fi
            if mv host moved-host; then exit 15; fi
            if printf forged > authority.json; then exit 16; fi
            if cat authority.json; then exit 17; fi
            if {command}; then exit 18; fi
            if /usr/bin/env -i PATH=/usr/bin:/bin {command}; then exit 19; fi
            if /bin/sh -c 'printf forged > .ailedger/events.jsonl'; then exit 20; fi
            if printf forged > .git/config; then exit 22; fi
            inspection="$(/bin/ps -p {Environment.ProcessId} -o command= 2>/dev/null || true)"
            if [ -n "$inspection" ]; then exit 23; fi
            printf PASS
            """;
        Directory.CreateDirectory(Path.Combine(work, ".ailedger"));
        Directory.CreateDirectory(Path.Combine(work, ".git"));
        await File.WriteAllTextAsync(Path.Combine(work, ".git", "config"), "host git configuration");
        var (exit, output, error) = await RunAsync(new("/bin/sh", work, ["-c", script], "",
            new Dictionary<string, string>(), TimeSpan.FromSeconds(25), new([work], [ledger, authority], [Path.GetDirectoryName(cli)!])));
        Assert.True(exit == 0, $"exit={exit}\n{output}\n{error}");
        Assert.Contains("PASS", output);
        Assert.Equal("allowed", await File.ReadAllTextAsync(Path.Combine(work, "allowed.txt")));
        Assert.Equal(original, await File.ReadAllTextAsync(events));
        Assert.Equal("trusted authority", await File.ReadAllTextAsync(authority));
        Assert.Empty((await service.GetStateAsync(task, default))!.Claims);
        // Same operation through the authenticated local owner path remains legal.
        using var errors = new StringWriter();
        var app = new CliApplication(TextWriter.Null, errors, _ => service, _ => throw new Exception("No provider"), new ContextAssembler());
        Assert.Equal(0, await app.RunAsync(["claim", "add", "--root", ledger, "--task", "T1", "--actor", "operator",
            "--id", "human", "--statement", "Authorized local operation"], default));
    }

    [Fact]
    public async Task HostConfigurationIsReadOnlyAndChildEnvironmentHasOnlyPrivateScratch()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var root = new TemporaryDirectory();
        var config = Path.Combine(root.Path, "settings.json");
        await File.WriteAllTextAsync(config, "host config");
        var script = "set -eu; cat settings.json; if echo changed > settings.json; then exit 10; fi; " +
            "if mv settings.json old.json; then exit 11; fi; echo scratch > \"$TMPDIR/check\"; cat \"$TMPDIR/check\"";
        var result = await RunAsync(new("/bin/sh", root.Path, ["-c", script], "", new Dictionary<string, string>(),
            TimeSpan.FromSeconds(10), new([root.Path], [], [config])));
        Assert.True(result.Exit == 0, result.Error);
        Assert.Contains("scratch", result.Output);
        Assert.Equal("host config", await File.ReadAllTextAsync(config));
    }

    internal static async Task<(int Exit, string Output, string Error)> RunAsync(ProcessInvocation invocation)
    {
        var output = new StringBuilder();
        var error = new StringBuilder();
        var result = await new SystemProcessRunner().RunAsync(invocation,
            (line, _) => { output.AppendLine(line); return ValueTask.CompletedTask; },
            (line, _) => { error.AppendLine(line); return ValueTask.CompletedTask; }, null, default);
        return (result.ExitCode, output.ToString(), error.ToString());
    }

    private static string Quote(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";
}
