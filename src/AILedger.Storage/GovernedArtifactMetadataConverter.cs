using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AILedger.Core.Contracts;

namespace AILedger.Storage;

// An artifact body belongs to the event log, which is authoritative and append-only. Every derived
// view over that log — state.json here, the status command in the CLI — is a projection, and a
// projection that repeats the body pays for text it does not own: one task of real documents
// measures 188 KB against a state file of 82 KB. So the projection keeps every field that
// identifies the artifact and replaces the body with the size of the body in the log.
//
// The metadata is produced by serialising the artifact with body-carrying options and dropping one
// property from the result, rather than by writing the fields out here. A converter that enumerates
// them omits whatever the record gains next, and the projection would then be missing a field
// nobody chose to remove.
internal sealed class GovernedArtifactMetadataConverter : JsonConverter<GovernedArtifact>
{
    private readonly JsonSerializerOptions _bodyCarrying;
    private readonly string _contentPropertyName;
    private readonly string _contentBytesPropertyName;

    internal GovernedArtifactMetadataConverter(JsonSerializerOptions bodyCarrying)
    {
        _bodyCarrying = bodyCarrying ?? throw new ArgumentNullException(nameof(bodyCarrying));
        _contentPropertyName = PropertyName(bodyCarrying, nameof(GovernedArtifact.Content));
        _contentBytesPropertyName = PropertyName(bodyCarrying, "ContentBytes");
    }

    // A projection is not a source. Reading one back would hand the caller an artifact whose body is
    // silently absent, and that value would look like ledger truth, so it fails instead. The event
    // log is where the body is.
    public override GovernedArtifact Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        throw new NotSupportedException(
            "A governed artifact projection carries no body and cannot be read back as an artifact; " +
            "replay the event log instead.");

    public override void Write(Utf8JsonWriter writer, GovernedArtifact value, JsonSerializerOptions options)
    {
        var metadata = JsonSerializer.SerializeToNode(value, _bodyCarrying)!.AsObject();
        metadata.Remove(_contentPropertyName);
        // The count is of the bytes the body occupies in the log, not of its characters, because the
        // budget it is read against — 16 MB of events per task — is measured in bytes.
        metadata[_contentBytesPropertyName] = Encoding.UTF8.GetByteCount(value.Content);
        metadata.WriteTo(writer, options);
    }

    private static string PropertyName(JsonSerializerOptions options, string propertyName) =>
        options.PropertyNamingPolicy?.ConvertName(propertyName) ?? propertyName;
}
