using AILedger.Core.Assurance;
using AILedger.Core.Episodes;

namespace AILedger.Providers.Assurance;

public sealed partial class AssuranceService
{
    private const string PlanKind = "assurance_plan_recorded";

    // Called under the case lease. This records trusted host configuration, not an agent approval.
    private async Task<EpisodeRecord?> RecordPlanAsync(AssurancePolicy policy,
        IReadOnlyList<EpisodeRecord> records, CancellationToken token)
    {
        if (policy.Plan is null || records.Any(r => r.Kind == PlanKind &&
            r.Data.GetProperty("policy_sha256").GetString() == _policyHash)) return null;
        return await _store.AppendAsync(_id, PlanKind, new
        {
            policy_sha256 = _policyHash,
            plan = policy.Plan,
            areas = policy.Areas,
            checks = policy.Checks,
            basis = "trusted operator-owned policy; authored scope and spending judgment, not assurance acceptance"
        }, token).ConfigureAwait(false);
    }
}
