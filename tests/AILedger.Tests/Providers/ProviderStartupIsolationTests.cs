using System.Net.Sockets;
using System.Text.Json;
using AILedger.Cli;
using AILedger.Core.Contracts;
using AILedger.Providers.Adapters;
using AILedger.Providers.Navigation;
using AILedger.Providers.Process;
using AILedger.Tests.Support;

namespace AILedger.Tests.Providers;

public sealed class ProviderStartupIsolationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NavigationBridgeCanStartOutsideHiddenLedgerDirectories(bool legacyLocation)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var work = new TemporaryDirectory();
        var toolDirectory = Directory.CreateDirectory(Path.Combine(work.Path,
            legacyLocation ? ".ailedger" : ".local", "tools", "roslyn")).FullName;
        var executable = Path.Combine(toolDirectory, "backend");
        await File.WriteAllTextAsync(executable, """
            #!/usr/bin/python3
            import sys, json, os
            assert os.environ.get('DOTNET_USE_POLLING_FILE_WATCHER') == '1'
            for line in sys.stdin:
                request = json.loads(line)
                if 'id' not in request: continue
                result = {'protocolVersion':'2024-11-05','capabilities':{'tools':{}},
                          'serverInfo':{'name':'fixture','version':'1'}}
                if request['method'] == 'tools/list':
                    result = {'tools':[{'name':'list_solutions','description':'List loaded solutions',
                                        'inputSchema':{'type':'object'}}]}
                print(json.dumps({'jsonrpc':'2.0','id':request['id'],'result':result}), flush=True)
            """ + "\n");
        File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var settingsPath = Path.Combine(work.Path, "navigation.json");
        var ledger = Path.Combine(work.Path, ".ailedger", "tasks");
        var settings = new RoslynNavigationSettings(executable, [work.Path], ledger,
            Path.Combine(work.Path, "failures"));
        await File.WriteAllTextAsync(settingsPath, JsonSerializer.Serialize(settings));
        var input = """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"probe","version":"1"}}}
            {"jsonrpc":"2.0","method":"notifications/initialized"}
            {"jsonrpc":"2.0","id":2,"method":"tools/list"}
            """ + "\n";
        var cli = typeof(CliApplication).Assembly.Location;
        var dotnet = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "../../..", "dotnet"));
        var result = await ProviderIsolationTests.RunAsync(new(dotnet, work.Path,
            [cli, "navigation", "serve", settingsPath], input, new Dictionary<string, string>(),
            TimeSpan.FromSeconds(20), new([work.Path], [ledger], [settingsPath, toolDirectory, Path.GetDirectoryName(cli)!])));
        if (legacyLocation)
        {
            Assert.Equal(1, result.Exit);
            Assert.Contains("configured Roslyn executable is unavailable", result.Error);
        }
        else
        {
            Assert.True(result.Exit == 0, result.Error);
            var response = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Last();
            using var listed = JsonDocument.Parse(response);
            Assert.Equal("list_solutions", listed.RootElement.GetProperty("result").GetProperty("tools")[0].GetProperty("name").GetString());
        }
    }

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
