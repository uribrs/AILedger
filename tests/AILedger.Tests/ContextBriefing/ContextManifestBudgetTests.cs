using System.Text;
using System.Text.Json;
using AILedger.Cli;
using AILedger.Cli.ContextBriefing;
using AILedger.Core.Contracts;
using AILedger.Storage;

namespace AILedger.Tests.ContextBriefing;

public sealed class ContextManifestBudgetTests
{
    private static readonly JsonSerializerOptions Json = LedgerJson.CreateOptions(indented: true);

    [Fact]
    public void BudgetMeasuresEscapedUtf8AndReportsWholeRecordOmissions()
    {
        var required = new ContextArtifact(ContextArtifactKind.Constraint, "K1", "Keep this rule", []);
        var background = Enumerable.Range(0, 100).Select(index => new ContextArtifact(
            ContextArtifactKind.Lesson, $"L{index:D3}", string.Concat(Enumerable.Repeat("שלום😀\n", 200)), []));
        var original = Manifest([required, .. background]);

        var bounded = ContextManifestBudget.Apply(original, 20_000, Json);

        Assert.True(Bytes(original) > 20_000);
        Assert.True(Bytes(bounded) <= 20_000);
        Assert.Contains(required, bounded.Artifacts);
        Assert.InRange(bounded.Budget!.OmittedArtifacts, 1, 99);
        Assert.Equal(original.Artifacts.Count - bounded.Artifacts.Count, bounded.Budget.OmittedArtifacts);
        Assert.Contains("--max-context-bytes", bounded.Budget.Notice);
        Assert.All(bounded.Artifacts, item => Assert.Contains(item, original.Artifacts));
        Assert.Equal(Serialize(bounded), Serialize(ContextManifestBudget.Apply(original, 20_000, Json)));
        // The next whole record cannot fit either: the bounded prefix uses the available room.
        var next = original.Artifacts[bounded.Artifacts.Count];
        Assert.True(Bytes(bounded with { Artifacts = [.. bounded.Artifacts, next] }) > 20_000);
    }

    [Fact]
    public void RequiredRecordsAndReferencedLessonsCannotBeDroppedToFit()
    {
        var dependency = new ContextArtifact(ContextArtifactKind.Claim, "C1", "Required claim", ["L1"]);
        var lesson = new ContextArtifact(ContextArtifactKind.Lesson, "L1", new string('x', 4000), []);
        var original = Manifest([dependency, lesson,
            new(ContextArtifactKind.Lesson, "L2", new string('y', 4000), [])]);

        var bounded = ContextManifestBudget.Apply(original, 6000, Json);
        Assert.Equal([dependency, lesson], bounded.Artifacts);
        var failure = Assert.Throws<CliUsageException>(() => ContextManifestBudget.Apply(original, 2000, Json));
        Assert.Contains("No required records were truncated", failure.Message);
        Assert.Contains("--work", failure.Message);
        Assert.Equal(3, original.Artifacts.Count);
    }

    [Fact]
    public void ExactLimitIncludesMetadataAndNewlineAndCanOmitTheEntireTail()
    {
        var original = Manifest([new(ContextArtifactKind.Rules, "rules", "required", [])]);
        var full = ContextManifestBudget.Apply(original, 1000, Json);
        var exact = Bytes(full);
        var atLimit = ContextManifestBudget.Apply(original, exact, Json);
        exact = Bytes(atLimit);
        Assert.Equal(exact, Bytes(ContextManifestBudget.Apply(original, exact, Json)));
        Assert.Throws<CliUsageException>(() => ContextManifestBudget.Apply(original, exact - 1, Json));
        var required = Manifest([new(ContextArtifactKind.Rules, "rules", new string('x', 3000), [])]);
        Assert.Throws<CliUsageException>(() => ContextManifestBudget.Apply(required, 1000, Json));

        var onlyTail = Manifest([new(ContextArtifactKind.LessonMark, "LM1", new string('x', 4000), [])]);
        var empty = ContextManifestBudget.Apply(onlyTail, 1000, Json);
        Assert.Empty(empty.Artifacts);
        Assert.Equal(1, empty.Budget!.OmittedArtifacts);
        Assert.True(Bytes(empty) <= 1000);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("2147483648")]
    public void InvalidLimitsAreRejected(string value) =>
        Assert.Throws<CliUsageException>(() => ContextManifestBudget.ReadMaximumBytes(
            CommandLine.Parse(["context", "build", "--max-context-bytes", value])));

    [Fact]
    public void OlderManifestWithoutBudgetStillDeserializes()
    {
        var original = Manifest([]);
        Assert.DoesNotContain("budget", Serialize(original));
        Assert.Null(JsonSerializer.Deserialize<ContextManifest>(Serialize(original), Json)!.Budget);
    }

    private static ContextManifest Manifest(IReadOnlyList<ContextArtifact> artifacts) =>
        new(1, new TaskId("T1"), new ActorId("worker"), RoleKind.Worker, null, 0, [], artifacts, [], DateTimeOffset.UnixEpoch);

    private static string Serialize(ContextManifest manifest) => JsonSerializer.Serialize(manifest, Json) + Environment.NewLine;
    private static int Bytes(ContextManifest manifest) => Encoding.UTF8.GetByteCount(Serialize(manifest));
}
