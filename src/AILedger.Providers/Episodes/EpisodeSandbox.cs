using System.Security.Cryptography;
using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Core.Episodes;

namespace AILedger.Providers.Episodes;

// Fail closed on unsupported hosts. The bridge gets no network, task store, credentials or write
// access. Only system runtime files and its exact pinned executable are readable.
public static class EpisodeSandbox
{
    public const string EnvironmentIdentity = "macos-seatbelt-offline-readonly-v1";
    public static async Task ValidateAsync(EpisodeAuthority authority, CancellationToken token)
    {
        if (!OperatingSystem.IsMacOS() || !File.Exists("/usr/bin/sandbox-exec"))
            throw new ArgumentException("The experimental bridge requires macOS sandbox-exec; no unsandboxed fallback.");
        if (!File.Exists(authority.Executable) || new FileInfo(authority.Executable).LinkTarget is not null)
            throw new ArgumentException("Pinned provider executable is unavailable or is a symbolic link.");
        if (new FileInfo(authority.Executable).Length is < 1 or > 16 * 1024 * 1024)
            throw new ArgumentException("Provider bridge must be 1 byte–16 MiB.");
        if ((File.GetUnixFileMode(authority.Executable) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) == 0)
            throw new ArgumentException("Provider bridge is not executable.");
        if (await HashFileAsync(authority.Executable, token).ConfigureAwait(false) != authority.ExecutableSha256)
            throw new ArgumentException("Provider executable changed; a new trusted grant is required.");
    }

    public static ProcessInvocation Invocation(string executable, string directory, string input, TimeSpan timeout)
    {
        executable = ResolvePath(executable);
        var profile = "(version 1)(deny default)(allow process-exec)(allow process-fork)(allow signal (target self))" +
            "(allow sysctl-read)(allow file-read-metadata)(allow file-read-data (literal \"/\"))" +
            "(allow file-read* (subpath \"/System\") (subpath \"/usr/lib\") (subpath \"/usr/bin\") " +
            "(subpath \"/bin\") (subpath \"/Library/Apple\") (literal \"/dev/null\") (literal \"/dev/urandom\") " +
            "(literal " + JsonSerializer.Serialize(executable) + "))(allow file-write* (literal \"/dev/null\"))";
        return new("/usr/bin/sandbox-exec", "/", ["-p", profile, executable], input,
            new Dictionary<string, string> { ["HOME"] = directory, ["TMPDIR"] = directory,
                ["PATH"] = "/usr/bin:/bin", ["CODEX_HOME"] = directory, ["CLAUDE_CONFIG_DIR"] = directory,
                ["XDG_CONFIG_HOME"] = directory, ["XDG_CACHE_HOME"] = directory, ["XDG_DATA_HOME"] = directory }, timeout);
    }

    private static string ResolvePath(string path)
    {
        var resolved = Path.GetPathRoot(Path.GetFullPath(path))!;
        foreach (var part in Path.GetFullPath(path)[resolved.Length..].Split(Path.DirectorySeparatorChar))
        {
            resolved = Path.Combine(resolved, part);
            FileSystemInfo entry = Directory.Exists(resolved) ? new DirectoryInfo(resolved) : new FileInfo(resolved);
            if (entry.LinkTarget is not null) resolved = entry.ResolveLinkTarget(true)!.FullName;
        }
        return resolved;
    }

    public static async Task<string> HashFileAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)).ToLowerInvariant();
    }
}
