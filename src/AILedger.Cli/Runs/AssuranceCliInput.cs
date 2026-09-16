using AILedger.Core.Contracts;

namespace AILedger.Cli.Runs;

// CLI transport only. Core admission validates readiness and the snapshot provenance again
// under the command lock; constructing a binding here never authorizes a run.
internal static class AssuranceCliInput
{
    public static WorkItemId? Anchor(CommandLine input) =>
        input.Optional("work") is { } value ? new WorkItemId(value) : null;

    public static IReadOnlyList<WorkItemId>? Selection(CommandLine input)
    {
        var anchor = Anchor(input);
        var additional = input.Many("also-work");
        if (additional.Count > 0 && anchor is null)
        {
            throw new CliUsageException("Assurance coverage: '--also-work' requires '--work'.");
        }

        // An anchor alone is legacy transport, not an explicit coverage assertion. Passing
        // null lets Core preserve historical IDs; candidate bindings normalize below.
        return additional.Count == 0 ? null : WorkCoverage.Normalize(anchor,
            [anchor!.Value, .. additional.Select(value => new WorkItemId(value))]);
    }

    public static AssuranceBinding? Binding(GovernedTaskState state, CommandLine input) =>
        Binding(state, Anchor(input), Selection(input), input.Optional("candidate"),
            input.Optional("verifier-run") is { } pair ? new RunId(pair) : null,
            input.Many("also-work").Count > 0);

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
