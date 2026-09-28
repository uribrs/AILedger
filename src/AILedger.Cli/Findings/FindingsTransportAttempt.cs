using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AILedger.Core.Findings;
using AILedger.Core.Alternatives;

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
    internal bool IsAlternatives { get; set; }
    internal AlternativesResult? AlternativesResult { get; set; }
    private FindingsError? Error => Result?.Error ?? AlternativesResult?.Error;
    private AILedger.Core.Contracts.IRecordingReceiptIdentity? Receipt =>
        (AILedger.Core.Contracts.IRecordingReceiptIdentity?)Result?.Receipt ?? AlternativesResult?.Receipt;
    internal string? Failure { get; set; }
    internal string Delivery { get; set; } = "not_sent";

    internal object Row(FindingsHostConfiguration host, string sessionId) => new
    {
        schema_version = 1, message_kind = MessageKind, transport_attempt_id = Id, transport_session_id = sessionId,
        adapter_identity = KernelVersion.Identity,
        request_id = RequestId, payload_fingerprint = Receipt?.PayloadFingerprint, application_attempt_id = ApplicationAttemptId,
        task_id = host.TaskId, actor_id = host.ActorId, run_id = host.RunId,
        correlation_id = host.CorrelationId, causation_id = host.CausationId,
        provider = host.Provider, provider_session_id = host.ProviderSessionId,
        started_at = StartedAt, ended_at = DateTimeOffset.UtcNow,
        duration_ms = MessageKind == "connection" ? (double?)null : Stopwatch.GetElapsedTime(_started).TotalMilliseconds,
        application_entered = ApplicationEntered, application_ms = ApplicationMs,
        application_collection_status = ApplicationEntered ? (Result?.CollectionStatus ?? AlternativesResult?.CollectionStatus) : null,
        outcome = (Result?.Status ?? AlternativesResult?.Status), code = Error?.Code, boundary = Error?.Boundary,
        commit_state = Error?.CommitState ?? (Receipt is null ? "unknown" : "committed"),
        transaction_id = Receipt?.TransactionId, event_ids = Receipt?.EventIds,
        replayed = Result?.Replayed ?? AlternativesResult?.Replayed,
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
            var path = Path.Combine(host.DiagnosticsDirectory, $"{(attempt.IsAlternatives ? "alternatives" : "findings")}-transport-{SessionId}.jsonl");
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
