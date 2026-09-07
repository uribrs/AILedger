using System.Text.Json;
using System.Text.Json.Serialization;

namespace AILedger.Storage;

public static class LedgerJson
{
    public static JsonSerializerOptions CreateOptions(bool indented = false)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = indented,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.Converters.Add(new StringIdentifierConverterFactory());
        return options;
    }

    // For a view over the log rather than the log itself: identical to CreateOptions except that a
    // governed artifact renders as its metadata and the size of its body, not as the body. Use it
    // wherever whole state is written out for reading — state.json, the status command — and never
    // for the event log, which is the one place the body has to survive.
    public static JsonSerializerOptions CreateProjectionOptions(bool indented = false)
    {
        var options = CreateOptions(indented);
        options.Converters.Add(new GovernedArtifactMetadataConverter(CreateOptions()));
        return options;
    }
}
