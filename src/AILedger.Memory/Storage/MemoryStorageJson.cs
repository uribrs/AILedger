using System.Text.Json;
using System.Text.Json.Serialization;
using AILedger.Memory.Contracts;

namespace AILedger.Memory.Storage;

internal static class MemoryStorageJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static SourceCheckpoint DeserializeCheckpoint(string json, string sourceId)
    {
        try
        {
            return JsonSerializer.Deserialize<SourceCheckpoint>(json, Options)
                ?? throw new JsonException("The checkpoint payload was null.");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new InvalidDataException(
                $"Checkpoint for source '{sourceId}' is unreadable in the memory projection; rebuild the disposable projection.",
                exception);
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}
