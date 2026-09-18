using System.Security.Cryptography;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Storage;

internal static class TaskRetentionScan
{
    internal static IReadOnlySet<string> NeverDeletable(TaskWorkspaceLayout layout) =>
        new HashSet<string>(StringComparer.Ordinal)
        {
            // These names must come from the caller's layout, not from a default layout that may
            // differ from the task workspace being cleaned.
            layout.EventsFileName,
            RefusalJournal.FileName,
            layout.LockFileName,
            layout.StateFileName
        };

    internal const string CleanupDirectoryName = "cleanup";

    internal static IReadOnlyList<ScannedFile> Scan(
        string taskDirectory,
        TaskWorkspaceLayout layout)
    {
        var requestedRoot = Path.GetFullPath(taskDirectory);
        if (!Directory.Exists(requestedRoot))
        {
            throw new DirectoryNotFoundException($"Task directory '{requestedRoot}' does not exist.");
        }

        EnsureNoLink(requestedRoot, requestedRoot);
        var root = requestedRoot;
        var files = new List<ScannedFile>();
        Walk(root, root, layout.LockFileName, files);
        return files.OrderBy(file => file.RelativePath, StringComparer.Ordinal).ToArray();
    }

    private static void Walk(
        string root,
        string directory,
        string lockFileName,
        List<ScannedFile> files)
    {
        var atRoot = PathsEqual(directory, root);
        foreach (var path in Directory.EnumerateFileSystemEntries(directory)
                     .OrderBy(item => item, StringComparer.Ordinal))
        {
            EnsureNoLink(root, path);

            if (Directory.Exists(path))
            {
                if (atRoot && string.Equals(
                        Path.GetFileName(path), CleanupDirectoryName, StringComparison.Ordinal))
                {
                    continue;
                }

                Walk(root, path, lockFileName, files);
                continue;
            }

            if (atRoot &&
                (string.Equals(Path.GetFileName(path), lockFileName, StringComparison.Ordinal) ||
                 // RefusalJournal is failure-path telemetry beside the event log, never plan input.
                 string.Equals(Path.GetFileName(path), RefusalJournal.FileName, StringComparison.Ordinal)))
            {
                continue;
            }

            if (!File.Exists(path))
            {
                throw new GovernanceException(
                    $"'{Relative(root, path)}' is neither a regular file nor a directory. Retention " +
                    "refuses an entry it cannot classify.");
            }

            var info = new FileInfo(path);
            files.Add(new ScannedFile(Relative(root, path), info.Length, Sha256(path)));
        }
    }

    internal static string ResolveContained(string taskDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new GovernanceException("A retention path must not be empty.");
        }

        var segments = relativePath.Split('/', '\\');
        if (Path.IsPathRooted(relativePath) ||
            relativePath[0] is '/' or '\\' ||
            (segments[0].Length == 2 && segments[0][1] == ':') ||
            segments.Any(segment => segment is ".." or "." or ""))
        {
            throw new GovernanceException(
                $"Retention path '{relativePath}' must be relative to the task directory and must " +
                "contain no '.' or '..' segment.");
        }

        var requestedRoot = Path.GetFullPath(taskDirectory);
        EnsureNoLink(requestedRoot, requestedRoot);
        var root = requestedRoot;
        var resolved = Path.GetFullPath(Path.Combine(root, relativePath));
        var relative = Path.GetRelativePath(root, resolved);
        if (relative.Equals("..", PathComparison) ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, PathComparison) ||
            Path.IsPathRooted(relative))
        {
            throw new GovernanceException($"Retention path '{relativePath}' resolves outside its task directory.");
        }

        EnsureNoLinkOnPath(root, resolved);
        return resolved;
    }

    private static void EnsureNoLinkOnPath(string root, string path)
    {
        var segments = new List<string>();
        for (var current = path;
             current is not null && !PathsEqual(current, root);
             current = Path.GetDirectoryName(current))
        {
            segments.Add(current);
        }

        segments.Reverse();
        foreach (var segment in segments)
        {
            EnsureNoLink(root, segment);
        }
    }

    private static void EnsureNoLink(string root, string path)
    {
        FileSystemInfo info = Directory.Exists(path)
            ? new DirectoryInfo(path)
            : new FileInfo(path);
        if (info.LinkTarget is not null ||
            (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0))
        {
            throw new GovernanceException(
                $"'{Relative(root, path)}' is a symbolic link or reparse point. Retention refuses a " +
                "task directory containing one because its boundary is not established.");
        }
    }

    internal static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    internal static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    internal static string Sha256OfText(string text) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), PathComparison);
}

internal sealed record ScannedFile(string RelativePath, long Length, string Sha256);
