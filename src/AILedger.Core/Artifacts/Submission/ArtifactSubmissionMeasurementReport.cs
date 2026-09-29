using AILedger.Core.Findings;

namespace AILedger.Core.Artifacts;

public sealed record ArtifactSubmissionMeasurementReport(
    long CanonicalVersion,
    int? CommittedTransactions,
    int? GranularEvents,
    int? ObservedTransportAttempts,
    int? ObservedToolAttempts,
    int? ObservedApplicationAttempts,
    IReadOnlyList<ArtifactSubmissionReceipt> Transactions,
    IReadOnlyList<FindingsAttemptJoin> Attempts,
    IReadOnlyList<FindingsRunObservation> Runs,
    IReadOnlyList<FindingsCoverageGap> CoverageGaps,
    IReadOnlyList<string> NotMeasured);
