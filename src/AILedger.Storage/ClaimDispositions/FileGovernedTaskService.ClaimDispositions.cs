using System.Diagnostics;
using System.Text.Json;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.Findings;
using AILedger.Core.ClaimDispositions;
using AILedger.Storage.ClaimDispositions;

namespace AILedger.Storage;

public sealed partial class FileGovernedTaskService
{
    public async Task<ClaimDispositionsResult> RecordClaimDispositionsAsync(ClaimDispositionsBinding binding, ClaimDispositionsRequest request,
        CancellationToken cancellationToken)
    {
        var attempt = new ClaimDispositionsAttempt();
        IAsyncDisposable? lease = null;
        string? directory = null;
        ClaimDispositionsResult result;
        try
        {
            ValidateClaimDispositionsBinding(binding);
            directory = _pathResolver.Resolve(binding.TaskId);
            request = ClaimDispositionsValidation.Snapshot(request);
            attempt.Fingerprint = ClaimDispositionsFingerprint.Compute(binding, request);
            if (!Directory.Exists(directory))
                return attempt.Fail("task_not_found", "The bound task does not exist.", "application",
                    commitState: "not_committed");
            _pathResolver.EnsureTaskDirectory(directory);
            var waiting = Stopwatch.GetTimestamp();
            lease = await _mutationLock.AcquireAsync(Path.Combine(directory, _layout.LockFileName), cancellationToken)
                .ConfigureAwait(false);
            attempt.LockWaitMs = Stopwatch.GetElapsedTime(waiting).TotalMilliseconds;
            result = await RecordClaimDispositionsUnderLeaseAsync(directory, binding, request, attempt, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (FindingsRequestException e)
        {
            result = attempt.Fail(e.Code, e.Message, e.Code == "authorization_denied" ? "binding" :
                e.Code is "state_conflict" or "invalid_reference" ? "application" : "request",
                e.Code == "authorization_denied" ? "none" : "after_correction", attempt.CommitState, e.ItemPath);
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
                e.Message, "storage", "same_request");
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

        return await FinishClaimDispositionsAttemptAsync(lease, directory, binding, request, attempt, result).ConfigureAwait(false);
    }

    private async Task<ClaimDispositionsResult> FinishClaimDispositionsAttemptAsync(IAsyncDisposable? lease, string? directory,
        ClaimDispositionsBinding binding, ClaimDispositionsRequest request, ClaimDispositionsAttempt attempt, ClaimDispositionsResult result)
    {
        if (lease is null) return result;
        await using (lease.ConfigureAwait(false))
            return await attempt.CaptureAsync(directory!, binding, request, _kernelIdentity, result).ConfigureAwait(false);
    }

    private async Task<ClaimDispositionsResult> RecordClaimDispositionsUnderLeaseAsync(string directory, ClaimDispositionsBinding binding,
        ClaimDispositionsRequest request, ClaimDispositionsAttempt attempt, CancellationToken cancellationToken)
    {
        var receipts = new List<ClaimDispositionsReceipt>();
        var state = await ReplayClaimDispositionsAsync(directory, binding.TaskId, receipts, cancellationToken).ConfigureAwait(false);
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
            return await ReplayClaimDispositionsReceiptAsync(directory, state, binding, existing, attempt, cancellationToken)
                .ConfigureAwait(false);
        attempt.LookupComplete = true;
        var validationStarted = Stopwatch.GetTimestamp();
        CommandOutcome candidate;
        try { candidate = BuildClaimDispositionsCandidate(state, binding, request, attempt); }
        finally { attempt.ValidationMs = Stopwatch.GetElapsedTime(validationStarted).TotalMilliseconds; }
        if (candidate.State.Version > _maximumEventsPerTask)
            return attempt.Fail("capacity_exceeded", "The batch would exceed the task event limit.", "storage");
        var receiptNew = CreateClaimDispositionsReceipt(state, binding, request, attempt.Fingerprint!, candidate);
        attempt.Receipt = receiptNew;
        // Validate the complete resolution and consequence envelope before writing.
        using (var metadata = JsonDocument.Parse(ClaimDispositionsReceiptEnvelope.Serialize(receiptNew)))
            ClaimDispositionsReceiptEnvelope.Validate(metadata.RootElement, candidate.Events, candidate.State.Version);
        var appendStarted = Stopwatch.GetTimestamp();
        try
        {
            await AppendEventsAsync(directory, candidate.Events, cancellationToken, appendStarting: () => attempt.AppendStarted = true,
                dispositionsReceipt: receiptNew).ConfigureAwait(false);
        }
        catch (GovernanceException e)
        {
            return attempt.Fail("capacity_exceeded", e.Message, "storage");
        }
        finally { attempt.AppendMs = Stopwatch.GetElapsedTime(appendStarted).TotalMilliseconds; }
        await TryRepairDerivedStateAsync(directory, candidate.State).ConfigureAwait(false);
        return attempt.Success(receiptNew, false);
    }

    private async Task<GovernedTaskState?> ReplayClaimDispositionsAsync(string directory, TaskId taskId,
        List<ClaimDispositionsReceipt> receipts, CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, _layout.EventsFileName);
        if (!File.Exists(path)) return null;
        GovernedTaskState? state = null;
        // Mutation budgets do not revoke receipts. The same group parser supplies replay and lookup.
        await foreach (var e in ReadEventsAsync(path, cancellationToken, dispositionReceipts: receipts, enforceLimits: false)
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

    private async Task<ClaimDispositionsResult> ReplayClaimDispositionsReceiptAsync(string directory, GovernedTaskState state,
        ClaimDispositionsBinding binding, ClaimDispositionsReceipt receipt, ClaimDispositionsAttempt attempt, CancellationToken cancellationToken)
    {
        // Check the original operation shape, including on conflicts. Do not run today's stage or
        // reference rules against an already committed operation. No IDs are allocated here.
        var policy = new AuthorizationPolicy();
        attempt.Command = new ResolveClaimCommand(binding.ActorId, binding.CausationId,
            binding.CorrelationId, new ClaimId(receipt.Dispositions[0].ClaimId),
            receipt.Dispositions[0].Status, []);
        attempt.ItemPath = "dispositions";
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

    private static void ValidateClaimDispositionsBinding(ClaimDispositionsBinding binding)
    {
        if (binding is null || !binding.AllowRecordClaimDispositions || string.IsNullOrWhiteSpace(binding.TaskId.Value) ||
            string.IsNullOrWhiteSpace(binding.ActorId.Value) || string.IsNullOrWhiteSpace(binding.CorrelationId) ||
            binding.RunId is null && !binding.AllowRunless ||
            binding.RunId is { } run && (string.IsNullOrWhiteSpace(run.Value) || binding.CorrelationId != run.Value) ||
            binding.CausationId is { } cause && string.IsNullOrWhiteSpace(cause.Value))
            throw new FindingsRequestException("authorization_denied", "Invalid host claim_dispositions binding or grant.");
    }

    private static void ValidateBoundRun(ClaimDispositionsBinding binding, GovernedTaskState state)
    {
        if (binding.RunId is { } run && (!state.Runs.TryGetValue(run, out var existing) || existing.ActorId != binding.ActorId))
            throw new FindingsRequestException("authorization_denied", "The bound run does not belong to the bound actor.");
        if (binding.CausationId is { } cause && !IsPriorEventId(binding.TaskId, cause, state.Version))
            throw new FindingsRequestException("authorization_denied", "The bound cause is not an existing task event.");
    }

}
