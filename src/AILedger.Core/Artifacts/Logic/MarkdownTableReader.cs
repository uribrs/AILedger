using AILedger.Core.Domain;

namespace AILedger.Core.Artifacts;

internal static class MarkdownTableReader
{
    internal static IReadOnlyList<string[]> Read(
        string content,
        IReadOnlyList<string> header,
        string? refusal = null)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (!SplitRow(lines[index]).SequenceEqual(header, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var rows = new List<string[]>();
            for (var rowIndex = index + 2; rowIndex < lines.Length; rowIndex++)
            {
                var row = SplitRow(lines[rowIndex]);
                if (row.Length != header.Count)
                {
                    break;
                }

                rows.Add(row);
            }

            return rows;
        }

        throw new GovernanceException(refusal ?? TableRefusal(header));
    }

    internal static void EnsureUnique<T>(
        IReadOnlyList<T> values,
        string label,
        IEqualityComparer<T>? comparer = null)
    {
        if (values.Count != values.Distinct(comparer).Count())
        {
            throw new GovernanceException($"{label} cannot contain duplicates.");
        }
    }

    internal static string TableRefusal(IReadOnlyList<string> header) =>
        $"Artifact body is missing the required '{string.Join(" | ", header)}' table. " +
        "This format is specified by the 'task-orchestrator' skill, and the kernel checks only " +
        "part of it — the columns and the R-prefixed ids. The skill also caps attention items at " +
        "five, fixes the naming convention as 'R1 (descriptive-name)', and makes an item " +
        "mandatory when an artifact trace finds a design-invalidating interaction. Passing this " +
        "check is not the same as meeting the specification; read the skill.";

    private static string[] SplitRow(string line)
    {
        var trimmed = line.Trim();
        return trimmed.StartsWith('|') && trimmed.EndsWith('|')
            ? trimmed[1..^1].Split('|').Select(cell => cell.Trim()).ToArray()
            : [];
    }
}
