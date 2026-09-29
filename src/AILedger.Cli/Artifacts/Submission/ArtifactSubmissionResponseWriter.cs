using System.Text.Json;
using AILedger.Core.Artifacts;

namespace AILedger.Cli.Artifacts;

internal static class ArtifactSubmissionResponseWriter
{
    internal static JsonElement Write(ArtifactSubmissionResult result)
    {
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WriteString("attempt_id", result.AttemptId);
            writer.WriteString("status", result.Status);
            if (result.Error is { } error)
            {
                writer.WriteStartObject("error");
                writer.WriteString("code", error.Code);
                writer.WriteString("message", error.Message);
                writer.WriteString("boundary", error.Boundary);
                writer.WriteString("commit_state", error.CommitState);
                writer.WriteString("retry", error.Retry);
                writer.WriteString("item_path", error.ItemPath);
                writer.WriteString("rule_id", error.RuleId);
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteBoolean("replayed", result.Replayed);
                Receipt(writer, result.Receipt ?? throw new InvalidOperationException("Missing committed receipt."));
            }
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(bytes.ToArray());
        return document.RootElement.Clone();
    }

    private static void Receipt(Utf8JsonWriter writer, ArtifactSubmissionReceipt receipt)
    {
        writer.WritePropertyName("receipt");
        JsonSerializer.Serialize(writer, receipt, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
    }
}
