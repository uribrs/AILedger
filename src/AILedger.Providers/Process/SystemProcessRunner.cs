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
        TruncatedLineTally? tally,
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
            // Protocol JSON must reach the adapter whole; the existing stream ceiling bounds it.
            var stdout = DrainAsync(process.StandardOutput, onStandardOutputLine, tally, linked.Token,
                ProviderOutputLimits.MaximumCharactersPerStream);
            var stderr = DrainAsync(process.StandardError, onStandardErrorLine, tally, linked.Token);
            var stdin = WriteInputAsync(process, invocation.StandardInput, linked.Token);
            var exit = process.WaitForExitAsync(linked.Token);

            activeTasks = [exit, stdout, stderr, stdin];
            await AwaitFailFastAsync(activeTasks).ConfigureAwait(false);
            // Both drains have completed by here, so awaiting them again only reads their counts.
            // One number for both streams: the run record says the output was cut, and which pipe
            // it was cut on is a question only the retained stream in the sidecar can answer.
            return new ProcessExit(
                process.ExitCode,
                startedAt,
                DateTimeOffset.UtcNow,
                await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false));
        }
        catch (Exception originalException)
        {
            // Read before anything is cancelled here, so the two flags say which source actually
            // ended the process rather than which one this handler touched on the way out.
            var propagated = Reclassify(
                originalException,
                invocation.Timeout,
                deadlineExpired: timeout.IsCancellationRequested,
                callerCancelled: cancellationToken.IsCancellationRequested);
            TryCancel(linked);
            await RethrowAfterCleanupAsync(
                propagated,
                new SystemProcessCleanupTarget(process),
                activeTasks,
                TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            throw new UnreachableException();
        }
    }

    /// <summary>
    /// Names the run's own expired deadline as the thing that ended it, when that is what happened.
    /// </summary>
    /// <remarks>
    /// This runner links two cancellation sources — the caller's token and a source built from
    /// <see cref="ProcessInvocation.Timeout"/> — and both surface as the same
    /// <see cref="OperationCanceledException"/>. Which one fired is knowable only here, and only
    /// while the two sources are still in hand; a caller left to work it out has to infer it, and
    /// the inference measure 11 used to make from elapsed time was wrong (VC6).
    ///
    /// The caller's own cancellation wins where both are set. A caller that asked this to stop got
    /// what it asked for, and calling that a timeout would attribute to the launch's limit a
    /// termination the limit did not cause.
    /// </remarks>
    private static Exception Reclassify(
        Exception originalException,
        TimeSpan timeout,
        bool deadlineExpired,
        bool callerCancelled) =>
        originalException is OperationCanceledException && deadlineExpired && !callerCancelled
            ? new ProviderProcessTimeoutException(timeout)
            : originalException;

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

    /// <summary>
    /// Reads one stream line by line and returns how many lines had to be cut to its configured cap.
    /// </summary>
    /// <remarks>
    /// Stderr is plain text and may be cut here. Stdout carries JSON records, so its line cap is
    /// the stream ceiling: the adapter receives a complete record without dropping its contents.
    /// Cutting raw JSON here would turn successful runs into parse failures.
    /// Both streams remain bounded by MaximumCharactersPerStream, including discarded characters.
    /// </remarks>
    internal static async Task<int> DrainAsync(
        StreamReader reader,
        Func<string, CancellationToken, ValueTask> receiver,
        TruncatedLineTally? tally,
        CancellationToken cancellationToken,
        int maximumCharactersPerLine = ProviderOutputLimits.MaximumCharactersPerLine)
    {
        var buffer = new char[4096];
        var line = new StringBuilder();
        long totalCharacters = 0;
        var truncatedLines = 0;
        // Set when the current line has reached the cap. The remainder is read and discarded, and
        // only a newline clears it: resuming mid-line would hand the receiver a fragment as if it
        // were a record, which is worse than the truncation it came from (R3).
        var discardingRestOfLine = false;
        // A carriage return is held back rather than appended, because until the next character
        // arrives there is no telling whether it terminates the line or belongs to it. Appending it
        // first and trimming it later is what made a CRLF line of exactly the cap length cross the
        // cap on its own terminator: its content was emitted whole and the run record said it had
        // been cut (CC1).
        var heldCarriageReturn = false;
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
                    // A held carriage return was the first half of a CRLF terminator after all, so
                    // it leaves with the newline instead of being emitted as content.
                    heldCarriageReturn = false;
                    await receiver(line.ToString(), cancellationToken).ConfigureAwait(false);
                    line.Clear();
                    discardingRestOfLine = false;
                    continue;
                }

                if (discardingRestOfLine)
                {
                    continue;
                }

                if (heldCarriageReturn)
                {
                    // No newline followed it, so it is content, and it counts against the cap like
                    // any other character.
                    heldCarriageReturn = false;
                    if (!Append('\r', line, tally, ref truncatedLines, ref discardingRestOfLine, maximumCharactersPerLine))
                    {
                        continue;
                    }
                }

                if (character == '\r')
                {
                    heldCarriageReturn = true;
                    continue;
                }

                Append(character, line, tally, ref truncatedLines, ref discardingRestOfLine, maximumCharactersPerLine);
            }
        }

        // A stream that ends without a newline still hands over what it holds. The held-carriage-
        // return test keeps a stream of nothing but a bare carriage return emitting the one empty
        // line it used to emit.
        if (line.Length > 0 || heldCarriageReturn)
        {
            await receiver(line.ToString(), cancellationToken).ConfigureAwait(false);
        }

        return truncatedLines;
    }

    /// <summary>
    /// Appends one character of content and reports whether the line is still open. A line that
    /// reaches the cap is cut there, counted once, and the rest of it discarded.
    /// </summary>
    private static bool Append(
        char character,
        StringBuilder line,
        TruncatedLineTally? tally,
        ref int truncatedLines,
        ref bool discardingRestOfLine,
        int maximumCharactersPerLine)
    {
        line.Append(character);
        if (line.Length <= maximumCharactersPerLine)
        {
            return true;
        }

        line.Length = maximumCharactersPerLine;
        truncatedLines++;
        // The same count, on an object the caller still holds. A drain that dies before it can
        // return — a receiver crossing the adapter's retained-output cap — leaves its count here
        // and nowhere else (CC2).
        tally?.Increment();
        discardingRestOfLine = true;
        return false;
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
