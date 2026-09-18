using AILedger.Core.Application;
using AILedger.Core.Artifacts;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Storage;

public static class TaskRetentionPlanner
{
    public static TaskRetentionPlan Plan(
        string taskDirectory,
        GovernedTaskState state,
        TaskCloseoutEligibilityReport eligibility,
        DateTimeOffset now,
        TaskWorkspaceLayout? layout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskDirectory);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(eligibility);
        layout ??= new TaskWorkspaceLayout();

        if (eligibility.Task != state.TaskId || eligibility.Version != state.Version)
        {
            throw new GovernanceException(
                "The closeout eligibility report is for a different task snapshot. Recompute it.");
        }

        var currentEligibility = TaskCloseoutEligibility.Evaluate(state);
        if (!currentEligibility.Eligible)
        {
            throw new GovernanceException(IneligibleRefusal(currentEligibility));
        }

        if (!eligibility.Eligible)
        {
            throw new GovernanceException(IneligibleRefusal(eligibility));
        }

        var synthesis = Current(state, GovernedArtifactKind.CloseoutSynthesis)
            ?? throw new GovernanceException(
                "A retention plan requires a current closeout synthesis; its retention table is " +
                "the only source of deletion authority.");
        var retrospective = Current(state, GovernedArtifactKind.WorkflowRetrospective)
            ?? throw new GovernanceException("A retention plan requires a current workflow retrospective.");

        var files = TaskRetentionScan.Scan(taskDirectory, layout);
        var authorisation = ReadAuthorisation(synthesis.Content, files, layout);
        var artifactBodies = ArtifactBodyHashes(state);
        var entries = files.Select(file => Entry(file, authorisation, artifactBodies)).ToArray();

        return new TaskRetentionPlan(
            TaskRetentionPlan.CurrentSchemaVersion,
            PlanId(state, now),
            state.TaskId,
            now,
            Inputs(taskDirectory, state, synthesis, retrospective, layout),
            entries,
            Totals(entries));
    }

    private static TaskRetentionEntry Entry(
        ScannedFile file,
        IReadOnlyDictionary<string, CloseoutRetentionRow> authorisation,
        IReadOnlyDictionary<string, string> artifactBodies)
    {
        var replacement = artifactBodies.TryGetValue(file.Sha256, out var artifactId)
            ? artifactId
            : null;
        if (!authorisation.TryGetValue(file.RelativePath, out var row) ||
            !string.Equals(row.Decision, "delete", StringComparison.Ordinal))
        {
            return new TaskRetentionEntry(
                file.RelativePath,
                file.Sha256,
                file.Length,
                TaskRetentionDecision.Retain,
                row is null
                    ? "No retention row names this path, so it is retained."
                    : $"The closeout synthesis retains it: {row.Reason}",
                replacement,
                Evidence: row?.Evidence);
        }

        return new TaskRetentionEntry(
            file.RelativePath,
            file.Sha256,
            file.Length,
            TaskRetentionDecision.Delete,
            row.Reason,
            replacement,
            DiagnosticsLost(file, replacement),
            row.Evidence);
    }

    private static string DiagnosticsLost(ScannedFile file, string? replacement) =>
        replacement is null
            ? $"The whole of '{file.RelativePath}' ({file.Length} bytes) is lost; no filed artifact " +
              "body byte-matches it."
            : $"The file and its path metadata are lost; its exact content remains in filed artifact " +
              $"'{replacement}'.";

    private static IReadOnlyDictionary<string, CloseoutRetentionRow> ReadAuthorisation(
        string synthesisContent,
        IReadOnlyList<ScannedFile> files,
        TaskWorkspaceLayout layout)
    {
        var present = files.Select(file => file.RelativePath).ToHashSet(StringComparer.Ordinal);
        var authorisation = new Dictionary<string, CloseoutRetentionRow>(StringComparer.Ordinal);
        foreach (var row in ReadRetention(synthesisContent))
        {
            var path = row.Path.Replace('\\', '/');
            if (!authorisation.TryAdd(path, row))
            {
                throw new GovernanceException(
                    $"The closeout synthesis names '{path}' more than once after path normalisation.");
            }

            if (!string.Equals(row.Decision, "delete", StringComparison.Ordinal))
            {
                continue;
            }

            if (TaskRetentionScan.NeverDeletable(layout).Contains(path))
            {
                throw new GovernanceException(
                    $"The closeout synthesis marks '{path}' for deletion, but canonical logs and " +
                    "the task mutation lock may never be deleted. Correct the synthesis.");
            }

            if (path.Equals(TaskRetentionScan.CleanupDirectoryName, StringComparison.Ordinal) ||
                path.StartsWith(TaskRetentionScan.CleanupDirectoryName + "/", StringComparison.Ordinal))
            {
                throw new GovernanceException(
                    $"The closeout synthesis marks '{path}' for deletion, but cleanup records may " +
                    "not be deleted by the cleanup that writes them.");
            }

            if (!present.Contains(path))
            {
                throw new GovernanceException(
                    $"The closeout synthesis marks '{path}' for deletion and no such file is in the " +
                    "task directory. A plan cannot authorise an unseen removal.");
            }
        }

        return authorisation;
    }

    internal static IReadOnlyList<CloseoutRetentionRow> ReadRetention(string synthesisContent)
    {
        var lines = synthesisContent.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        const string header = "path | decision | reason | evidence";
        var headerIndex = Array.FindIndex(
            lines,
            line => string.Equals(NormaliseRow(line), header, StringComparison.OrdinalIgnoreCase));
        if (headerIndex >= 0)
        {
            for (var lineIndex = headerIndex + 2; lineIndex < lines.Length; lineIndex++)
            {
                var trimmed = lines[lineIndex].Trim();
                if (trimmed.Length == 0 || !trimmed.StartsWith('|') || !trimmed.EndsWith('|'))
                {
                    break;
                }

                var cellCount = trimmed[1..^1].Split('|').Length;
                if (cellCount != 4)
                {
                    var row = lineIndex - headerIndex - 1;
                    throw new GovernanceException(
                        $"Retention table row {row} has {cellCount} cells; expected 4. " +
                        "Pipe characters are not supported inside retention cells.");
                }
            }
        }

        return CloseoutSynthesisDocument.ReadRetention(synthesisContent);
    }

    private static string NormaliseRow(string line)
    {
        var trimmed = line.Trim();
        return trimmed.StartsWith('|') && trimmed.EndsWith('|')
            ? string.Join(" | ", trimmed[1..^1].Split('|').Select(cell => cell.Trim()))
            : string.Empty;
    }

    private static IReadOnlyDictionary<string, string> ArtifactBodyHashes(GovernedTaskState state)
    {
        var bodies = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var artifact in state.Artifacts.Values
                     .OrderBy(item => item.ArtifactId.Value, StringComparer.Ordinal))
        {
            bodies.TryAdd(TaskRetentionScan.Sha256OfText(artifact.Content), artifact.ArtifactId.Value);
        }

        return bodies;
    }

    private static TaskRetentionInputs Inputs(
        string taskDirectory,
        GovernedTaskState state,
        GovernedArtifact synthesis,
        GovernedArtifact retrospective,
        TaskWorkspaceLayout layout)
    {
        var eventsPath = Path.Combine(taskDirectory, layout.EventsFileName);
        var refusalsPath = RefusalJournal.ResolvePath(taskDirectory);
        return new TaskRetentionInputs(
            state.Version,
            TaskRetentionScan.Sha256(eventsPath),
            new FileInfo(eventsPath).Length,
            File.Exists(refusalsPath) ? TaskRetentionScan.Sha256(refusalsPath) : null,
            File.Exists(refusalsPath) ? new FileInfo(refusalsPath).Length : null,
            synthesis.ArtifactId.Value,
            TaskRetentionScan.Sha256OfText(synthesis.Content),
            retrospective.ArtifactId.Value);
    }

    private static TaskRetentionTotals Totals(IReadOnlyList<TaskRetentionEntry> entries) =>
        new(
            entries.Count(entry => entry.Decision == TaskRetentionDecision.Retain),
            entries.Count(entry => entry.Decision == TaskRetentionDecision.Delete),
            entries.Where(entry => entry.Decision == TaskRetentionDecision.Retain).Sum(entry => entry.Length),
            entries.Where(entry => entry.Decision == TaskRetentionDecision.Delete).Sum(entry => entry.Length));

    private static string PlanId(GovernedTaskState state, DateTimeOffset now) =>
        $"{state.TaskId.Value}-v{state.Version}-{now:yyyyMMddTHHmmssfffffffZ}";

    private static GovernedArtifact? Current(GovernedTaskState state, GovernedArtifactKind kind) =>
        ArtifactApplicability.Current(state)
            .Where(artifact => artifact.Kind == kind)
            .OrderBy(artifact => artifact.ArtifactId.Value, StringComparer.Ordinal)
            .FirstOrDefault();

    internal static string IneligibleRefusal(TaskCloseoutEligibilityReport eligibility) =>
        "This task has not finished closing out, so nothing in it may be removed. Unsatisfied: " +
        string.Join("; ", eligibility.Unsatisfied.Select(check => $"{check.Name} — {check.Detail}"));
}
