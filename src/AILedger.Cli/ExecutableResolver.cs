namespace AILedger.Cli;

internal static class ExecutableResolver
{
    public static string Resolve(string provider, string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var path = Path.GetFullPath(configuredPath);
            return File.Exists(path)
                ? path
                : throw new FileNotFoundException("Provider executable was not found.", path);
        }

        var executable = OperatingSystem.IsWindows() ? $"{provider}.exe" : provider;
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.GetFullPath(Path.Combine(directory, executable));
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not find '{executable}' on PATH. Use '--executable PATH'.");
    }
}
