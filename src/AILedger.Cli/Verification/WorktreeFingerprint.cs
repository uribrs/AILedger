using System.Security.Cryptography;
using System.Text;

namespace AILedger.Cli.Verification;

/// <summary>
/// Contract S12: a SHA-256 over the paths git lists for the work tree, tracked and untracked but not
/// ignored, and what each one holds on disk. Two checkouts at one HEAD whose edits differ in content
/// get different digests, which a hash of the status listing does not (PC40). It reads the files
/// directly and writes nothing: no temporary index, no objects (PALT19), and no diff configuration
/// can change it (PALT20).
/// </summary>
internal static class WorktreeFingerprint
{
    public static async Task<string> ComputeAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var listing = await VerificationCliCommands.GitAsync(
            repositoryRoot, ["ls-files", "-z", "--cached", "--others", "--exclude-standard"], cancellationToken)
            .ConfigureAwait(false);
        var paths = Encoding.UTF8.GetString(listing)
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .Select(path => (Path: path, Bytes: Encoding.UTF8.GetBytes(path)))
            .OrderBy(entry => entry.Bytes, Utf8Order.Instance)
            .ToArray();

        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (path, bytes) in paths)
        {
            var (kind, value) = await DescribeAsync(Path.Combine(repositoryRoot, path), cancellationToken)
                .ConfigureAwait(false);
            digest.AppendData(bytes);
            digest.AppendData(Encoding.UTF8.GetBytes($"\0{kind}\0{value}\n"));
        }

        return Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
    }

    private static async Task<(string Kind, string Value)> DescribeAsync(string path, CancellationToken cancellationToken)
    {
        // A link is described by its target text and never followed, whatever it points at.
        if (new FileInfo(path).LinkTarget is { } target)
        {
            return ("symlink", target);
        }

        if (Directory.Exists(path))
        {
            return ("directory", "");
        }

        try
        {
            string content;
            await using (var stream = new FileStream(
                             path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                             bufferSize: 81920, useAsync: true))
            {
                content = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false))
                    .ToLowerInvariant();
            }

            // This version is macOS only (K1); Windows has no execute bit to record.
            var executable = !OperatingSystem.IsWindows() &&
                             (File.GetUnixFileMode(path) & UnixFileMode.UserExecute) != 0;
            return (executable ? "executable" : "file", content);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return ("missing", "");
        }
    }

    private sealed class Utf8Order : IComparer<byte[]>
    {
        public static readonly Utf8Order Instance = new();

        public int Compare(byte[]? left, byte[]? right) => left.AsSpan().SequenceCompareTo(right);
    }
}
