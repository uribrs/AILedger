using AILedger.Core.Findings;

namespace AILedger.Core.Alternatives;

public sealed record AlternativesMeasurementReport(
    long CanonicalVersion,
    int? CommittedTransactions,
    int? GranularEvents,
    int? ObservedTransportAttempts,
    int? ObservedToolAttempts,
    int? ObservedApplicationAttempts,
    IReadOnlyList<AlternativesReceipt> Transactions,
    IReadOnlyList<FindingsAttemptJoin> Attempts,
    IReadOnlyList<FindingsRunObservation> Runs,
    IReadOnlyList<FindingsCoverageGap> CoverageGaps,
    IReadOnlyList<string> NotMeasured);
