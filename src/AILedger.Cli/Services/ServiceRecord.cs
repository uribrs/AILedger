using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AILedger.Cli.Services;

internal sealed record ServicePlan(
    string TaskId, string Id, string Checkout, string GitHead, string Worktree,
    string ProfileName, string ProfileSha256, ServiceProfile Profile, string ImageId,
    string DockerHost, string Owner, string ContainerName);

/// <summary>Operational receipt; immutable start/stop observations are cited by ordinary ledger evidence.</summary>
internal sealed record ServiceRecord(
    ServicePlan Plan, string Status, DateTimeOffset ObservedAt,
    string? ContainerId = null, string? Endpoint = null, string? Error = null,
    string? LogSha256 = null, string? LogError = null)
{
    public const string FileName = "service.json";
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    public static string Hash(string text) => Hash(Encoding.UTF8.GetBytes(text));
    public static string Confirmation(ServicePlan plan) => Hash(JsonSerializer.SerializeToUtf8Bytes(plan, ServiceProfiles.Json));

    public async Task SaveAsync(string directory, CancellationToken token)
    {
        var temporary = Path.Combine(directory, $".state-{Guid.NewGuid():N}");
        await File.WriteAllBytesAsync(temporary, JsonSerializer.SerializeToUtf8Bytes(this, ServiceProfiles.Json), token)
            .ConfigureAwait(false);
        File.Move(temporary, Path.Combine(directory, FileName), overwrite: true);
    }

    public static async Task<ServiceRecord> ReadAsync(string directory, CancellationToken token) =>
        JsonSerializer.Deserialize<ServiceRecord>(
            await File.ReadAllBytesAsync(Path.Combine(directory, FileName), token).ConfigureAwait(false), ServiceProfiles.Json)
        ?? throw new InvalidDataException("Service receipt is empty.");
}
