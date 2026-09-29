using AILedger.Core.Contracts;
using AILedger.Core.Findings;
using AILedger.Core.Alternatives;
using AILedger.Core.Artifacts;
using AILedger.Core.ClaimDispositions;

namespace AILedger.Storage.Inspection;

internal sealed class InspectionSnapshotData(GovernedTaskState state)
{
    internal long EventLogBytes { get; init; }
    internal GovernedTaskState State { get; } = state;
    internal List<FindingsReceipt> Findings { get; init; } = [];
    internal List<AlternativesReceipt> Alternatives { get; init; } = [];
    internal List<ArtifactSubmissionReceipt> Artifacts { get; init; } = [];
    internal List<ClaimDispositionsReceipt> Dispositions { get; init; } = [];
}
