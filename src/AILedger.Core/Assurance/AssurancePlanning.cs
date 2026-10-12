namespace AILedger.Core.Assurance;

public static class AssurancePlanning
{
    public const int LegacyCheckBatchLimit = 8;
    public const int ChecksPerBatch = 8;
    public const int BatchTimeoutSeconds = 300;

    public static int InitialBatchLimit(AssurancePolicy policy) => policy.Plan?.CheckBatchLimit ?? LegacyCheckBatchLimit;

    internal static void Validate(AssurancePolicy policy)
    {
        if (policy.Plan is not { } plan) return;
        AssuranceValidation.Text(plan.AreaRationale, "plan.area_rationale");
        AssuranceValidation.Text(plan.CheckBudgetRationale, "plan.check_budget_rationale");
        var minimum = MinimumCheckBatches(policy);
        AssuranceValidation.Require(plan.CheckBatchLimit >= minimum, "invalid_policy",
            $"The plan needs at least {minimum} check batches for one coverage pass. Size repair/re-verification allowance explicitly; this lower bound is not a complete execution plan.");
    }

    // Necessary capacity, not a promise that a particular packing of checks or a repair will fit.
    public static int MinimumCheckBatches(AssurancePolicy policy) => policy.Areas.Sum(area =>
    {
        var checks = area.Criteria.Select(c => c.CheckId).Distinct(StringComparer.Ordinal)
            .Select(id => policy.Checks.Single(c => c.Id == id)).ToArray();
        return Math.Max((checks.Length + ChecksPerBatch - 1) / ChecksPerBatch,
            (checks.Sum(c => c.TimeoutSeconds) + BatchTimeoutSeconds - 1) / BatchTimeoutSeconds);
    });
}
