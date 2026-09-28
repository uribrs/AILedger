using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Findings;

public sealed class ProviderFindingsLaunchTests
{
    [Fact]
    public async Task ProductionLaunchBindsSubjectAndPreservesOneUsageRecordAndExactManifest()
    {
        using var f = new FindingsFixture();
        using var work = new TemporaryDirectory();
        await f.OpenAsync();
        var researcher = new ActorId("researcher");
        await f.ExecuteAsync(new AssignRoleCommand(f.Actor, null, "assign", researcher, RoleKind.Researcher,
            [Capability.BuildContext, Capability.AddClaim, Capability.AddEvidence]));
        await f.ExecuteAsync(new RequestStageTransitionCommand(f.Actor, null, "research", TaskStage.Research,
            WithoutPrerequisitesReason: "Disposable launcher integration fixture"));
        await ContextBrief.BuildAsync(f.Root, f.TaskId.Value);
        var adapter = new RecordingAdapter();
        using var errors = new StringWriter();
        var app = new CliApplication(TextWriter.Null, errors, _ => f.Service(), _ => adapter, new ContextAssembler());
        var exit = await app.RunAsync(["provider", "launch", "--root", f.Root, "--task", f.TaskId.Value,
            "--actor", "operator", "--subject", "researcher", "--run", "R1", "--provider", "codex",
            "--executable", "/usr/bin/true", "--working-directory", work.Path,
            "--cognitive-root", ContextBrief.CognitiveRoot()], default);
        Assert.True(exit == 0, errors.ToString());
        var state = await f.StateAsync();
        var run = state.Runs[new("R1")];
        Assert.Equal(AgentRunStatus.Completed, run.Status);
        Assert.Equal("actual-observed-session", run.ProviderSessionId);
        Assert.Equal(19, run.OutputTokens);
        Assert.Equal(31, run.TokensInUncached);
        Assert.Null(run.Turns);
        Assert.NotNull(run.MillisecondsToFirstLedgerWrite);
        var request = adapter.Request!;
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.StandardInput))).ToLowerInvariant(), run.ManifestHash);
        Assert.Equal(JsonDocument.Parse(request.StandardInput).RootElement.GetProperty("artifacts").GetArrayLength(), run.ManifestArtifactCount);
        Assert.Single(state.Claims);
        Assert.Single(state.Evidence);
        var history = new List<LedgerEvent>();
        await foreach (var row in f.Service().GetHistoryAsync(f.TaskId, default)) history.Add(row);
        Assert.Single(history.Where(row => row.Data is RunCompleted));
        var measurement = await f.Service().ReadFindingsMeasurementAsync(state, history, null, default);
        Assert.Equal(2, measurement.ObservedToolAttempts);
        Assert.Equal(2, measurement.ObservedApplicationAttempts);
        Assert.Equal(1, measurement.CommittedTransactions);
        Assert.Equal(2, measurement.GranularEvents);
        Assert.Empty(measurement.CoverageGaps);
        Assert.All(measurement.Attempts, row => Assert.Equal("actual-observed-session", row.JoinedProviderSessionId));
        Assert.Equal(run.ManifestHash, Assert.Single(measurement.Runs).Completion!.ManifestHash);
        Assert.All(history.Where(row => row.Data is ClaimAdded or EvidenceAdded), row =>
        {
            Assert.Equal(researcher, row.ActorId);
            Assert.Equal("R1", row.CorrelationId);
        });
    }

    private sealed class RecordingAdapter : IAgentAdapter
    {
        public string Provider => "codex";
        internal AgentLaunchRequest? Request { get; private set; }
        public Task<string> ProbeVersionAsync(string executablePath, CancellationToken cancellationToken) => Task.FromResult("test");
        public async Task<AgentRunResult> RunAsync(AgentLaunchRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            Assert.NotNull(request.FindingsEndpoint);
            // Real subprocess relay and recorder; only the external model response is scripted.
            using var relay = new McpProcess(request.FindingsEndpoint.Arguments.Skip(1).ToArray());
            await ProviderFindingsSessionTests.InitializeAsync(relay);
            await relay.SendAsync(FindingsMcpFixture.Call(FindingsMcpFixture.Body()));
            var first = FindingsMcpFixture.Payload(await relay.ReadAsync());
            Assert.Equal("researcher", first.GetProperty("receipt").GetProperty("actor_id").GetString());
            await relay.SendAsync(FindingsMcpFixture.Call(FindingsMcpFixture.Body(), 3));
            Assert.True(FindingsMcpFixture.Payload(await relay.ReadAsync()).GetProperty("replayed").GetBoolean());
            Assert.Empty(await relay.FinishAsync());
            return new(request.RunId, "codex", "actual-observed-session", AgentRunStatus.Completed,
                DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow, 0, "done",
                [new(0, "turn.completed", """{"type":"turn.completed","usage":{"input_tokens":31,"output_tokens":19}}""",
                    null, true, false)], "", "test", [], false, null);
        }
    }
}
