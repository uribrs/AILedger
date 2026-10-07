using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AILedger.Cli;
using AILedger.Cli.Dispatch;
using AILedger.Core.Application;
using AILedger.Core.Authority;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Findings;
using AILedger.Tests.Support;
using AILedger.Tests.Cli;
namespace AILedger.Tests.Dispatch;

public sealed class ProviderDispatchTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task DirectAndCliUseSameBriefGrantsAndCompletion(bool cli)
    {
        using var f = await Fixture.OpenAsync();
        ProviderDispatchResult? receipt = null;
        if (cli)
        {
            using var error = new StringWriter();
            var app = new CliApplication(TextWriter.Null, error, _ => f.Ledger.Service(), _ => f.Adapter, new ContextAssembler());
            Assert.True(await app.RunAsync(["provider", "launch", "--root", f.Ledger.Root, "--task", f.Ledger.TaskId.Value,
                "--actor", "operator", "--run", "R1", "--provider", "codex", "--working-directory", f.Work.Path,
                "--executable", "/usr/bin/true", "--cognitive-root", ContextBrief.CognitiveRoot()], default) == 0, error.ToString());
        }
        else
        {
            receipt = await f.Direct().LaunchAsync(f.Request, default);
            Assert.Null(receipt.Failure);
            Assert.Equal(AssurancePreparationStatus.NotConfigured, receipt.AssurancePreparation);
            Assert.Equal(LedgerRecordingStatus.Recorded, receipt.StartRecording);
            Assert.Equal(LedgerRecordingStatus.Recorded, receipt.CompletionRecording);
            Assert.NotEmpty(receipt.Started!.EventIds);
            Assert.NotEmpty(receipt.Completed!.EventIds);
            Assert.True(receipt.Completed.TaskVersion > receipt.Started.TaskVersion);
        }
        var delivered = Assert.Single(f.Adapter.Requests);
        var state = await f.Ledger.StateAsync();
        var run = state.Runs[f.Request.RunId];
        Assert.Equal(AgentRunStatus.Completed, run.Status);
        Assert.Equal("observed", run.ProviderSessionId);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(delivered.StandardInput))).ToLowerInvariant(), run.ManifestHash);
        Assert.NotNull(delivered.FindingsEndpoint);
        Assert.DoesNotContain("accept_assurance", delivered.FindingsEndpoint.AssuranceTools ?? []);
        Assert.NotNull(delivered.Isolation);
        Assert.DoesNotContain(f.Ledger.Root, delivered.AdditionalDirectories);
        Assert.Null(run.OutputTokens);
        Assert.Null(run.Turns);
        var retained = await File.ReadAllTextAsync(Path.Combine(f.Ledger.Directory, "runs", "R1.json"));
        Assert.Contains("observed", retained);
        if (receipt is not null)
        {
            Assert.Equal(run.ManifestHash, receipt.DeliveredManifestHash);
            Assert.Equal(ResultRetentionStatus.Retained, receipt.Retention.Status);
        }
    }

    [Theory]
    [InlineData("waiver")] [InlineData("stale")] [InlineData("actor")] [InlineData("revoked")] [InlineData("expired")]
    public async Task RoutineAuthorityRefusesBeforeAdapterResolution(string refusal)
    {
        using var f = await Fixture.OpenAsync();
        var authority = new RoutineOrchestrationAuthority(await f.Ledger.StateAsync(), f.Request.ActorId,
            DateTimeOffset.UtcNow.AddHours(refusal == "expired" ? -1 : 1));
        if (refusal == "revoked") authority.Revoke();
        var request = refusal switch
        {
            "waiver" => f.Request with { WithoutBriefReason = "human says yes" },
            "stale" => f.Request with { StaleBriefEvidence = new("E1") },
            "actor" => f.Request with { ActorId = new("pretender") },
            _ => f.Request
        };
        var service = ProviderDispatchHost.CreateRoutine(f.Ledger.Service(), authority, f.Options,
            _ => throw new InvalidOperationException("Must not resolve adapter"), new ContextAssembler());
        var result = await service.LaunchAsync(request, default);
        Assert.Equal(DispatchFailureKind.Refused, result.Failure!.Kind);
        Assert.Equal(LedgerRecordingStatus.NotAttempted, result.StartRecording);
        Assert.Null(result.ProviderResult);
        Assert.Empty((await f.Ledger.StateAsync()).Runs);
    }

    [Fact]
    public async Task FreshRunAdmissionRechecksRoutineRevocationAfterProbe()
    {
        using var f = await Fixture.OpenAsync();
        var authority = new RoutineOrchestrationAuthority(await f.Ledger.StateAsync(), f.Request.ActorId, DateTimeOffset.UtcNow.AddHours(1));
        f.Adapter.Probe = () => { authority.Revoke(); return Task.CompletedTask; };
        var service = ProviderDispatchHost.CreateRoutine(f.Ledger.Service(), authority, f.Options, _ => f.Adapter, new ContextAssembler());
        var result = await service.LaunchAsync(f.Request, default);
        Assert.Equal(DispatchFailureKind.Refused, result.Failure!.Kind);
        Assert.Equal(LedgerRecordingStatus.Refused, result.StartRecording);
        Assert.Empty(f.Adapter.Requests);
        Assert.Empty((await f.Ledger.StateAsync()).Runs);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task HumanBriefExceptionRemainsAvailableOnlyOnHumanComposition(bool routine)
    {
        using var f = await Fixture.OpenAsync(brief: false);
        var service = routine ? ProviderDispatchHost.CreateRoutine(f.Ledger.Service(),
            new(await f.Ledger.StateAsync(), f.Request.ActorId, DateTimeOffset.UtcNow.AddHours(1)),
            f.Options, _ => f.Adapter, new ContextAssembler()) : f.Direct();
        var result = await service.LaunchAsync(f.Request with { WithoutBriefReason = "Authorized isolated fixture" }, default);
        Assert.Equal(routine ? DispatchFailureKind.Refused : (DispatchFailureKind?)null, result.Failure?.Kind);
        Assert.Equal(routine ? 0 : 1, f.Adapter.Requests.Count);
    }

    [Theory]
    [InlineData("executable", DispatchFailureKind.PreparationUnavailable)]
    [InlineData("configuration", DispatchFailureKind.PreparationUnavailable)]
    [InlineData("context", DispatchFailureKind.Refused)]
    public async Task PreparationFailuresCannotExecuteProvider(string failure, DispatchFailureKind kind)
    {
        using var f = await Fixture.OpenAsync();
        var options = failure switch
        {
            "executable" => f.Options with { Executable = Path.Combine(f.Work.Path, "absent") },
            "configuration" => f.Options with { AssuranceAuthority = Path.Combine(f.Work.Path, "absent") },
            _ => f.Options with { CognitiveRoot = Path.Combine(f.Work.Path, "absent") }
        };
        var result = await f.Direct(options: options).LaunchAsync(f.Request, default);
        Assert.Equal(kind, result.Failure!.Kind);
        Assert.Empty(f.Adapter.Requests);
        Assert.Empty((await f.Ledger.StateAsync()).Runs);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task UnacknowledgedCompletionStaysUnknownEvenIfItActuallyCommitted(bool commit)
    {
        using var f = await Fixture.OpenAsync();
        var faulted = new CompletionFault(f.Ledger.Service(), commit);
        var result = await f.Direct(faulted).LaunchAsync(f.Request, default);
        Assert.Equal(AgentRunStatus.Completed, result.ProviderResult!.Status);
        Assert.Equal(DispatchFailureKind.CompletionRecordingFailed, result.Failure!.Kind);
        Assert.Equal(LedgerRecordingStatus.Unknown, result.CompletionRecording);
        Assert.Null(result.Completed);
        Assert.Equal(ResultRetentionStatus.Retained, result.Retention.Status);
        Assert.True(File.Exists(result.Retention.Path));
        Assert.Equal(commit ? AgentRunStatus.Completed : AgentRunStatus.Active,
            (await f.Ledger.StateAsync()).Runs[f.Request.RunId].Status);
    }

    [Fact]
    public async Task RetentionFailureDoesNotEraseProviderOrAcknowledgedCompletion()
    {
        using var f = await Fixture.OpenAsync();
        await File.WriteAllTextAsync(Path.Combine(f.Ledger.Directory, "runs"), "blocks the result directory");
        var result = await f.Direct().LaunchAsync(f.Request, default);
        Assert.Equal(ResultRetentionStatus.Failed, result.Retention.Status);
        Assert.NotNull(result.Retention.Diagnostic);
        Assert.Equal(AgentRunStatus.Completed, result.ProviderResult!.Status);
        Assert.Equal(AgentRunStatus.Completed, result.Completed!.Status);
        Assert.Null(result.Failure);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ProviderFaultAndCancellationRetainHonestCleanupReceipts(bool cancelled)
    {
        using var f = await Fixture.OpenAsync();
        f.Adapter.Execute = (_, _) => throw (cancelled ? new OperationCanceledException() : new IOException("provider died"));
        var result = await f.Direct().LaunchAsync(f.Request, default);
        Assert.Equal(cancelled ? DispatchFailureKind.Cancelled : DispatchFailureKind.ProviderFault, result.Failure!.Kind);
        Assert.Null(result.ProviderResult);
        Assert.Equal(ResultRetentionStatus.NotAttempted, result.Retention.Status);
        Assert.Equal(cancelled ? AgentRunStatus.Cancelled : AgentRunStatus.Failed, result.Completed!.Status);
        Assert.NotNull(result.DeliveredManifestHash);
    }

    [Fact]
    public async Task MismatchedProviderResultIsNotRetainedOrAttributed()
    {
        using var f = await Fixture.OpenAsync();
        f.Adapter.Execute = (request, _) => Task.FromResult(Adapter.Result(request) with { RunId = new("foreign") });
        var result = await f.Direct().LaunchAsync(f.Request, default);
        Assert.Equal(DispatchFailureKind.ProviderFault, result.Failure!.Kind);
        Assert.Null(result.ProviderResult);
        Assert.Equal(AgentRunStatus.Failed, result.Completed!.Status);
        Assert.False(File.Exists(Path.Combine(f.Ledger.Directory, "runs", "foreign.json")));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task UnconfirmedCleanupRetainsDiagnosticsAndCannotBeReportedAsOrdinaryCancellation(bool cancelled)
    {
        using var f = await Fixture.OpenAsync(); using var cancellation = new CancellationTokenSource();
        f.Adapter.Execute = (request, _) =>
        {
            if (cancelled) cancellation.Cancel();
            return Task.FromResult(Adapter.Result(request) with
            {
                Status = cancelled ? AgentRunStatus.Cancelled : AgentRunStatus.Failed,
                ExitCode = -1, StandardError = "original startup diagnostic", Failure = "Cleanup is unconfirmed",
                ProcessFailure = new(ProviderProcessFailureKind.Cleanup, "original input failure", null, null, false, false, "reap failed")
            });
        };
        var result = await f.Direct().LaunchAsync(f.Request, cancellation.Token);
        Assert.Equal(DispatchFailureKind.ProcessCleanupUnconfirmed, result.Failure!.Kind);
        Assert.Equal(ResultRetentionStatus.Retained, result.Retention.Status);
        Assert.Equal(LedgerRecordingStatus.Recorded, result.CompletionRecording);
        Assert.Contains("original startup diagnostic", await File.ReadAllTextAsync(result.Retention.Path!));
        Assert.Null(result.ProviderResult!.ProcessFailure!.ObservedExitCode);
    }

    [Fact]
    public async Task LostRunStartAcknowledgementDoesNotDispatchAndCannotBeReportedAsAbsent()
    {
        using var f = await Fixture.OpenAsync();
        var result = await f.Direct(new CompletionFault(f.Ledger.Service(), commit: true, atStart: true)).LaunchAsync(f.Request, default);
        Assert.Equal(LedgerRecordingStatus.Unknown, result.StartRecording);
        Assert.Equal(LedgerRecordingStatus.NotAttempted, result.CompletionRecording);
        Assert.Null(result.Started);
        Assert.Null(result.ProviderResult);
        Assert.Empty(f.Adapter.Requests);
        Assert.Equal(AgentRunStatus.Active, (await f.Ledger.StateAsync()).Runs[f.Request.RunId].Status);
    }

    [Fact]
    public void CognitiveHandoffsDeclareImplementedOperationsAndCannotCarryCommands()
    {
        Assert.Equal(Enum.GetValues<CognitiveHostOperationKind>().Length, DispatchHostHandoffs.Support.Count);
        Assert.All(DispatchHostHandoffs.Support, support => Assert.True(support.Available));
        Assert.Equal(["InternalRecon", "PromptContract", "OrchestrationPlan"], Enum.GetNames<GoverningArtifactHandoffKind>());
        Assert.DoesNotContain(typeof(GoverningArtifactHandoff).GetProperties(), property =>
            property.PropertyType == typeof(LedgerCommand) || property.Name is "ActorId" or "TaskId");
    }

    private sealed class Fixture : IDisposable
    {
        public FindingsFixture Ledger { get; } = new();
        public TemporaryDirectory Work { get; } = new();
        public Adapter Adapter { get; } = new();
        public ProviderDispatchRequest Request => new(Ledger.TaskId, Ledger.Actor, new("R1"), "codex") { WorkingDirectory = Work.Path };
        public DispatchHostOptions Options => new(Ledger.Root, Path.Combine(Ledger.Root, "lessons"))
            { CognitiveRoot = ContextBrief.CognitiveRoot(), Executable = "/usr/bin/true" };
        public static async Task<Fixture> OpenAsync(bool brief = true)
        {
            var f = new Fixture();
            await f.Ledger.OpenAsync();
            if (brief) await ContextBrief.BuildAsync(f.Ledger.Root, f.Ledger.TaskId.Value);
            return f;
        }
        public IProviderDispatchService Direct(IGovernedTaskService? service = null, DispatchHostOptions? options = null) =>
            ProviderDispatchHost.CreateHuman(service ?? Ledger.Service(), options ?? Options, _ => Adapter, new ContextAssembler());
        public void Dispose() { Work.Dispose(); Ledger.Dispose(); }
    }

    private sealed class Adapter : IAgentAdapter
    {
        public string Provider => "codex";
        public List<AgentLaunchRequest> Requests { get; } = [];
        public Func<Task>? Probe { get; set; }
        public Func<AgentLaunchRequest, CancellationToken, Task<AgentRunResult>>? Execute { get; set; }
        public async Task<string> ProbeVersionAsync(string executablePath, CancellationToken token)
        { if (Probe is not null) await Probe(); return "fixture"; }
        public Task<AgentRunResult> RunAsync(AgentLaunchRequest request, CancellationToken token)
        { Requests.Add(request); return Execute?.Invoke(request, token) ?? Task.FromResult(Result(request)); }
        public static AgentRunResult Result(AgentLaunchRequest request) => new(request.RunId, "codex", "observed",
            AgentRunStatus.Completed, DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow, 0, "done", [], "", "fixture", [], false, null);
    }

    private sealed class CompletionFault(IGovernedTaskService inner, bool commit, bool atStart = false) : IGovernedTaskService, IAgentSessionServiceFactory
    {
        private bool _attempted;
        public IGovernedTaskService BindAgentSession(AgentSessionAuthority authority) => ((IAgentSessionServiceFactory)inner).BindAgentSession(authority);
        public async Task<CommandOutcome> ExecuteAsync(TaskId taskId, LedgerCommand command, CancellationToken token)
        {
            if (atStart ? command is StartRunCommand : command is CompleteRunCommand)
            {
                if (commit && !_attempted) { _attempted = true; await inner.ExecuteAsync(taskId, command, token); }
                else if (commit) return await inner.ExecuteAsync(taskId, command, token);
                throw new IOException("Acknowledgement lost");
            }
            return await inner.ExecuteAsync(taskId, command, token);
        }
        public Task<GovernedTaskState?> GetStateAsync(TaskId taskId, CancellationToken token) => inner.GetStateAsync(taskId, token);
        public IAsyncEnumerable<LedgerEvent> GetHistoryAsync(TaskId taskId, CancellationToken token) => inner.GetHistoryAsync(taskId, token);
    }
}
