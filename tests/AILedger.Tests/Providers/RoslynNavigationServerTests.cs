using System.Diagnostics;
using System.Text.Json.Nodes;
using AILedger.Providers.Navigation;
using AILedger.Tests.Support;

namespace AILedger.Tests.Providers;

public sealed class RoslynNavigationServerTests
{
    [Theory]
    [InlineData("timeout")]
    [InlineData("wrong-id")]
    [InlineData("invalid-json")]
    public async Task TransportFailureStopsBackendAndRejectsLaterRequests(string failure)
    {
        if (OperatingSystem.IsWindows()) return;
        using var directory = new TemporaryDirectory();
        var executable = Path.Combine(directory.Path, "backend.sh");
        var pidPath = Path.Combine(directory.Path, "backend.pid");
        var response = failure switch
        {
            "wrong-id" => "printf '%s\\n' '{\"jsonrpc\":\"2.0\",\"id\":99,\"result\":{}}'",
            "invalid-json" => "printf '%s\\n' 'invalid JSON'",
            _ => ":"
        };
        await File.WriteAllTextAsync(executable, $$$"""
            #!/bin/sh
            printf '%s' "$$" > '{{{pidPath.Replace("'", "'\\''")}}}'
            IFS= read -r request
            {{{response}}}
            IFS= read -r next_request
            printf '%s\n' '{"jsonrpc":"2.0","id":1,"result":{}}'
            """ + "\n");
        File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var exchangeTimeout = failure == "timeout" ? TimeSpan.FromMilliseconds(500) : TimeSpan.FromSeconds(10);
        await using var backend = RoslynStdioBackend.Start(executable, exchangeTimeout);
        var pid = await WaitForBackendPidAsync(pidPath);
        var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "initialize" };

        var exchange = backend.ExchangeAsync(request, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(20));
        if (failure == "timeout")
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => exchange);
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(() => exchange);
        }

        // Check before disposal: a tool error must not leave the old operation running.
        try
        {
            using var process = Process.GetProcessById(pid);
            Assert.True(process.HasExited);
        }
        catch (ArgumentException)
        {
            // Already reaped by backend cleanup.
        }

        var second = await Assert.ThrowsAsync<IOException>(() =>
            backend.ExchangeAsync(request, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Contains("previous transport failure", second.Message, StringComparison.Ordinal);
    }

    private static async Task<int> WaitForBackendPidAsync(string path)
    {
        var readiness = Stopwatch.StartNew();
        while (readiness.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (File.Exists(path) && int.TryParse(await File.ReadAllTextAsync(path), out var pid))
            {
                return pid;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("The backend fixture did not report readiness within ten seconds.");
    }

    [Fact]
    public async Task ShutdownDoesNotWaitForExitedBackendsDescendantToCloseStderr()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Fixture is a POSIX executable; the production cancellation is platform-independent.
        }

        using var directory = new TemporaryDirectory();
        var executable = Path.Combine(directory.Path, "backend.sh");
        var childPidPath = Path.Combine(directory.Path, "child.pid");
        var backendPidPath = Path.Combine(directory.Path, "backend.pid");
        await File.WriteAllTextAsync(executable, $$$"""
            #!/bin/sh
            IFS= read -r request
            sleep 30 >&2 &
            printf '%s' "$!" > '{{{childPidPath.Replace("'", "'\\''")}}}'
            printf '%s' "$$" > '{{{backendPidPath.Replace("'", "'\\''")}}}'
            printf '%s\n' '{"jsonrpc":"2.0","id":1,"result":{}}'
            exit 0
            """);
        File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var backend = RoslynStdioBackend.Start(executable);
        Task? disposal = null;
        try
        {
            await backend.ExchangeAsync(new JsonObject
            {
                ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "initialize"
            }, CancellationToken.None);
            var backendPid = int.Parse(await File.ReadAllTextAsync(backendPidPath));
            try
            {
                using var exited = Process.GetProcessById(backendPid);
                await exited.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (ArgumentException)
            {
                // Already exited and reaped.
            }

            disposal = backend.DisposeAsync().AsTask();
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            // The fixture deliberately orphans this bounded child; never leave it running.
            if (File.Exists(childPidPath))
            {
                try
                {
                    using var child = Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(childPidPath)));
                    child.Kill();
                }
                catch (ArgumentException)
                {
                }
            }

            await (disposal ?? backend.DisposeAsync().AsTask());
        }
    }
}
