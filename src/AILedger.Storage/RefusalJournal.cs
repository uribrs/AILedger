using System.Text;
using System.Text.Json;
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

// One refused attempt. Six fields, and Message is the kernel's own text copied verbatim: a
// retrospective reads which gate fired, so the gate's own words are the only faithful record of it.
public sealed record RefusalRecord(
    DateTimeOffset RecordedAt,
    ActorId ActorId,
    string Command,
    string Site,
    long TaskVersion,
    string Message);

// Telemetry beside the event log, never part of it. Nothing in the kernel may refuse, gate or score
// on this file, replay never reads it, and no state carries it — so a task whose journal is deleted
// materialises exactly the state it did before. That is what makes it safe to write from a failure
// path: the file can be absent, stale or truncated without any consequence for task truth.
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
    internal async Task TryAppendAsync(string taskDirectory, RefusalRecord refusal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskDirectory);
        ArgumentNullException.ThrowIfNull(refusal);

        try
        {
            await AppendAsync(taskDirectory, refusal).ConfigureAwait(false);
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            // A lost row costs a retrospective one line. Failing the caller would cost the operator
            // the refusal message itself.
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
