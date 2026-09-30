using AILedger.Core.Artifacts;
using AILedger.Core.Domain;

namespace AILedger.Core.Contracts;

/// <summary>The shared attention-table shape for plan authors, filing and verifier consumption.</summary>
public static class OrchestrationPlanDocuments
{
    public static IReadOnlyList<string> AttentionColumns { get; } = Array.AsReadOnly<string>(
        ["id", "name", "failure mode", "causal path and impact", "planned handling", "source"]);

    public const string NoMaterialAttentionItems = "No material attention items";

    /// <summary>Table scaffold only; task-orchestrator owns the substantive planning guidance.</summary>
    public static string AttentionTableTemplate =>
        $"| {string.Join(" | ", AttentionColumns)} |\n" +
        $"| {string.Join(" | ", AttentionColumns.Select(_ => "---"))} |\n";

    internal static IReadOnlyList<string> ReadAttentionIds(ArtifactId planId, string content)
    {
        // Preserve the consumer's existing escape, row parsing and identifier selection. This
        // extraction does not impose the skill's cap, substantive cells or reason requirement.
        if (content.Contains(NoMaterialAttentionItems, StringComparison.OrdinalIgnoreCase))
            return [];

        try
        {
            var ids = MarkdownTableReader.Read(content, AttentionColumns,
                    $"Missing attention table with columns '{string.Join(" | ", AttentionColumns)}'. " +
                    $"Alternatively, explicitly state '{NoMaterialAttentionItems}'.")
                .Select(row => row[0])
                .Where(IsAttentionId)
                .ToArray();
            MarkdownTableReader.EnsureUnique(ids, "Orchestration-plan attention item IDs", StringComparer.Ordinal);
            return ids;
        }
        catch (GovernanceException error)
        {
            throw new GovernanceException($"Orchestration plan '{planId}' has invalid attention items: {error.Message}");
        }
    }

    internal static IReadOnlyList<string> ReadAttentionIdsForVerification(GovernedArtifact plan)
    {
        try
        {
            return ReadAttentionIds(plan.ArtifactId, plan.Content);
        }
        catch (GovernanceException error)
        {
            throw new GovernanceException(error.Message +
                " Repair the upstream plan before submitting verifier output: use supported replanning " +
                "transitions to Design or Scope, then have an authorized active planning producer file " +
                $"a corrected OrchestrationPlan with --supersedes {plan.ArtifactId}. " +
                "The historical artifact remains unchanged; verifier dispositions are still required.");
        }
    }

    private static bool IsAttentionId(string value)
    {
        if (value.Length < 2 || value[0] != 'R')
            return false;

        var digitCount = value.Skip(1).TakeWhile(char.IsDigit).Count();
        return digitCount > 0 && (digitCount == value.Length - 1 ||
            digitCount == value.Length - 2 && char.IsLetter(value[^1]));
    }
}
