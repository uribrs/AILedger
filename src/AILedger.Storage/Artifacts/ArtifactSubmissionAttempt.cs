using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Core.Artifacts;

namespace AILedger.Storage.Artifacts;

internal sealed class ArtifactSubmissionAttempt
{
    internal string Id { get; } = Guid.NewGuid().ToString("N");
    internal DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
    private readonly long _started = Stopwatch.GetTimestamp();
    internal string? Fingerprint { get; set; }
    internal double? LockWaitMs { get; set; }
    internal double? ValidationMs { get; set; }
    internal double? AppendMs { get; set; }
    internal bool LookupComplete { get; set; }
    internal bool AppendStarted { get; set; }
    internal ArtifactSubmissionReceipt? Receipt { get; set; }
    internal LedgerCommand? Command { get; set; }
    internal string? ItemPath { get; set; }
    internal long OriginalVersion { get; set; }
    internal string CommitState => AppendStarted || !LookupComplete ? "unknown" : "not_committed";

    internal ArtifactSubmissionResult Fail(string code, string message, string boundary, string retry = "none",
        string? commitState = null, string? itemPath = null) =>
        new(Id, null, false, new(code, message, boundary, commitState ?? CommitState, retry, itemPath));
    internal ArtifactSubmissionResult Success(ArtifactSubmissionReceipt receipt, bool replayed) => new(Id, receipt, replayed, null);

    // Called under the already-held task lease. No evidence prose or provider cost is copied here.
    // Missing rows are collection gaps; this file is never read to decide whether a batch committed.
    internal async Task<ArtifactSubmissionResult> CaptureAsync(string directory, ArtifactSubmissionBinding binding,
        ArtifactSubmissionRequest request, KernelBuildIdentity kernel, ArtifactSubmissionResult result)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
            var row = new
            {
                schema_version = 1, attempt_id = Id, request_id = request.RequestId,
                payload_fingerprint = Fingerprint, task_id = binding.TaskId.Value,
                actor_id = binding.ActorId.Value, run_id = binding.RunId.Value, kernel_identity = kernel,
                started_at = StartedAt, ended_at = DateTimeOffset.UtcNow,
                duration_ms = Stopwatch.GetElapsedTime(_started).TotalMilliseconds,
                lock_wait_ms = LockWaitMs, validation_ms = ValidationMs, append_ms = AppendMs,
                outcome = result.Status, code = result.Error?.Code,
                commit_state = result.Error?.CommitState ?? "committed",
                transaction_id = Receipt?.TransactionId, event_ids = Receipt?.EventIds,
                replayed = result.Replayed, collection_status = "collected"
            };
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(row) + "\n");
            await using var stream = new FileStream(Path.Combine(directory, "artifact_submission-attempts.jsonl"),
                FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous);
            await stream.WriteAsync(bytes, timeout.Token).ConfigureAwait(false);
            await stream.FlushAsync(timeout.Token).ConfigureAwait(false);
            return result with { CollectionStatus = "collected" };
        }
        catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            return result with { CollectionStatus = "unavailable" };
        }
    }
}
