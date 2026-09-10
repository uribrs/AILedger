using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using AILedger.Core.Application;
using AILedger.Core.Contracts;

namespace AILedger.Cli;

/// <summary>
/// What running one lesson's verify command found. There are three outcomes and not two: the
/// command ran and matched, the command ran and matched nothing, and the command could not run.
/// Present is the first — a grep that matched, a path that exists, a test that passed. Absent is the
/// second, and it is the only negative that is evidence. Indeterminate is the third: a command that
/// was not found, was not executable, was killed, failed inside itself, or outlasted its deadline
/// observed nothing at all, so it agrees with neither direction and is never reported as a lesson
/// that holds or as one that stopped holding.
/// </summary>
public enum VerifyObservation
{
    Present,
    Absent,
    Indeterminate
}

public sealed record LessonRecheckResult(
    string Lesson,
    VerifyExpectation Expects,
    string Verify,
    VerifyObservation Observed,
    bool? Holds,
    int? ExitCode,
    string? Output);

public sealed record LessonRecheckSkip(string Lesson, string Reason);

/// <summary>
/// One selected row as the listing shows it: what it would run, which way it has to come out, and
/// the confirmation an operator passes back to run it. Confirm is null for a row that cannot be
/// rechecked at all, and Reason then says why.
/// </summary>
public sealed record LessonRecheckCandidate(
    string Lesson,
    string? Verify,
    VerifyExpectation? Expects,
    string? Confirm,
    string? Reason);

public sealed record LessonRecheckListing(
    string Repo,
    string WorkingDirectory,
    int Selected,
    int Runnable,
    IReadOnlyList<LessonRecheckCandidate> Lessons);

public sealed record LessonRecheckReport(
    string Repo,
    string WorkingDirectory,
    int Rechecked,
    int Holding,
    int NoLongerHolding,
    int Indeterminate,
    int Skipped,
    IReadOnlyList<LessonRecheckResult> Results,
    IReadOnlyList<LessonRecheckSkip> SkippedLessons);

/// <summary>
/// Runs the verify command a marked lesson carries and reports whether the direction it recorded
/// still holds. It writes no event, no projection and no lesson row — it takes the store's lock to
/// read, as every reader does, and changes nothing — and it runs only when an operator asks for it:
/// nothing on the recall path, the context-build path or any stage transition reaches it. That is
/// deliberate. A gate that executed strings from a shared store on an agent's behalf would be both
/// a remote-execution surface and a gate an agent could optimise.
/// </summary>
/// <remarks>
/// It runs one named row at a time, and only against a confirmation of the exact text it will run.
/// A verify is a caller-supplied field on a mark, so the store is a list of shell commands written
/// by whoever marked the lesson; a sweep that ran all of them would make the operator's approval of
/// the sweep stand in for approval of commands nobody read. The two are not the same property.
/// Listing is therefore the default and executes nothing, and execution is refused unless the
/// operator passes back the confirmation printed beside the command — which is evidence that the
/// text on screen is the text that will run, where a yes-or-no prompt would not be.
/// </remarks>
public static class LessonRecheck
{
    // Enough for a grep over a repository, short enough that a hung command does not hold the whole
    // report. A command that outlasts it is reported Indeterminate rather than failed.
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    // One line, bounded. The report is a summary, and an unbounded line from a verify that happens
    // to print a whole file is how a run ends with nothing recorded at all.
    private const int OutputPreviewCharacters = 200;
    private const int OutputCaptureCharacters = 4096;

    // Short enough for an operator to compare by eye and retype, long enough that two different
    // commands do not share one. It identifies a command; it is not a security boundary of its own,
    // because the operator reads the command text beside it.
    private const int ConfirmationCharacters = 12;

    // How long a killed tree is given to be gone before the report is written anyway. It is a
    // SIGKILL, so this is the margin for reaping rather than a grace period to shut down in.
    private const int KillGraceMilliseconds = 5000;

    /// <summary>
    /// What an operator passes back to run one row: the first characters of the SHA-256 of the exact
    /// verify text, as printed beside that text in the listing.
    /// </summary>
    public static string Confirmation(string verify) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(verify)))
            .ToLowerInvariant()[..ConfirmationCharacters];

    /// <summary>
    /// The selected rows and what each would run. Nothing is started here: this is the read an
    /// operator does before deciding which single command is worth running.
    /// </summary>
    public static LessonRecheckListing List(
        IReadOnlyList<Lesson> lessons,
        string repo,
        string workingDirectory,
        IReadOnlyList<string> onlyLessonIds)
    {
        var candidates = Select(lessons, repo, onlyLessonIds)
            .Select(lesson =>
            {
                var reason = SkipReason(lesson);
                return new LessonRecheckCandidate(
                    lesson.Id.Value,
                    lesson.Verify,
                    lesson.VerifyExpects,
                    reason is null ? Confirmation(lesson.Verify!) : null,
                    reason);
            })
            .ToArray();

        return new LessonRecheckListing(
            repo,
            workingDirectory,
            candidates.Length,
            candidates.Count(candidate => candidate.Confirm is not null),
            candidates);
    }

    /// <summary>
    /// The one row the operator named and confirmed, or a refusal. Every path out of here that is
    /// not the named, runnable, confirmed row throws before a process exists.
    /// </summary>
    public static Lesson RequireConfirmed(
        IReadOnlyList<Lesson> lessons,
        string repo,
        IReadOnlyList<string> onlyLessonIds,
        string confirmation)
    {
        if (onlyLessonIds.Count != 1)
        {
            throw new CliUsageException(
                "--confirm runs one stored command, so exactly one --id is required. Run without " +
                "--confirm to list the rows and their confirmations.");
        }

        var lesson = Select(lessons, repo, onlyLessonIds).SingleOrDefault()
            ?? throw new CliUsageException(
                $"Lesson '{onlyLessonIds[0]}' was not found for repository '{repo}'.");

        if (SkipReason(lesson) is { } reason)
        {
            throw new CliUsageException($"Lesson '{lesson.Id.Value}' cannot be rechecked: {reason}.");
        }

        var expected = Confirmation(lesson.Verify!);
        if (!string.Equals(confirmation.Trim(), expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new CliUsageException(
                $"The confirmation for lesson '{lesson.Id.Value}' does not match the command on record, " +
                "so nothing was run. List the row and pass back the confirmation printed beside its verify.");
        }

        return lesson;
    }

    /// <summary>
    /// Runs one confirmed row. The report keeps the shape it always had, holding the single result.
    /// </summary>
    public static async Task<LessonRecheckReport> RunAsync(
        Lesson lesson,
        string repo,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var (observed, exitCode, output) =
            await ObserveAsync(lesson.Verify!, workingDirectory, timeout, cancellationToken)
                .ConfigureAwait(false);
        var expects = lesson.VerifyExpects!.Value;
        var result = new LessonRecheckResult(
            lesson.Id.Value,
            expects,
            lesson.Verify!,
            observed,
            observed == VerifyObservation.Indeterminate
                ? null
                : LessonVerification.Holds(expects, observed == VerifyObservation.Present),
            exitCode,
            output);

        return new LessonRecheckReport(
            repo,
            workingDirectory,
            1,
            result.Holds == true ? 1 : 0,
            result.Holds == false ? 1 : 0,
            result.Holds is null ? 1 : 0,
            0,
            [result],
            []);
    }

    private static Lesson[] Select(
        IReadOnlyList<Lesson> lessons,
        string repo,
        IReadOnlyList<string> onlyLessonIds) =>
        lessons
            .Where(lesson => string.Equals(lesson.Repo, repo, StringComparison.OrdinalIgnoreCase))
            .Where(lesson => onlyLessonIds.Count == 0 ||
                onlyLessonIds.Contains(lesson.Id.Value, StringComparer.Ordinal))
            .OrderBy(lesson => lesson.Id.Value, StringComparer.Ordinal)
            .ToArray();

    // Why a row cannot be rechecked, or null when it can. Every one of these is a row that says
    // nothing about whether it still holds, so reporting it as drifted would be inventing a finding.
    private static string? SkipReason(Lesson lesson)
    {
        if (lesson.Verify is null)
        {
            return "the row carries no verify command";
        }

        if (LessonVerification.ClaimsNothingToRun(lesson.Verify))
        {
            return "the verify records that there is nothing to run";
        }

        return lesson.VerifyExpects is null
            ? "the row records no direction for its verify, so running it cannot fail"
            : null;
    }

    private static async Task<(VerifyObservation Observed, int? ExitCode, string? Output)> ObserveAsync(
        string verify,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false
        };
        // The verify is one shell command line, written to be pasted into a terminal, so it is
        // handed to a shell rather than split into an argument list here.
        if (OperatingSystem.IsWindows())
        {
            startInfo.FileName = "cmd.exe";
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(verify);
        }
        else
        {
            startInfo.FileName = "/bin/sh";
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(verify);
        }

        // Before the process exists, not after. A token already cancelled when the run begins must
        // start nothing at all: an operator who withdrew before execution began did not approve a
        // command that is started and then chased down.
        cancellationToken.ThrowIfCancellationRequested();

        using var process = Process.Start(startInfo)
            ?? throw new CliUsageException($"Could not start a shell to run '{verify}'.");
        // Closed immediately: a verify that reads standard input would otherwise wait for an
        // operator who is not there, and every one of them would then time out.
        process.StandardInput.Close();

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var standardOutput = ReadBoundedAsync(process.StandardOutput, deadline.Token);
        var standardError = ReadBoundedAsync(process.StandardError, deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Either source ends the same way, because the command is running either way. A
            // cancellation that only stopped waiting would leave the shell and everything it
            // spawned running with the operator's privileges after the CLI reported it stopped,
            // and this command exists to keep that execution under the operator's control.
            Kill(process);
            await Task.WhenAll(standardOutput, standardError).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                // The operator withdrew. There is no observation to report — a killed command
                // saw nothing, which is neither Present nor Absent — so the run ends rather
                // than returning a result nobody asked to be produced.
                throw;
            }

            return (VerifyObservation.Indeterminate, null, $"no result within {timeout.TotalSeconds:0} seconds");
        }

        var output = await standardOutput.ConfigureAwait(false);
        var error = await standardError.ConfigureAwait(false);
        var wroteOutput = !string.IsNullOrWhiteSpace(output);
        var wroteError = !string.IsNullOrWhiteSpace(error);
        return (
            Classify(process.ExitCode, wroteOutput, wroteError),
            process.ExitCode,
            Preview(wroteOutput ? output : error));
    }

    /// <summary>
    /// Which of the three outcomes an exit status is, and why the middle of the range is decided by
    /// the streams rather than by the number.
    /// </summary>
    /// <remarks>
    /// Zero is the match.
    /// <para>
    /// A shell reports two failures that are its own rather than the command's: 126 is found but not
    /// executable, 127 is not found. So is anything at or above 128, because a command killed by
    /// signal n exits 128 plus n and a process that was killed did not finish looking. A negative
    /// code is the Windows shape of the same thing. None of those observed anything.
    /// </para>
    /// <para>
    /// Every other nonzero code — 1 through 125 — is decided by the streams and not by the number.
    /// One is the canonical "ran and found nothing" that grep, test and the path checks a verify is
    /// written from all use, and 2 through 125 is where a real tool reports its own error: grep
    /// exits 2 for an unreadable path or a bad expression, which means "I could not look", not "the
    /// symbol is gone". But one is also the code most tools use to report their own failure, so the
    /// number separates the two nowhere in the range. A command that wrote a diagnostic to standard
    /// error and produced no result on standard output could not do its job, whatever it exited
    /// with. Anything else in the range is read as a negative, so a clean no-match — exit 1, both
    /// streams silent — stays the evidence it has always been.
    /// </para>
    /// <para>
    /// The bias is deliberate and runs one way. An outcome wrongly called indeterminate costs the
    /// operator a rerun; an outcome wrongly called absent reports a broken check as a confirmation,
    /// which is the failure this command exists to remove.
    /// </para>
    /// </remarks>
    public static VerifyObservation Classify(int exitCode, bool wroteOutput, bool wroteError) =>
        exitCode switch
        {
            0 => VerifyObservation.Present,
            < 0 or >= 126 => VerifyObservation.Indeterminate,
            _ => wroteError && !wroteOutput ? VerifyObservation.Indeterminate : VerifyObservation.Absent
        };

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            // Kill signals the tree; it does not wait for it. Returning before the child is reaped
            // would report a stopped command while it is still running, which is the whole defect.
            // The wait is bounded so an unreapable child delays the report rather than hanging it,
            // and it is the overload that takes a timeout because that one does not also wait for
            // the redirected streams to close.
            process.WaitForExit(KillGraceMilliseconds);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            // It exited between the cancellation and the kill. Nothing to stop.
        }
    }

    // Drains the stream to the end so the child never blocks on a full pipe, and keeps only the
    // first few thousand characters of it.
    private static async Task<string> ReadBoundedAsync(TextReader reader, CancellationToken cancellationToken)
    {
        var kept = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            int read;
            try
            {
                read = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (read == 0)
            {
                break;
            }

            if (kept.Length < OutputCaptureCharacters)
            {
                kept.Append(buffer, 0, Math.Min(read, OutputCaptureCharacters - kept.Length));
            }
        }

        return kept.ToString();
    }

    private static string? Preview(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var line = text.Split('\n', 2)[0].TrimEnd('\r');
        return line.Length <= OutputPreviewCharacters
            ? line
            : string.Concat(line.AsSpan(0, OutputPreviewCharacters), "…");
    }
}
