using System.Text;
using System.Text.Json;
using AILedger.Core.Application;
using AILedger.Core.Contracts;

namespace AILedger.Storage;

// Which boundary refused, not how bad the refusal was. There is deliberately no severity, code or
// category here: a value derived from a refusal message is a second, looser model of what the gate
// actually requires, and this repository has already paid for one of those in TaskDebt.
public static class RefusalSite
{
    // A command-time rule refused inside FileGovernedTaskService.
    public const string Service = "service";

    // Authority or scope refused in the CLI before the run began, or the manifest and the adapter
    // request failed to build after it did.
    public const string ProviderLaunch = "provider-launch";
}

// One refused attempt. Message is the kernel's own text copied verbatim: a
// retrospective reads which gate fired, so the gate's own words are the only faithful record of it.
public sealed record RefusalRecord(
    DateTimeOffset RecordedAt,
    ActorId ActorId,
    string Command,
    string Site,
    long TaskVersion,
    string Message,
    // Optional and trailing so journals written before executable identity was recorded remain
    // readable, and existing construction sites remain source-compatible.
    KernelBuildIdentity? KernelIdentity = null);

// How many rows this task's journal already holds for one rule and one actor, and how many rows it
// could not read while counting. Occurrences is a floor whenever UnreadableRows is above zero, and
// the two travel together for that reason: a count taken from a partly readable file that is
// reported as a total is the defect 2026-09-09_1010 recorded as RC1, and separating the two fields
// is what stops a caller from printing one without the other.
//
// Zero occurrences is the answer for a journal that is absent, empty, entirely unreadable, or that
// simply holds no row for this pair. The caller prints nothing in all four cases, so none of them
// has to be told apart from the others here.
internal readonly record struct RefusalRecurrence(long Occurrences, long UnreadableRows)
{
    internal static RefusalRecurrence None => new(0, 0);

    internal bool IsFloor => UnreadableRows > 0;
}

// Telemetry beside the event log, never part of it. Nothing in the kernel may refuse, gate or score
// on this file, replay never reads it, and no state carries it — so a task whose journal is deleted
// materialises exactly the state it did before. That is what makes it safe to write from a failure
// path: the file can be absent, stale or truncated without any consequence for task truth.
//
// Counting is now read from here on a command path, which is new and is the only reading this file
// admits: the number reaches the refusal message the operator is about to see and nothing else. No
// rule, gate, score, event or projected state may take it, because the value that makes this file
// safe to write from a failure path is the same value that bars it from any decision (K4).
public sealed class RefusalJournal
{
    public const string FileName = "refusals.jsonl";

    // Bounded well under TaskMutationLock's own 30 second deadline. On a refusal path the operator
    // is already waiting to be told what was refused, so a row lost to contention costs one line of
    // telemetry and a stall would cost the refusal message its promptness. Long enough to outlast
    // several commands queued on the same task, short enough that nobody reads it as a hang.
    private static readonly TimeSpan MaximumLockWait = TimeSpan.FromSeconds(5);
    // The same file FileGovernedTaskService takes on every command, so a row written here cannot
    // interleave with one written there. The CLI constructs the service with the default layout
    // (CliApplication.cs:85); a service given a different one would lock a different file.
    private static readonly string LockFileName = new TaskWorkspaceLayout().LockFileName;

    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private readonly TaskMutationLock _mutationLock = new();
    private readonly JsonSerializerOptions _json = LedgerJson.CreateOptions();

    public static string ResolvePath(string taskDirectory) => Path.Combine(taskDirectory, FileName);

    // Best-effort by construction, and named for it. Every caller is already on a refusal path, so
    // a throw here would replace the governance message the operator needs with an I/O error about
    // a telemetry file. The cancellation token is deliberately not taken: the attempt has already
    // been refused, and a cancelled token would silently drop the row that records it.
    //
    // Takes no lock, so it is only correct from a caller that already holds the task's mutation
    // lease — which today means FileGovernedTaskService and nothing else. The lock is not
    // re-entrant: taking it a second time there would stall the full deadline and then drop the
    // row (C14). A caller holding no lease must use TryAppendUnderTaskLockAsync instead; two
    // unsynchronised processes lose or truncate rows, measured at every row size from 256 bytes to
    // four megabytes (VC1).
    //
    // Internal, because that precondition cannot be expressed in the type system and a caller in
    // another assembly would reach for the shorter name (RC1). AILedger.Cli, which is about to add
    // the launch site, therefore sees only the locking method. The InternalsVisibleTo grant beside
    // this file keeps it reachable from AILedger.Tests, where pointing the probe at it is what
    // proves the concurrency test detects the defect it was written for (ALT8).
    //
    // Returns whether the row landed. The caller counts the file straight afterwards and names the
    // number in the refusal, so a row that was lost would make that number one short and it would
    // be stated as a total. False is how the caller learns to state a floor instead; it is not an
    // error to handle, and no caller may fail on it.
    internal async Task<bool> TryAppendAsync(string taskDirectory, RefusalRecord refusal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskDirectory);
        ArgumentNullException.ThrowIfNull(refusal);

        try
        {
            await AppendAsync(taskDirectory, refusal).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            // A lost row costs a retrospective one line. Failing the caller would cost the operator
            // the refusal message itself.
            return false;
        }
    }

    // For a caller that holds no mutation lease: the CLI's provider-launch site, which refuses
    // before any command reaches the service. It takes the task's own mutation lock, which is a
    // real cross-process mutex, so a row written here is serialised against every other writer of
    // this task — another launch in another process, and the service site inside its own lease.
    //
    // Never call this while holding the lease; see TryAppendAsync. Failure to acquire within the
    // bound drops the row rather than delaying the refusal the caller is about to report.
    public async Task TryAppendUnderTaskLockAsync(string taskDirectory, RefusalRecord refusal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskDirectory);
        ArgumentNullException.ThrowIfNull(refusal);

        try
        {
            using var bound = new CancellationTokenSource(MaximumLockWait);
            await using var lease = await _mutationLock.AcquireAsync(
                Path.Combine(taskDirectory, LockFileName), bound.Token).ConfigureAwait(false);
            await AppendAsync(taskDirectory, refusal).ConfigureAwait(false);
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            // Includes the bound expiring: AcquireAsync honours the token in both the semaphore
            // wait and the file-lock retry, and a cancelled wait leaves the row unwritten.
        }
    }

    // How many rows this journal holds for one actor and the rule that produced this message,
    // counted after the row for this refusal has been appended, so the number is exactly what a
    // reader of the file would count and exactly what a retrospective will later group. It is read
    // by the service's refusal path while that path holds the task's mutation lease, so it takes no
    // lock: the lease is not re-entrant and acquiring it a second time would stall the full deadline,
    // as the comment on TryAppendAsync above already records. Sharing is ReadWrite so an open writer
    // never turns a count into an exception. W2C4 is the claim that a refused command still returns.
    //
    // Best-effort by construction, like every other method here. Every caller is already on a
    // refusal path, so a throw would replace the governance message the operator needs with an I/O
    // error about a telemetry file — and a journal that could fail a command would invert the one
    // property this file's header promises. An absent, empty, unreadable or wholly unparseable
    // journal all return None, which prints nothing and leaves the refusal exactly as it is today.
    //
    // A row that cannot be read is counted as unreadable rather than skipped, because it offers no
    // actor and no message to test and so can be neither admitted nor excluded. That covers a row
    // that does not parse and a row that parses into something the key cannot read. That makes the
    // number a floor, which the caller must say rather than report as a total.
    internal async Task<RefusalRecurrence> TryCountAsync(
        string taskDirectory,
        ActorId actorId,
        string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskDirectory);
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            var path = ResolvePath(taskDirectory);
            if (!File.Exists(path))
            {
                return RefusalRecurrence.None;
            }

            var rule = RefusalRuleKey.Of(message);
            var occurrences = 0L;
            var unreadable = 0L;
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new StreamReader(stream, Utf8WithoutBom);
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                // The whole row is inside the guard, not the deserialisation alone. A row can be
                // well-formed JSON and still be unusable — a null message deserialises and then
                // throws where the rule key reads it — and a fault there escaping the loop would
                // discard every row already counted and return None, which prints nothing at all
                // where a floor is the honest answer. Any fault on one row costs that row and says
                // so; only the read itself failing costs the count.
                try
                {
                    var row = JsonSerializer.Deserialize<RefusalRecord>(line, _json);
                    if (row is null)
                    {
                        unreadable++;
                    }
                    else if (row.ActorId == actorId &&
                             string.Equals(RefusalRuleKey.Of(row.Message), rule, StringComparison.Ordinal))
                    {
                        occurrences++;
                    }
                }
                catch (Exception exception) when (!IsFatal(exception))
                {
                    unreadable++;
                }
            }

            return new RefusalRecurrence(occurrences, unreadable);
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            // A count abandoned partway is discarded rather than returned short: half a file read is
            // a number nobody can qualify, and silence is the honest form of it.
            return RefusalRecurrence.None;
        }
    }

    private async Task AppendAsync(string taskDirectory, RefusalRecord refusal)
    {
        var bytes = Utf8WithoutBom.GetBytes(JsonSerializer.Serialize(refusal, _json) + "\n");
        // Append mode opens O_APPEND, so a writer that does hold the lock lands after the last row
        // rather than over it, and ReadWrite sharing lets a reader in while the file is open.
        await using var stream = new FileStream(
            ResolvePath(taskDirectory),
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite,
            bufferSize: 4096,
            FileOptions.Asynchronous);
        await stream.WriteAsync(bytes, CancellationToken.None).ConfigureAwait(false);
        await stream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static bool IsFatal(Exception exception) =>
        exception is OutOfMemoryException or StackOverflowException or AccessViolationException;
}
