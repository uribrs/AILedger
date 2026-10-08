using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AILedger.Core.Domain;

namespace AILedger.Cli.Services;

/// <summary>Deliberately no host mounts, environment inheritance, network mode or Docker options.</summary>
internal sealed record ServiceProfile(
    string Image,
    int ContainerPort,
    int HostPort = 0,
    string ReadinessPath = "/",
    int ReadinessStatus = 200,
    int StartupSeconds = 60);

internal sealed record ServiceProfiles(int SchemaVersion, Dictionary<string, ServiceProfile> Profiles)
{
    public const string FileName = "ailedger.services.json";
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static async Task<(ServiceProfile Profile, string Hash)> ReadAsync(
        string checkout, string name, CancellationToken token)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(checkout, FileName), token).ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(bytes);
            RejectDuplicates(document.RootElement);
            var file = JsonSerializer.Deserialize<ServiceProfiles>(bytes, Json)
                ?? throw new JsonException("Profile file is null.");
            if (file.SchemaVersion != 1 || file.Profiles is null || file.Profiles.Count == 0)
                throw new JsonException("schemaVersion must be 1 and profiles must be nonempty.");
            foreach (var (key, profile) in file.Profiles)
            {
                if (!Regex.IsMatch(key, "^[a-z0-9][a-z0-9-]{0,63}$") || profile is null)
                    throw new JsonException("Invalid profile name or null profile.");
                Validate(profile);
            }
            if (!file.Profiles.TryGetValue(name, out var selected))
                throw new JsonException($"Unknown service profile '{name}'.");
            return (selected, ServiceRecord.Hash(bytes));
        }
        catch (JsonException exception)
        {
            throw new GovernanceException($"{FileName}: {exception.Message}");
        }
    }

    private static void Validate(ServiceProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Image) || profile.Image.Length > 256 ||
            profile.Image.Any(char.IsWhiteSpace) || profile.Image.Any(char.IsControl))
            throw new JsonException("image must be a local Docker image reference.");
        if (profile.ContainerPort is < 1 or > 65535 || profile.HostPort is < 0 or > 65535)
            throw new JsonException("containerPort must be 1..65535; hostPort must be 0..65535 (0 allocates a port).");
        if (profile.ReadinessPath is null || !profile.ReadinessPath.StartsWith('/') ||
            profile.ReadinessPath.StartsWith("//", StringComparison.Ordinal) ||
            profile.ReadinessPath.Contains('\\') || profile.ReadinessPath.Any(char.IsControl) ||
            profile.ReadinessPath.Length > 1024)
            throw new JsonException("readinessPath must be a local HTTP path starting with one slash.");
        if (profile.ReadinessStatus is < 200 or > 499 || profile.StartupSeconds is < 1 or > 300)
            throw new JsonException("readinessStatus must be 200..499; startupSeconds must be 1..300.");
    }

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name)) throw new JsonException($"Duplicate property '{property.Name}'.");
            RejectDuplicates(property.Value);
        }
    }
}
