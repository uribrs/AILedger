using System.Diagnostics;
using System.Text.Json;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.Findings;
using AILedger.Core.Artifacts;
using AILedger.Storage.Artifacts;

namespace AILedger.Storage;

public sealed partial class FileGovernedTaskService
{
    public async Task<ArtifactSubmissionResult> SubmitArtifactAsync(ArtifactSubmissionBinding binding, ArtifactSubmissionRequest request,
        CancellationToken cancellationToken)
    {
        var attempt = new ArtifactSubmissionAttempt();
        IAsyncDisposable? lease = null;
        string? directory = null;
        ArtifactSubmissionResult result;
        try
        {
            ValidateArtifactSubmissionBinding(binding);
            directory = _pathResolver.Resolve(binding.TaskId);
            request = ArtifactSubmissionIdentity.Validate(request);
            attempt.Fingerprint = ArtifactSubmissionIdentity.Compute(binding, request);
            if (!Directory.Exists(directory))
                return attempt.Fail("task_not_found", "The bound task does not exist.", "application",
                    commitState: "not_committed");
            _pathResolver.EnsureTaskDirectory(directory);
            var waiting = Stopwatch.GetTimestamp();
            lease = await _mutationLock.AcquireAsync(Path.Combine(directory, _layout.LockFileName), cancellationToken)
                .ConfigureAwait(false);
            attempt.LockWaitMs = Stopwatch.GetElapsedTime(waiting).TotalMilliseconds;
            result = await RecordArtifactSubmissionUnderLeaseAsync(directory, binding, request, attempt, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (FindingsRequestException e)
        {
            result = attempt.Fail(e.Code, e.Message, e.Code == "authorization_denied" ? "binding" : "request",
                e.Code == "authorization_denied" ? "none" : "after_correction", "unknown", e.ItemPath);
        }
        catch (GovernanceException e)
        {
            if (lease is not null && attempt.Command is { } command)
                await _refusalJournal.TryAppendAsync(directory!, new RefusalRecord(DateTimeOffset.UtcNow,
                    command.ActorId, command.GetType().Name, RefusalSite.Service, attempt.OriginalVersion,
                    e.Message, _kernelIdentity, e.Kind.ToString())).ConfigureAwait(false);
            result = attempt.Fail("kernel_refused", e.Message, "kernel", itemPath: attempt.ItemPath);
        }
        catch (Exception e) when (e is InvalidDataException or JsonException)
        {
            result = attempt.Fail("history_corrupt", e.Message, "storage", commitState: "unknown");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            result = attempt.Fail(attempt.AppendStarted ? "outcome_unknown" : "storage_unavailable",
                e.Message + " Retry the unchanged request ID and body on the original trusted binding.", "storage", "same_request");
        }
        catch (ArgumentException e)
        {
            result = attempt.Fail("authorization_denied", e.Message, "binding", commitState: "unknown");
        }
        catch
        {
            if (lease is not null) await lease.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return await FinishArtifactSubmissionAttemptAsync(lease, directory, binding, request, attempt, result).ConfigureAwait(false);
    }

    private async Task<ArtifactSubmissionResult> FinishArtifactSubmissionAttemptAsync(IAsyncDisposable? lease, string? directory,
        ArtifactSubmissionBinding binding, ArtifactSubmissionRequest request, ArtifactSubmissionAttempt attempt, ArtifactSubmissionResult result)
    {
        if (lease is null) return result;
        await using (lease.ConfigureAwait(false))
            return await attempt.CaptureAsync(directory!, binding, request, _kernelIdentity, result).ConfigureAwait(false);
    }

    private async Task<ArtifactSubmissionResult> RecordArtifactSubmissionUnderLeaseAsync(string directory, ArtifactSubmissionBinding binding,
        ArtifactSubmissionRequest request, ArtifactSubmissionAttempt attempt, CancellationToken cancellationToken)
    {
        var receipts = new List<ArtifactSubmissionReceipt>();
        var state = await ReplayArtifactSubmissionAsync(directory, binding.TaskId, receipts, cancellationToken).ConfigureAwait(false);
        if (state is null)
        {
            attempt.LookupComplete = true;
            return attempt.Fail("task_not_found", "The bound task does not exist.", "application");
        }
        attempt.OriginalVersion = state.Version;
        ValidateBoundRun(binding, state);
        foreach (var receipt in receipts)
            if (receipt.RunId is { } run && (!state.Runs.TryGetValue(new RunId(run), out var recordedRun) ||
                recordedRun.ActorId.Value != receipt.ActorId))
                throw new InvalidDataException("Receipt run does not belong to its actor.");
        var existing = receipts.SingleOrDefault(r => r.ActorId == binding.ActorId.Value && r.RequestId == request.RequestId);
        if (existing is not null)
            return await ReplayArtifactSubmissionReceiptAsync(directory, state, binding, existing, attempt, cancellationToken)
                .ConfigureAwait(false);
        attempt.LookupComplete = true;
        var validationStarted = Stopwatch.GetTimestamp();
        CommandOutcome candidate;
        try { candidate = BuildArtifactSubmissionCandidate(state, binding, request, attempt); }
        finally { attempt.ValidationMs = Stopwatch.GetElapsedTime(validationStarted).TotalMilliseconds; }
        if (candidate.State.Version > _maximumEventsPerTask)
            return attempt.Fail("capacity_exceeded", "The batch would exceed the task event limit.", "storage");
        var receiptNew = CreateArtifactSubmissionReceipt(binding, request, attempt.Fingerprint!, candidate);
        attempt.Receipt = receiptNew;
        // Validate our own serialized envelope before writing, including the one-event assumption.
        using (var metadata = JsonDocument.Parse(ArtifactSubmissionReceiptEnvelope.Serialize(receiptNew)))
            ArtifactSubmissionReceiptEnvelope.Validate(metadata.RootElement, candidate.Events, candidate.State.Version);
        var appendStarted = Stopwatch.GetTimestamp();
        try
        {
            await AppendEventsAsync(directory, candidate.Events, cancellationToken, appendStarting: () => attempt.AppendStarted = true,
                artifactReceipt: receiptNew).ConfigureAwait(false);
        }
        catch (GovernanceException e)
        {
            return attempt.Fail("capacity_exceeded", e.Message, "storage");
        }
        finally { attempt.AppendMs = Stopwatch.GetElapsedTime(appendStarted).TotalMilliseconds; }
        await TryRepairDerivedStateAsync(directory, candidate.State).ConfigureAwait(false);
        return attempt.Success(receiptNew, false);
    }

    private async Task<GovernedTaskState?> ReplayArtifactSubmissionAsync(string directory, TaskId taskId,
        List<ArtifactSubmissionReceipt> receipts, CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, _layout.EventsFileName);
        if (!File.Exists(path)) return null;
        GovernedTaskState? state = null;
        // Mutation budgets do not revoke receipts. The same group parser supplies replay and lookup.
        await foreach (var e in ReadEventsAsync(path, cancellationToken, artifactReceipts: receipts, enforceLimits: false)
                           .ConfigureAwait(false))
        {
            ValidateEventEnvelope(taskId, e, state?.Version ?? 0);
            try { state = _reducer.Apply(state, e); }
            catch (GovernanceException error) { throw new InvalidDataException("Invalid event history.", error); }
        }
        if (state is not null)
            await EnsureEventLogHasNotLostHistoryAsync(directory, state, cancellationToken).ConfigureAwait(false);
        return state;
    }

    private async Task<ArtifactSubmissionResult> ReplayArtifactSubmissionReceiptAsync(string directory, GovernedTaskState state,
        ArtifactSubmissionBinding binding, ArtifactSubmissionReceipt receipt, ArtifactSubmissionAttempt attempt, CancellationToken cancellationToken)
    {
        // Check the original operation shape, including on conflicts. Do not run today's stage or
        // reference rules against an already committed operation. No IDs are allocated here.
        var policy = new AuthorizationPolicy();
        attempt.Command = new RecordArtifactCommand(binding.ActorId, binding.CausationId,
            binding.CorrelationId, new ArtifactId(receipt.Artifact.ArtifactId),
            GovernedArtifactKind.VerifierOutput, "receipt access", "receipt access", null, binding.RunId, null);
        attempt.ItemPath = "artifact";
        policy.Authorize(state, attempt.Command);
        attempt.Receipt = receipt;
        // A visible group may be from a prior failed flush. Re-flush before any committed assertion.
        attempt.AppendStarted = true;
        cancellationToken.ThrowIfCancellationRequested();
        await using (var stream = new FileStream(Path.Combine(directory, _layout.EventsFileName), FileMode.Open,
                         FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            FlushFindingsStream(stream, true);
        if (receipt.PayloadFingerprint != attempt.Fingerprint)
            return attempt.Fail("idempotency_conflict",
                "This key already committed different content or attribution; the new submission was not applied.",
                "application", commitState: "committed");
        return attempt.Success(receipt, true);
    }

    private static void ValidateArtifactSubmissionBinding(ArtifactSubmissionBinding binding)
    {
        if (binding is null || !binding.AllowSubmitArtifact || string.IsNullOrWhiteSpace(binding.TaskId.Value) ||
            string.IsNullOrWhiteSpace(binding.ActorId.Value) || string.IsNullOrWhiteSpace(binding.CorrelationId) ||
            string.IsNullOrWhiteSpace(binding.RunId.Value) || binding.CorrelationId != binding.RunId.Value ||
            binding.CausationId is { } cause && string.IsNullOrWhiteSpace(cause.Value))
            throw new FindingsRequestException("authorization_denied", "Invalid host artifact submission binding or grant; a trusted producer run and submit grant are required.");
    }

    private static void ValidateBoundRun(ArtifactSubmissionBinding binding, GovernedTaskState state)
    {
        if (binding.RunId is { } run && (!state.Runs.TryGetValue(run, out var existing) || existing.ActorId != binding.ActorId))
            throw new FindingsRequestException("authorization_denied", "The bound run does not belong to the bound actor.");
        if (binding.CausationId is { } cause && !IsPriorEventId(binding.TaskId, cause, state.Version))
            throw new FindingsRequestException("authorization_denied", "The bound cause is not an existing task event.");
    }

}
