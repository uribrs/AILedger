using System.Text.Json;
using System.Text.Json.Serialization;
using AILedger.Core.ClaimDispositions;

namespace AILedger.Cli.ClaimDispositions;

internal static class ClaimDispositionsResponseWriter
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) }
    };

    internal static JsonElement Write(ClaimDispositionsResult result) => result.Error is { } error
        ? JsonSerializer.SerializeToElement(new { schema_version = 1, result.AttemptId, result.Status, error }, Json)
        : JsonSerializer.SerializeToElement(new { schema_version = 1, result.AttemptId, result.Status, result.Replayed,
            receipt = result.Receipt ?? throw new InvalidOperationException("Missing committed receipt.") }, Json);
}
