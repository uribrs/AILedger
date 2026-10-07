using AILedger.Core.Contracts;
namespace AILedger.Cli.Dispatch;
internal static class DispatchAssuranceInput
{
    public static IReadOnlyList<WorkItemId>? Selection(ProviderDispatchRequest input)
    {
        if (input.AdditionalWork.Count == 0) return null;
        if (input.WorkItemId is null) throw new CliUsageException("Assurance coverage: '--also-work' requires '--work'.");
        return WorkCoverage.Normalize(input.WorkItemId, [input.WorkItemId.Value, .. input.AdditionalWork]);
    }
    public static AssuranceBinding? Binding(GovernedTaskState state, ProviderDispatchRequest input) =>
        Binding(state, input.WorkItemId, Selection(input), input.Candidate, input.VerifierRun, input.AdditionalWork.Count > 0);
    public static AssuranceBinding? Binding(
        GovernedTaskState state,
        WorkItemId? anchor,
        IReadOnlyList<WorkItemId>? coverage,
        string? candidate,
        RunId? verifierRun,
        bool explicitCoverage = false)
    {
        if (candidate is null)
        {
            if (explicitCoverage || verifierRun is not null)
            {
                throw new CliUsageException(
                    "Assurance coverage: additional members and '--verifier-run' require '--candidate'.");
            }

            return null;
        }

        if (anchor is null)
        {
            throw new CliUsageException("Assurance coverage: '--candidate' requires '--work'.");
        }

        var members = WorkCoverage.Normalize(anchor, coverage ?? [anchor.Value]);
        // Missing/tied provenance is deliberately not refused here: Core owns the full admission
        // order, including capability, stage, authority, briefing and member state before readiness.
        var versions = members.SelectMany(member => state.Runs.Values
            .Where(run => run.WorkItemId == member && run.Status == AgentRunStatus.Completed
                && !string.Equals(run.Provider, AgentRun.NoProvider, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(run.ProviderSessionId)
                && run.SubjectRole is RoleKind.Worker or RoleKind.Researcher)
            .OrderByDescending(run => run.EndedAt)
            .ThenBy(run => run.Id.Value, StringComparer.Ordinal)
            .Take(1)
            .Select(run => new AssuranceWorkVersion(member, run.Id))).ToArray();
        return new AssuranceBinding(1, members, versions, candidate, verifierRun);
    }
}
