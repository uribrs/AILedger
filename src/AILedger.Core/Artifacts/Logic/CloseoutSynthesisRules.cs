using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Core.Artifacts;

internal static class CloseoutSynthesisRules
{
    internal static readonly IReadOnlyList<string> FindingsHeader =
    [
        "finding", "kind", "severity", "detection", "occurrences", "opportunity", "repair",
        "disposition", "lesson"
    ];

    internal static readonly IReadOnlyList<string> RetentionHeader =
        ["path", "decision", "reason", "evidence"];

    private static readonly IReadOnlySet<TaskStage> AdmittingStages =
        new HashSet<TaskStage> { TaskStage.Review, TaskStage.Learn, TaskStage.Archive };

    private static readonly IReadOnlySet<string> Kinds = Vocabulary(
        "product-defect", "test-defect", "process-defect", "evidence-gap", "observation");

    private static readonly IReadOnlySet<string> Severities = Vocabulary(
        "high", "medium", "low", "unmeasured");

    private static readonly IReadOnlySet<string> Opportunities = Vocabulary(
        "missed", "detected-in-scope", "outside-scope", "introduced-later", "insufficient-evidence");

    private static readonly IReadOnlySet<string> Repairs = Vocabulary(
        "demonstrated", "unverified", "incomplete", "reintroduced", "repair-regression",
        "accepted-risk", "not-attempted", "not-rechecked");

    private static readonly IReadOnlySet<string> Dispositions = Vocabulary(
        "verified-fixed", "still-present", "accepted-risk", "disputed", "deferred", "not-rechecked");

    private static readonly IReadOnlySet<string> RetentionDecisions = Vocabulary("delete", "retain");

    // Command-time only. Aggregate stage gates must not retroactively invalidate old histories.
    internal static void EnsureEntryCondition(GovernedTaskState state)
    {
        if (!AdmittingStages.Contains(state.Stage))
        {
            throw new GovernanceException(StageRefusal(state.Stage));
        }
    }

    internal static void Validate(string content)
    {
        ValidateFindings(content);
        ValidateRetention(content);
    }

    private static void ValidateFindings(string content)
    {
        var refusal = FindingsRefusal();
        EnsureConsistentRowWidths(
            content,
            FindingsHeader,
            "findings",
            refusal);
        var rows = MarkdownTableReader.Read(content, FindingsHeader, refusal);
        MarkdownTableReader.EnsureUnique(
            rows.Select(row => row[0]).ToArray(),
            "Closeout synthesis finding IDs",
            StringComparer.Ordinal);

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row[0]))
            {
                throw new GovernanceException(
                    "A closeout synthesis finding must carry a stable identifier. " + refusal);
            }

            if (!Kinds.Contains(row[1]) ||
                !Severities.Contains(row[2]) ||
                !Opportunities.Contains(row[5]) ||
                !Repairs.Contains(row[6]) ||
                !Dispositions.Contains(row[7]))
            {
                throw new GovernanceException(
                    $"Closeout synthesis finding '{row[0]}' uses a value outside the frozen vocabulary. " +
                    refusal);
            }

            if (string.IsNullOrWhiteSpace(row[3]) ||
                string.IsNullOrWhiteSpace(row[4]) ||
                string.IsNullOrWhiteSpace(row[8]))
            {
                throw new GovernanceException(
                    $"Closeout synthesis finding '{row[0]}' must carry detection, occurrences and " +
                    "lesson answers; write 'none' rather than leaving a cell empty. " + refusal);
            }
        }
    }

    private static void ValidateRetention(string content)
    {
        var refusal = RetentionRefusal();
        EnsureConsistentRowWidths(
            content,
            RetentionHeader,
            "retention",
            refusal);
        var rows = MarkdownTableReader.Read(content, RetentionHeader, refusal);
        MarkdownTableReader.EnsureUnique(
            rows.Select(row => row[0]).ToArray(),
            "Closeout synthesis retention paths",
            StringComparer.Ordinal);

        foreach (var row in rows)
        {
            EnsureTaskRelativePath(row[0], refusal);
            if (!RetentionDecisions.Contains(row[1]))
            {
                throw new GovernanceException(
                    $"Closeout synthesis retention decision for '{row[0]}' must be " +
                    $"'{string.Join("' or '", RetentionDecisions.Order(StringComparer.Ordinal))}'. " +
                    refusal);
            }

            if (string.IsNullOrWhiteSpace(row[2]) || string.IsNullOrWhiteSpace(row[3]))
            {
                throw new GovernanceException(
                    $"Closeout synthesis retention row '{row[0]}' must carry a reason and evidence. " +
                    refusal);
            }
        }
    }

    private static void EnsureTaskRelativePath(string path, string refusal)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new GovernanceException("A closeout synthesis retention row must name a path. " + refusal);
        }

        var segments = path.Split('/', '\\');
        var rootedOnEitherPlatform = Path.IsPathRooted(path) || path[0] is '/' or '\\' ||
            (segments[0].Length == 2 && segments[0][1] == ':');
        if (rootedOnEitherPlatform || segments.Any(segment => segment is "" or "." or ".."))
        {
            throw new GovernanceException(
                $"Closeout synthesis retention path '{path}' must be relative to the task directory " +
                "and must contain no '.' or '..' segment. " + refusal);
        }
    }

    private static void EnsureConsistentRowWidths(
        string content,
        IReadOnlyList<string> header,
        string tableName,
        string refusal)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var headerIndexes = lines
            .Select((line, index) => new { Cells = SplitDelimitedRow(line), Index = index })
            .Where(candidate => candidate.Cells.SequenceEqual(header, StringComparer.OrdinalIgnoreCase))
            .Select(candidate => candidate.Index)
            .ToArray();

        if (headerIndexes.Length == 0)
        {
            throw new GovernanceException(refusal);
        }

        if (headerIndexes.Length > 1)
        {
            throw new GovernanceException(
                $"Closeout synthesis {tableName} table header appears more than once. {refusal}");
        }

        var index = headerIndexes[0];
        var separatorIndex = index + 1;
        if (separatorIndex >= lines.Length || !IsSeparatorRow(lines[separatorIndex], header.Count))
        {
            throw new GovernanceException(
                $"Closeout synthesis {tableName} table line {separatorIndex + 1} must be a Markdown " +
                $"separator row with exactly {header.Count} cells. {refusal}");
        }

        // A table region is the contiguous run of non-blank lines after its separator. Prose and
        // headings may follow after a blank boundary, but a later same-width pipe row is an orphan
        // table row and is refused so a blank line cannot hide rows from validation.
        var regionStart = index + 2;
        var regionEnd = regionStart;
        while (regionEnd < lines.Length && !string.IsNullOrWhiteSpace(lines[regionEnd]))
        {
            regionEnd++;
        }

        for (var rowIndex = regionStart; rowIndex < regionEnd; rowIndex++)
        {
            var row = SplitDelimitedRow(lines[rowIndex]);
            if (row.Length == 0)
            {
                throw new GovernanceException(
                    $"Closeout synthesis {tableName} table line {rowIndex + 1} is not a " +
                    $"well-formed pipe row with exactly {header.Count} cells. {refusal}");
            }

            if (row.Length != header.Count)
            {
                throw new GovernanceException(
                    $"Closeout synthesis {tableName} table row {rowIndex - index - 1} has " +
                    $"{row.Length} cells; expected {header.Count}. {refusal}");
            }
        }

        for (var lineIndex = regionEnd; lineIndex < lines.Length; lineIndex++)
        {
            if (SplitDelimitedRow(lines[lineIndex]).Length == header.Count)
            {
                throw new GovernanceException(
                    $"Closeout synthesis {tableName} table line {lineIndex + 1} is an orphan " +
                    $"pipe row with {header.Count} cells after the table region ended. {refusal}");
            }
        }
    }

    private static bool IsSeparatorRow(string line, int expectedCells)
    {
        var cells = SplitDelimitedRow(line);
        return cells.Length == expectedCells && cells.All(IsSeparatorCell);
    }

    private static bool IsSeparatorCell(string cell)
    {
        var value = cell.Trim(':');
        return value.Length >= 3 && value.All(character => character == '-');
    }

    private static string[] SplitDelimitedRow(string line)
    {
        var trimmed = line.Trim();
        return trimmed.StartsWith('|') && trimmed.EndsWith('|')
            ? trimmed[1..^1].Split('|').Select(cell => cell.Trim()).ToArray()
            : [];
    }

    private static IReadOnlySet<string> Vocabulary(params string[] values) =>
        new HashSet<string>(values, StringComparer.Ordinal);

    internal static string StageRefusal(TaskStage stage) =>
        "A closeout synthesis is recorded once the technical cycle has finished, at stage " +
        $"{string.Join(", ", AdmittingStages.Order().Select(item => $"'{item}'"))}; this task is at " +
        $"stage '{stage}'.";

    internal static string FindingsRefusal() =>
        $"A closeout synthesis must carry the '{string.Join(" | ", FindingsHeader)}' table with a " +
        $"distinct identifier per row. Allowed values are kind {Join(Kinds)}, severity " +
        $"{Join(Severities)}, opportunity {Join(Opportunities)}, repair {Join(Repairs)}, disposition " +
        $"{Join(Dispositions)}; detection, occurrences and lesson must not be empty. The table may " +
        "have no rows. The vocabulary is checked and the judgement is not.";

    internal static string RetentionRefusal() =>
        $"A closeout synthesis must carry the '{string.Join(" | ", RetentionHeader)}' table. Paths " +
        $"must be task-relative and unique, decisions must be {Join(RetentionDecisions)}, and reason " +
        "and evidence must not be empty. The table may have no rows.";

    private static string Join(IReadOnlySet<string> values) =>
        string.Join("/", values.Order(StringComparer.Ordinal));
}

public static class CloseoutSynthesisDocument
{
    public static IReadOnlyList<CloseoutRetentionRow> ReadRetention(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return MarkdownTableReader
            .Read(content, CloseoutSynthesisRules.RetentionHeader, CloseoutSynthesisRules.RetentionRefusal())
            .Select(row => new CloseoutRetentionRow(row[0], row[1], row[2], row[3]))
            .ToArray();
    }
}

public sealed record CloseoutRetentionRow(string Path, string Decision, string Reason, string Evidence);
