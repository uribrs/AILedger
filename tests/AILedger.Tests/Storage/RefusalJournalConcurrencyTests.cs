using System.Diagnostics;
using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Storage;

// The provider-launch site holds no mutation lease, so its journal writer has to serialise itself.
// These two tests cover that from both ends: one proves the lock is actually taken, the other proves
// what taking it buys — two operating-system processes appending at once and losing nothing.
//
// The cross-process test spawns this test assembly as a probe (see Program.cs). A same-process test
// is not a substitute: two writers inside one process were never observed to lose a row, so such a
// test passes against the unrepaired lock-free writer as well and proves nothing.
public sealed class RefusalJournalConcurrencyTests
{
    private const int RowsPerWriter = 25;
    private static readonly string[] Writers = ["launch-alpha", "launch-beta"];

    [Fact]
    public async Task TwoProcessesAppendingAtOnceEachLandEveryRowIntact()
    {
        using var root = new TemporaryDirectory();
        var taskDirectory = Path.Combine(root.Path, "refusal-concurrency-task");
        Directory.CreateDirectory(taskDirectory);
        var startMarkerPath = Path.Combine(root.Path, "start");

        // Both are launched first and block on the marker, so neither pays process start-up inside
        // the window under test. Without that they would run one after the other and the test would
        // pass against a writer that takes no lock at all.
        var probes = Writers
            .Select(writer => StartProbe(taskDirectory, startMarkerPath, writer))
            .ToArray();
        try
        {
            await File.WriteAllTextAsync(startMarkerPath, "go");
            foreach (var probe in probes)
            {
                await AssertProbeExitedCleanlyAsync(probe);
            }
        }
        finally
        {
            foreach (var probe in probes)
            {
                if (!probe.HasExited)
                {
                    probe.Kill(entireProcessTree: true);
                }

                probe.Dispose();
            }
        }

        var lines = await File.ReadAllLinesAsync(RefusalJournal.ResolvePath(taskDirectory));
        var expected = Writers
            .SelectMany(writer => Enumerable.Range(0, RowsPerWriter)
                .Select(index => (Writer: writer, Message: Program.ProbeMessage(writer, index))))
            .OrderBy(row => row.Writer, StringComparer.Ordinal)
            .ThenBy(row => row.Message, StringComparer.Ordinal)
            .ToArray();

        // A lost row shows up here; a torn row shows up in the parse below.
        Assert.Equal(expected.Length, lines.Length);
        var observed = lines
            .Select((line, number) => ParseRow(line, number))
            .OrderBy(row => row.Writer, StringComparer.Ordinal)
            .ThenBy(row => row.Message, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected, observed);
    }

    // The lock is what makes the test above pass, so it is worth pinning on its own: this one is
    // deterministic where a race is statistical. The held handle is the task's own lock file, taken
    // the way TaskMutationLock takes it, so the writer sees exactly the contention a second process
    // running a command against this task would create.
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
            "LaunchProvider",
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
        startInfo.ArgumentList.Add(RowsPerWriter.ToString());

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
