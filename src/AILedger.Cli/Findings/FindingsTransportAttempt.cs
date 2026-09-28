using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AILedger.Core.Findings;

namespace AILedger.Cli.Findings;

internal sealed class FindingsTransportAttempt
{
    internal string Id { get; } = Guid.NewGuid().ToString("N");
    internal DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
    private readonly long _started = Stopwatch.GetTimestamp();
    internal string MessageKind { get; set; } = "unclassified";
    internal string? RequestId { get; set; }
    internal string? ApplicationAttemptId { get; set; }
    internal bool ApplicationEntered { get; set; }
    internal double? ApplicationMs { get; set; }
    internal FindingsResult? Result { get; set; }
    internal string? Failure { get; set; }
    internal string Delivery { get; set; } = "not_sent";

    internal object Row(FindingsHostConfiguration host, string sessionId) => new
    {
        schema_version = 1, message_kind = MessageKind, transport_attempt_id = Id, transport_session_id = sessionId,
        adapter_identity = KernelVersion.Identity,
        request_id = RequestId, payload_fingerprint = Result?.Receipt?.PayloadFingerprint, application_attempt_id = ApplicationAttemptId,
        task_id = host.TaskId, actor_id = host.ActorId, run_id = host.RunId,
        correlation_id = host.CorrelationId, causation_id = host.CausationId,
        provider = host.Provider, provider_session_id = host.ProviderSessionId,
        started_at = StartedAt, ended_at = DateTimeOffset.UtcNow,
        duration_ms = MessageKind == "connection" ? (double?)null : Stopwatch.GetElapsedTime(_started).TotalMilliseconds,
        application_entered = ApplicationEntered, application_ms = ApplicationMs,
        application_collection_status = ApplicationEntered ? Result?.CollectionStatus : null,
        outcome = Result?.Status, code = Result?.Error?.Code, boundary = Result?.Error?.Boundary,
        commit_state = Result?.Error?.CommitState ?? (Result?.Receipt is null ? "unknown" : "committed"),
        transaction_id = Result?.Receipt?.TransactionId, event_ids = Result?.Receipt?.EventIds,
        replayed = Result is null ? (bool?)null : Result.Replayed,
        transport_failure = Failure, response_delivery = Delivery, collection_status = "collected"
    };
}

internal sealed class FindingsTransportJournal(FindingsHostConfiguration host, TextWriter error)
{
    internal string SessionId { get; } = Guid.NewGuid().ToString("N");
    private readonly SemaphoreSlim _gate = new(1, 1);

    internal async Task CaptureAsync(FindingsTransportAttempt attempt)
    {
        var acquired = false;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
            await _gate.WaitAsync(timeout.Token).ConfigureAwait(false);
            acquired = true;
            Directory.CreateDirectory(host.DiagnosticsDirectory);
            var path = Path.Combine(host.DiagnosticsDirectory, $"findings-transport-{SessionId}.jsonl");
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(attempt.Row(host, SessionId)) + "\n");
            await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read,
                4096, FileOptions.Asynchronous);
            await stream.WriteAsync(bytes, timeout.Token).ConfigureAwait(false);
            await stream.FlushAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            // Observations cannot revoke a receipt or pollute stdout. This fallback contains no prose.
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
                var row = JsonSerializer.Serialize(new { transport_attempt_id = attempt.Id,
                    collection_status = "unavailable" });
                await error.WriteLineAsync(row.AsMemory(), timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ignored) when (ignored is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) { }
        }
        finally { if (acquired) _gate.Release(); }
    }
}
