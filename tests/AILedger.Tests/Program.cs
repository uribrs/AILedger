using AILedger.Core.Contracts;
using AILedger.Storage;

namespace AILedger.Tests;

// This assembly is also a probe process. RefusalJournalConcurrencyTests has to prove what a second
// operating-system process appending to one task's journal sees while the task lock is held, and a
// same-process test cannot prove it: the single-process path was never broken (VE5), so such a test
// passes against the unrepaired lock-free writer as well. So the test holds the lock and spawns this
// assembly as the writer.
//
// With no probe argument Main returns 0, which is the same empty entry point a test project has
// anyway: the test runners load the assembly and never call it.
internal static class Program
{
    internal const string ProbeArgument = "--refusal-journal-probe";

    // A probe that never sees its start marker must not become an orphan holding a lock file.
    private static readonly TimeSpan StartMarkerWait = TimeSpan.FromSeconds(60);

    public static async Task<int> Main(string[] arguments)
    {
        if (arguments.Length == 0 || arguments[0] != ProbeArgument)
        {
            return 0;
        }

        if (arguments.Length != 5 ||
            !int.TryParse(arguments[4], out var rowCount))
        {
            await Console.Error.WriteLineAsync(
                $"Usage: {ProbeArgument} <taskDirectory> <startMarkerPath> <writerName> <rowCount>");
            return 2;
        }

        var taskDirectory = arguments[1];
        var startMarkerPath = arguments[2];
        var writerName = arguments[3];

        if (!await WaitForStartMarkerAsync(startMarkerPath).ConfigureAwait(false))
        {
            await Console.Error.WriteLineAsync($"Start marker '{startMarkerPath}' never appeared.");
            return 3;
        }

        var journal = new RefusalJournal();
        for (var index = 0; index < rowCount; index++)
        {
            // The method the provider-launch site will call: this process holds no mutation lease,
            // which is exactly the condition the repair exists for.
            await journal.TryAppendUnderTaskLockAsync(taskDirectory, new RefusalRecord(
                DateTimeOffset.UtcNow,
                new ActorId(writerName),
                "LaunchProvider",
                RefusalSite.ProviderLaunch,
                index,
                ProbeMessage(writerName, index))).ConfigureAwait(false);
        }

        return 0;
    }

    // Shared with the test so the expected rows and the written rows cannot drift apart. About two
    // hundred characters, which is the size of a real authority-and-scope refusal.
    internal static string ProbeMessage(string writerName, int index) =>
        $"Actor '{writerName}' holds no run authority for work item 'W{index:D2}' and cannot launch a " +
        $"provider against it; an operator may dispatch on its behalf with --subject. Row {index:D3}.";

    private static async Task<bool> WaitForStartMarkerAsync(string startMarkerPath)
    {
        var deadline = DateTimeOffset.UtcNow + StartMarkerWait;
        while (!File.Exists(startMarkerPath))
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(1)).ConfigureAwait(false);
        }

        return true;
    }
}
