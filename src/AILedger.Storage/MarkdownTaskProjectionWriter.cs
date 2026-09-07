using System.Text;
using AILedger.Core.Contracts;

namespace AILedger.Storage;

public sealed class MarkdownTaskProjectionWriter : ITaskProjectionWriter
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private readonly TaskWorkspaceLayout _layout;

    public MarkdownTaskProjectionWriter(TaskWorkspaceLayout? layout = null)
    {
        _layout = layout ?? new TaskWorkspaceLayout();
    }

    public async Task WriteAsync(
        string taskDirectory,
        GovernedTaskState state,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskDirectory);
        ArgumentNullException.ThrowIfNull(state);

        await Task.WhenAll(
            WriteAtomicAsync(
                Path.Combine(taskDirectory, _layout.TaskProjectionFileName),
                RenderTask(state),
                cancellationToken),
            WriteAtomicAsync(
                Path.Combine(taskDirectory, _layout.AssumptionsProjectionFileName),
                RenderAssumptions(state),
                cancellationToken),
            WriteAtomicAsync(
                Path.Combine(taskDirectory, _layout.DecisionsProjectionFileName),
                RenderDecisions(state),
                cancellationToken)).ConfigureAwait(false);
    }

    internal static async Task WriteAtomicAsync(
        string destinationPath,
        string content,
        CancellationToken cancellationToken)
    {
        var temporaryPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, Utf8WithoutBom, cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string RenderTask(GovernedTaskState state)
    {
        var builder = new StringBuilder()
            .AppendLine($"# {Clean(state.Title)}")
            .AppendLine()
            .AppendLine($"- Task: `{Clean(state.TaskId.Value)}`")
            .AppendLine($"- Stage: `{state.Stage}`")
            .AppendLine($"- Version: `{state.Version}`")
            .AppendLine()
            .AppendLine("## Goal")
            .AppendLine()
            .AppendLine(Clean(state.Goal))
            .AppendLine()
            .AppendLine("## Roles")
            .AppendLine();

        AppendItems(builder, state.Roles.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal), pair =>
            $"- `{Clean(pair.Key.Value)}` — {pair.Value.Role}; capabilities: {Join(pair.Value.Capabilities.Select(value => value.ToString()))}");

        builder.AppendLine().AppendLine("## Work Items").AppendLine();
        AppendItems(builder, state.WorkItems.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal), pair =>
            $"- `{Clean(pair.Key.Value)}` — **{pair.Value.Status}** — {Clean(pair.Value.Title)}" +
            (pair.Value.Owner is null ? string.Empty : $" (owner: `{Clean(pair.Value.Owner.Value.Value)}`)"));

        builder.AppendLine().AppendLine("## Runs").AppendLine();
        AppendItems(builder, state.Runs.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal), pair =>
        {
            var run = pair.Value;
            var role = state.Roles.TryGetValue(run.ActorId, out var assignment)
                ? assignment.Role.ToString()
                : "unassigned";
            var cognition = Clean(run.Provider) +
                            (run.Model is null ? string.Empty : $" {Clean(run.Model)}") +
                            (run.ProviderVersion is null ? string.Empty : $" ({Clean(run.ProviderVersion)})");
            var work = run.WorkItemId is { } workItemId ? $" on `{Clean(workItemId.Value)}`" : string.Empty;
            var live = run.Status == AgentRunStatus.Active ? " ← live" : string.Empty;
            return $"- `{Clean(pair.Key.Value)}` — **{run.Status}** — {role} `{Clean(run.ActorId.Value)}`" +
                   $" via {cognition}{work}{live}";
        });

        builder.AppendLine().AppendLine("## Artifacts").AppendLine();
        // Current context is the set of artifacts nothing supersedes. A list of every revision that
        // did not say which one is live would read as five contracts rather than one contract and
        // four repairs, so each row carries either its successor or the mark that it is the current
        // one. Replay refuses to supersede an artifact that is not current, so a predecessor has at
        // most one successor and this index cannot collide.
        var successors = state.Artifacts.Values
            .Where(artifact => artifact.SupersedesArtifactId is not null)
            .ToDictionary(artifact => artifact.SupersedesArtifactId!.Value, artifact => artifact.ArtifactId);
        AppendItems(builder, state.Artifacts.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal), pair =>
        {
            var artifact = pair.Value;
            var scope = artifact.WorkItemId is { } workItemId
                ? $" on `{Clean(workItemId.Value)}`"
                : " task-wide";
            var producer = artifact.ProducerRunId is { } runId ? $" from `{Clean(runId.Value)}`" : string.Empty;
            var standing = successors.TryGetValue(artifact.ArtifactId, out var successor)
                ? $" — superseded by `{Clean(successor.Value)}`"
                : " — current";
            // The body is in the event log. What a reader of a projection needs is the handle to
            // read it with and the size it will be.
            return $"- `{Clean(pair.Key.Value)}` — **{artifact.Kind}** — {Clean(artifact.Title)}" +
                   $"{scope}{producer}{standing} ({Utf8WithoutBom.GetByteCount(artifact.Content)} bytes in the log)";
        });

        builder.AppendLine().AppendLine("## Active Constraints").AppendLine();
        AppendItems(builder,
            state.Constraints.Where(pair => pair.Value.Status == ConstraintStatus.Active)
                .OrderBy(pair => pair.Key.Value, StringComparer.Ordinal),
            pair => $"- `{Clean(pair.Key.Value)}` — {Clean(pair.Value.Statement)} (source: {Clean(pair.Value.Source)})");

        builder.AppendLine().AppendLine("## Open Escalations").AppendLine();
        AppendItems(builder,
            state.Escalations.Where(pair => pair.Value.Status == EscalationStatus.Open)
                .OrderBy(pair => pair.Key.Value, StringComparer.Ordinal),
            pair => $"- `{Clean(pair.Key.Value)}` — **{pair.Value.Kind}** — {Clean(pair.Value.Question)}" +
                    (pair.Value.Recommendation is null
                        ? string.Empty
                        : $" Recommended: {Clean(pair.Value.Recommendation)}"));

        builder.AppendLine().AppendLine("## Open Challenges").AppendLine();
        AppendItems(builder,
            state.Challenges.Where(pair => pair.Value.Status == ChallengeStatus.Open)
                .OrderBy(pair => pair.Key.Value, StringComparer.Ordinal),
            pair => $"- `{Clean(pair.Key.Value)}` — {Clean(pair.Value.TargetType)} `{Clean(pair.Value.TargetId)}`: {Clean(pair.Value.Reason)}");

        builder.AppendLine().AppendLine("## Lessons").AppendLine();
        AppendItems(builder, state.Lessons.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal), pair =>
            // A lesson minted here was earned by this task. A lesson recalled from elsewhere was
            // earned in a world that may since have changed, and reads as settled fact unless the
            // projection says otherwise, so it is labelled the same way the manifest labels it.
            (pair.Value.SourceTaskId == state.TaskId
                ? $"- `{Clean(pair.Key.Value)}` — {pair.Value.SourceKind} from "
                : $"- `{Clean(pair.Key.Value)}` — **Unverified prior evidence** — {pair.Value.SourceKind} from ") +
            $"`{Clean(pair.Value.SourceTaskId.Value)}`/" +
            $"`{Clean(pair.Value.SourceRecordId)}`" +
            (pair.Value.Repo is null ? string.Empty : $" in `{Clean(pair.Value.Repo)}`") +
            (pair.Value.Class is null ? string.Empty : $" [{pair.Value.Class}]") +
            $": {Clean(pair.Value.Statement)} Outcome: {Clean(pair.Value.Outcome)}" +
            (pair.Value.Tags is null || pair.Value.Tags.Count == 0
                ? string.Empty
                : $" Tags: {Join(pair.Value.Tags.Select(tag => $"`{Clean(tag)}`"))}") +
            (pair.Value.SupersedesLessonId is null
                ? string.Empty
                : $" Supersedes: `{Clean(pair.Value.SupersedesLessonId.Value.Value)}`") +
            (pair.Value.SourceTaskId == state.TaskId
                ? string.Empty
                : " Re-establish it before relying on it."));

        builder.AppendLine().AppendLine("## Lesson Marks").AppendLine();
        AppendItems(builder, state.LessonMarks.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal), pair =>
            $"- `{Clean(pair.Key.Value)}` — {pair.Value.SourceKind} `{Clean(pair.Value.SourceRecordId)}`" +
            (pair.Value.SupersedesLessonId is null
                ? string.Empty
                : $" supersedes `{Clean(pair.Value.SupersedesLessonId.Value.Value)}`"));

        return NormalizeEnding(builder);
    }

    private static string RenderAssumptions(GovernedTaskState state)
    {
        var builder = new StringBuilder().AppendLine("# Assumptions and Claims").AppendLine();
        AppendItems(builder, state.Claims.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal), pair =>
        {
            var evidence = Join(pair.Value.EvidenceIds.Select(id => $"`{Clean(id.Value)}`"));
            var consequence = string.IsNullOrWhiteSpace(pair.Value.ConsequenceIfWrong)
                ? string.Empty
                : $" Consequence if wrong: {Clean(pair.Value.ConsequenceIfWrong)}";
            var replacement = pair.Value.SupersededByClaimId is { } replacementId
                ? $" Superseded by `{Clean(replacementId.Value)}`."
                : string.Empty;
            return $"- `{Clean(pair.Key.Value)}` — **{pair.Value.Status}** — {Clean(pair.Value.Statement)} Evidence: {evidence}.{consequence}{replacement}";
        });

        return NormalizeEnding(builder);
    }

    private static string RenderDecisions(GovernedTaskState state)
    {
        var builder = new StringBuilder().AppendLine("# Decisions").AppendLine();
        AppendItems(builder, state.Decisions.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal), pair =>
        {
            var dependencies = Join(pair.Value.DependsOnClaims.Select(id => $"`{Clean(id.Value)}`"));
            return $"- `{Clean(pair.Key.Value)}` — **{pair.Value.Status}** — {Clean(pair.Value.Statement)} " +
                   $"Rationale: {Clean(pair.Value.Rationale)} Depends on: {dependencies}.";
        });

        builder.AppendLine().AppendLine("## Rejected Alternatives").AppendLine();
        AppendItems(builder, state.Alternatives.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal), pair =>
        {
            var replacement = pair.Value.ReplacedByDecisionId is { } decisionId
                ? $" Replaced by `{Clean(decisionId.Value)}`."
                : string.Empty;
            return $"- `{Clean(pair.Key.Value)}` — {Clean(pair.Value.Statement)} " +
                   $"Rejected because: {Clean(pair.Value.RejectionRationale)}.{replacement}";
        });

        return NormalizeEnding(builder);
    }

    private static void AppendItems<T>(StringBuilder builder, IEnumerable<T> items, Func<T, string> render)
    {
        var hasItems = false;
        foreach (var item in items)
        {
            builder.AppendLine(render(item));
            hasItems = true;
        }

        if (!hasItems)
        {
            builder.AppendLine("_None._");
        }
    }

    private static string Join(IEnumerable<string> values)
    {
        var materialized = values.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        return materialized.Length == 0 ? "none" : string.Join(", ", materialized);
    }

    private static string Clean(string value) =>
        value.Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

    private static string NormalizeEnding(StringBuilder builder) => builder.ToString().TrimEnd() + "\n";
}
