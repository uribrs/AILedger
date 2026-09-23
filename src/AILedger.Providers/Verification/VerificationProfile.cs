using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AILedger.Providers.Verification;

/// <summary>
/// One named entry of a repository verification profile file (contract S1). The command is shell
/// text the operator confirms by digest before it runs; nothing here executes it.
/// </summary>
public sealed record VerificationProfile(
    string Name,
    string Command,
    IReadOnlyList<string> Requires,
    bool LaunchPreflight,
    IReadOnlyList<string> ResultFiles,
    int TimeoutSeconds)
{
    public bool RequiresDocker => Requires.Contains(VerificationProfileFile.DockerRequirement, StringComparer.Ordinal);
}

/// <summary>
/// A parsed <c>ailedger.verification.json</c>: where it was found, the SHA-256 of its exact bytes,
/// and its profiles in file order.
/// </summary>
public sealed record VerificationProfileFile(
    string Path,
    string Sha256,
    IReadOnlyList<VerificationProfile> Profiles)
{
    public const string FileName = "ailedger.verification.json";
    public const string DockerRequirement = "docker";
    public const int DefaultTimeoutSeconds = 1800;
    public const int MaxTimeoutSeconds = 86400;
    public const int SchemaVersion = 1;

    private const char LineSeparator = (char)0x2028;
    private const char ParagraphSeparator = (char)0x2029;

    /// <summary>
    /// Contract S1: the profile file at the repository root of <paramref name="workingDirectory"/>, or
    /// null. The root is the first directory at or above it holding a <c>.git</c> entry, a directory
    /// in a clone and a file in a worktree. Nothing above the root and no intermediate directory is
    /// looked at, so a repository without the file has no profile and nothing changes for it.
    /// </summary>
    public static string? Discover(string workingDirectory)
    {
        for (var directory = new DirectoryInfo(System.IO.Path.GetFullPath(workingDirectory));
             directory is not null;
             directory = directory.Parent)
        {
            var git = System.IO.Path.Combine(directory.FullName, ".git");
            if (File.Exists(git) || Directory.Exists(git))
            {
                var candidate = System.IO.Path.Combine(directory.FullName, FileName);
                return File.Exists(candidate) ? candidate : null;
            }
        }

        return null;
    }

    /// <summary>Discovers and reads the profile file, or returns null when there is none.</summary>
    public static VerificationProfileFile? Find(string workingDirectory) =>
        Discover(workingDirectory) is { } path ? Read(path) : null;

    /// <summary>
    /// Reads and validates one profile file against schema version 1. Every refusal names the file
    /// path and the field, and an unknown property anywhere is refused rather than ignored.
    /// </summary>
    public static VerificationProfileFile Read(string path)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(fullPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new VerificationProfileException(fullPath, "(file)", $"cannot be read: {exception.Message}");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes);
        }
        catch (JsonException exception)
        {
            throw new VerificationProfileException(fullPath, "(file)", $"is not valid JSON: {exception.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            RequireKind(fullPath, "(root)", root, JsonValueKind.Object);
            var fields = Properties(fullPath, "(root)", root, "schemaVersion", "profiles");

            if (!fields.TryGetValue("schemaVersion", out var version))
            {
                throw new VerificationProfileException(fullPath, "schemaVersion", "is required");
            }

            if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) ||
                number != SchemaVersion)
            {
                throw new VerificationProfileException(fullPath, "schemaVersion", $"must be {SchemaVersion}");
            }

            if (!fields.TryGetValue("profiles", out var profilesElement))
            {
                throw new VerificationProfileException(fullPath, "profiles", "is required");
            }

            RequireKind(fullPath, "profiles", profilesElement, JsonValueKind.Object);
            var profiles = new List<VerificationProfile>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in profilesElement.EnumerateObject())
            {
                var field = $"profiles.{entry.Name}";
                if (!names.Add(entry.Name))
                {
                    throw new VerificationProfileException(fullPath, field, "is declared more than once");
                }

                if (!ProfileName.IsMatch(entry.Name))
                {
                    throw new VerificationProfileException(
                        fullPath, field, "has a name that does not match [a-z0-9][a-z0-9-]*");
                }

                profiles.Add(ReadProfile(fullPath, field, entry.Name, entry.Value));
            }

            if (profiles.Count == 0)
            {
                throw new VerificationProfileException(fullPath, "profiles", "must declare at least one profile");
            }

            return new VerificationProfileFile(
                fullPath, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), profiles);
        }
    }

    private static VerificationProfile ReadProfile(string path, string field, string name, JsonElement element)
    {
        RequireKind(path, field, element, JsonValueKind.Object);
        var fields = Properties(
            path, field, element, "command", "requires", "launchPreflight", "resultFiles", "timeoutSeconds");

        if (!fields.TryGetValue("command", out var commandElement) ||
            commandElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(commandElement.GetString()))
        {
            throw new VerificationProfileException(path, $"{field}.command", "is required and must be a non-blank string");
        }

        // The command is rendered into every governed briefing for the repository, one line per
        // field; a line break or other control character in it could add lines of its own.
        if (commandElement.GetString()!.Any(character =>
                char.IsControl(character) || character is LineSeparator or ParagraphSeparator))
        {
            throw new VerificationProfileException(
                path, $"{field}.command", "must be one line with no control characters or line separators");
        }

        var requires = fields.TryGetValue("requires", out var requiresElement)
            ? Strings(path, $"{field}.requires", requiresElement)
            : [];
        for (var index = 0; index < requires.Count; index++)
        {
            if (!string.Equals(requires[index], DockerRequirement, StringComparison.Ordinal))
            {
                throw new VerificationProfileException(
                    path, $"{field}.requires[{index}]", $"'{requires[index]}' is not supported; the only allowed value is '{DockerRequirement}'");
            }
        }

        if (requires.Count != requires.Distinct(StringComparer.Ordinal).Count())
        {
            throw new VerificationProfileException(path, $"{field}.requires", "lists a value more than once");
        }

        var launchPreflight = false;
        if (fields.TryGetValue("launchPreflight", out var preflightElement))
        {
            if (preflightElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw new VerificationProfileException(path, $"{field}.launchPreflight", "must be true or false");
            }

            launchPreflight = preflightElement.GetBoolean();
        }

        if (launchPreflight && !requires.Contains(DockerRequirement, StringComparer.Ordinal))
        {
            throw new VerificationProfileException(
                path, $"{field}.launchPreflight", $"is valid only when requires contains '{DockerRequirement}'");
        }

        var resultFiles = fields.TryGetValue("resultFiles", out var resultElement)
            ? Strings(path, $"{field}.resultFiles", resultElement)
            : [];
        for (var index = 0; index < resultFiles.Count; index++)
        {
            var glob = resultFiles[index];
            if (System.IO.Path.IsPathRooted(glob) || glob.StartsWith('/') ||
                glob.Replace('\\', '/').Split('/').Contains(".."))
            {
                throw new VerificationProfileException(
                    path, $"{field}.resultFiles[{index}]", "must be relative to AILEDGER_VERIFICATION_OUTPUT and may not contain '..'");
            }
        }

        var timeoutSeconds = DefaultTimeoutSeconds;
        if (fields.TryGetValue("timeoutSeconds", out var timeoutElement) &&
            (timeoutElement.ValueKind != JsonValueKind.Number ||
             !timeoutElement.TryGetInt32(out timeoutSeconds) || timeoutSeconds is < 1 or > MaxTimeoutSeconds))
        {
            throw new VerificationProfileException(
                path, $"{field}.timeoutSeconds", $"must be an integer from 1 to {MaxTimeoutSeconds}");
        }

        return new VerificationProfile(
            name, commandElement.GetString()!, requires, launchPreflight, resultFiles, timeoutSeconds);
    }

    private static Dictionary<string, JsonElement> Properties(
        string path, string field, JsonElement element, params string[] allowed)
    {
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            var name = field == "(root)" ? property.Name : $"{field}.{property.Name}";
            if (!allowed.Contains(property.Name, StringComparer.Ordinal))
            {
                throw new VerificationProfileException(path, name, "is not a known property");
            }

            if (!properties.TryAdd(property.Name, property.Value))
            {
                throw new VerificationProfileException(path, name, "is declared more than once");
            }
        }

        return properties;
    }

    private static List<string> Strings(string path, string field, JsonElement element)
    {
        RequireKind(path, field, element, JsonValueKind.Array);
        var values = new List<string>();
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                throw new VerificationProfileException(path, $"{field}[{index}]", "must be a non-blank string");
            }

            values.Add(item.GetString()!);
            index++;
        }

        return values;
    }

    private static void RequireKind(string path, string field, JsonElement element, JsonValueKind kind)
    {
        if (element.ValueKind != kind)
        {
            throw new VerificationProfileException(
                path, field, $"must be a JSON {kind.ToString().ToLowerInvariant()}");
        }
    }

    private static readonly Regex ProfileName = new("^[a-z0-9][a-z0-9-]*$", RegexOptions.CultureInvariant);
}

/// <summary>
/// A verification profile file that cannot be used. The message names the file and the field.
/// Callers that refuse on it wrap it in their own refusal type.
/// </summary>
public sealed class VerificationProfileException(string path, string field, string problem)
    : Exception($"Verification profile '{path}': field '{field}' {problem}.")
{
    public string ProfilePath { get; } = path;
    public string Field { get; } = field;
}
