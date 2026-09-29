using AILedger.Core.Findings;

namespace AILedger.Core.ClaimDispositions;

public sealed record ClaimDispositionsMeasurementReport(
    long CanonicalVersion,
    int? CommittedTransactions,
    int? GranularEvents,
    int? ObservedTransportAttempts,
    int? ObservedToolAttempts,
    int? ObservedApplicationAttempts,
    IReadOnlyList<ClaimDispositionsReceipt> Transactions,
    IReadOnlyList<FindingsAttemptJoin> Attempts,
    IReadOnlyList<FindingsRunObservation> Runs,
    IReadOnlyList<FindingsCoverageGap> CoverageGaps,
    IReadOnlyList<string> NotMeasured);
