using System.Text.Json;
using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Cli;

// The command this whole change exists for. A verify was required to be runnable and never required
// to be able to fail, so a grep for a symbol present in both the defective and the repaired state
// passed either way and a lesson whose defect had since been fixed was still recalled as current.
// A recorded direction is what makes the check falsifiable, and lesson recheck is what reads it.
//
// It reads those rows in two steps, because the rows are shell commands written by whoever marked
// the lesson: listing shows what each would run and starts nothing, and running one takes the
// confirmation printed beside it.
public sealed class LessonRecheckCliTests
{
    private static readonly DateTimeOffset RecordedAt = new(2026, 9, 7, 12, 51, 51, TimeSpan.Zero);

    private const string StageArmsC1 = "2026-09-07_0957-stage-arms:validatedclaim:C1";

    // The acceptance case, named in the orchestration plan. The lesson is the real store row for
    // 2026-09-07_0957-stage-arms:validatedclaim:C1: it asserts that recall never reads the tags it
    // stores, and its verify greps for openingTags — which resolves today, at the code that refutes
    // it. With a direction of absent, the grep resolving is the evidence that the lesson is stale.
    //
    // The grep points at a fixture rather than at the live src/AILedger.Storage file on purpose. The
    // semantics under test are identical, and a test that greps the real tree would flip its verdict
    // the day someone renames that identifier for unrelated reasons.
    [Fact]
    public async Task ALessonWhoseVerifyStillResolvesAgainstADirectionOfAbsentIsReportedAsNoLongerHolding()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var lessonRoot = new TemporaryDirectory();
        using var workingDirectory = new TemporaryDirectory();
        var probed = Path.Combine(workingDirectory.Path, "FileGovernedTaskService.cs");
        await File.WriteAllTextAsync(
            probed,
            "var candidates = SelectRecalled(archived, openingTaskId, openingTags);" + Environment.NewLine);
        await PublishAsync(lessonRoot, StageArmsLesson(
            $"grep -n openingTags {probed}", VerifyExpectation.Absent));

        var report = await RecheckAsync(lessonRoot.Path, workingDirectory.Path, StageArmsC1);

        var result = Assert.Single(report.Results);
        Assert.Equal(StageArmsC1, result.Lesson);
        Assert.Equal(VerifyExpectation.Absent, result.Expects);
        Assert.Equal(VerifyObservation.Present, result.Observed);
        Assert.False(result.Holds);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, report.NoLongerHolding);
        Assert.Equal(0, report.Holding);
    }

    [Fact]
    public async Task ALessonWhoseVerifyComesOutTheWayItRecordedIsReportedAsHolding()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var lessonRoot = new TemporaryDirectory();
        using var workingDirectory = new TemporaryDirectory();
        var probed = Path.Combine(workingDirectory.Path, "FileGovernedTaskService.cs");
        await File.WriteAllTextAsync(probed, "var candidates = SelectByRecency(archived);" + Environment.NewLine);
        await PublishAsync(lessonRoot, StageArmsLesson(
            $"grep -n openingTags {probed}", VerifyExpectation.Absent));

        var report = await RecheckAsync(lessonRoot.Path, workingDirectory.Path, StageArmsC1);

        var result = Assert.Single(report.Results);
        Assert.Equal(VerifyObservation.Absent, result.Observed);
        Assert.True(result.Holds);
        Assert.Equal(1, report.Holding);
        Assert.Equal(0, report.NoLongerHolding);
    }

    // The command could not run at all, which is the third outcome and not the second. Reporting it
    // as a negative observation would report this absent-directed lesson as still holding on the
    // evidence that its verify was broken — a broken check turned into a confirmation, which is the
    // same false confirmation the direction field exists to remove, arrived at from the other side.
    [Fact]
    public async Task AVerifyThatCouldNotRunIsIndeterminateRatherThanAConfirmation()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var lessonRoot = new TemporaryDirectory();
        using var workingDirectory = new TemporaryDirectory();
        await PublishAsync(lessonRoot, StageArmsLesson(
            "ailedger-no-such-verify-command --check", VerifyExpectation.Absent));

        var report = await RecheckAsync(lessonRoot.Path, workingDirectory.Path, StageArmsC1);

        var result = Assert.Single(report.Results);
        Assert.Equal(VerifyObservation.Indeterminate, result.Observed);
        Assert.Null(result.Holds);
        Assert.Equal(127, result.ExitCode);
        Assert.Equal(1, report.Indeterminate);
        Assert.Equal(0, report.Holding);
        Assert.Equal(0, report.NoLongerHolding);
    }

    // The same defect in the form this store actually produces. A verify carries repository-relative
    // paths, and grep exits 2 with a diagnostic on standard error when the path is not there. That
    // is "I could not look", not "the symbol is gone", and the two must not be one result.
    [Fact]
    public async Task AVerifyWhoseOwnToolFailedIsIndeterminateRatherThanANegativeObservation()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var lessonRoot = new TemporaryDirectory();
        using var workingDirectory = new TemporaryDirectory();
        await PublishAsync(lessonRoot, StageArmsLesson(
            "grep -n openingTags no-such-file.cs", VerifyExpectation.Absent));

        var report = await RecheckAsync(lessonRoot.Path, workingDirectory.Path, StageArmsC1);

        var result = Assert.Single(report.Results);
        Assert.Equal(VerifyObservation.Indeterminate, result.Observed);
        Assert.Null(result.Holds);
        Assert.Equal(2, result.ExitCode);
        Assert.Equal(1, report.Indeterminate);
    }

    // The same defect in the exit code most tools use. Exit 1 is the canonical "found nothing", so it
    // was accepted as a negative observation before the streams were consulted — and a tool that
    // exits 1 to report its own failure was therefore read as evidence that the symbol is gone. Here
    // the file the verify names is not in the working directory: cat exits 1 with a diagnostic on
    // standard error and nothing on standard output, which is a command that could not look.
    [Fact]
    public async Task AVerifyThatExitsOneWithOnlyADiagnosticIsIndeterminateRatherThanAConfirmation()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var lessonRoot = new TemporaryDirectory();
        using var workingDirectory = new TemporaryDirectory();
        await PublishAsync(lessonRoot, StageArmsLesson(
            "cat no-such-file.cs", VerifyExpectation.Absent));

        var report = await RecheckAsync(lessonRoot.Path, workingDirectory.Path, StageArmsC1);

        var result = Assert.Single(report.Results);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal(VerifyObservation.Indeterminate, result.Observed);
        Assert.Null(result.Holds);
        Assert.Equal(1, report.Indeterminate);
        Assert.Equal(0, report.Holding);
    }

    // The other side of the same rule, and the one that must not move. A grep that ran and matched
    // nothing exits 1 with both streams silent, and that is still the negative the report is built
    // on: an absent-directed lesson checked this way is reported as holding.
    [Fact]
    public async Task ACleanNoMatchAtExitOneIsStillTheNegativeObservation()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var lessonRoot = new TemporaryDirectory();
        using var workingDirectory = new TemporaryDirectory();
        var probed = Path.Combine(workingDirectory.Path, "FileGovernedTaskService.cs");
        await File.WriteAllTextAsync(probed, "var candidates = SelectByRecency(archived);" + Environment.NewLine);
        await PublishAsync(lessonRoot, StageArmsLesson(
            $"grep -n openingTags {probed}", VerifyExpectation.Absent));

        var report = await RecheckAsync(lessonRoot.Path, workingDirectory.Path, StageArmsC1);

        var result = Assert.Single(report.Results);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal(VerifyObservation.Absent, result.Observed);
        Assert.True(result.Holds);
        Assert.Equal(1, report.Holding);
        Assert.Equal(0, report.Indeterminate);
    }

    // The whole mapping in one place. The ends of the range are decided by the number and everything
    // between by the streams, and a table is the only honest way to say that.
    [Theory]
    // A match, whether or not the tool prints one.
    [InlineData(0, true, false, VerifyObservation.Present)]
    [InlineData(0, false, false, VerifyObservation.Present)]
    // The canonical "ran and found nothing": exit 1 with both streams silent, or with a result.
    [InlineData(1, false, false, VerifyObservation.Absent)]
    [InlineData(1, true, false, VerifyObservation.Absent)]
    [InlineData(1, true, true, VerifyObservation.Absent)]
    // Exit 1 is also the code most tools use to report their own failure. A diagnostic and no result
    // is a command that could not look, and it is not special for being the most common code.
    [InlineData(1, false, true, VerifyObservation.Indeterminate)]
    // A tool reporting its own error: a diagnostic and no result is a command that could not look.
    [InlineData(2, false, true, VerifyObservation.Indeterminate)]
    [InlineData(2, true, false, VerifyObservation.Absent)]
    [InlineData(2, true, true, VerifyObservation.Absent)]
    // The shell's own failures: not executable, not found, and killed by a signal as 128 plus n.
    [InlineData(126, false, true, VerifyObservation.Indeterminate)]
    [InlineData(127, false, true, VerifyObservation.Indeterminate)]
    [InlineData(139, false, false, VerifyObservation.Indeterminate)]
    // The Windows shape of the same thing.
    [InlineData(-1073741819, false, false, VerifyObservation.Indeterminate)]
    public void AnExitStatusIsOneOfThreeOutcomesAndNotTwo(
        int exitCode,
        bool wroteOutput,
        bool wroteError,
        VerifyObservation expected) =>
        Assert.Equal(expected, LessonRecheck.Classify(exitCode, wroteOutput, wroteError));

    // The default is a read of the store, not an execution of it. A store row is a shell command
    // somebody else wrote, so an operator who asks what the repository's lessons check must be able
    // to ask without running any of them.
    [Fact]
    public async Task ListingShowsWhatEachRowWouldRunAndStartsNothing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var lessonRoot = new TemporaryDirectory();
        using var workingDirectory = new TemporaryDirectory();
        var sentinel = Path.Combine(workingDirectory.Path, "the-command-ran");
        await PublishAsync(lessonRoot, StageArmsLesson($"touch {sentinel}", VerifyExpectation.Present));

        var listing = await ListAsync(lessonRoot.Path, workingDirectory.Path);

        Assert.False(File.Exists(sentinel));
        var candidate = Assert.Single(listing.Lessons);
        Assert.Equal($"touch {sentinel}", candidate.Verify);
        Assert.Equal(VerifyExpectation.Present, candidate.Expects);
        Assert.NotNull(candidate.Confirm);
        Assert.Null(candidate.Reason);
        Assert.Equal(1, listing.Selected);
        Assert.Equal(1, listing.Runnable);
    }

    // The Blocker this repair closes. Naming the row is not approving it: an operator who has not
    // passed the confirmation back has not seen the text, so the command is never started — not
    // started and then stopped, and not started with its output discarded.
    [Fact]
    public async Task ARowThatWasNotConfirmedIsNeverStarted()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var lessonRoot = new TemporaryDirectory();
        using var workingDirectory = new TemporaryDirectory();
        var sentinel = Path.Combine(workingDirectory.Path, "the-command-ran");
        await PublishAsync(lessonRoot, StageArmsLesson($"touch {sentinel}", VerifyExpectation.Present));

        var output = new StringWriter();
        var exit = await Create(output, TextWriter.Null).RunAsync(
            ["lesson", "recheck", "--lesson-root", lessonRoot.Path, "--repo", "AILedger",
             "--working-directory", workingDirectory.Path, "--id", StageArmsC1],
            CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.False(File.Exists(sentinel));
        // What came back is the listing for that one row, not a result of running it.
        Assert.Contains("confirm", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("observed", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // A confirmation that does not match the stored text is the case that matters most: it is what
    // happens when the row changed between the listing and the run, or when the value was pasted
    // from somewhere else. Refused before a process exists.
    [Fact]
    public async Task AConfirmationThatDoesNotMatchTheStoredCommandRunsNothing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var lessonRoot = new TemporaryDirectory();
        using var workingDirectory = new TemporaryDirectory();
        var sentinel = Path.Combine(workingDirectory.Path, "the-command-ran");
        await PublishAsync(lessonRoot, StageArmsLesson($"touch {sentinel}", VerifyExpectation.Present));
        var error = new StringWriter();

        var exit = await Create(TextWriter.Null, error).RunAsync(
            ["lesson", "recheck", "--lesson-root", lessonRoot.Path, "--repo", "AILedger",
             "--working-directory", workingDirectory.Path, "--id", StageArmsC1,
             "--confirm", LessonRecheck.Confirmation("touch somewhere-else")],
            CancellationToken.None);

        Assert.Equal(2, exit);
        Assert.False(File.Exists(sentinel));
        Assert.Contains("confirmation", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // The other half of the same rule: the confirmation from the listing runs the row it named, and
    // the command really does execute. Without this the refusals above would be satisfied by a
    // command that never runs anything at all.
    [Fact]
    public async Task TheConfirmationFromTheListingRunsThatRow()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var lessonRoot = new TemporaryDirectory();
        using var workingDirectory = new TemporaryDirectory();
        var sentinel = Path.Combine(workingDirectory.Path, "the-command-ran");
        await PublishAsync(lessonRoot, StageArmsLesson($"touch {sentinel}", VerifyExpectation.Present));

        var report = await RecheckAsync(lessonRoot.Path, workingDirectory.Path, StageArmsC1);

        Assert.True(File.Exists(sentinel));
        var result = Assert.Single(report.Results);
        Assert.Equal(VerifyObservation.Present, result.Observed);
        Assert.True(result.Holds);
    }

    // Cancelling must stop the command, not stop waiting for it. The confirmed text runs with the
    // operator's privileges, so a CLI that reports it stopped while the shell and everything the
    // shell spawned keep running takes away the operator's last control after execution has begun.
    [Fact]
    public async Task CancellingAConfirmedRunLeavesNothingOfItRunning()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var lessonRoot = new TemporaryDirectory();
        using var workingDirectory = new TemporaryDirectory();
        var started = Path.Combine(workingDirectory.Path, "the-command-started");
        var survived = Path.Combine(workingDirectory.Path, "a-child-outlived-the-cancellation");
        // The second marker is written by a grandchild: the verify backgrounds a second shell and
        // waits for it. Killing only the direct child leaves that shell alive to write the marker
        // two seconds later, so this marker is exactly the difference between ending the process
        // tree and ending the process.
        await PublishAsync(lessonRoot, StageArmsLesson(
            $"sh -c 'sleep 2; touch {survived}' & touch {started}; wait", VerifyExpectation.Present));

        var listing = await ListAsync(lessonRoot.Path, workingDirectory.Path);
        var confirm = Assert.Single(listing.Lessons).Confirm;
        Assert.NotNull(confirm);
        using var operatorCancelled = new CancellationTokenSource();
        var error = new StringWriter();
        var run = Create(TextWriter.Null, error).RunAsync(
            ["lesson", "recheck", "--lesson-root", lessonRoot.Path, "--repo", "AILedger",
             "--working-directory", workingDirectory.Path, "--id", StageArmsC1, "--confirm", confirm],
            operatorCancelled.Token);

        await WaitForFileAsync(started);
        await operatorCancelled.CancelAsync();
        var exit = await run;

        Assert.Equal(130, exit);
        Assert.Contains("Cancelled", error.ToString(), StringComparison.Ordinal);
        // Well past the sleep the backgrounded shell was in when the cancellation arrived. If it
        // was only abandoned rather than killed, the marker is here by now.
        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.False(File.Exists(survived));
    }

    // The other end of the same rule. A token already cancelled when the run begins must start
    // nothing at all: observing it only after the process exists means running the command and
    // then chasing it, which is not the same as never having run it.
    [Fact]
    public async Task AConfirmedRunCancelledBeforeItBeganStartsNothing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var workingDirectory = new TemporaryDirectory();
        var sentinel = Path.Combine(workingDirectory.Path, "the-command-ran");
        var lesson = StageArmsLesson($"touch {sentinel}", VerifyExpectation.Present);
        using var operatorCancelled = new CancellationTokenSource();
        await operatorCancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LessonRecheck.RunAsync(
            lesson,
            "AILedger",
            workingDirectory.Path,
            LessonRecheck.DefaultTimeout,
            operatorCancelled.Token));

        // Long enough for a touch that was started to have finished. Asserting the moment the
        // exception arrives would pass against a version that starts the command and abandons it,
        // which is the version this test exists to exclude.
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        Assert.False(File.Exists(sentinel));
    }

    // One command per confirmation. A confirmation naming no row, or covering several, would be the
    // sweep approval again under another name.
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ConfirmingAnythingOtherThanOneNamedRowIsRefused(int namedRows)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var lessonRoot = new TemporaryDirectory();
        using var workingDirectory = new TemporaryDirectory();
        var sentinel = Path.Combine(workingDirectory.Path, "the-command-ran");
        var verify = $"touch {sentinel}";
        await PublishAsync(
            lessonRoot,
            StageArmsLesson(verify, VerifyExpectation.Present, "C1"),
            StageArmsLesson(verify, VerifyExpectation.Present, "C2"));
        var arguments = new List<string>
        {
            "lesson", "recheck", "--lesson-root", lessonRoot.Path, "--repo", "AILedger",
            "--working-directory", workingDirectory.Path
        };
        if (namedRows == 2)
        {
            arguments.AddRange(["--id", StageArmsC1]);
            arguments.AddRange(["--id", "2026-09-07_0957-stage-arms:validatedclaim:C2"]);
        }

        arguments.AddRange(["--confirm", LessonRecheck.Confirmation(verify)]);
        var error = new StringWriter();

        var exit = await Create(TextWriter.Null, error).RunAsync([.. arguments], CancellationToken.None);

        Assert.Equal(2, exit);
        Assert.False(File.Exists(sentinel));
        Assert.Contains("one --id", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // Every one of these rows says nothing about whether it still holds, so reporting it as drifted
    // would be inventing a finding. All 166 lessons already minted are the second case. They carry a
    // reason and no confirmation, which is also what makes them unrunnable: there is no value an
    // operator could pass back for a row with nothing to run.
    [Fact]
    public async Task ARowThatCannotBeRecheckedCarriesAReasonRatherThanAConfirmation()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var lessonRoot = new TemporaryDirectory();
        using var workingDirectory = new TemporaryDirectory();
        await PublishAsync(
            lessonRoot,
            StageArmsLesson("grep -n openingTags nowhere.cs", null, "C1"),
            StageArmsLesson("none - this is a scope judgement, not a fact about the code",
                VerifyExpectation.Present, "C2"),
            StageArmsLesson(null, null, "C3"));

        var listing = await ListAsync(lessonRoot.Path, workingDirectory.Path);

        Assert.Equal(3, listing.Selected);
        Assert.Equal(0, listing.Runnable);
        Assert.All(listing.Lessons, candidate => Assert.Null(candidate.Confirm));
        Assert.Contains("no direction", Reason(listing, "C1"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nothing to run", Reason(listing, "C2"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no verify command", Reason(listing, "C3"), StringComparison.OrdinalIgnoreCase);
    }

    // A verify carries repository-relative paths, so another repository's lessons run from this
    // working directory would all report as no longer holding on the evidence that its files are
    // not here. --repo is what keeps that finding from being manufactured.
    [Fact]
    public async Task OnlyTheNamedRepositorysLessonsAreListed()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var lessonRoot = new TemporaryDirectory();
        using var workingDirectory = new TemporaryDirectory();
        await PublishAsync(
            lessonRoot,
            StageArmsLesson("grep -n absent-everywhere nowhere.cs", VerifyExpectation.Present, "C1"),
            StageArmsLesson("grep -n absent-everywhere nowhere.cs", VerifyExpectation.Present, "C2",
                repo: "cymulate-integration-adapters"));

        var listing = await ListAsync(lessonRoot.Path, workingDirectory.Path);

        Assert.Equal("AILedger", listing.Repo);
        Assert.Equal("C1", Assert.Single(listing.Lessons).Lesson.Split(':')[^1]);
    }

    // Rechecking one row rather than a repository's whole set, which is what an operator does after
    // repairing the defect a single lesson describes.
    [Fact]
    public async Task NamingALessonIdRunsOnlyThatRow()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var lessonRoot = new TemporaryDirectory();
        using var workingDirectory = new TemporaryDirectory();
        await PublishAsync(
            lessonRoot,
            StageArmsLesson("grep -n absent-everywhere nowhere.cs", VerifyExpectation.Absent, "C1"),
            StageArmsLesson("grep -n absent-everywhere nowhere.cs", VerifyExpectation.Absent, "C2"));

        var report = await RecheckAsync(
            lessonRoot.Path, workingDirectory.Path, "2026-09-07_0957-stage-arms:validatedclaim:C2");

        Assert.Equal("C2", Assert.Single(report.Results).Lesson.Split(':')[^1]);
        Assert.Equal(1, report.Rechecked);
    }

    // It is a read. Nothing about the store changes, and there is no ledger write for it to make:
    // the command takes no --task and no --actor at all.
    [Fact]
    public async Task RecheckWritesNothingBackToTheStore()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var lessonRoot = new TemporaryDirectory();
        using var workingDirectory = new TemporaryDirectory();
        await PublishAsync(lessonRoot, StageArmsLesson(
            "grep -n openingTags nowhere.cs", VerifyExpectation.Absent));
        var storePath = Path.Combine(lessonRoot.Path, FileLessonStore.LessonsFileName);
        var before = await File.ReadAllTextAsync(storePath);

        await RecheckAsync(lessonRoot.Path, workingDirectory.Path, StageArmsC1);

        Assert.Equal(before, await File.ReadAllTextAsync(storePath));
    }

    [Fact]
    public async Task RecheckRefusesTheTaskAndActorOptionsBecauseItGovernsNothing()
    {
        using var lessonRoot = new TemporaryDirectory();
        var error = new StringWriter();

        var exit = await Create(TextWriter.Null, error).RunAsync(
            ["lesson", "recheck", "--lesson-root", lessonRoot.Path, "--repo", "AILedger",
             "--task", "T1", "--actor", "operator"],
            CancellationToken.None);

        Assert.Equal(2, exit);
        Assert.Contains("task", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // Cancelling before the process exists proves nothing about killing one, so the cancellation
    // test waits for the command's own first marker rather than for a duration.
    private static async Task WaitForFileAsync(string path)
    {
        for (var attempt = 0; attempt < 100 && !File.Exists(path); attempt++)
        {
            await Task.Delay(50);
        }

        Assert.True(File.Exists(path), $"The verify never started: '{path}' was not created.");
    }

    private static string Reason(LessonRecheckListing listing, string sourceRecordId) =>
        listing.Lessons.Single(candidate =>
            candidate.Lesson.EndsWith($":{sourceRecordId}", StringComparison.Ordinal)).Reason!;

    private static async Task<LessonRecheckListing> ListAsync(string lessonRoot, string workingDirectory)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = await Create(output, error).RunAsync(
            ["lesson", "recheck", "--lesson-root", lessonRoot, "--repo", "AILedger",
             "--working-directory", workingDirectory],
            CancellationToken.None);

        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal(0, exit);
        return JsonSerializer.Deserialize<LessonRecheckListing>(
            output.ToString(), LedgerJson.CreateOptions())!;
    }

    // The operator's two steps, as the operator does them: list the rows, then run the one named
    // row with the confirmation the listing printed beside it. Taking the confirmation from the
    // listing rather than computing it here is the point — it is the round trip that is under test.
    private static async Task<LessonRecheckReport> RecheckAsync(
        string lessonRoot,
        string workingDirectory,
        string lessonId)
    {
        var listing = await ListAsync(lessonRoot, workingDirectory);
        var confirm = listing.Lessons.Single(candidate =>
            string.Equals(candidate.Lesson, lessonId, StringComparison.Ordinal)).Confirm;
        Assert.NotNull(confirm);

        var output = new StringWriter();
        var error = new StringWriter();
        var exit = await Create(output, error).RunAsync(
            ["lesson", "recheck", "--lesson-root", lessonRoot, "--repo", "AILedger",
             "--working-directory", workingDirectory, "--id", lessonId, "--confirm", confirm],
            CancellationToken.None);

        Assert.Equal(string.Empty, error.ToString());
        // The report is a read, not a gate: what it found never changes the exit code.
        Assert.Equal(0, exit);
        return JsonSerializer.Deserialize<LessonRecheckReport>(
            output.ToString(), LedgerJson.CreateOptions())!;
    }

    private static async Task PublishAsync(TemporaryDirectory lessonRoot, params Lesson[] lessons) =>
        await new FileLessonStore(lessonRoot.Path).PublishAsync(lessons, CancellationToken.None);

    // The real store row, field for field, with only the verify path and the direction supplied by
    // the test.
    private static Lesson StageArmsLesson(
        string? verify,
        VerifyExpectation? expects,
        string sourceRecordId = "C1",
        string repo = "AILedger") =>
        new(
            new LessonId($"2026-09-07_0957-stage-arms:validatedclaim:{sourceRecordId}"),
            new TaskId("2026-09-07_0957-stage-arms"),
            LessonSourceKind.ValidatedClaim,
            sourceRecordId,
            "Recall selects lessons by recency alone and never reads the Tags it stores, so a lesson " +
            "reaches a task it has nothing to do with and is withheld from the task it was earned for",
            "Validated",
            ["src/AILedger.Storage/FileGovernedTaskService.cs:117-141"],
            new Provenance(new ActorId("operator"), RecordedAt, "stage.archive"),
            null,
            LessonClass.Refuted,
            repo,
            ["lessons", "recall", "ailedger-kernel"],
            verify,
            "do not assume a stored field is used; grep for a read of it before trusting that recall consults it",
            LessonActor.Verifier,
            LessonKind.Workflow,
            null,
            expects);

    private static CliApplication Create(TextWriter output, TextWriter error) => new(
        output,
        error,
        (root, lessonRoot) =>
        {
            var reducer = new TaskReducer();
            return new FileGovernedTaskService(
                root, new CommandHandler(reducer, new AuthorizationPolicy()), reducer,
                lessonStore: new FileLessonStore(lessonRoot));
        },
        _ => throw new InvalidOperationException("Provider adapter is not used by this test."),
        new ContextAssembler());
}
