using System.Text;
using System.Text.Json;

namespace AILedger.Providers.Adapters;

internal sealed class SensitiveDataRedactor(IEnumerable<string> secrets)
{
    private const string Replacement = "[REDACTED]";
    private readonly string[] _secrets = secrets
        .Where(secret => !string.IsNullOrEmpty(secret))
        .Distinct(StringComparer.Ordinal)
        .OrderByDescending(secret => secret.Length)
        .ToArray();

    public string RedactText(string value)
    {
        foreach (var secret in _secrets)
        {
            value = value.Replace(secret, Replacement, StringComparison.Ordinal);
        }

        return value;
    }

    public string RedactJson(string json)
    {
        if (_secrets.Length == 0)
        {
            return json;
        }

        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteElement(writer, document.RootElement);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private void WriteElement(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(RedactText(property.Name));
                    WriteElement(writer, property.Value);
                }

                writer.WriteEndObject();
                return;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteElement(writer, item);
                }

                writer.WriteEndArray();
                return;
            case JsonValueKind.String:
                writer.WriteStringValue(RedactText(element.GetString()!));
                return;
            case JsonValueKind.Number:
                WriteLiteral(writer, element.GetRawText());
                return;
            case JsonValueKind.True:
                if (ContainsSecret("true"))
                {
                    writer.WriteStringValue(Replacement);
                }
                else
                {
                    writer.WriteBooleanValue(true);
                }

                return;
            case JsonValueKind.False:
                if (ContainsSecret("false"))
                {
                    writer.WriteStringValue(Replacement);
                }
                else
                {
                    writer.WriteBooleanValue(false);
                }

                return;
            case JsonValueKind.Null:
                if (ContainsSecret("null"))
                {
                    writer.WriteStringValue(Replacement);
                }
                else
                {
                    writer.WriteNullValue();
                }

                return;
            default:
                throw new JsonException($"Unsupported JSON value kind '{element.ValueKind}'.");
        }
    }

    private void WriteLiteral(Utf8JsonWriter writer, string value)
    {
        if (ContainsSecret(value))
        {
            writer.WriteStringValue(RedactText(value));
            return;
        }

        writer.WriteRawValue(value);
    }

    private bool ContainsSecret(string value) =>
        _secrets.Any(secret => value.Contains(secret, StringComparison.Ordinal));
}
