using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AILedger.Cli;

namespace AILedger.Tests.Findings;

// Tests interpret the assertions used by the frozen schema, not the serializer's DTOs. Unknown
// assertion keywords fail here so a schema evolution cannot silently weaken conformance coverage.
internal static class FindingsResponseSchema
{
    internal static void AssertValid(JsonElement value, string resource = "Findings.ResponseSchema")
    {
        using var stream = typeof(CliApplication).Assembly.GetManifestResourceStream(resource)!;
        using var schema = JsonDocument.Parse(stream);
        Assert.True(Matches(value, schema.RootElement, schema.RootElement), value.GetRawText());
    }

    private static bool Matches(JsonElement value, JsonElement schema, JsonElement root)
    {
        foreach (var rule in schema.EnumerateObject())
        {
            var operand = rule.Value;
            var valid = rule.Name switch
            {
                "$schema" or "title" or "$defs" => true,
                "$ref" => Matches(value, root.GetProperty("$defs").GetProperty(operand.GetString()!.Split('/')[^1]), root),
                "oneOf" => operand.EnumerateArray().Count(s => Matches(value, s, root)) == 1,
                "const" => value.GetRawText() == operand.GetRawText(),
                "enum" => operand.EnumerateArray().Any(item => item.GetRawText() == value.GetRawText()),
                "type" => Type(value, operand),
                "required" => value.ValueKind == JsonValueKind.Object && operand.EnumerateArray()
                    .All(name => value.TryGetProperty(name.GetString()!, out _)),
                "properties" => value.ValueKind != JsonValueKind.Object || operand.EnumerateObject()
                    .All(p => !value.TryGetProperty(p.Name, out var item) || Matches(item, p.Value, root)),
                "additionalProperties" => operand.GetBoolean() || value.ValueKind != JsonValueKind.Object ||
                    value.EnumerateObject().All(p => schema.GetProperty("properties").TryGetProperty(p.Name, out _)),
                "minLength" => value.ValueKind != JsonValueKind.String || value.GetString()!.EnumerateRunes().Count() >= operand.GetInt32(),
                "maxLength" => value.ValueKind != JsonValueKind.String || value.GetString()!.EnumerateRunes().Count() <= operand.GetInt32(),
                "pattern" => value.ValueKind != JsonValueKind.String || Regex.IsMatch(value.GetString()!, operand.GetString()!, RegexOptions.CultureInvariant),
                "format" => value.ValueKind != JsonValueKind.String || operand.GetString() == "date-time" &&
                    DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                "minimum" => value.ValueKind != JsonValueKind.Number || value.GetDecimal() >= operand.GetDecimal(),
                "minItems" => value.ValueKind != JsonValueKind.Array || value.GetArrayLength() >= operand.GetInt32(),
                "maxItems" => value.ValueKind != JsonValueKind.Array || value.GetArrayLength() <= operand.GetInt32(),
                "items" => value.ValueKind != JsonValueKind.Array || value.EnumerateArray().All(v => Matches(v, operand, root)),
                _ => throw new InvalidOperationException("Unimplemented schema assertion: " + rule.Name)
            };
            if (!valid) return false;
        }
        return true;
    }

    private static bool Type(JsonElement value, JsonElement type)
    {
        if (type.ValueKind == JsonValueKind.Array) return type.EnumerateArray().Any(t => Type(value, t));
        return type.GetString() switch
        {
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "string" => value.ValueKind == JsonValueKind.String,
            "null" => value.ValueKind == JsonValueKind.Null,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            _ => throw new InvalidOperationException("Unexpected schema type.")
        };
    }
}
