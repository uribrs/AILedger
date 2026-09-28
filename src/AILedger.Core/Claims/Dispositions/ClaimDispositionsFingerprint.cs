using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace AILedger.Core.ClaimDispositions;

public static class ClaimDispositionsFingerprint
{
    public const string Algorithm = "claim_dispositions-v1-c14n1";
    public static string Compute(ClaimDispositionsBinding binding, ClaimDispositionsRequest request) =>
        Convert.ToHexString(SHA256.HashData(CanonicalBytes(binding, request))).ToLowerInvariant();

    public static byte[] CanonicalBytes(ClaimDispositionsBinding binding, ClaimDispositionsRequest request) => Write(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("operation", "record_claim_dispositions");
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

    internal static byte[] RequestBytes(ClaimDispositionsRequest request) => Write(writer =>
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

    private static void WriteItems(Utf8JsonWriter writer, ClaimDispositionsRequest request)
    {
        writer.WriteStartArray("dispositions");
        foreach (var item in request.Dispositions)
        {
            writer.WriteStartObject();
            writer.WriteString("key", item.Key);
            writer.WriteStartObject("claim");
            writer.WriteString("claim_id", item.Claim.ClaimId);
            writer.WriteEndObject();
            writer.WriteString("expected_status", item.ExpectedStatus.ToString().ToLowerInvariant());
            writer.WriteString("status", item.Status.ToString().ToLowerInvariant());
            writer.WriteString("rationale", item.Rationale);
            writer.WriteStartArray("evidence");
            foreach (var evidence in item.Evidence)
            {
                writer.WriteStartObject();
                writer.WriteString("evidence_id", evidence.EvidenceId);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
}
