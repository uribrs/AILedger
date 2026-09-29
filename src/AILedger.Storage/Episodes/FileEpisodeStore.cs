using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using AILedger.Core.Artifacts;
using AILedger.Core.Episodes;
using AILedger.Core.Handoffs;

namespace AILedger.Storage.Episodes;

// Separate experimental namespace, using the existing cross-process lease and task-8 identities.
// Content and receipt share one immutable, flushed record; there is no second content store.
public sealed class FileEpisodeStore(string root) : IEpisodeStore
{
    private const int MaximumBytes = 32 * 1024 * 1024;
    private readonly TaskMutationLock _locks = new();

    public async Task<IAsyncDisposable> AcquireAsync(string executionId, CancellationToken token)
    {
        var directory = DirectoryFor(executionId);
        Directory.CreateDirectory(directory);
        // Persist each newly created directory entry before acknowledging admission.
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
            SyncDirectory(current.FullName);
        return await _locks.AcquireAsync(Path.Combine(directory, ".execution.lock"), token).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<EpisodeRecord>> ReadAsync(string executionId, CancellationToken token)
    {
        var directory = DirectoryFor(executionId);
        if (!Directory.Exists(directory)) return [];
        var paths = Directory.GetFiles(directory, "*.json").Order(StringComparer.Ordinal).ToArray();
        if (paths.Length > 2048) throw new InvalidDataException("Episode journal exceeds its record limit.");
        var records = new List<EpisodeRecord>();
        var previous = "";
        long bytes = 0;
        foreach (var path in paths)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Episode records cannot be symbolic links.");
            bytes += new FileInfo(path).Length;
            if (bytes > MaximumBytes) throw new InvalidDataException("Episode journal exceeds 32 MiB.");
            var envelope = JsonSerializer.Deserialize<EpisodeRecordEnvelope>(await File.ReadAllTextAsync(path, token).ConfigureAwait(false), HandoffJson.Options)
                ?? throw new InvalidDataException("Missing episode envelope.");
            if (ArtifactSubmissionIdentity.ContentHash(envelope.RecordJson) != envelope.Sha256)
                throw new InvalidDataException("Episode journal content identity mismatch.");
            var record = JsonSerializer.Deserialize<EpisodeRecord>(envelope.RecordJson, HandoffJson.Options)
                ?? throw new InvalidDataException("Missing episode record.");
            if (record.SchemaVersion != 1 || record.Sequence != records.Count + 1 || record.PreviousSha256 != previous ||
                Path.GetFileName(path) != $"{record.Sequence:D6}.json")
                throw new InvalidDataException("Episode journal sequence/chain mismatch; investigate, do not replay as completion.");
            records.Add(record); previous = envelope.Sha256;
        }
        return records;
    }

    // Callers hold the execution lease across every mutation, including retry/reconciliation.
    public async Task<EpisodeRecord> AppendAsync(string executionId, string kind, object data, CancellationToken token)
    {
        var records = await ReadAsync(executionId, token).ConfigureAwait(false);
        var previous = records.Count == 0 ? "" : EpisodeExecutionValidation.Hash(records[^1]);
        var record = new EpisodeRecord(1, records.Count + 1, previous, DateTimeOffset.UtcNow, kind,
            JsonSerializer.SerializeToElement(data, HandoffJson.Options));
        var json = EpisodeExecutionValidation.Serialize(record);
        var bytes = Encoding.UTF8.GetBytes(EpisodeExecutionValidation.Serialize(new EpisodeRecordEnvelope(
            ArtifactSubmissionIdentity.ContentHash(json), json)));
        var directory = DirectoryFor(executionId);
        if (records.Count >= 2048 || Directory.GetFiles(directory, "*.json").Sum(p => new FileInfo(p).Length) + bytes.Length > MaximumBytes)
            throw new IOException("Episode journal capacity exhausted; existing output remains discoverable.");
        var pending = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".pending");
        await using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            8192, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await stream.WriteAsync(bytes, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }
        // Cancellation after durable writing cannot turn a visible receipt into an uncommitted claim.
        File.Move(pending, Path.Combine(directory, $"{record.Sequence:D6}.json"), overwrite: false);
        SyncDirectory(directory);
        return record;
    }

    public Task<int> CountUncommittedAsync(string executionId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var directory = DirectoryFor(executionId);
        return Task.FromResult(Directory.Exists(directory) ? Directory.GetFiles(directory, "*.pending").Length : 0);
    }

    private static void SyncDirectory(string path)
    {
        if (OperatingSystem.IsWindows()) return; // Runtime admission is macOS-only.
        var descriptor = OpenDirectory(path, 0);
        if (descriptor < 0) throw new IOException("Unable to open episode directory for durable synchronization.");
        try
        {
            if (SyncDescriptor(descriptor) != 0) throw new IOException("Episode directory durability is unknown; inspect and retry unchanged.");
        }
        finally { CloseDescriptor(descriptor); }
    }
    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int OpenDirectory(string path, int flags);
    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int SyncDescriptor(int descriptor);
    [DllImport("libc", EntryPoint = "close")]
    private static extern int CloseDescriptor(int descriptor);

    private string DirectoryFor(string executionId)
    {
        if (!ArtifactSubmissionIdentity.IsHash(executionId)) throw new ArgumentException("Invalid execution identity.");
        var directory = Path.Combine(Path.GetFullPath(root), "episodes-v1", executionId);
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
            if (current.Exists && current.LinkTarget is not null && current.FullName is not ("/tmp" or "/var"))
                throw new IOException("Episode store directories cannot be symbolic links; use the resolved path.");
        return directory;
    }
}
