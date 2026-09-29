using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace AILedger.Core.Alternatives;

public static class AlternativesFingerprint
{
    public const string Algorithm = "alternatives-v1-c14n1";
    public static string Compute(AlternativesBinding binding, AlternativesRequest request) =>
        Convert.ToHexString(SHA256.HashData(CanonicalBytes(binding, request))).ToLowerInvariant();

    public static byte[] CanonicalBytes(AlternativesBinding binding, AlternativesRequest request) => Write(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("operation", "record_alternatives");
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

    internal static byte[] RequestBytes(AlternativesRequest request) => Write(writer =>
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

    private static void WriteItems(Utf8JsonWriter writer, AlternativesRequest request)
    {
        writer.WriteStartArray("alternatives");
        foreach (var item in request.Alternatives)
        {
            writer.WriteStartObject();
            writer.WriteString("key", item.Key);
            writer.WriteString("statement", item.Statement);
            writer.WriteString("rejection_rationale", item.RejectionRationale);
            writer.WriteString("replaced_by_decision_id", item.ReplacedByDecisionId);
            writer.WriteString("from_lesson", item.FromLesson);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
}
