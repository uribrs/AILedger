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

    public static AssuranceBinding? Binding(GovernedTaskState state, WorkItemId? anchor,
        IReadOnlyList<WorkItemId>? coverage, string? candidate, RunId? verifierRun, bool explicitCoverage = false) =>
        AILedger.Cli.Dispatch.DispatchAssuranceInput.Binding(state, anchor, coverage, candidate, verifierRun, explicitCoverage);
}
