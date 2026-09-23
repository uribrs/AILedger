namespace AILedger.Providers.Navigation;

// This bounds solution selection, not MSBuild's transitive project/package reads.
internal sealed class RoslynSolutionPaths
{
    private readonly string[] directories;
    private readonly string ledgerRoot;

    internal RoslynSolutionPaths(RoslynNavigationSettings settings)
    {
        directories = settings.AllowedDirectories.Select(Canonicalize).Distinct(Comparer).ToArray();
        ledgerRoot = Canonicalize(settings.LedgerRoot);
    }

    internal static StringComparer Comparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal string Resolve(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
        {
            throw new ArgumentException("Select an existing solution by its absolute path.");
        }

        var resolved = Canonicalize(path);
        if (Path.GetExtension(resolved).ToLowerInvariant() is not (".sln" or ".slnx"))
        {
            throw new ArgumentException("Select a .sln or .slnx file; use CLI navigation for other layouts.");
        }

        if (IsLedgerPath(resolved) || !directories.Any(directory => Contains(directory, resolved)))
        {
            throw new ArgumentException("Solution is outside this launch's authorized repository directories.");
        }

        return resolved;
    }

    private bool IsLedgerPath(string path)
    {
        // macOS commonly aliases case and Unicode normalization. Be conservative for the
        // exclusion even on case-sensitive volumes; do not broaden the directory grants.
        return OperatingSystem.IsMacOS()
            ? Contains(ledgerRoot.Normalize(), path.Normalize(), StringComparison.OrdinalIgnoreCase)
            : Contains(ledgerRoot, path);
    }

    private static bool Contains(string directory, string path) => Contains(directory, path,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool Contains(string directory, string path, StringComparison comparison) =>
        string.Equals(directory, path, comparison) ||
        path.StartsWith(Path.EndsInDirectorySeparator(directory) ? directory : directory + Path.DirectorySeparatorChar,
            comparison);

    private static string Canonicalize(string path, int links = 0)
    {
        if (links > 40)
        {
            throw new ArgumentException("Too many symbolic links in solution path.");
        }

        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)!;
        var current = root;
        foreach (var segment in fullPath[root.Length..].Split(Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo entry = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (entry.LinkTarget is not null)
            {
                var target = entry.ResolveLinkTarget(returnFinalTarget: false)
                    ?? throw new ArgumentException("Cannot resolve solution path link.");
                current = Canonicalize(target.FullName, links + 1);
            }
        }

        return Path.TrimEndingDirectorySeparator(current);
    }
}
