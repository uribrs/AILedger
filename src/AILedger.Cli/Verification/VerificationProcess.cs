using System.Diagnostics;

namespace AILedger.Cli.Verification;

/// <summary>What the verification command hands the process that runs the profile command.</summary>
internal sealed record VerificationProcessStart(
    string Command,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment,
    string StandardOutputPath,
    string StandardErrorPath);

/// <summary>
/// Runs the confirmed profile command. Returns the exit code, or null when the process was killed
/// because <c>cancellationToken</c> fired; the caller decides whether that was the timeout or a
/// cancellation. The implementation must kill the whole process tree before it returns null.
/// </summary>
internal interface IVerificationProcessSpawner
{
    Task<int?> RunAsync(VerificationProcessStart start, CancellationToken cancellationToken);
}

/// <summary>
/// <c>/bin/sh -c</c> in the checkout root with the invoker's environment plus the S2 variables.
/// Mirrors <see cref="LessonRecheck"/>'s bounded execution: standard input closed, both streams
/// drained, the entire tree killed on cancellation and reaped within a bounded wait.
/// </summary>
internal sealed class ShellVerificationProcessSpawner : IVerificationProcessSpawner
{
    // Each log keeps this many bytes; the rest of the stream is drained and dropped so the child
    // never blocks on a full pipe.
    internal const long LogCapBytes = 16L * 1024 * 1024;

    // A SIGKILL margin, not a shutdown grace period.
    private const int KillGraceMilliseconds = 5000;

    // A grandchild that inherited the pipes (a build server, for one) can hold them open after the
    // shell exits. The logs are finished after this long whether or not the pipes closed.
    private static readonly TimeSpan DrainGrace = TimeSpan.FromSeconds(10);

    public async Task<int?> RunAsync(VerificationProcessStart start, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            WorkingDirectory = start.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(start.Command);
        foreach (var (name, value) in start.Environment)
        {
            startInfo.Environment[name] = value;
        }

        // An operator who withdrew before execution began approved nothing that should start.
        cancellationToken.ThrowIfCancellationRequested();

        // Open both logs before the child exists: an unwritable log must not leave an orphan.
        await using var standardOutput = File.Create(start.StandardOutputPath);
        await using var standardError = File.Create(start.StandardErrorPath);
        cancellationToken.ThrowIfCancellationRequested();
        using var process = Process.Start(startInfo)
            ?? throw new IOException($"Could not start /bin/sh for '{start.Command}'.");
        process.StandardInput.Close();

        var drains = Task.WhenAll(
            CopyBoundedAsync(process.StandardOutput.BaseStream, standardOutput),
            CopyBoundedAsync(process.StandardError.BaseStream, standardError));

        int? exitCode;
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            exitCode = process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            exitCode = null;
        }

        await Task.WhenAny(drains, Task.Delay(DrainGrace, CancellationToken.None)).ConfigureAwait(false);
        return exitCode;
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(KillGraceMilliseconds);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            // It exited between the cancellation and the kill.
        }
    }

    private static async Task CopyBoundedAsync(Stream source, Stream destination)
    {
        var buffer = new byte[81920];
        long kept = 0;
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer, CancellationToken.None).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                if (kept < LogCapBytes)
                {
                    var take = (int)Math.Min(read, LogCapBytes - kept);
                    await destination.WriteAsync(buffer.AsMemory(0, take), CancellationToken.None)
                        .ConfigureAwait(false);
                    kept += take;
                }
            }

            await destination.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // The pipe closed underneath the reader after a kill, or the log was finished after the
            // drain grace. What was written is kept.
        }
    }
}
