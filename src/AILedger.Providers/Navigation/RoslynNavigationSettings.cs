namespace AILedger.Providers.Navigation;

internal sealed record RoslynNavigationSettings(
    string Executable,
    IReadOnlyList<string> AllowedDirectories,
    string LedgerRoot,
    string? GuardDirectory = null);
