using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Core.Artifacts;

internal static class WorkflowRetrospectiveRules
{
    internal static readonly IReadOnlyList<string> TableHeader =
        ["dimension", "score", "confidence", "controllable", "evidence"];

    private static readonly IReadOnlyList<string> DimensionIds =
        ["D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9", "D10"];

    private static readonly IReadOnlySet<string> Scores =
        new HashSet<string>(StringComparer.Ordinal) { "0", "1", "2", "3", "4", "5", "unmeasured" };

    private static readonly IReadOnlySet<string> Confidences =
        new HashSet<string>(StringComparer.Ordinal) { "low", "medium", "high" };

    private static readonly IReadOnlySet<string> Controllable =
        new HashSet<string>(StringComparer.Ordinal) { "yes", "no", "not-applicable" };

    // Command-time only. It reads aggregate stage, work, and run state, so adding it to replay would
    // retroactively reject histories that were legal when written.
    internal static void EnsureEntryCondition(GovernedTaskState state)
    {
        if (state.Stage != TaskStage.Archive)
        {
            throw new GovernanceException(StageRefusal(state.Stage));
        }

        var live = state.WorkItems.Values
            .Where(item => item.Status is not (WorkItemStatus.Completed or WorkItemStatus.Stale
                or WorkItemStatus.Abandoned))
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault();
        if (live is not null)
        {
            throw new GovernanceException(LiveWorkRefusal(live.Id, live.Status));
        }

        var active = state.Runs.Values
            .Where(run => run.Status == AgentRunStatus.Active)
            .OrderBy(run => run.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault();
        if (active is not null)
        {
            throw new GovernanceException(ActiveRunRefusal(active.Id));
        }
    }

    internal static void Validate(string content)
    {
        var refusal = TableRefusal();
        var rows = MarkdownTableReader.Read(content, TableHeader, refusal);
        var ids = rows.Select(row => row[0]).ToArray();
        MarkdownTableReader.EnsureUnique(ids, "Workflow retrospective dimension IDs", StringComparer.Ordinal);
        foreach (var dimensionId in DimensionIds)
        {
            var row = rows.SingleOrDefault(candidate => candidate[0] == dimensionId)
                ?? throw new GovernanceException(
                    $"Workflow retrospective must score dimension '{dimensionId}'. " + refusal);
            if (!Scores.Contains(row[1]) ||
                !Confidences.Contains(row[2]) ||
                !Controllable.Contains(row[3]) ||
                string.IsNullOrWhiteSpace(row[4]))
            {
                throw new GovernanceException(
                    $"Workflow retrospective row '{dimensionId}' uses a value outside the frozen vocabulary. " +
                    refusal);
            }
        }

        var unknown = ids.Except(DimensionIds, StringComparer.Ordinal).ToArray();
        if (unknown.Length != 0)
        {
            throw new GovernanceException(
                $"Workflow retrospective scores '{string.Join("', '", unknown)}', which is not a dimension. " +
                refusal);
        }
    }

    internal static string StageRefusal(TaskStage stage) =>
        $"A workflow retrospective is recorded only at stage 'Archive'; this task is at stage '{stage}'.";

    internal static string LiveWorkRefusal(WorkItemId workItemId, WorkItemStatus status) =>
        $"A workflow retrospective is recorded only when no work item is live; '{workItemId}' is still " +
        $"live in status '{status}'.";

    internal static string ActiveRunRefusal(RunId runId) =>
        $"A workflow retrospective is recorded only when no run is active; run '{runId}' is still active.";

    internal static string TableRefusal() =>
        $"A workflow retrospective must carry the '{string.Join(" | ", TableHeader)}' table " +
        $"with exactly the rows {string.Join(", ", DimensionIds)}, each once. Allowed cell " +
        $"values are score {string.Join("/", Scores.Order(StringComparer.Ordinal))}, " +
        $"confidence {string.Join("/", Confidences.Order(StringComparer.Ordinal))}, " +
        $"controllable {string.Join("/", Controllable.Order(StringComparer.Ordinal))}, and a " +
        "non-empty evidence cell. The vocabulary is checked and the score itself is not: every score " +
        "from 0 to 5 is equally acceptable here, because no rule in this kernel may read one.";
}
