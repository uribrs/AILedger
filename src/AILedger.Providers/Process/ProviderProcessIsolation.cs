using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Providers.Navigation;

namespace AILedger.Providers.Process;

/// <summary>
/// Outer OS boundary inherited by every child, including arbitrary shell/CLI invocations.
/// Provider permission settings cannot widen it. Unsupported hosts refuse.
/// </summary>
public static class ProviderProcessIsolation
{
    public static void EnsureSupported()
    {
        if (!OperatingSystem.IsMacOS() || !File.Exists("/usr/bin/sandbox-exec"))
            throw new ArgumentException("Governed provider isolation requires macOS sandbox-exec; no unconfined fallback is available.");
    }

    internal static ProcessInvocation Confine(ProcessInvocation invocation, string scratch)
    {
        if (invocation.Isolation is not { } isolation) return invocation;
        EnsureSupported();
        var environment = new Dictionary<string, string>(invocation.Environment)
        {
            ["TMPDIR"] = scratch, ["TMP"] = scratch, ["TEMP"] = scratch
        };
        var writable = isolation.WritableDirectories.Append(scratch).Select(Canonical).Distinct().ToArray();
        var hidden = isolation.HiddenPaths.Select(Canonical).Distinct().ToArray();
        var readOnly = isolation.ReadOnlyPaths.Select(Canonical).Concat(hidden).Distinct().ToArray();
        return invocation with
        {
            ExecutablePath = "/usr/bin/sandbox-exec",
            Arguments = ["-p", Profile(writable, hidden, readOnly), invocation.ExecutablePath, .. invocation.Arguments],
            Environment = environment,
            Isolation = null
        };
    }

    private static string Profile(string[] writable, string[] hidden, string[] readOnly)
    {
        // No process inspection, tracing, arbitrary signals, Unix sockets, Apple events,
        // or service launches: those can reach a more privileged same-user host outside the jail.
        var profile = "(version 1)(deny default)" +
            "(allow process-exec)(allow process-fork)(allow signal (target self))" +
            "(allow sysctl-read)(allow mach-lookup (global-name \"com.apple.system.logger\") " +
            "(global-name \"com.apple.trustd.agent\") (global-name \"com.apple.SecurityServer\"))" +
            "(allow network-outbound (remote tcp))(allow network-outbound (remote udp))" +
            "(allow file-read* (require-all (require-not (regex #\"(^|/)\\.ailedger(/|$)\"))" +
            Exclusions(hidden, ancestors: false) + "))";
        profile += "(allow file-write* (require-all (require-any " +
            string.Join(" ", writable.Select(Subpath)) + " (literal \"/dev/null\") (literal \"/dev/tty\"))" +
            "(require-not (regex #\"(^|/)\\.(ailedger|git|claude|agents)(/|$)\"))" + Exclusions(readOnly, ancestors: true) + "))";
        return profile;
    }

    private static string Exclusions(IEnumerable<string> paths, bool ancestors)
    {
        var filters = paths.Select(Subpath).ToList();
        if (ancestors)
            foreach (var path in paths)
                for (var parent = Path.GetDirectoryName(path); parent is not null; parent = Path.GetDirectoryName(parent))
                    filters.Add("(literal " + Quote(parent) + ")");
        return string.Concat(filters.Distinct().Select(filter => "(require-not " + filter + ")"));
    }

    private static string Subpath(string path) => "(subpath " + Quote(path) + ")";
    private static string Quote(string text) => JsonSerializer.Serialize(text);
    private static string Canonical(string path) => RoslynSolutionPaths.Canonicalize(path);
}
