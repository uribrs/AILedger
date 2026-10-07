using AILedger.Cli.Retrospectives;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Tests.Findings;
using AILedger.Storage;
using System.Text.Json;

namespace AILedger.Tests.Orchestration;

public sealed class AdoptionMeasurementTests
{
    [Fact]
    public async Task MalformedRetainedEventsAreUnavailableInsteadOfMeasuredUsage()
    {
        using var ledger = new FindingsFixture(); await ledger.OpenAsync();
        var folder = Path.Combine(ledger.Directory, "runs");
        Directory.CreateDirectory(folder);
        const string json = """{"runId":"R-cost","provider":"claude","events":null}""";
        var path = Path.Combine(folder, "R-cost.json");
        await File.WriteAllTextAsync(path, json);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ledger.Service().InspectProviderResultAsync(ledger.TaskId, new RunId("R-cost"), default));
        Assert.Equal(json, await File.ReadAllTextAsync(path));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":1,\"intents\":null}")]
    [InlineData("{\"schemaVersion\":1,\"intents\":[null]}")]
    [InlineData("{\"schemaVersion\":1,\"intents\":[{}]}")]
    public async Task InvalidObservationSchemaIsUnavailableWithoutChangingTheJournal(string payload)
    {
        using var ledger = new FindingsFixture(); await ledger.OpenAsync();
        var snapshot = new CoordinationSnapshot(1, 1, 1, "prior-owner", DateTimeOffset.UtcNow, payload);
        var json = JsonSerializer.Serialize(snapshot, LedgerJson.CreateOptions());
        var path = Path.Combine(ledger.Directory, "coordination-v1.json");
        await File.WriteAllTextAsync(path, json);

        var result = await OrchestrationMeasurement.BuildAsync(ledger.Service(), await ledger.StateAsync(), [], null, default);

        Assert.Equal("unavailable", result.JournalStatus);
        Assert.Empty(result.Observations);
        Assert.Contains(result.UnknownMeasurements, v => v.Contains("Coordination observations unavailable"));
        Assert.Equal(json, await File.ReadAllTextAsync(path));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task MissingOrCorruptTelemetryNeverBecomesZeroMeasuredIntervention(bool corrupt)
    {
        using var ledger = new FindingsFixture(); await ledger.OpenAsync();
        var path = Path.Combine(ledger.Directory, "coordination-v1.json");
        if (corrupt) await File.WriteAllTextAsync(path, "{broken");
        var state = await ledger.StateAsync();
        var history = new List<LedgerEvent>();
        await foreach (var row in ledger.Service().GetHistoryAsync(ledger.TaskId, default)) history.Add(row);
        var result = await OrchestrationMeasurement.BuildAsync(ledger.Service(), state, history, null, default);
        Assert.Equal(corrupt ? "unavailable" : "absent", result.JournalStatus);
        Assert.Null(result.RefusalsByCause);
        Assert.Empty(result.ProviderUsage);
        Assert.Contains(result.UnknownMeasurements, v => v.Contains("Human attention/time is unknown"));
        Assert.Contains(result.UnknownMeasurements, v => v.Contains("User corrections"));
        Assert.Equal(state.Version, (await ledger.StateAsync()).Version);
        if (corrupt) Assert.Equal("{broken", await File.ReadAllTextAsync(path));
        else Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("{}", null)] [InlineData("{\"total_cost_usd\":0}", "0")]
    [InlineData("{\"total_cost_usd\":0.013}", "0.013")]
    [InlineData("{\"total_cost_usd\":-1}", null)] [InlineData("{\"total_cost_usd\":\"0.1\"}", null)]
    [InlineData("{\"total_cost_usd\":1e100}", null)] [InlineData("[]", null)] [InlineData("{", null)]
    public void ReportedCostIsNotInferredFromTokensOrMalformedOutput(string json, string? expected)
    {
        ProviderEvent[] events = [new(0, "result", json, "session", true, false)];
        Assert.Equal(expected is null ? null : decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), RunCostReader.ReadReportedUsd("claude", events));
        Assert.Null(RunCostReader.ReadReportedUsd("codex", events));
        Assert.Null(RunCostReader.ReadReportedUsd("claude", []));
    }

    [Fact]
    public async Task RefusalCauseComesFromOwningTypeAndOldRowsRemainUnclassified()
    {
        using var ledger = new FindingsFixture(); await ledger.OpenAsync();
        var refusals = new RetrospectiveRefusalJournal([
            new(ledger.Actor, "command", "service", "Arbitrary text naming approval"),
            new(ledger.Actor, "command", "service", "Different prose", Cause: "StaleBasis")], 1);
        var result = await OrchestrationMeasurement.BuildAsync(ledger.Service(), await ledger.StateAsync(), [], refusals, default);
        Assert.Equal(1, result.RefusalsByCause!["unclassified"]);
        Assert.Equal(1, result.RefusalsByCause["StaleBasis"]);
        Assert.Contains(result.UnknownMeasurements, v => v.Contains("observed counts are not a total"));
    }
}
