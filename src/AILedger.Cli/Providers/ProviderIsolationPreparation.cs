using AILedger.Core.Contracts;
using AILedger.Cli.Dispatch;
using AILedger.Providers.Process;

namespace AILedger.Cli.Providers;

internal static class ProviderIsolationPreparation
{
    internal static ProviderIsolation Create(ProviderGrants grants, string ledgerRoot, string lessonRoot, DispatchHostOptions options)
    {
        try { ProviderProcessIsolation.EnsureSupported(); }
        catch (ArgumentException error)
        {
            throw new DispatchPreparationException(DispatchFailureKind.PreparationUnsupported,
                "process_isolation_unsupported", error);
        }
        var hidden = new List<string> { ledgerRoot, lessonRoot };
        hidden.AddRange(options.ProtectedPaths.Select(Path.GetFullPath));
        foreach (var configuredPath in new[] { options.AssuranceAuthority, options.AssuranceStore })
            if (configuredPath is { } path) hidden.Add(Path.GetFullPath(path));
        var readOnly = new List<string> { AppContext.BaseDirectory };
        if (CognitiveArtifactLoader.ResolveRoot(options.CognitiveRoot) is { } cognitive)
            readOnly.Add(cognitive);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        readOnly.AddRange(CodexStatePaths(Path.Combine(home, ".codex")));
        readOnly.Add(Path.Combine(home, ".claude"));
        readOnly.Add(Path.Combine(home, ".claude.json"));
        if (Environment.GetEnvironmentVariable("CODEX_HOME") is { } codexHome)
            readOnly.AddRange(CodexStatePaths(Path.GetFullPath(codexHome)));
        if (Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { } claudeHome)
            readOnly.Add(Path.GetFullPath(claudeHome));
        foreach (var directory in grants.AdditionalDirectories.Prepend(grants.WorkingDirectory)
                     .Concat(grants.NavigationDirectories ?? []))
            readOnly.Add(Path.Combine(directory, ".codex"));
        return new(grants.AdditionalDirectories.Prepend(grants.WorkingDirectory).ToArray(),
            hidden.ToArray(), readOnly.ToArray());
    }

    // Codex-managed worktrees are source workspaces, not operator configuration. Do not turn
    // every checkout below CODEX_HOME into a read-only workspace. Existing state entries and
    // configuration entry points stay protected even when the selected scope is an ancestor.
    internal static IEnumerable<string> CodexStatePaths(string home) =>
        new[] { "config.toml", "auth.json", "hooks.json", "plugins", "skills", "automations", "memories" }
            .Select(name => Path.Combine(home, name))
            .Concat(Directory.Exists(home) ? Directory.EnumerateFileSystemEntries(home)
                .Where(path => Path.GetFileName(path) != "worktrees") : []);

    internal static ProviderIsolation ProtectExecutable(ProviderIsolation isolation, string executable)
    {
        var physical = ProviderGrantResolver.ResolveExistingScope(executable);
        return isolation with
        {
            ReadOnlyPaths = isolation.ReadOnlyPaths.Concat(new[]
                { executable, Path.GetDirectoryName(physical)! }).ToArray()
        };
    }
}
