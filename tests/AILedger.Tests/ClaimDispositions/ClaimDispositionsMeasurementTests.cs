using System.Text;
using System.Text.Json.Nodes;
using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.ClaimDispositions;
using AILedger.Core.Contracts;
using AILedger.Tests.Findings;

namespace AILedger.Tests.ClaimDispositions;

[Collection(ClaimDispositionsRecordingCollection.Name)]
public sealed class ClaimDispositionsMeasurementTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task LostResponseRetryHasOneCanonicalCommitDespiteCollectorFailures(bool applicationFailure, bool transportFailure)
    {
        using var f = new ClaimDispositionsMcpFixture();
        await f.OpenAsync();
        var applicationPath = Path.Combine(f.Ledger.Directory, "claim_dispositions-attempts.jsonl");
        if (applicationFailure) Directory.CreateDirectory(applicationPath);
        if (transportFailure) await File.WriteAllTextAsync(f.Configuration.DiagnosticsDirectory, "blocked");
        using var output = new LostResponseStream();
        await f.ExchangeAsync(ClaimDispositionsMcpFixture.Call(ClaimDispositionsMcpFixture.Body()) + "\n", output: output);
        var before = await File.ReadAllBytesAsync(f.Ledger.EventsPath);
        var retry = await f.RecordAsync();
        Assert.True(retry.GetProperty("replayed").GetBoolean());
        Assert.Equal(before, await File.ReadAllBytesAsync(f.Ledger.EventsPath));
        var report = await ReadAsync(f);
        Assert.Equal(1, report.CommittedTransactions);
        Assert.Equal(2, report.GranularEvents);
        Assert.Equal(applicationFailure ? (int?)null : 2, report.ObservedApplicationAttempts);
        Assert.Equal(transportFailure ? (int?)null : 2, report.ObservedToolAttempts);
        Assert.All(report.Attempts, row => Assert.Equal(Assert.Single(report.Transactions).TransactionId, row.CanonicalTransactionId));
        if (!transportFailure)
        {
            var calls = report.Attempts.Where(x => x.Kind == "transport").ToArray();
            Assert.Contains(calls, x => x.Observation.ResponseDelivery == "failed" && x.Observation.CommitState == "committed");
            Assert.Contains(calls, x => x.Observation.ResponseDelivery == "written" && x.Observation.Replayed == true);
            Assert.All(calls, x => Assert.True(x.Observation.DurationMs >= x.Observation.ApplicationMs));
        }
        if (applicationFailure || transportFailure) Assert.NotEmpty(report.CoverageGaps);
    }

    [Theory]
    [InlineData("claude", 8, 0)]
    [InlineData("codex", null, 0)]
    [InlineData("codex", null, 2)]
    public async Task RunCompletionJoinsSessionOnceAndPreservesCostAndMissingServedModel(string provider, int? turns, int truncatedLines)
    {
        using var f = new ClaimDispositionsMcpFixture();
        await f.OpenAsync();
        var actor = new ActorId("researcher");
        await f.Ledger.ExecuteAsync(new AssignRoleCommand(f.Ledger.Actor, null, "assign", actor, RoleKind.PlanningLead,
            [Capability.ResolveClaim]));
        await f.Ledger.ExecuteAsync(new RequestStageTransitionCommand(f.Ledger.Actor, null, "research", TaskStage.Research,
            WithoutPrerequisitesReason: "Disposable measurement fixture"));
        await f.Ledger.ExecuteAsync(new StartRunCommand(f.Ledger.Actor, null, "dispatch", new("R1"), null,
            provider, "actual-session", SubjectActorId: actor, Model: "requested-model"));
        f.Configuration = f.Configuration with { ActorId = actor.Value, RunId = "R1", CorrelationId = "R1",
            AllowRunless = false, Provider = provider };
        await f.RecordAsync();
        var active = await ReadAsync(f);
        Assert.All(active.Attempts, row => Assert.Null(row.JoinedProviderSessionId));
        await f.Ledger.ExecuteAsync(new CompleteRunCommand(f.Ledger.Actor, null, "close", new("R1"), AgentRunStatus.Completed,
            "actual-session", ManifestHash: new string('a', 64), ManifestArtifactCount: 8,
            Turns: turns, OutputTokens: 19, MillisecondsToFirstLedgerWrite: 12,
            TokensInUncached: 31, TokensInCacheWrite: 0, TokensInCacheRead: 7,
            TruncatedLines: truncatedLines, LaunchTimeoutSeconds: 100, EndedAtTheLaunchTimeout: false));
        await f.RecordAsync();
        var report = await ReadAsync(f);
        Assert.All(report.Attempts, row => { Assert.Equal("actual-session", row.JoinedProviderSessionId); Assert.Null(row.Observation.ProviderSessionId); });
        var run = Assert.Single(report.Runs);
        Assert.Equal("requested-model", run.RequestedModel);
        Assert.Null(run.Completion!.Model);
        Assert.Equal(turns, run.Completion.Turns);
        Assert.Equal(8, run.Completion.ManifestArtifactCount);
        Assert.Equal(12, run.Completion.MillisecondsToFirstLedgerWrite);
        Assert.False(run.Completion.EndedAtTheLaunchTimeout);
        Assert.Equal(truncatedLines, run.Completion.TruncatedLines);
        if (truncatedLines > 0) Assert.Contains(report.CoverageGaps, x => x.Source == "run:R1" && x.Reason.Contains("truncated"));
        var state = await f.Ledger.StateAsync();
        var history = await HistoryAsync(f.Ledger);
        var old = TaskRetrospective.Build(state, history, null);
        Assert.Equal(19, old.Cost.OutputTokens.Total);
        Assert.Equal(1, old.Cost.OutputTokens.RunsMeasured);
        Assert.Equal(0, old.Cost.TokensInCacheWrite.Total);
        Assert.Equal(turns, old.Cost.Turns.Total);
        Assert.Equal(2, report.ObservedToolAttempts);
        Assert.Equal(1, report.CommittedTransactions);
    }

    [Fact]
    public async Task CliOptInAddsOnlyClaimDispositionsAndIsReadOnlyWithExplicitStandaloneDirectory()
    {
        using var f = new ClaimDispositionsMcpFixture();
        await f.OpenAsync();
        await f.RecordAsync();
        var before = await File.ReadAllBytesAsync(f.Ledger.EventsPath);
        using var output = new StringWriter();
        using var errors = new StringWriter();
        var cli = new CliApplication(output, errors, _ => f.Ledger.Service(),
            _ => throw new InvalidOperationException("No provider needed"), new ContextAssembler());
        string[] args = ["retrospective", "build", "--root", f.Ledger.Root, "--task", f.Ledger.TaskId.Value];
        Assert.Equal(0, await cli.RunAsync(args, default));
        var original = JsonNode.Parse(output.ToString())!.AsObject();
        Assert.False(original.ContainsKey("claimDispositions"));
        output.GetStringBuilder().Clear();
        Assert.True(await cli.RunAsync([..args, "--dispositions", "--dispositions-telemetry", f.Configuration.DiagnosticsDirectory], default) == 0, errors.ToString());
        var extended = JsonNode.Parse(output.ToString())!.AsObject();
        Assert.Equal(1, extended["claimDispositions"]!["observedToolAttempts"]!.GetValue<int>());
        extended.Remove("claimDispositions");
        Assert.True(JsonNode.DeepEquals(original, extended));
        Assert.Equal(before, await File.ReadAllBytesAsync(f.Ledger.EventsPath));
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("empty")]
    [InlineData("truncated")]
    [InlineData("unreadable")]
    [InlineData("invalid-json")]
    [InlineData("missing-member")]
    [InlineData("wrong-task")]
    [InlineData("duplicate-property")]
    [InlineData("unsupported-version")]
    [InlineData("duplicate-attempt")]
    public async Task DamagedTelemetryNeverBecomesZeroOrRevokesCanonicalCommit(string damage)
    {
        using var f = new ClaimDispositionsMcpFixture();
        await f.OpenAsync();
        await f.RecordAsync();
        var path = Directory.GetFiles(f.Configuration.DiagnosticsDirectory).Single();
        var original = await File.ReadAllTextAsync(path);
        switch (damage)
        {
            case "absent": File.Delete(path); break;
            case "empty": await File.WriteAllTextAsync(path, ""); break;
            case "truncated": await File.WriteAllTextAsync(path, original.TrimEnd()); break;
            case "unreadable": File.Delete(path); Directory.CreateDirectory(path); break;
            case "invalid-json": await File.WriteAllTextAsync(path, "{broken}\n"); break;
            case "duplicate-attempt": await File.AppendAllTextAsync(path, original); break;
            case "duplicate-property": await File.WriteAllTextAsync(path, original.Replace("{", "{\"schema_version\":1,", StringComparison.Ordinal)); break;
            default:
                var row = JsonNode.Parse(original)!.AsObject();
                if (damage == "missing-member") row.Remove("application_entered");
                if (damage == "wrong-task") row["task_id"] = "other";
                if (damage == "unsupported-version") row["schema_version"] = 2;
                await File.WriteAllTextAsync(path, row.ToJsonString() + "\n");
                break;
        }
        var report = await ReadAsync(f);
        Assert.Equal(1, report.CommittedTransactions);
        Assert.Null(report.ObservedToolAttempts);
        Assert.Equal(1, report.ObservedApplicationAttempts);
        Assert.NotEmpty(report.CoverageGaps);
        Assert.True((await f.RecordAsync()).GetProperty("replayed").GetBoolean());
    }

    [Fact]
    public async Task ForgedReceiptRunCannotBecomeAReportedCanonicalCommit()
    {
        using var f = new ClaimDispositionsMcpFixture();
        await f.OpenAsync();
        await f.RecordAsync();
        var state = await f.Ledger.StateAsync();
        var history = await HistoryAsync(f.Ledger);
        var lines = await File.ReadAllLinesAsync(f.Ledger.EventsPath);
        var line = JsonNode.Parse(lines[7])!;
        line["_ailedgerClaimDispositionsReceipt"]!["run_id"] = "stable";
        lines[7] = line.ToJsonString();
        await File.WriteAllTextAsync(f.Ledger.EventsPath, string.Join("\n", lines) + "\n");
        var report = await f.Ledger.Service().ReadClaimDispositionsMeasurementAsync(state, history,
            f.Configuration.DiagnosticsDirectory, default);
        Assert.Null(report.CommittedTransactions);
        Assert.Empty(report.Transactions);
        Assert.Contains(report.CoverageGaps, gap => gap.Reason.Contains("Canonical receipts unavailable"));
    }

    private static async Task<List<LedgerEvent>> HistoryAsync(ClaimDispositionsFixture f)
    {
        var events = new List<LedgerEvent>();
        await foreach (var e in f.Service().GetHistoryAsync(f.TaskId, default)) events.Add(e);
        return events;
    }

    private static async Task<ClaimDispositionsMeasurementReport> ReadAsync(ClaimDispositionsMcpFixture f)
    {
        var state = await f.Ledger.StateAsync();
        var history = new List<LedgerEvent>();
        await foreach (var e in f.Ledger.Service().GetHistoryAsync(f.Ledger.TaskId, default)) history.Add(e);
        var findings = await f.Ledger.Service().ReadFindingsMeasurementAsync(state, history, f.Configuration.DiagnosticsDirectory, default);
        Assert.Equal(0, findings.CommittedTransactions);
        Assert.Equal(0, findings.GranularEvents);
        return await f.Ledger.Service().ReadClaimDispositionsMeasurementAsync(state, history, f.Configuration.DiagnosticsDirectory, default);
    }
    private sealed class LostResponseStream : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            Encoding.UTF8.GetString(buffer.Span).Contains("structuredContent", StringComparison.Ordinal)
                ? throw new IOException("Response lost after commit") : base.WriteAsync(buffer, cancellationToken);
    }
}
