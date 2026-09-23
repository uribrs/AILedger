using System.Diagnostics;
using System.Text.Json.Nodes;
using AILedger.Providers.Navigation;
using AILedger.Tests.Support;

namespace AILedger.Tests.Providers;

public sealed class RoslynNavigationServerTests
{
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
