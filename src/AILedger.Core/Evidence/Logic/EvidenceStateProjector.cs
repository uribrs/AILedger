using AILedger.Core.Contracts;

namespace AILedger.Core.Evidences;

internal static class EvidenceStateProjector
{
    internal static GovernedTaskState Add(GovernedTaskState state, EvidenceAdded added)
    {
        var evidence = new Dictionary<EvidenceId, Evidence>(state.Evidence)
        {
            [added.Evidence.Id] = added.Evidence
        };
        return state with { Evidence = evidence };
    }
}
