using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace AILedger.Core.Findings;

public static class FindingsFingerprint
{
    public const string Algorithm = "findings-v1-c14n1";
    public static string Compute(FindingsBinding binding, FindingsRequest request) =>
        Convert.ToHexString(SHA256.HashData(CanonicalBytes(binding, request))).ToLowerInvariant();

    public static byte[] CanonicalBytes(FindingsBinding binding, FindingsRequest request) => Write(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("operation", "record_findings");
        writer.WriteNumber("schema_version", request.SchemaVersion);
        writer.WriteStartObject("binding");
        writer.WriteString("task_id", binding.TaskId.Value);
        writer.WriteString("actor_id", binding.ActorId.Value);
        writer.WriteString("run_id", binding.RunId?.Value);
        writer.WriteString("correlation_id", binding.CorrelationId);
        writer.WriteString("causation_id", binding.CausationId?.Value);
        writer.WriteEndObject();
        WriteItems(writer, request);
        writer.WriteEndObject();
    });

    internal static byte[] RequestBytes(FindingsRequest request) => Write(writer =>
    {
        writer.WriteStartObject();
        writer.WriteNumber("schema_version", request.SchemaVersion);
        writer.WriteString("request_id", request.RequestId);
        WriteItems(writer, request);
        writer.WriteEndObject();
    });

    private static byte[] Write(Action<Utf8JsonWriter> action)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
               { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })) action(writer);
        return stream.ToArray();
    }

    private static void WriteItems(Utf8JsonWriter writer, FindingsRequest request)
    {
        writer.WriteStartArray("findings");
        foreach (var item in request.Findings)
        {
            writer.WriteStartObject();
            writer.WriteString("key", item.Key);
            writer.WriteString("statement", item.Statement);
            writer.WriteString("consequence_if_wrong", item.ConsequenceIfWrong);
            writer.WriteString("from_lesson", item.FromLesson);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteStartArray("evidence");
        foreach (var item in request.Evidence)
        {
            writer.WriteStartObject();
            writer.WriteString("key", item.Key);
            writer.WriteString("source_type", item.SourceType);
            writer.WriteString("citation", item.Citation);
            writer.WriteString("summary", item.Summary);
            WriteReferences(writer, "supports", item.Supports);
            WriteReferences(writer, "refutes", item.Refutes);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteReferences(Utf8JsonWriter writer, string name, IReadOnlyList<FindingReference> references)
    {
        writer.WriteStartArray(name);
        foreach (var item in references)
        {
            writer.WriteStartObject();
            if (item.Finding is not null) writer.WriteString("finding", item.Finding);
            else writer.WriteString("claim_id", item.ClaimId);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
}
