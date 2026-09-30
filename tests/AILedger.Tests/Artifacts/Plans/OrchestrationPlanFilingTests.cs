using AILedger.Core.Contracts;
using AILedger.Tests.Support;

namespace AILedger.Tests.Artifacts.Plans;

[Collection(StandardInput.Collection)]
public sealed class OrchestrationPlanFilingTests
{
    private const string WrongHeaders =
        "| id | risk | mitigation | owner |\n| --- | --- | --- | --- |\n| R1 | Drift | Test | worker |\n";
    private const string Row = "| R1 | schema-drift | Wrong columns | Late refusal | test: contract | source.cs |\n";
    private static string Plan => OrchestrationPlanDocuments.AttentionTableTemplate + Row;

    [Fact]
    public async Task AuthoringShapeFilesAndVerifierConsumesItsAttentionIds()
    {
        using var f = new PlanFilingFixture();
        await f.StartAsync();
        Assert.Equal(0, await f.FileAsync(Plan));
        Assert.Equal(Plan, (await f.StateAsync()).Artifacts[new("P1")].Content);
        await f.PrepareVerifierAsync();
        Assert.NotEqual(0, await f.VerifyAsync(ArtifactCommands.VerifierBody));
        Assert.Contains("must dispose attention item 'R1'", f.Error);
        Assert.Equal(0, await f.VerifyAsync(Disposition("handled")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MalformedNewOrRevisedPlanIsRefusedBeforeAnyArtifactIsCommitted(bool revision)
    {
        using var f = new PlanFilingFixture();
        await f.StartAsync();
        if (revision) Assert.Equal(0, await f.FileAsync(Plan));
        var before = await File.ReadAllTextAsync(f.EventsPath);
        Assert.NotEqual(0, await f.FileAsync(WrongHeaders, "P-bad", revision ? "P1" : null));
        Assert.Contains("Orchestration plan 'P-bad'", f.Error);
        Assert.Contains("id | name | failure mode | causal path and impact | planned handling | source", f.Error);
        Assert.Equal(before, await File.ReadAllTextAsync(f.EventsPath));
        var state = await f.StateAsync();
        Assert.DoesNotContain(new ArtifactId("P-bad"), state.Artifacts.Keys);
        Assert.DoesNotContain(state.Runs.Values, r => r.SubjectRole == RoleKind.Verifier);
    }

    [Theory]
    [InlineData("No material attention items.")]
    [InlineData("NO MATERIAL ATTENTION ITEMS — scoped documentation only.")]
    [InlineData("table")]
    [InlineData("case-and-crlf")]
    public async Task ExistingEmptyAndFormattedPlansRemainUsable(string format)
    {
        var body = format switch
        {
            "table" => OrchestrationPlanDocuments.AttentionTableTemplate,
            "case-and-crlf" => Plan.Replace("| id | name |", "| ID | NAME |", StringComparison.Ordinal)
                .Replace("\n", "\r\n", StringComparison.Ordinal),
            _ => format
        };
        using var f = new PlanFilingFixture();
        await f.StartAsync();
        Assert.Equal(0, await f.FileAsync(body));
        await f.PrepareVerifierAsync();
        Assert.Equal(0, await f.VerifyAsync(format == "case-and-crlf" ? Disposition("handled") : ArtifactCommands.VerifierBody));
    }

    [Fact]
    public async Task DuplicateRecognizedIdsAreRefusedButExistingIdentifierSelectionIsUnchanged()
    {
        using var f = new PlanFilingFixture();
        await f.StartAsync();
        Assert.NotEqual(0, await f.FileAsync(Plan + Row));
        Assert.Contains("Orchestration plan 'P1'", f.Error);
        Assert.Contains("cannot contain duplicates", f.Error);
        var body = Plan + Row.Replace("R1", "R2a", StringComparison.Ordinal) +
            Row.Replace("R1", "R3 (descriptive-name)", StringComparison.Ordinal) +
            Row.Replace("R1", "r4", StringComparison.Ordinal);
        Assert.Equal(0, await f.FileAsync(body));
        await f.PrepareVerifierAsync();
        Assert.NotEqual(0, await f.VerifyAsync(Disposition("handled")));
        Assert.Contains("must dispose attention item 'R2a'", f.Error);
        Assert.Equal(0, await f.VerifyAsync(Disposition("handled") + "| R2a | handled | suffix | test: passed |\n"));
    }

    [Fact]
    public async Task ExtractionDoesNotAddCellCapOrRowParsingRequirements()
    {
        using var f = new PlanFilingFixture();
        await f.StartAsync();
        // The old reader skips the line after the header and stops at the first wrong-width row.
        // Preserve that behavior, blank non-ID cells and >5 items; these are separate policy gaps.
        var body = OrchestrationPlanDocuments.AttentionTableTemplate.Replace(
            "| --- | --- | --- | --- | --- | --- |", "legacy separator", StringComparison.Ordinal) +
            string.Concat(Enumerable.Range(1, 6).Select(i => $"| R{i} | | | | | |\n")) +
            "| wrong width |\n" + Row.Replace("R1", "R7", StringComparison.Ordinal);
        Assert.Equal(0, await f.FileAsync(body));
        await f.PrepareVerifierAsync();
        var output = ArtifactCommands.VerifierBody +
            string.Concat(Enumerable.Range(1, 6).Select(i => $"| R{i} | handled | item | test: passed |\n"));
        Assert.Equal(0, await f.VerifyAsync(output));
    }

    [Fact]
    public async Task RevisionAndActiveProducerOwnershipStillApply()
    {
        using var f = new PlanFilingFixture();
        await f.StartAsync();
        Assert.NotEqual(0, await f.FileAsync(Plan, run: "unknown"));
        Assert.Contains("producer run", f.Error);
        Assert.Equal(0, await f.FileAsync(Plan));
        Assert.NotEqual(0, await f.FileAsync(Plan, "P2"));
        Assert.Contains("--supersedes P1", f.Error);
        Assert.Equal(0, await f.FileAsync(ArtifactCommands.PlanBody, "P2", "P1"));
        Assert.NotEqual(0, await f.FileAsync(Plan, "P3", "P1"));
        Assert.Contains("not current", f.Error);
        await f.PrepareVerifierAsync();
        Assert.Equal(0, await f.VerifyAsync(ArtifactCommands.VerifierBody));
        Assert.Equal(Plan, (await f.StateAsync()).Artifacts[new("P1")].Content);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoricalMalformedPlanReplaysAndSelectionDiagnosesSupportedRepair(bool duplicate)
    {
        using var f = new PlanFilingFixture();
        await f.StartAsync();
        Assert.Equal(0, await f.FileAsync(Plan));
        var oldBody = duplicate ? Plan + Row : WrongHeaders;
        await f.ReplaceWithHistoricalPlanAsync(oldBody);
        await f.PrepareVerifierAsync();
        var before = await File.ReadAllTextAsync(f.EventsPath);
        Assert.NotEqual(0, await f.VerifyAsync(Disposition("handled")));
        Assert.Contains("Orchestration plan 'P1'", f.Error);
        Assert.Contains("Repair the upstream plan", f.Error);
        Assert.Contains("Design or Scope", f.Error);
        Assert.Contains("--supersedes P1", f.Error);
        Assert.Equal(before, await File.ReadAllTextAsync(f.EventsPath));
        Assert.DoesNotContain(new ArtifactId("V1"), (await f.StateAsync()).Artifacts.Keys);
        await f.ReplanAsync();
        Assert.NotEqual(0, await f.FileAsync(Plan, "P2", "P1"));
        Assert.Contains("must be active", f.Error);
        Assert.NotEqual(0, await f.FileAsync(oldBody, "P2", "P1", run: "RP2"));
        Assert.Equal(0, await f.FileAsync(Plan, "P2", "P1", run: "RP2"));
        Assert.Equal(oldBody, (await f.StateAsync()).Artifacts[new("P1")].Content);
    }

    [Theory]
    [InlineData("handled", true)]
    [InlineData("accepted-risk", true)]
    [InlineData("not-applicable", true)]
    [InlineData("unresolved", true)]
    [InlineData("passed", false)]
    public async Task VerifierDispositionVocabularyRemainsEnforced(string disposition, bool accepted)
    {
        using var f = new PlanFilingFixture();
        await f.StartAsync();
        Assert.Equal(0, await f.FileAsync(Plan));
        await f.PrepareVerifierAsync();
        Assert.Equal(accepted, await f.VerifyAsync(Disposition(disposition)) == 0);
        if (!accepted) Assert.Contains("must use an allowed value", f.Error);
    }

    [Theory]
    [InlineData("| R1 | handled | | test |", "name and evidence")]
    [InlineData("| R1 | handled | name | |", "name and evidence")]
    [InlineData("| R1 | handled | name | test |\n| R1 | handled | name | test |", "cannot contain duplicates")]
    public async Task VerifierAttentionRowsStillRequireNamesEvidenceAndUniqueIds(string rows, string diagnostic)
    {
        using var f = new PlanFilingFixture();
        await f.StartAsync();
        Assert.Equal(0, await f.FileAsync(Plan));
        await f.PrepareVerifierAsync();
        Assert.NotEqual(0, await f.VerifyAsync(ArtifactCommands.VerifierBody + rows + "\n"));
        Assert.Contains(diagnostic, f.Error);
    }

    [Fact]
    public async Task DependentClaimDispositionsRemainRequiredAndTerminal()
    {
        using var f = new PlanFilingFixture();
        await f.StartAsync();
        Assert.Equal(0, await f.FileAsync(Plan));
        await f.PrepareVerifierAsync(dependentClaim: true);
        Assert.NotEqual(0, await f.VerifyAsync(Disposition("handled")));
        Assert.Contains("must dispose dependent claim 'C1'", f.Error);
        var output = Disposition("handled").Replace("| --- | --- | --- | --- | --- |\n",
            "| --- | --- | --- | --- | --- |\n| C1 | OPEN | assumption | source | verifier |\n", StringComparison.Ordinal);
        Assert.NotEqual(0, await f.VerifyAsync(output));
        Assert.Contains("must be terminal", f.Error);
        Assert.Equal(0, await f.VerifyAsync(output.Replace("OPEN", "NEVER-TESTED", StringComparison.Ordinal)));
    }

    private static string Disposition(string value) =>
        ArtifactCommands.VerifierBody + $"| R1 | {value} | schema-drift | test: passed |\n";
}
