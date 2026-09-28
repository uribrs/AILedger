using System.Text.Json;
using AILedger.Core.Findings;

namespace AILedger.Cli.Findings;

internal static class FindingsResponseWriter
{
    internal static JsonElement Write(FindingsResult result)
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

    private static void Receipt(Utf8JsonWriter writer, FindingsReceipt receipt)
    {
        writer.WriteStartObject("receipt");
        writer.WriteNumber("schema_version", receipt.SchemaVersion);
        writer.WriteString("request_id", receipt.RequestId);
        writer.WriteString("transaction_id", receipt.TransactionId);
        writer.WriteString("task_id", receipt.TaskId);
        writer.WriteString("actor_id", receipt.ActorId);
        writer.WriteString("run_id", receipt.RunId);
        writer.WriteString("correlation_id", receipt.CorrelationId);
        writer.WriteString("causation_id", receipt.CausationId);
        writer.WriteString("payload_fingerprint", receipt.PayloadFingerprint);
        writer.WriteString("fingerprint_algorithm", receipt.FingerprintAlgorithm);
        writer.WriteString("committed_at", receipt.CommittedAt);
        writer.WriteNumber("ledger_version", receipt.LedgerVersion);
        writer.WriteStartArray("event_ids");
        foreach (var id in receipt.EventIds) writer.WriteStringValue(id);
        writer.WriteEndArray();
        writer.WriteStartArray("findings");
        foreach (var item in receipt.Findings) Map(writer, item.Key, "claim_id", item.ClaimId, item.EventId);
        writer.WriteEndArray();
        writer.WriteStartArray("evidence");
        foreach (var item in receipt.Evidence) Map(writer, item.Key, "evidence_id", item.EvidenceId, item.EventId);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void Map(Utf8JsonWriter writer, string key, string idName, string id, string eventId)
    {
        writer.WriteStartObject();
        writer.WriteString("key", key);
        writer.WriteString(idName, id);
        writer.WriteString("event_id", eventId);
        writer.WriteEndObject();
    }
}
