using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AILedger.Storage;

internal sealed class StringIdentifierConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsValueType &&
        typeToConvert.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)?.PropertyType == typeof(string) &&
        typeToConvert.GetConstructor([typeof(string)]) is not null;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var converterType = typeof(StringIdentifierConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }

    private sealed class StringIdentifierConverter<TIdentifier> : JsonConverter<TIdentifier>
        where TIdentifier : struct
    {
        private static readonly ConstructorInfo Constructor =
            typeof(TIdentifier).GetConstructor([typeof(string)])!;

        private static readonly PropertyInfo ValueProperty =
            typeof(TIdentifier).GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)!;

        public override TIdentifier Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Create(reader.GetString());

        public override void Write(Utf8JsonWriter writer, TIdentifier value, JsonSerializerOptions options) =>
            writer.WriteStringValue(GetValue(value));

        public override TIdentifier ReadAsPropertyName(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options) => Create(reader.GetString());

        public override void WriteAsPropertyName(
            Utf8JsonWriter writer,
            TIdentifier value,
            JsonSerializerOptions options) => writer.WritePropertyName(GetValue(value));

        private static TIdentifier Create(string? value)
        {
            if (value is null)
            {
                throw new JsonException($"A {typeof(TIdentifier).Name} cannot be null.");
            }

            return (TIdentifier)Constructor.Invoke([value]);
        }

        private static string GetValue(TIdentifier value) => (string)ValueProperty.GetValue(value)!;
    }
}
