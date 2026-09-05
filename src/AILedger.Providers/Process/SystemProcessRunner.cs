using System.Text;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using AILedger.Core.Contracts;

namespace AILedger.Providers.Process;

public sealed class SystemProcessRunner : IProcessRunner
{
    public async Task<ProcessExit> RunAsync(
        ProcessInvocation invocation,
        Func<string, CancellationToken, ValueTask> onStandardOutputLine,
        Func<string, CancellationToken, ValueTask> onStandardErrorLine,
        CancellationToken cancellationToken)
    {
        Validate(invocation);

        using var process = new System.Diagnostics.Process
        {
            StartInfo = CreateStartInfo(invocation),
            EnableRaisingEvents = true
        };

        var startedAt = DateTimeOffset.UtcNow;
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start '{invocation.ExecutablePath}'.");
        }

        using var timeout = new CancellationTokenSource(invocation.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        IReadOnlyList<Task> activeTasks = [];
        try
        {
            var stdout = DrainAsync(process.StandardOutput, onStandardOutputLine, linked.Token);
            var stderr = DrainAsync(process.StandardError, onStandardErrorLine, linked.Token);
            var stdin = WriteInputAsync(process, invocation.StandardInput, linked.Token);
            var exit = process.WaitForExitAsync(linked.Token);

            activeTasks = [exit, stdout, stderr, stdin];
            await AwaitFailFastAsync(activeTasks).ConfigureAwait(false);
            return new ProcessExit(process.ExitCode, startedAt, DateTimeOffset.UtcNow);
        }
        catch (Exception originalException)
        {
            TryCancel(linked);
            await RethrowAfterCleanupAsync(
                originalException,
                new SystemProcessCleanupTarget(process),
                activeTasks,
                TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            throw new UnreachableException();
        }
    }

    internal static async Task RethrowAfterCleanupAsync(
        Exception originalException,
        IProcessCleanupTarget process,
        IReadOnlyList<Task> activeTasks,
        TimeSpan cleanupTimeout)
    {
        var cleanupFailure = await CleanupProcessAsync(process, activeTasks, cleanupTimeout).ConfigureAwait(false);
        if (cleanupFailure is not null)
        {
            throw new ProviderProcessCleanupException(originalException, cleanupFailure);
        }

        ExceptionDispatchInfo.Capture(originalException).Throw();
    }

    internal static async Task<Exception?> CleanupProcessAsync(
        IProcessCleanupTarget process,
        IReadOnlyList<Task> activeTasks,
        TimeSpan cleanupTimeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(cleanupTimeout, TimeSpan.Zero);

        var cleanupProblems = new List<Exception>();
        var exited = TryReadHasExited(process, cleanupProblems);
        if (exited != true)
        {
            try
            {
                process.Kill();
            }
            catch (Exception exception)
            {
                cleanupProblems.Add(new InvalidOperationException("Killing the provider process tree failed.", exception));
            }
        }

        using var cleanupCancellation = new CancellationTokenSource(cleanupTimeout);
        try
        {
            await process.WaitForExitAsync(cleanupCancellation.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            cleanupProblems.Add(new InvalidOperationException("Reaping the provider process failed.", exception));
        }

        var tasksStopped = await ObserveTasksAsync(activeTasks, cleanupCancellation.Token, cleanupProblems)
            .ConfigureAwait(false);
        exited = TryReadHasExited(process, cleanupProblems);
        if (exited == true && tasksStopped)
        {
            return null;
        }

        var state = exited == true
            ? "the process exited but its I/O tasks did not stop"
            : "the process remained alive after kill and bounded reap attempts";
        return new InvalidOperationException(
            $"Provider process cleanup failed: {state}.",
            cleanupProblems.Count == 0 ? null : new AggregateException(cleanupProblems));
    }

    private static async Task AwaitFailFastAsync(IReadOnlyList<Task> tasks)
    {
        var pending = tasks.ToList();
        while (pending.Count > 0)
        {
            var completed = await Task.WhenAny(pending).ConfigureAwait(false);
            await completed.ConfigureAwait(false);
            pending.Remove(completed);
        }
    }

    private static ProcessStartInfo CreateStartInfo(ProcessInvocation invocation)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = invocation.ExecutablePath,
            WorkingDirectory = invocation.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in invocation.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment.Clear();
        foreach (var variableName in ProcessEnvironment.AmbientAllowlist)
        {
            var value = Environment.GetEnvironmentVariable(variableName);
            if (value is not null)
            {
                startInfo.Environment[variableName] = value;
            }
        }

        foreach (var variable in invocation.Environment)
        {
            startInfo.Environment[variable.Key] = variable.Value;
        }

        return startInfo;
    }

    private static async Task DrainAsync(
        StreamReader reader,
        Func<string, CancellationToken, ValueTask> receiver,
        CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var line = new StringBuilder();
        long totalCharacters = 0;
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            for (var index = 0; index < read; index++)
            {
                var character = buffer[index];
                totalCharacters++;
                if (totalCharacters > ProviderOutputLimits.MaximumCharactersPerStream)
                {
                    throw new InvalidDataException("Provider output exceeded the per-stream limit.");
                }

                if (character == '\n')
                {
                    await receiver(TrimCarriageReturn(line), cancellationToken).ConfigureAwait(false);
                    line.Clear();
                    continue;
                }

                line.Append(character);
                if (line.Length > ProviderOutputLimits.MaximumCharactersPerLine)
                {
                    throw new InvalidDataException("Provider output contained an overlong line.");
                }
            }
        }

        if (line.Length > 0)
        {
            await receiver(TrimCarriageReturn(line), cancellationToken).ConfigureAwait(false);
        }
    }

    private static string TrimCarriageReturn(StringBuilder line)
    {
        if (line.Length > 0 && line[^1] == '\r')
        {
            line.Length--;
        }

        return line.ToString();
    }

    private static async Task WriteInputAsync(
        System.Diagnostics.Process process,
        string input,
        CancellationToken cancellationToken)
    {
        await process.StandardInput.WriteAsync(input.AsMemory(), cancellationToken).ConfigureAwait(false);
        await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        process.StandardInput.Close();
    }

    private static void Validate(ProcessInvocation invocation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invocation.ExecutablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(invocation.WorkingDirectory);
        if (!Path.IsPathFullyQualified(invocation.ExecutablePath))
        {
            throw new ArgumentException("Executable path must be absolute.", nameof(invocation));
        }

        if (!Path.IsPathFullyQualified(invocation.WorkingDirectory))
        {
            throw new ArgumentException("Working directory must be absolute.", nameof(invocation));
        }

        if (invocation.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(invocation), "Timeout must be positive.");
        }
    }

    private static bool? TryReadHasExited(
        IProcessCleanupTarget process,
        ICollection<Exception> cleanupProblems)
    {
        try
        {
            return process.HasExited;
        }
        catch (Exception exception)
        {
            cleanupProblems.Add(new InvalidOperationException("Reading provider process state failed.", exception));
            return null;
        }
    }

    private static async Task<bool> ObserveTasksAsync(
        IReadOnlyList<Task> activeTasks,
        CancellationToken cancellationToken,
        ICollection<Exception> cleanupProblems)
    {
        var observations = activeTasks.Select(ObserveTaskAsync).ToArray();
        try
        {
            await Task.WhenAll(observations).WaitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException exception)
        {
            cleanupProblems.Add(new TimeoutException("Provider I/O tasks did not stop within the cleanup deadline.", exception));
            return false;
        }
    }

    private static async Task ObserveTaskAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private static void TryCancel(CancellationTokenSource cancellation)
    {
        try
        {
            cancellation.Cancel();
        }
        catch (Exception)
        {
            // Process termination and reaping still have to run if a cancellation callback misbehaves.
        }
    }
}

public sealed class ProviderProcessCleanupException : Exception
{
    internal ProviderProcessCleanupException(Exception originalFailure, Exception cleanupFailure)
        : base(
            $"Provider process cleanup failed after '{originalFailure.GetType().Name}': {cleanupFailure.Message}",
            originalFailure)
    {
        CleanupFailure = cleanupFailure;
    }

    public Exception CleanupFailure { get; }
}

internal interface IProcessCleanupTarget
{
    bool HasExited { get; }
    void Kill();
    Task WaitForExitAsync(CancellationToken cancellationToken);
}

internal sealed class SystemProcessCleanupTarget(System.Diagnostics.Process process) : IProcessCleanupTarget
{
    public bool HasExited => process.HasExited;

    public void Kill() => process.Kill(entireProcessTree: true);

    public Task WaitForExitAsync(CancellationToken cancellationToken) => process.WaitForExitAsync(cancellationToken);
}

internal static class ProcessEnvironment
{
    // Keep provider CLIs usable without forwarding ambient credential, proxy, or CI variables.
    // Provider-specific authentication variables must be supplied explicitly on the invocation.
    public static readonly string[] AmbientAllowlist =
    [
        "HOME",
        "USER",
        "LOGNAME",
        "PATH",
        "SHELL",
        "TMPDIR",
        "TMP",
        "TEMP",
        "LANG",
        "LC_ALL",
        "LC_CTYPE",
        "TERM",
        "COLORTERM",
        "NO_COLOR",
        "XDG_CONFIG_HOME",
        "XDG_CACHE_HOME",
        "XDG_DATA_HOME",
        "CODEX_HOME",
        "CLAUDE_CONFIG_DIR",
        "SSL_CERT_FILE",
        "SSL_CERT_DIR",
        "NODE_EXTRA_CA_CERTS"
    ];
}

internal static class ProviderOutputLimits
{
    public const int MaximumCharactersPerLine = 1024 * 1024;
    public const int MaximumCharactersPerStream = 8 * 1024 * 1024;
    public const int MaximumRetainedCharacters = 8 * 1024 * 1024;
}
