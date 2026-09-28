using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AILedger.Cli;
using AILedger.Cli.Findings;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Findings;
using AILedger.Storage;
using AILedger.Storage.Findings;

namespace AILedger.Tests.Findings;

public sealed class FindingsMeasurementTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task LostResponseRetryHasOneCanonicalCommitDespiteCollectorFailures(bool applicationFailure, bool transportFailure)
    {
        using var f = new FindingsMcpFixture();
        await f.OpenAsync();
        var applicationPath = Path.Combine(f.Ledger.Directory, "findings-attempts.jsonl");
        if (applicationFailure) Directory.CreateDirectory(applicationPath);
        if (transportFailure) await File.WriteAllTextAsync(f.Configuration.DiagnosticsDirectory, "blocked");
        using var output = new LostResponseStream();
        await f.ExchangeAsync(FindingsMcpFixture.Call(FindingsMcpFixture.Body()) + "\n", output: output);
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
        using var f = new FindingsMcpFixture();
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
    public async Task ReadableSubsetAndInvalidOrUnterminatedRowsAreReportedTogether()
    {
        using var f = new FindingsMcpFixture();
        await f.OpenAsync();
        await f.RecordAsync();
        var path = Directory.GetFiles(f.Configuration.DiagnosticsDirectory).Single();
        await File.AppendAllTextAsync(path, "null\n{\"partial\":");
        var report = await ReadAsync(f);
        Assert.Equal(1, report.ObservedToolAttempts);
        Assert.Contains(report.CoverageGaps, x => x.Line == 2 && x.Reason.Contains("Unreadable"));
        Assert.Contains(report.CoverageGaps, x => x.Line == 3 && x.Reason.Contains("Unterminated"));
    }

    [Fact]
    public async Task UnknownFlushCanJoinLaterCanonicalReceiptWithoutRewritingItsOutcome()
    {
        using var f = new FindingsMcpFixture();
        await f.OpenAsync();
        await f.RecordAsync(recorder: f.Ledger.Faulted(new FindingsStorageFaults(Flush: _ => throw new IOException("fault"))));
        await f.RecordAsync();
        var report = await ReadAsync(f);
        Assert.Equal(1, report.CommittedTransactions);
        Assert.Equal(2, report.ObservedApplicationAttempts);
        Assert.Contains(report.Attempts, row => row.Kind == "transport" && row.Observation.CommitState == "unknown" &&
            row.CanonicalTransactionId == report.Transactions.Single().TransactionId && row.JoinedApplicationAttemptId is not null);
    }

    [Fact]
    public async Task PreApplicationDenialAndKernelRefusalKeepDifferentBoundaries()
    {
        using var f = new FindingsMcpFixture();
        await f.OpenAsync();
        await f.RecordAsync();
        using (var input = new MemoryStream(Encoding.UTF8.GetBytes(FindingsMcpFixture.Initialize + "\n" +
            FindingsMcpFixture.Ready + "\n" + FindingsMcpFixture.Call(FindingsMcpFixture.Body()) + "\n")))
        using (var output = new MemoryStream())
        {
            var host = new FindingsMcpHost(f.Configuration, f.Ledger.Service(),
                _ => throw new IOException("Host binding unavailable"));
            await new FindingsMcpServer(host, input, output, TextWriter.Null).RunAsync(default);
        }
        var invalid = new FindingsRequest(1, "refused", [new("f", "Uncommitted prefix")],
            [new("e", "source", "citation", "summary", [new(ClaimId: "missing")], [])]);
        await f.RecordAsync(FindingsMcpFixture.Body(invalid));
        var report = await ReadAsync(f);
        var denial = Assert.Single(report.Attempts.Where(x => x.Kind == "transport" && x.Observation.Code == "authorization_denied"));
        Assert.Null(denial.Observation.ApplicationAttemptId);
        Assert.Null(denial.CanonicalTransactionId);
        Assert.Equal("unknown", denial.Observation.CommitState);
        var refusal = Assert.Single(report.Attempts.Where(x => x.Kind == "transport" && x.Observation.Code == "kernel_refused"));
        Assert.Equal("kernel", refusal.Observation.Boundary);
        Assert.NotNull(refusal.JoinedApplicationAttemptId);
        Assert.Equal(1, report.CommittedTransactions);
        Assert.Single(await File.ReadAllLinesAsync(RefusalJournal.ResolvePath(f.Ledger.Directory)));
        Assert.Single((await f.Ledger.StateAsync()).Claims);
    }

    [Theory]
    [InlineData("claude", 8, 0)]
    [InlineData("codex", null, 0)]
    [InlineData("codex", null, 2)]
    public async Task RunCompletionJoinsSessionOnceAndPreservesCostAndMissingServedModel(string provider, int? turns, int truncatedLines)
    {
        using var f = new FindingsMcpFixture();
        await f.OpenAsync();
        var actor = new ActorId("researcher");
        await f.Ledger.ExecuteAsync(new AssignRoleCommand(f.Ledger.Actor, null, "assign", actor, RoleKind.Researcher,
            [Capability.AddClaim, Capability.AddEvidence]));
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanonicalReaderDoesNotCountTornGroupAndDoesNotTrustCorruptReceipt(bool corrupt)
    {
        using var f = new FindingsMcpFixture();
        await f.OpenAsync();
        var oldState = await f.Ledger.StateAsync();
        var oldHistory = await HistoryAsync(f.Ledger);
        await f.RecordAsync();
        var state = await f.Ledger.StateAsync();
        var history = await HistoryAsync(f.Ledger);
        var lines = await File.ReadAllLinesAsync(f.Ledger.EventsPath);
        if (corrupt)
        {
            lines[^2] = lines[^2].Replace("findings-v1-c14n1", "unsupported");
            await File.WriteAllLinesAsync(f.Ledger.EventsPath, lines);
        }
        else await File.WriteAllLinesAsync(f.Ledger.EventsPath, lines[..^1]);
        var report = await f.Ledger.Service().ReadFindingsMeasurementAsync(corrupt ? state : oldState,
            corrupt ? history : oldHistory, f.Configuration.DiagnosticsDirectory, default);
        Assert.Equal(corrupt ? (int?)null : 0, report.CommittedTransactions);
        Assert.All(report.Attempts, row => Assert.Null(row.CanonicalTransactionId));
        Assert.NotEmpty(report.CoverageGaps);
    }

    [Fact]
    public async Task CliOptInAddsOnlyFindingsAndIsReadOnlyWithExplicitStandaloneDirectory()
    {
        using var f = new FindingsMcpFixture();
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
        Assert.False(original.ContainsKey("findings"));
        output.GetStringBuilder().Clear();
        Assert.Equal(0, await cli.RunAsync([..args, "--findings", "--findings-telemetry", f.Configuration.DiagnosticsDirectory], default));
        var extended = JsonNode.Parse(output.ToString())!.AsObject();
        Assert.Equal(1, extended["findings"]!["observedToolAttempts"]!.GetValue<int>());
        extended.Remove("findings");
        Assert.True(JsonNode.DeepEquals(original, extended));
        Assert.Equal(before, await File.ReadAllBytesAsync(f.Ledger.EventsPath));
    }

    [Theory]
    [InlineData("transaction_id", "conflicting-transaction")]
    [InlineData("application_attempt_id", "missing-application")]
    [InlineData("correlation_id", "conflicting-correlation")]
    public async Task ConflictingJoinKeysAreNotSilentlyReconciled(string field, string value)
    {
        using var f = new FindingsMcpFixture();
        await f.OpenAsync();
        await f.RecordAsync();
        var path = Directory.GetFiles(f.Configuration.DiagnosticsDirectory).Single();
        var row = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        row[field] = value;
        await File.WriteAllTextAsync(path, row.ToJsonString() + "\n");
        var report = await ReadAsync(f);
        var transport = Assert.Single(report.Attempts.Where(x => x.Kind == "transport"));
        if (field == "transaction_id")
        {
            Assert.Null(transport.CanonicalTransactionId);
            Assert.Null(transport.JoinedApplicationAttemptId);
        }
        else if (field == "application_attempt_id") Assert.Null(transport.JoinedApplicationAttemptId);
        else Assert.Null(transport.CanonicalTransactionId);
        Assert.NotEmpty(report.CoverageGaps);
        Assert.Equal(1, report.CommittedTransactions);
    }

    [Fact]
    public async Task ApplicationExceptionLeavesMissingIdentityAndExplicitCoverage()
    {
        using var f = new FindingsMcpFixture();
        await f.OpenAsync();
        await f.RecordAsync(recorder: new ThrowingRecorder());
        var report = await ReadAsync(f);
        var row = Assert.Single(report.Attempts);
        Assert.Equal("application_exception", row.Observation.TransportFailure);
        Assert.Null(row.Observation.ApplicationAttemptId);
        Assert.Null(report.ObservedApplicationAttempts);
        Assert.Null(row.CanonicalTransactionId);
        Assert.Equal("unknown", row.Observation.CommitState);
        Assert.Contains(report.CoverageGaps, x => x.Reason.Contains("without an observed attempt identity"));
    }

    [Fact]
    public async Task AdvancingCanonicalSnapshotIsAGapInsteadOfMixingVersions()
    {
        using var f = new FindingsMcpFixture();
        await f.OpenAsync();
        var state = await f.Ledger.StateAsync();
        var history = await HistoryAsync(f.Ledger);
        await f.RecordAsync();
        var report = await f.Ledger.Service().ReadFindingsMeasurementAsync(state, history,
            f.Configuration.DiagnosticsDirectory, default);
        Assert.Null(report.CommittedTransactions);
        Assert.Equal(state.Version, report.CanonicalVersion);
        Assert.Contains(report.CoverageGaps, x => x.Reason.Contains("snapshot changed"));
    }

    private sealed class ThrowingRecorder : IFindingsRecorder
    {
        public Task<FindingsResult> RecordAsync(FindingsBinding binding, FindingsRequest request, CancellationToken cancellationToken) =>
            throw new IOException("No application result observed");
    }

    private static async Task<FindingsMeasurementReport> ReadAsync(FindingsMcpFixture f) =>
        await f.Ledger.Service().ReadFindingsMeasurementAsync(await f.Ledger.StateAsync(),
            await HistoryAsync(f.Ledger), f.Configuration.DiagnosticsDirectory, default);

    private static async Task<List<LedgerEvent>> HistoryAsync(FindingsFixture f)
    {
        var history = new List<LedgerEvent>();
        await foreach (var e in f.Service().GetHistoryAsync(f.TaskId, default)) history.Add(e);
        return history;
    }

    private sealed class LostResponseStream : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            Encoding.UTF8.GetString(buffer.Span).Contains("structuredContent", StringComparison.Ordinal)
                ? throw new IOException("Response lost after commit") : base.WriteAsync(buffer, cancellationToken);
    }
}
