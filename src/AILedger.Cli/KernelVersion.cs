using System.Diagnostics;
using System.Reflection;

namespace AILedger.Cli;

// What this build was made from, and whether the ledger home has moved past it.
//
// The installed tool and the built solution are two separate artifacts and nothing connected them.
// A green build and a green suite said nothing about whether `ailedger` carried the change, and the
// failure was silent: a run recorded a null field for a feature that was already written and tested,
// because provider launch went through a launcher packed before it. That is the cost this exists to
// remove — not the inability to update, but not knowing an update was needed.
//
// `scripts/install.sh` already embeds the stamp as `2.0.<count>[-dirty]+<sha>`, so nothing here
// computes a version. It reads one and compares it.
internal static class KernelVersion
{
    private const string UnknownSha = "unknown";
    private const int GitTimeoutMilliseconds = 2000;

    private static readonly Lazy<(string Version, string Sha)> Stamp = new(() =>
    {
        var informational = typeof(KernelVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            return ("unknown", UnknownSha);
        }

        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        if (plus < 0)
        {
            return (informational, UnknownSha);
        }

        // The build metadata can carry more than one segment. install.sh passes `+<short sha>`, and
        // the SDK appends its own source revision on top, giving `+<short>.<full>`. Only the first
        // segment is this build's own stamp; taking the rest printed the commit twice.
        var metadata = informational[(plus + 1)..];
        var dot = metadata.IndexOf('.', StringComparison.Ordinal);
        return (informational[..plus], dot < 0 ? metadata : metadata[..dot]);
    });

    internal static string Version => Stamp.Value.Version;

    internal static string Sha => Stamp.Value.Sha;

    internal static bool IsDirtyBuild => Version.Contains("-dirty", StringComparison.Ordinal);

    internal static string Describe() =>
        Sha == UnknownSha ? Version : $"{Version}  from {Sha}";

    // Null when there is nothing honest to say: no ledger home to compare against, no git there, a
    // build with no sha, a build packed from a dirty tree — which has no commit to compare — or a
    // home that has not moved. Warn, never refuse: a stale tool still reads a ledger correctly, and
    // a kernel that refuses to run because a source tree moved is a kernel that gets uninstalled.
    internal static string? StalenessWarning(string? ledgerHome) =>
        ledgerHome is null
            ? null
            : Warning(
                Sha,
                IsDirtyBuild,
                // KC1: the comparison only means something when the ledger home is this kernel's own
                // source tree. A ledger opened in any other repository would be compared against a
                // foreign commit and told to run a script that is not there.
                File.Exists(Path.Combine(ledgerHome, "AILedger.sln")),
                // Lazy: a second git invocation, never run when there is nothing to compare.
                () => Git(ledgerHome, "rev-parse HEAD") is { Length: > 0 } head ? head : null);

    // The decision, with every input supplied. Pure so the fire path can be proved without a git or
    // a filesystem — the defect this feature shipped with (LC6) was unreachable by any test that
    // only asserted silence against this machine's own repository.
    //
    // KC2: the home working tree's cleanliness is deliberately not an input. buildIsDirty already
    // covers the only case with nothing to compare — a build packed from uncommitted work. A build
    // carrying a real commit against a home at a different one is stale whether or not there are
    // edits on top, and gating on that suppressed exactly the true positives.
    private static string? Warning(
        string buildSha,
        bool buildIsDirty,
        bool isKernelTree,
        Func<string?> homeSha)
    {
        if (buildSha == UnknownSha || buildIsDirty || !isKernelTree)
        {
            return null;
        }

        var head = homeSha();
        if (head is null || SameCommit(head, buildSha))
        {
            return null;
        }

        return $"warning: this ailedger was built from {Short(buildSha)}; the ledger home is at {Short(head)}. " +
            "Run `sh scripts/install.sh` to rebuild the installed tool from this tree.";
    }

    // The embedded sha is short when install.sh packed it and full when the SDK derived it from the
    // source revision, which is what a plain `dotnet build` produces. Either is the same commit when
    // one is a prefix of the other, so compare that way rather than demanding equal length.
    private static bool SameCommit(string first, string second)
    {
        var shortest = Math.Min(first.Length, second.Length);
        return shortest >= 7 &&
            first.AsSpan(0, shortest).SequenceEqual(second.AsSpan(0, shortest));
    }

    private static string Short(string sha) => sha.Length <= 12 ? sha : sha[..12];

    private static string? Git(string workingDirectory, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            if (process is null)
            {
                return null;
            }

            // Read both pipes before waiting, and asynchronously. A blocking ReadToEnd here would
            // return only when git closed the pipe, so a git that hangs without writing would hang
            // this call and the two-second bound below would never be reached — the bound has to sit
            // on the wait, not after a read that can outlast it. Standard error is drained rather
            // than ignored for the same reason: a full pipe stops the process it belongs to.
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            // Bounded so a hung git cannot hold a ledger command open. The comparison is a courtesy;
            // it must never become the slowest thing a mutation does.
            if (!process.WaitForExit(GitTimeoutMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Already gone between the timeout and the kill; nothing to stop.
                }

                return null;
            }

            // The process has exited, so both pipes are closed and the reads are finishing. The
            // bound is repeated rather than dropped: an inherited pipe held open by a child that
            // outlived git would otherwise keep these tasks pending for as long as it lives.
            // KC3: Task.WaitAll throws AggregateException when a read faults, and that matches
            // neither the filter below nor the one in CliApplication.RunAsync — a faulted pipe would
            // have crashed a ledger command over a courtesy check.
            try
            {
                if (!Task.WaitAll([output, errors], GitTimeoutMilliseconds))
                {
                    return null;
                }
            }
            catch (AggregateException)
            {
                return null;
            }

            // Null means git could not answer; the exit code is the only thing that decides that.
            // An empty answer from a successful command is still an answer, and each caller judges
            // what emptiness means for the question it asked.
            return process.ExitCode == 0 ? output.Result.Trim() : null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException)
        {
            // No git on PATH, or it could not be started. Not a reason to fail a ledger command.
            return null;
        }
    }
}
