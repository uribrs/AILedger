using AILedger.Core.Contracts;

namespace AILedger.Tests.Support;

// Every test that records a governed artifact builds its command here. RecordArtifactCommand takes
// ten positional arguments, three of them optional identifiers that are almost always null, so a
// signature change would otherwise land in every call site in the suite instead of in one place.
internal static class ArtifactCommands
{
    // What a real workflow document looks like on the way in: YAML front matter, a horizontal rule,
    // indentation that carries meaning. This is the shape validated claim C10 is about — none of it
    // survives being passed as a command-line option value.
    public const string Body = "---\ntitle: Governed document\n---\n\n# Goal\n\n  An indented line\n";

    public const string PlanBody = "# Attention items\n\nNo material attention items.\n";

    public const string VerifierBody =
        "# Assumption Disposition\n\n" +
        "| id | status | name | citation | actor |\n" +
        "| --- | --- | --- | --- | --- |\n\n" +
        "# Attention Item Disposition\n\n" +
        "| id | final disposition | name | evidence |\n" +
        "| --- | --- | --- | --- |\n";

    public static RecordArtifactCommand Record(
        TestTask task,
        ActorId actor,
        string artifactId,
        GovernedArtifactKind kind,
        string content = Body,
        string title = "Governed document",
        WorkItemId? workItem = null,
        RunId? producerRun = null,
        string? supersedes = null) =>
        new(
            actor,
            null,
            task.NextCorrelation(),
            new ArtifactId(artifactId),
            kind,
            title,
            content,
            workItem,
            producerRun,
            supersedes is null ? null : new ArtifactId(supersedes));
}
