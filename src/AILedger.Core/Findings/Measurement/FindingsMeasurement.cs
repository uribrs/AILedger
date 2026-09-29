using AILedger.Core.Contracts;

namespace AILedger.Core.Findings;

public static class FindingsMeasurement
{
    public static FindingsMeasurementReport Build(GovernedTaskState state,
        IReadOnlyList<LedgerEvent> history, IReadOnlyList<FindingsReceipt>? receipts,
        IReadOnlyList<LocatedFindingsAttempt> observations, IReadOnlyList<FindingsCoverageGap> gaps)
    {
        var report = RecordingMeasurement.Build(state, history, receipts, observations, gaps);
        return new(report.CanonicalVersion, report.CommittedTransactions, report.GranularEvents,
            report.ObservedTransportAttempts, report.ObservedToolAttempts, report.ObservedApplicationAttempts,
            report.Transactions, report.Attempts, report.Runs, report.CoverageGaps, report.NotMeasured);
    }
}
