using AILedger.Core.Artifacts;

namespace AILedger.Core.Contracts;

public static class VerifierOutputDocuments
{
    public static IReadOnlyList<string> AssumptionColumns { get; } = Array.AsReadOnly<string>(["id", "status", "name", "citation", "actor"]);
    public static IReadOnlyList<string> AttentionColumns { get; } = Array.AsReadOnly<string>(["id", "final disposition", "name", "evidence"]);
    internal static VerifierDispositionObservation Observe(GovernedArtifact artifact) => new(
        artifact.ArtifactId.Value, artifact.ProducerRunId!.Value.Value,
        Counts(artifact.Content, AssumptionColumns, ["VALIDATED", "REJECTED", "NEVER-TESTED"]),
        Counts(artifact.Content, AttentionColumns, ["handled", "accepted-risk", "not-applicable", "unresolved"]));

    private static IReadOnlyDictionary<string, int> Counts(string content, IReadOnlyList<string> columns, IReadOnlyList<string> allowed) =>
        MarkdownTableReader.Read(content, columns).Where(row => allowed.Contains(row[1])).GroupBy(row => row[1], StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    public static string AuthoringShape =>
        $"| {string.Join(" | ", AssumptionColumns)} |\n| --- | --- | --- | --- | --- |\n" +
        "Dispose every dependent Open/Validated claim across the member union with VALIDATED, REJECTED or NEVER-TESTED; name, citation and actor nonblank.\n" +
        $"| {string.Join(" | ", AttentionColumns)} |\n| --- | --- | --- | --- |\n" +
        "Dispose every recognized current-plan attention ID with handled, accepted-risk, not-applicable or unresolved; name and evidence nonblank. IDs unique per table. unresolved is not a clean pass.";
}

// Authored dispositions, not independently established truth or an overall verdict.
public sealed record VerifierDispositionObservation(string ArtifactId, string ProducerRunId,
    IReadOnlyDictionary<string, int> ClaimStatuses, IReadOnlyDictionary<string, int> AttentionDispositions);
