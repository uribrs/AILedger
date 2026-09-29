using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using AILedger.Core.Findings;
using AILedger.Storage;

namespace FindingsPilot;

internal static class PilotJson
{
    internal static JsonSerializerOptions Wire { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    internal static Task SaveAsync<T>(string path, T value, CancellationToken token) =>
        File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, LedgerJson.CreateOptions(indented: true)) + "\n", token);

    internal static async Task<FindingsRequest> ReadDatasetAsync(string root, string name, CancellationToken token)
    {
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "manifest.json"), token));
        foreach (var entry in manifest.RootElement.GetProperty("sha256").EnumerateObject())
        {
            Require(Path.GetFileName(entry.Name) == entry.Name, "Fixture file must be directly inside dataset directory.");
            var bytes = await File.ReadAllBytesAsync(Path.Combine(root, entry.Name), token);
            Require(Convert.ToHexString(SHA256.HashData(bytes)).Equals(entry.Value.GetString(), StringComparison.OrdinalIgnoreCase),
                "Dataset hash changed: " + entry.Name);
        }
        var request = JsonSerializer.Deserialize<FindingsRequest>(
            await File.ReadAllTextAsync(Path.Combine(root, name + ".json"), token), Wire)!;
        request = FindingsValidation.Snapshot(request);
        Require(request.Findings.Count == 20 && request.Evidence.Count == 20, "RN1 requires 20 findings and 20 evidence.");
        return request;
    }

    internal static FindingsRequest[] Batches(FindingsRequest source, int size) =>
        source.Findings.Chunk(size).Select((findings, index) =>
        {
            var keys = findings.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
            var evidence = source.Evidence.Where(e => e.Supports.Concat(e.Refutes)
                .All(r => r.Finding is not null && keys.Contains(r.Finding))).ToArray();
            return source with { RequestId = $"{source.RequestId}-{index + 1}", Findings = findings, Evidence = evidence };
        }).ToArray();
}
