using AILedger.Core.Contracts;

using AILedger.Core.Findings;

namespace AILedger.Core.ClaimDispositions;

public static class ClaimDispositionsMeasurement
{
    public static ClaimDispositionsMeasurementReport Build(GovernedTaskState state,
        IReadOnlyList<LedgerEvent> history, IReadOnlyList<ClaimDispositionsReceipt>? receipts,
        IReadOnlyList<LocatedFindingsAttempt> observations, IReadOnlyList<FindingsCoverageGap> gaps)
    {
        var report = RecordingMeasurement.Build(state, history, receipts, observations, gaps);
        return new(report.CanonicalVersion, report.CommittedTransactions, report.GranularEvents,
            report.ObservedTransportAttempts, report.ObservedToolAttempts, report.ObservedApplicationAttempts,
            report.Transactions, report.Attempts, report.Runs, report.CoverageGaps, report.NotMeasured);
    }
}
