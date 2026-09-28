using System.Text.Json;
using AILedger.Core.Findings;

namespace AILedger.Storage.Findings;

// Read-only and independent of recording. Never repairs journals, scans private provider homes,
// or assumes an empty directory is a measured zero. Rows carry their source and line for audit.
internal static class FindingsObservationReader
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private const int MaximumLineBytes = 256 * 1024;
    private const long MaximumFileBytes = 64 * 1024 * 1024;

    internal static async Task<IReadOnlyList<LocatedFindingsAttempt>> ReadAsync(string taskDirectory,
        string taskId, string? transportDirectory, List<FindingsCoverageGap> gaps, CancellationToken cancellationToken,
        bool alternatives = false, bool artifacts = false)
    {
        var rows = new List<LocatedFindingsAttempt>();
        await ReadFileAsync(Path.Combine(taskDirectory, artifacts ? "artifact_submission-attempts.jsonl" : alternatives ? "alternatives-attempts.jsonl" : "findings-attempts.jsonl"), "application", taskId,
            rows, gaps, cancellationToken).ConfigureAwait(false);
        var directory = transportDirectory ?? Path.Combine(taskDirectory, "telemetry");
        try
        {
            var paths = Directory.GetFiles(directory, artifacts ? "artifact_submission-transport-*.jsonl" : alternatives ? "alternatives-transport-*.jsonl" : "findings-transport-*.jsonl");
            if (paths.Length == 0) gaps.Add(new(directory, "No transport files observed; attempts are unknown, not zero."));
            foreach (var path in paths.Order(StringComparer.Ordinal))
                await ReadFileAsync(path, "transport", taskId, rows, gaps, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            gaps.Add(new(directory, "Transport directory unavailable: " + e.GetType().Name));
        }
        // Copied or conflicting rows must not inflate counts or select an arbitrary attribution.
        var duplicates = rows.GroupBy(x => (x.Kind, Id(x))).Where(g => g.Count() > 1).ToArray();
        foreach (var group in duplicates)
        {
            foreach (var row in group)
            {
                gaps.Add(new(row.Source, "Duplicate attempt identity; all copies excluded from joins/counts.", row.Line));
                rows.Remove(row);
            }
        }
        return rows;
    }

    private static string? Id(LocatedFindingsAttempt row) => row.Kind == "application"
        ? row.Observation.AttemptId : row.Observation.TransportAttemptId;

    private static async Task ReadFileAsync(string path, string kind, string taskId,
        List<LocatedFindingsAttempt> rows, List<FindingsCoverageGap> gaps, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var remaining = stream.Length;
            if (remaining > MaximumFileBytes)
            {
                gaps.Add(new(path, "Telemetry exceeds the 64 MiB read bound; not read."));
                return;
            }
            if (remaining == 0) gaps.Add(new(path, "Empty journal; no attempts observed."));
            var buffer = new byte[4096];
            using var line = new MemoryStream();
            var oversized = false;
            long number = 1;
            while (remaining > 0)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken).ConfigureAwait(false);
                if (count == 0) { gaps.Add(new(path, "Journal shortened during read.")); break; }
                remaining -= count;
                for (var i = 0; i < count; i++)
                {
                    if (buffer[i] == (byte)'\n')
                    {
                        if (oversized) gaps.Add(new(path, "Telemetry row exceeds the 256 KiB read bound.", number));
                        else Parse(line.ToArray(), path, number, kind, taskId, rows, gaps);
                        number++;
                        line.SetLength(0);
                        oversized = false;
                    }
                    else if (line.Length < MaximumLineBytes) line.WriteByte(buffer[i]);
                    else oversized = true;
                }
            }
            if (line.Length > 0 || oversized) gaps.Add(new(path, "Unterminated telemetry tail excluded.", number));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            gaps.Add(new(path, "Telemetry unavailable: " + e.GetType().Name));
        }
    }

    private static void Parse(byte[] bytes, string path, long line, string kind, string taskId,
        List<LocatedFindingsAttempt> rows, List<FindingsCoverageGap> gaps)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var value = document.RootElement;
            Require(value.ValueKind == JsonValueKind.Object);
            var names = value.EnumerateObject().Select(p => p.Name).ToArray();
            Require(names.Distinct(StringComparer.Ordinal).Count() == names.Length);
            string[] common = ["schema_version", "request_id", "payload_fingerprint", "task_id", "actor_id", "run_id",
                "started_at", "ended_at", "duration_ms", "outcome", "code", "commit_state", "transaction_id", "event_ids", "replayed", "collection_status"];
            string[] specific = kind == "application"
                ? ["attempt_id", "kernel_identity", "lock_wait_ms", "validation_ms", "append_ms"]
                : ["transport_attempt_id", "transport_session_id", "message_kind", "adapter_identity", "correlation_id", "causation_id",
                   "provider", "provider_session_id", "application_attempt_id", "application_entered", "application_ms",
                   "application_collection_status", "boundary", "transport_failure", "response_delivery"];
            Require(common.Concat(specific).All(name => names.Contains(name, StringComparer.Ordinal)));
            var row = value.Deserialize<FindingsAttemptObservation>(Json)!;
            Require(row.SchemaVersion == 1 && row.TaskId == taskId && !string.IsNullOrWhiteSpace(row.ActorId) &&
                row.StartedAt is not null && row.EndedAt is not null && row.CollectionStatus == "collected" &&
                row.CommitState is "committed" or "not_committed" or "unknown");
            Require(kind == "application"
                ? !string.IsNullOrWhiteSpace(row.AttemptId) && row.RequestId is not null && row.Replayed is not null &&
                  row.DurationMs is not null && row.Outcome is "committed" or "error"
                : !string.IsNullOrWhiteSpace(row.TransportAttemptId) && !string.IsNullOrWhiteSpace(row.TransportSessionId) &&
                  row.ApplicationEntered is not null && row.MessageKind is "tool_call" or "protocol" or "connection" or "unclassified" &&
                  row.ResponseDelivery is "written" or "failed" or "not_sent");
            if (kind == "transport")
                Require(!string.IsNullOrWhiteSpace(row.CorrelationId) &&
                    (row.RunId is null || row.CorrelationId == row.RunId) &&
                    (row.ApplicationEntered == true || row.ApplicationAttemptId is null));
            Require(new[] { row.DurationMs, row.ApplicationMs, row.LockWaitMs, row.ValidationMs, row.AppendMs }
                .All(x => x is null || double.IsFinite(x.Value) && x.Value >= 0));
            rows.Add(new(path, line, kind, row));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            gaps.Add(new(path, "Unreadable, unsupported, incomplete or mismatched telemetry row.", line));
        }
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new JsonException("Invalid observation.");
    }
}
