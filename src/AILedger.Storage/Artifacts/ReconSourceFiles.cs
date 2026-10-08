using System.Security.Cryptography;
using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Storage;

/// <summary>Live source observations. Never invoked by event replay; hashes do not prove comprehension.</summary>
public static class ReconSourceFiles
{
    private const long MaximumFileBytes = 16 * 1024 * 1024;
    private const long MaximumTotalBytes = 64 * 1024 * 1024;

    public static async Task<IReadOnlyList<ReconSourceFile>> ObserveAsync(
        IReadOnlyList<string> paths, CancellationToken token)
    {
        if (paths.Count > 256) throw Invalid("select at most 256 source files per recon.");
        var rows = new List<ReconSourceFile>();
        long total = 0;
        foreach (var path in paths)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (!Path.IsPathFullyQualified(path) || Canonicalize(path) != path)
                    throw Invalid($"'{path}' is not a canonical physical path; refresh the source template.");
                if (Directory.Exists(path)) throw Invalid($"'{path}' is a directory; select source files.");
                var file = new FileInfo(path);
                if (!file.Exists)
                {
                    rows.Add(new(path, "missing", [], [], ""));
                    continue;
                }
                var length = file.Length;
                var modified = file.LastWriteTimeUtc;
                total += length;
                if (length > MaximumFileBytes || total > MaximumTotalBytes)
                    throw Invalid("source observation exceeds 16 MiB per file or 64 MiB total; narrow the source selection explicitly.");
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.Asynchronous);
                var hash = await HashAsync(stream, token).ConfigureAwait(false);
                file.Refresh();
                if (!file.Exists || file.Length != length || file.LastWriteTimeUtc != modified || Canonicalize(path) != path)
                    throw Invalid($"'{path}' changed during observation; refresh the source template.");
                rows.Add(new(path, hash, [], [], ""));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {
                throw Invalid($"cannot observe '{path}': {error.Message}");
            }
        }
        return rows;
    }

    private static async Task<string> HashAsync(Stream stream, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long readTotal = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            readTotal += read;
            if (readTotal > MaximumFileBytes) throw Invalid("source file grew beyond the 16 MiB limit during observation.");
            hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public static async Task EnsureCurrentAsync(string content, CancellationToken token)
    {
        var document = JsonSerializer.Deserialize<InternalReconDocument>(content)!;
        if (document.SchemaVersion != 2 || document.SourceReview is not { } review) return;
        var observed = await ObserveAsync(review.Files.Select(file => file.Path).ToArray(), token).ConfigureAwait(false);
        for (var i = 0; i < observed.Count; i++)
            if (observed[i].Sha256 != review.Files[i].Sha256)
                throw Invalid($"'{observed[i].Path}' differs from the inspected source basis; reassess affected claims and refresh recon in an eligible Research/Design run before forward admission.");
    }

    public static string Canonicalize(string path, int links = 0)
    {
        if (links > 40) throw Invalid("too many source-path links.");
        var full = Path.GetFullPath(path);
        var current = Path.GetPathRoot(full)!;
        foreach (var part in full[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget is not null)
                current = Canonicalize(info.ResolveLinkTarget(true)?.FullName
                    ?? throw Invalid("source path has an unresolved link."), links + 1);
        }
        return current;
    }

    private static GovernanceException Invalid(string message) => new("InternalRecon source basis: " + message);
}
