using AILedger.Core.Contracts;
using AILedger.Core.Findings;
using AILedger.Core.ClaimDispositions;
using AILedger.Storage.Findings;

namespace AILedger.Storage;

public sealed partial class FileGovernedTaskService
{
    // Reuse the canonical group reader, including torn-tail and receipt validation. This read
    // acquires no mutation lease and repairs nothing. The supplied retrospective history provides
    // a comparison boundary when a writer advances the log between the two snapshots.
    public async Task<ClaimDispositionsMeasurementReport> ReadClaimDispositionsMeasurementAsync(GovernedTaskState state,
        IReadOnlyList<LedgerEvent> history, string? transportDirectory, CancellationToken cancellationToken)
    {
        var directory = _pathResolver.Resolve(state.TaskId);
        var path = Path.Combine(directory, _layout.EventsFileName);
        var gaps = new List<FindingsCoverageGap>();
        List<ClaimDispositionsReceipt>? receipts = [];
        try
        {
            var events = new List<LedgerEvent>();
            await foreach (var e in ReadEventsAsync(path, cancellationToken, allowConcurrentReplacement: true,
                dispositionReceipts: receipts, enforceLimits: false).ConfigureAwait(false)) events.Add(e);
            if (!events.Select(e => e.EventId).SequenceEqual(history.Select(e => e.EventId)) || state.Version != events.Count)
                throw new InvalidDataException("Canonical snapshot differs from the retrospective; retry the report.");
            if (receipts.Any(r => r.RunId is { } run &&
                    (!state.Runs.TryGetValue(new RunId(run), out var recorded) || recorded.ActorId.Value != r.ActorId)))
                throw new InvalidDataException("Canonical claim_dispositions receipt run does not belong to its actor.");
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            receipts = null;
            gaps.Add(new(path, "Canonical receipts unavailable or snapshot changed: " + e.GetType().Name));
        }
        var observations = await FindingsObservationReader.ReadAsync(directory, state.TaskId.Value,
            transportDirectory, gaps, cancellationToken, dispositions: true).ConfigureAwait(false);
        return ClaimDispositionsMeasurement.Build(state, history, receipts, observations, gaps);
    }
}
