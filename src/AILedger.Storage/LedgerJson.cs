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
}
