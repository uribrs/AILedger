using System.Net.Sockets;
using System.Text.Json;
using AILedger.Cli;
using AILedger.Core.Contracts;
using AILedger.Providers.Adapters;
using AILedger.Providers.Process;
using AILedger.Tests.Support;

namespace AILedger.Tests.Providers;

public sealed class ProviderStartupIsolationTests
{
    [Fact]
    public async Task HookDiscoveryRuntimeStaysWritableWhileConfigurationAndAuthLinkStayProtected()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var work = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(work.Path, ".git"));
        var runner = new RuntimeProbeRunner();
        var request = ProviderProtocolTests.Request("codex", AgentLaunchMode.New, null) with
        {
            WorkingDirectory = work.Path,
            NavigationHostAssembly = typeof(CliApplication).Assembly.Location,
            Isolation = new([work.Path], [], [])
        };
        var adapter = new CodexAgentAdapter(runner, async (_, home, _, _, cancellationToken) =>
        {
            // The real hook-trust app-server creates these before the confined exec starts.
            foreach (var name in new[] { "installation_id", "state_5.sqlite-shm" })
                await File.WriteAllTextAsync(Path.Combine(home, name), "runtime", cancellationToken);
        });

        var result = await adapter.RunAsync(request, default);

        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.True(runner.Probed);
    }

    [Fact]
    public async Task SystemResolverSocketIsReachableButUnrelatedHostSocketIsDenied()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var work = new TemporaryDirectory();
        // macOS Unix socket names have a 104-byte limit; the system temp prefix is long.
        var socketPath = Path.Combine(work.Path, "s");
        using var server = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        server.Bind(new UnixDomainSocketEndPoint(socketPath));
        server.Listen();
        var script = $$"""
            import socket
            with socket.socket(socket.AF_UNIX) as resolver:
                resolver.connect('/private/var/run/mDNSResponder')
            with socket.socket(socket.AF_UNIX) as unrelated:
                try:
                    unrelated.connect({{JsonSerializer.Serialize(socketPath)}})
                except PermissionError:
                    pass
                else:
                    raise AssertionError('Unrelated host socket was reachable')
            print('PASS')
            """;
        var result = await ProviderIsolationTests.RunAsync(new("/usr/bin/python3", work.Path, ["-c", script], "",
            new Dictionary<string, string>(), TimeSpan.FromSeconds(15), new([work.Path], [], [])));
        Assert.True(result.Exit == 0, result.Error);
        Assert.Contains("PASS", result.Output);
    }

    private sealed class RuntimeProbeRunner : IProcessRunner
    {
        private readonly ScriptedProcessRunner capabilities = new ScriptedProcessRunner()
            .Enqueue(0, ["codex-cli fixture"])
            .Enqueue(0, ["--strict-config --sandbox --cd --add-dir --output-schema --json"])
            .Enqueue(0, ["SESSION_ID --json"]);

        public bool Probed { get; private set; }

        public async Task<ProcessExit> RunAsync(ProcessInvocation invocation,
            Func<string, CancellationToken, ValueTask> onStandardOutputLine,
            Func<string, CancellationToken, ValueTask> onStandardErrorLine,
            TruncatedLineTally? tally, CancellationToken cancellationToken)
        {
            if (!invocation.Environment.TryGetValue("CODEX_HOME", out var home))
                return await capabilities.RunAsync(invocation, onStandardOutputLine, onStandardErrorLine, tally, cancellationToken);
            var script = $$"""
                import os, fcntl
                home = {{JsonSerializer.Serialize(home)}}
                for name in ['installation_id', 'state_5.sqlite-shm']:
                    with open(os.path.join(home, name), 'r+') as state:
                        fcntl.flock(state, fcntl.LOCK_EX)
                        state.write('updated')
                for name in ['config.toml', 'hooks.json', 'roslyn-navigation.json']:
                    try:
                        with open(os.path.join(home, name), 'a') as config:
                            config.write('forged')
                    except PermissionError:
                        pass
                    else:
                        raise AssertionError(name + ' was writable')
                # Never read or write the operator credential target; test only link replacement.
                try:
                    os.unlink(os.path.join(home, 'auth.json'))
                except PermissionError:
                    pass
                else:
                    raise AssertionError('Credential link could be replaced')
                print('{"type":"thread.started","thread_id":"runtime-probe"}')
                print('{"type":"turn.completed"}')
                """;
            Probed = true;
            return await new SystemProcessRunner().RunAsync(invocation with
            {
                ExecutablePath = "/usr/bin/python3", Arguments = ["-c", script], StandardInput = ""
            }, onStandardOutputLine, onStandardErrorLine, tally, cancellationToken);
        }
    }
}
