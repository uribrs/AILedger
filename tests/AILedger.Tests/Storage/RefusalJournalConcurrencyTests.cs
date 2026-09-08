using System.Diagnostics;
using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Storage;

// The provider-launch site holds no mutation lease, so its journal writer has to serialise itself.
// These two tests cover that from both ends: one proves the lock is taken and the waiting append
// lands once it is free, the other proves what a second operating-system process sees while the lock
// is held — nothing written, and its own bounded wait honoured rather than the mutation lock's.
//
// Both are deterministic, and that is the point. The cross-process test this file used to carry
// raced fifty appends from two processes and detected the lock-free writer only four times in five
// (IC7), so it could report green on code that loses rows — the shape of the four tests that passed
// while proving nothing in lesson kernel-version-stamp:LC20. D6 replaced it with the test below,
// which holds the lock deliberately instead of hoping two processes collide.
//
// The writer under test runs in a separate process, spawned from this test assembly (see Program.cs),
// because that is the condition the lock exists for: two writers inside one process were never
// observed to lose a row (VE5), so a same-process race passes against a lock-free writer as well.
public sealed class RefusalJournalConcurrencyTests
{
    // One row is enough once the test is deterministic: the question is whether the row appears while
    // another process holds the lock, not how many rows survive a race.
    private const int ProbeRows = 1;
    // The journal bounds its own wait at five seconds, well under TaskMutationLock's thirty second
    // deadline, because on a refusal path the operator is already waiting to be told what was
    // refused. Both ends of that are asserted: it must wait, and it must not wait the deadline.
    private static readonly TimeSpan MinimumObservedWait = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MaximumObservedWait = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task AnotherProcessAppendingWhileTheTaskLockIsHeldWritesNoRowAndStopsAtItsOwnBound()
    {
        using var root = new TemporaryDirectory();
        var taskDirectory = Path.Combine(root.Path, "refusal-held-lock-task");
        Directory.CreateDirectory(taskDirectory);
        var journalPath = RefusalJournal.ResolvePath(taskDirectory);

        // Taken before the probe is told to go, so the contention is arranged rather than raced. It
        // is the task's own lock file, opened the way TaskMutationLock opens it, so the probe sees
        // exactly what a command running against this task in another process would create.
        var heldLock = new FileStream(
            Path.Combine(taskDirectory, new TaskWorkspaceLayout().LockFileName),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);
        TimeSpan blockedProbeDuration;
        try
        {
            blockedProbeDuration = await RunProbeAsync(root.Path, taskDirectory, "launch-blocked", "start-blocked");

            // The row is dropped, not queued: the writer swallows its expired bound, because failing
            // the caller would replace the governance message the operator needs with an I/O error
            // about a telemetry file.
            Assert.False(File.Exists(journalPath));
            // Two bounds, and both matter. Below the minimum the probe wrote through the held lock
            // rather than waiting for it; above the maximum it waited the mutation lock's own
            // deadline, which is the stall the five second bound exists to prevent.
            Assert.True(
                blockedProbeDuration >= MinimumObservedWait,
                $"The probe returned after {blockedProbeDuration.TotalSeconds:F1}s, which is too fast to have " +
                "waited for the held lock at all.");
            Assert.True(
                blockedProbeDuration < MaximumObservedWait,
                $"The probe returned after {blockedProbeDuration.TotalSeconds:F1}s, so it waited past the " +
                "journal's own bound and delayed the refusal it was recording.");
        }
        finally
        {
            await heldLock.DisposeAsync();
        }

        // The pairing the absence above needs: the same probe binary, against the same task
        // directory, once the lock is free. Without this the test would pass against a writer that
        // appends nothing under any condition.
        var clearProbeDuration = await RunProbeAsync(root.Path, taskDirectory, "launch-clear", "start-clear");

        Assert.True(clearProbeDuration < MaximumObservedWait);
        Assert.Equal(
            ("launch-clear", Program.ProbeMessage("launch-clear", 0)),
            ParseRow(Assert.Single(await File.ReadAllLinesAsync(journalPath)), 0));
    }

    // The same rule from the other side, in one process and without a probe: the append that was
    // waiting is the one that lands, so the lock delays a row rather than losing it. Cheap enough to
    // keep beside the cross-process test, and it fails in a different place if the lock is dropped.
    [Fact]
    public async Task TheLaunchSiteWriterWaitsForAHeldTaskLockRatherThanWritingThroughIt()
    {
        using var root = new TemporaryDirectory();
        var taskDirectory = Path.Combine(root.Path, "refusal-lock-task");
        Directory.CreateDirectory(taskDirectory);
        var journalPath = RefusalJournal.ResolvePath(taskDirectory);
        var refusal = new RefusalRecord(
            DateTimeOffset.UtcNow,
            new ActorId("launch-alpha"),
            "provider launch",
            RefusalSite.ProviderLaunch,
            7,
            "Actor 'launch-alpha' holds no run authority for work item 'W1'.");

        var heldLock = new FileStream(
            Path.Combine(taskDirectory, new TaskWorkspaceLayout().LockFileName),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);
        var append = new RefusalJournal().TryAppendUnderTaskLockAsync(taskDirectory, refusal);

        await Task.Delay(TimeSpan.FromMilliseconds(250));
        // On its own this proves nothing — an unwritten journal is also what a dead writer leaves.
        // It is the pair with the row below that says the writer was waiting rather than absent.
        Assert.False(File.Exists(journalPath));

        await heldLock.DisposeAsync();
        await append.WaitAsync(TimeSpan.FromSeconds(30));

        var row = Assert.Single(await File.ReadAllLinesAsync(journalPath));
        Assert.Equal(("launch-alpha", refusal.Message), ParseRow(row, 0));
    }

    // Starts the probe, releases it, and returns how long it took from being released to exiting.
    // The marker is written after the process exists, so the measurement is the append and its wait
    // rather than the runtime's start-up.
    private static async Task<TimeSpan> RunProbeAsync(
        string markerRoot,
        string taskDirectory,
        string writerName,
        string markerName)
    {
        var startMarkerPath = Path.Combine(markerRoot, markerName);
        var probe = StartProbe(taskDirectory, startMarkerPath, writerName);
        try
        {
            var elapsed = Stopwatch.StartNew();
            await File.WriteAllTextAsync(startMarkerPath, "go");
            await AssertProbeExitedCleanlyAsync(probe);
            return elapsed.Elapsed;
        }
        finally
        {
            if (!probe.HasExited)
            {
                probe.Kill(entireProcessTree: true);
            }

            probe.Dispose();
        }
    }

    private static Process StartProbe(string taskDirectory, string startMarkerPath, string writerName)
    {
        var assemblyPath = typeof(Program).Assembly.Location;
        var startInfo = new ProcessStartInfo(DotnetMuxerPath())
        {
            WorkingDirectory = Path.GetDirectoryName(assemblyPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        // The assembly is an executable, so its runtimeconfig.json travels with it into whichever
        // output directory a host runs the tests from, and dotnet exec finds it unaided.
        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add(assemblyPath);
        startInfo.ArgumentList.Add(Program.ProbeArgument);
        startInfo.ArgumentList.Add(taskDirectory);
        startInfo.ArgumentList.Add(startMarkerPath);
        startInfo.ArgumentList.Add(writerName);
        startInfo.ArgumentList.Add(ProbeRows.ToString());

        return Process.Start(startInfo) ?? throw new InvalidOperationException(
            $"The probe process for '{writerName}' did not start.");
    }

    private static async Task AssertProbeExitedCleanlyAsync(Process probe)
    {
        var output = probe.StandardOutput.ReadToEndAsync();
        var error = probe.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await probe.WaitForExitAsync(timeout.Token);
        // A probe that cannot load the assembly exits non-zero and says why on standard error.
        // Carrying that text into the assertion is the difference between a diagnosable failure and
        // a mystery about a process nobody can see.
        Assert.True(
            probe.ExitCode == 0,
            $"The probe exited {probe.ExitCode}. Error: {(await error).Trim()} Output: {(await output).Trim()}");
    }

    // The muxer that is running this test: System.Private.CoreLib sits in
    // <root>/shared/Microsoft.NETCore.App/<version>, so the executable is three levels above it.
    private static string DotnetMuxerPath()
    {
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (runtimeDirectory is null)
        {
            return "dotnet";
        }

        var candidate = Path.Combine(
            Path.GetFullPath(Path.Combine(runtimeDirectory, "..", "..", "..")),
            OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        return File.Exists(candidate) ? candidate : "dotnet";
    }

    private static (string Writer, string Message) ParseRow(string line, int lineNumber)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Row {lineNumber + 1} is not one complete JSON object: '{line}'.", exception);
        }

        using (document)
        {
            var element = document.RootElement;
            Assert.Equal("provider-launch", element.GetProperty("site").GetString());
            return (element.GetProperty("actorId").GetString()!, element.GetProperty("message").GetString()!);
        }
    }
}
