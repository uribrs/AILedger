namespace AILedger.Core.Findings;

internal sealed record RecordingMeasurementReport<T>(
    long CanonicalVersion,
    int? CommittedTransactions,
    int? GranularEvents,
    int? ObservedTransportAttempts,
    int? ObservedToolAttempts,
    int? ObservedApplicationAttempts,
    IReadOnlyList<T> Transactions,
    IReadOnlyList<FindingsAttemptJoin> Attempts,
    IReadOnlyList<FindingsRunObservation> Runs,
    IReadOnlyList<FindingsCoverageGap> CoverageGaps,
    IReadOnlyList<string> NotMeasured);
