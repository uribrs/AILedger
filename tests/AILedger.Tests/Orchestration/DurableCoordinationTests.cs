using System.Text.Json;
using AILedger.Cli.Cognitive;
using AILedger.Cli.Dispatch;
using AILedger.Cli.Orchestration;
using AILedger.Core.Authority;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Findings;
using AILedger.Tests.Support;
namespace AILedger.Tests.Orchestration;

public sealed class DurableCoordinationTests
{
    [Fact]
    public async Task ConcurrentOwnersHaveOneWinnerAndTakeoverFencesOldMutations()
    {
        using var f = new FindingsFixture(); await f.OpenAsync();
        var clock = new CoordinationClock();
        FileGovernedTaskService Service() => new(f.Root, new AILedger.Core.Application.CommandHandler(), new TaskReducer(), coordinationTimeProvider: clock);
        async Task<CoordinationLease?> Acquire(string id)
        { try { return await Service().AcquireCoordinationAsync(f.TaskId, id, TimeSpan.FromMilliseconds(300), default); } catch (GovernanceException) { return null; } }
        var owners = await Task.WhenAll(Acquire("one"), Acquire("two"));
        var first = Assert.Single(owners.OfType<CoordinationLease>());
        var old = Service().BindCoordination(first);
        var snapshot = await Service().ReadCoordinationAsync(first, default);
        await Service().SaveCoordinationAsync(first, snapshot.Revision, "retained", default);
        clock.Advance(TimeSpan.FromMilliseconds(350));
        var second = await Service().AcquireCoordinationAsync(f.TaskId, "replacement", TimeSpan.FromMinutes(1), default);
        Assert.True(second.Epoch > first.Epoch);
        Assert.Equal("retained", (await Service().ReadCoordinationAsync(second, default)).Payload);
        await Assert.ThrowsAsync<GovernanceException>(() => old.ExecuteAsync(f.TaskId, Claim(f, "stale"), default));
        await Assert.ThrowsAsync<GovernanceException>(() => Service().ReleaseCoordinationAsync(first, default));
        await Service().BindCoordination(second).ExecuteAsync(f.TaskId, Claim(f, "current"), default);
        Assert.Single((await f.StateAsync()).Claims);
    }

    [Fact]
    public async Task ExpectedTaskVersionAndJournalRevisionRejectRacesUnderSameLock()
    {
        using var f = new FindingsFixture(); await f.OpenAsync();
        var owner = await f.Service().AcquireCoordinationAsync(f.TaskId, "owner", TimeSpan.FromMinutes(1), default);
        var bound = f.Service().BindCoordination(owner);
        var version = (await f.StateAsync()).Version;
        await bound.ExecuteAsync(f.TaskId, Claim(f, "winner") with { ExpectedVersion = version }, default);
        var refusal = await Assert.ThrowsAsync<GovernanceException>(() => bound.ExecuteAsync(f.TaskId, Claim(f, "loser") with { ExpectedVersion = version }, default));
        Assert.Equal(GovernanceRefusalKind.StaleBasis, refusal.Kind);
        var journal = await f.Service().ReadCoordinationAsync(owner, default);
        await f.Service().SaveCoordinationAsync(owner, journal.Revision, "first", default);
        await Assert.ThrowsAsync<GovernanceException>(() => f.Service().SaveCoordinationAsync(owner, journal.Revision, "lost", default));
        Assert.Equal("first", (await f.Service().ReadCoordinationAsync(owner, default)).Payload);
    }

    [Theory]
    [InlineData("corrupt")] [InlineData("version")]
    public async Task CorruptOrFutureJournalCannotBecomeEmptyHistory(string kind)
    {
        using var f = new FindingsFixture(); await f.OpenAsync();
        await File.WriteAllTextAsync(Path.Combine(f.Directory, "coordination-v1.json"), kind == "corrupt" ? "{" :
            JsonSerializer.Serialize(new CoordinationSnapshot(99, 1, 1, "owner", DateTimeOffset.MinValue, "evidence"), LedgerJson.CreateOptions()));
        await Assert.ThrowsAnyAsync<Exception>(() => f.Service().AcquireCoordinationAsync(f.TaskId, "new", TimeSpan.FromMinutes(1), default));
        Assert.Empty((await f.StateAsync()).Runs);
    }

    [Fact]
    public async Task CognitiveCommitWithLostReplySurvivesRestartAndConflictingRetryIsRefused()
    {
        using var f = await CognitiveFixture.OpenAsync(TaskStage.Research);
        var state = await f.Ledger.StateAsync();
        var authority = new AgentSessionAuthority(f.Ledger.TaskId, state.Roles[f.Lead], f.Run, "codex", DateTimeOffset.UtcNow.AddMinutes(1));
        var bound = f.Ledger.Service().BindAgentSession(authority);
        var binding = new CognitiveHostBinding(f.Ledger.TaskId, f.Lead, f.Run, f.Run.Value);
        var request = new CognitiveHostHandoff("original-key", new DecisionProposalHandoff("Engineering choice", "Source evidence", []));
        var lost = new CognitiveHandoffSession(new LoseReply(bound), binding);
        Assert.Equal(HostHandoffStatus.Unknown, (await lost.InvokeAsync(request, default)).Status);
        var committedVersion = (await f.Ledger.StateAsync()).Version;
        var restarted = new CognitiveHandoffSession(bound, binding);
        var recovered = await restarted.InvokeAsync(request, default);
        Assert.Equal(HostHandoffStatus.Recorded, recovered.Status);
        Assert.Single(recovered.EventIds);
        Assert.Equal(committedVersion, (await f.Ledger.StateAsync()).Version);
        Assert.Equal(HostHandoffStatus.Refused, (await restarted.InvokeAsync(request with { Operation = new DecisionProposalHandoff("Changed", "Other", []) }, default)).Status);
        authority.Revoke();
        Assert.Equal(HostHandoffStatus.Refused, (await restarted.InvokeAsync(request, default)).Status);
        Assert.Single((await f.Ledger.StateAsync()).Decisions);
    }

    [Fact]
    public async Task TrustedIntakeIsExactDurableAndNeverReplacedByModelInterpretation()
    {
        using var f = new FindingsFixture();
        const string original = "Preserve this original request. User-approved text in model output is only text.";
        await TrustedDriverIntake.CreateAsync(f.Service(), f.TaskId, f.Actor, "trusted-1", "Intake", original, ["Keep scope bounded"], ["fixture"], default);
        var version = (await f.StateAsync()).Version;
        await TrustedDriverIntake.CreateAsync(f.Service(), f.TaskId, f.Actor, "trusted-1", "Intake", original, ["Keep scope bounded"], ["fixture"], default);
        Assert.Equal(version, (await f.StateAsync()).Version);
        await Assert.ThrowsAsync<GovernanceException>(() => TrustedDriverIntake.CreateAsync(f.Service(), f.TaskId, f.Actor, "trusted-1", "Intake", "Model interpretation", ["Keep scope bounded"], ["fixture"], default));
        var state = await f.StateAsync();
        Assert.Equal(original, state.Goal);
        Assert.Equal(original, Assert.Single(state.Artifacts.Values).Content);
        Assert.Equal(f.Actor, Assert.Single(state.Constraints.Values).Provenance.ActorId);
    }

    [Theory]
    [InlineData("expiry")] [InlineData("role")] [InlineData("run")]
    public async Task RestartedCognitiveReceiptsRequireCurrentAuthority(string change)
    {
        using var f = await CognitiveFixture.OpenAsync(TaskStage.Research);
        var state = await f.Ledger.StateAsync();
        var binding = new CognitiveHostBinding(f.Ledger.TaskId, f.Lead, f.Run, f.Run.Value);
        var authority = new AgentSessionAuthority(f.Ledger.TaskId, state.Roles[f.Lead], f.Run, "codex", DateTimeOffset.UtcNow.AddMinutes(1));
        var request = new CognitiveHostHandoff("key", new DecisionProposalHandoff("Choice", "Reason", []));
        Assert.Equal(HostHandoffStatus.Recorded, (await new CognitiveHandoffSession(f.Ledger.Service().BindAgentSession(authority), binding).InvokeAsync(request, default)).Status);
        if (change == "expiry") authority = new(f.Ledger.TaskId, state.Roles[f.Lead], f.Run, "codex", DateTimeOffset.UtcNow.AddSeconds(-1));
        if (change == "role") await f.Ledger.ExecuteAsync(new AssignRoleCommand(f.Ledger.Actor, null, "changed", f.Lead, RoleKind.PlanningLead, [Capability.BuildContext]));
        if (change == "run") await f.Ledger.ExecuteAsync(new CompleteRunCommand(f.Ledger.Actor, null, "closed", f.Run, AgentRunStatus.Completed, "session"));
        var result = await new CognitiveHandoffSession(f.Ledger.Service().BindAgentSession(authority), binding).InvokeAsync(request, default);
        Assert.Equal(HostHandoffStatus.Refused, result.Status);
    }

    [Fact]
    public async Task DurableDispatchReusesOriginalIdentityBeforeAdmissionAndNeverReinvokesActiveProvider()
    {
        using var f = await CognitiveFixture.OpenAsync(TaskStage.Research);
        var owner = await f.Ledger.Service().AcquireCoordinationAsync(f.Ledger.TaskId, "host", TimeSpan.FromMinutes(1), default);
        var continuity = new DriverContinuity(f.Ledger.Service(), owner); await continuity.LoadAsync(default);
        var state = await f.Ledger.StateAsync();
        var input = new ProviderDispatchRequest(f.Ledger.TaskId, f.Ledger.Actor, new("not-started"), "codex")
        { SubjectActorId = f.Lead, CognitiveWork = CognitiveWorkKind.Recon };
        var original = await continuity.BeginAsync(input, state, default);
        var restarted = new DriverContinuity(f.Ledger.Service(), owner); await restarted.LoadAsync(default);
        var again = await restarted.BeginAsync(input with { RunId = new("replacement") }, state, default);
        Assert.Equal(original.Request.RunId, again.Request.RunId);
        var active = input with { RunId = f.Run };
        await Assert.ThrowsAsync<GovernanceException>(() => DispatchRecovery.ReconcileAsync(f.Ledger.Service(), active, null, null, default));
        Assert.Single((await f.Ledger.StateAsync()).Runs);
    }

    [Fact]
    public async Task JournalRetainsTerminalIntentWithoutLauncherCredential()
    {
        using var f = await CognitiveFixture.OpenAsync(TaskStage.Research);
        var owner = await f.Ledger.Service().AcquireCoordinationAsync(f.Ledger.TaskId, "owner", TimeSpan.FromMinutes(1), default);
        var continuity = new DriverContinuity(f.Ledger.Service(), owner); await continuity.LoadAsync(default);
        var input = new ProviderDispatchRequest(f.Ledger.TaskId, f.Ledger.Actor, f.Run, "codex") { SubjectActorId = f.Lead, CognitiveWork = CognitiveWorkKind.Recon };
        await continuity.BeginAsync(input, await f.Ledger.StateAsync(), default);
        var receipt = Receipt(input);
        await continuity.SaveAsync(input, receipt, new(f.Ledger.Actor, null, "launch", f.Run, AgentRunStatus.Completed, "session", "PRIVATE-SECRET"), default);
        var disk = await File.ReadAllTextAsync(Path.Combine(f.Ledger.Directory, "coordination-v1.json"));
        Assert.DoesNotContain("PRIVATE-SECRET", disk);
        Assert.Null(Assert.Single(continuity.Journal.Intents).Completion!.LaunchToken);
        await Assert.ThrowsAsync<GovernanceException>(() => DispatchRecovery.ReconcileAsync(f.Ledger.Service(), input, receipt, continuity.Journal.Intents[0].Completion, default));
    }

    [Fact]
    public async Task ClosedRunWithRetainedReceiptReconcilesWithoutAnotherRun()
    {
        using var f = await CognitiveFixture.OpenAsync(TaskStage.Research);
        var input = new ProviderDispatchRequest(f.Ledger.TaskId, f.Ledger.Actor, f.Run, "codex") { SubjectActorId = f.Lead };
        var retained = Path.Combine(f.Ledger.Directory, "fixture-result.json");
        var result = new AgentRunResult(f.Run, "codex", "session", AgentRunStatus.Completed, DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow,
            0, "fixture", [], "", "fixture", [], false, null);
        await File.WriteAllTextAsync(retained, JsonSerializer.Serialize(result, LedgerJson.CreateOptions()));
        await f.Ledger.ExecuteAsync(new CompleteRunCommand(f.Ledger.Actor, null, "end", f.Run, AgentRunStatus.Completed, "session"));
        var receipt = await DispatchRecovery.ReconcileAsync(f.Ledger.Service(), input,
            Receipt(input) with { Retention = new(ResultRetentionStatus.Retained, retained) }, null, default);
        Assert.Equal(LedgerRecordingStatus.Recorded, receipt!.CompletionRecording);
        Assert.Equal(AgentRunStatus.Completed, receipt.Completed!.Status);
        Assert.Single((await f.Ledger.StateAsync()).Runs);
    }

    [Fact]
    public async Task RefusedIntentDoesNotRedispatchOnSameBasisAcrossRestart()
    {
        using var f = await CognitiveFixture.OpenAsync(TaskStage.Research);
        var owner = await f.Ledger.Service().AcquireCoordinationAsync(f.Ledger.TaskId, "owner", TimeSpan.FromMinutes(1), default);
        var continuity = new DriverContinuity(f.Ledger.Service(), owner); await continuity.LoadAsync(default);
        var input = new ProviderDispatchRequest(f.Ledger.TaskId, f.Ledger.Actor, new("refused"), "codex") { SubjectActorId = f.Lead, CognitiveWork = CognitiveWorkKind.Recon };
        await continuity.BeginAsync(input, await f.Ledger.StateAsync(), default);
        await continuity.SaveAsync(input, Receipt(input) with { StartRecording = LedgerRecordingStatus.Refused,
            Failure = new(DispatchFailureKind.Refused, "Owning prerequisite diagnostic") }, null, default);
        var restarted = new DriverContinuity(f.Ledger.Service(), owner); await restarted.LoadAsync(default);
        var state = await f.Ledger.StateAsync();
        await Assert.ThrowsAsync<GovernanceException>(() => restarted.BeginAsync(input with { RunId = new("new-run") }, state, default));
        Assert.Single(restarted.Journal.Intents);
    }

    [Fact]
    public async Task ExternalEvidenceAfterBriefingRefusesDelayedRoutingJudgment()
    {
        using var f = await CognitiveFixture.OpenAsync(TaskStage.Research);
        await using var session = await f.Session();
        using var relay = await CognitiveRelay.OpenAsync(session.Endpoint);
        var evidence = await relay.EvidenceAsync();
        await f.Ledger.ExecuteAsync(new AddEvidenceCommand(f.Ledger.Actor, null, "external-change", new("late"), "source", "source:2", "Changed governing evidence", [], []));
        var result = await relay.HandoffAsync(new { kind = "routing_assessment", work = "recon", disposition = "proceed", rationale = "Old interpretation", evidence_ids = new[] { evidence } });
        Assert.Equal("refused", result.GetProperty("status").GetString());
        Assert.Null((await f.Ledger.StateAsync()).Runs[f.Run].RoutingAssessment);
        await relay.FinishAsync();
    }

    [Fact]
    public async Task StaleOwnerCannotWriteThroughStructuredChildRecording()
    {
        using var f = await CognitiveFixture.OpenAsync(TaskStage.Research);
        var state = await f.Ledger.StateAsync();
        var owner = await f.Ledger.Service().AcquireCoordinationAsync(f.Ledger.TaskId, "old", TimeSpan.FromMinutes(1), default);
        var old = f.Ledger.Service().BindCoordination(owner).BindAgentSession(new(f.Ledger.TaskId, state.Roles[f.Lead], f.Run, "codex", DateTimeOffset.UtcNow.AddMinutes(1)));
        await f.Ledger.Service().ReleaseCoordinationAsync(owner, default);
        await f.Ledger.Service().AcquireCoordinationAsync(f.Ledger.TaskId, "new", TimeSpan.FromMinutes(1), default);
        var session = new CognitiveHandoffSession(old, new(f.Ledger.TaskId, f.Lead, f.Run, f.Run.Value));
        Assert.Equal(HostHandoffStatus.Refused, (await session.InvokeAsync(new("stale", new DecisionProposalHandoff("Choice", "Reason", [])), default)).Status);
        Assert.Empty((await f.Ledger.StateAsync()).Decisions);
    }

    [Theory]
    [InlineData("failed")] [InlineData("active")] [InlineData("completed")] [InlineData("foreign-evidence")]
    public async Task TrustedStoppedRecoveryRequiresFailedOrphanAndOperatorTerminationEvidence(string scenario)
    {
        using var f = await CognitiveFixture.OpenAsync(TaskStage.Research);
        var owner = await f.Ledger.Service().AcquireCoordinationAsync(f.Ledger.TaskId, "old-host", TimeSpan.FromMinutes(1), default);
        var continuity = new DriverContinuity(f.Ledger.Service(), owner); await continuity.LoadAsync(default);
        var input = new ProviderDispatchRequest(f.Ledger.TaskId, f.Ledger.Actor, f.Run, "codex")
        { SubjectActorId = f.Lead, CognitiveWork = CognitiveWorkKind.Recon };
        await continuity.BeginAsync(input, await f.Ledger.StateAsync(), default);
        await f.Ledger.Service().ReleaseCoordinationAsync(owner, default);
        var author = scenario == "foreign-evidence" ? f.Lead : f.Ledger.Actor;
        await f.Ledger.ExecuteAsync(new AddEvidenceCommand(author, null, "termination", new("stopped"), "process-termination",
            $"{f.Ledger.TaskId.Value}/{f.Run.Value}", "Fixture confirms provider tree and check processes stopped.", [], []));
        if (scenario != "active") await f.Ledger.ExecuteAsync(new CompleteRunCommand(f.Ledger.Actor, null, "orphan",
            f.Run, scenario == "completed" ? AgentRunStatus.Completed : AgentRunStatus.Failed, "session"));
        if (scenario != "failed")
        {
            await Assert.ThrowsAsync<GovernanceException>(() => TrustedExecutionRecovery.ConfirmStoppedAsync(f.Ledger.Service(),
                f.Ledger.TaskId, f.Ledger.Actor, f.Run, new("stopped"), default));
            return;
        }
        var receipt = await TrustedExecutionRecovery.ConfirmStoppedAsync(f.Ledger.Service(), f.Ledger.TaskId,
            f.Ledger.Actor, f.Run, new("stopped"), default);
        Assert.Equal(AgentRunStatus.Failed, receipt.Completed!.Status);
        Assert.Null(receipt.ProviderResult);
        Assert.Equal(ResultRetentionStatus.NotAttempted, receipt.Retention.Status);
        var replacement = await f.Ledger.Service().AcquireCoordinationAsync(f.Ledger.TaskId, "new-host", TimeSpan.FromMinutes(1), default);
        var resumed = new DriverContinuity(f.Ledger.Service(), replacement); await resumed.LoadAsync(default);
        var next = await resumed.BeginAsync(input with { RunId = new("investigate"), CognitiveWork = CognitiveWorkKind.Findings },
            await f.Ledger.StateAsync(), default);
        Assert.Equal("investigate", next.Request.RunId.Value);
        Assert.Equal(2, resumed.Journal.Intents.Count);
        Assert.Equal(AgentRunStatus.Failed, (await f.Ledger.StateAsync()).Runs[f.Run].Status);
    }

    [Theory]
    [InlineData("evidence")] [InlineData("candidate")]
    public async Task RecoveredJudgmentCannotBeConsumedOnChangedBasis(string change)
    {
        using var f = await CognitiveFixture.OpenAsync(TaskStage.Research);
        var owner = await f.Ledger.Service().AcquireCoordinationAsync(f.Ledger.TaskId, "host", TimeSpan.FromMinutes(1), default);
        var continuity = new DriverContinuity(f.Ledger.Service(), owner); await continuity.LoadAsync(default);
        var input = new ProviderDispatchRequest(f.Ledger.TaskId, f.Ledger.Actor, f.Run, "codex")
        { SubjectActorId = f.Lead, CognitiveWork = CognitiveWorkKind.Findings, RoutingCandidate = "candidate-a" };
        await continuity.BeginAsync(input, await f.Ledger.StateAsync(), default);
        await continuity.SaveAsync(input, Receipt(input) with { Started = new(1, [], AgentRunStatus.Active) }, null, default);
        if (change == "evidence") await f.Ledger.ExecuteAsync(new AddEvidenceCommand(f.Ledger.Actor, null, "changed", new("changed"), "source", "source:2", "Changed basis", [], []));
        var resumed = new DriverContinuity(f.Ledger.Service(), owner); await resumed.LoadAsync(default);
        var dispatch = new DurableDispatch(new MustNotDispatch(), f.Ledger.Service(), resumed);
        await Assert.ThrowsAsync<GovernanceException>(() => dispatch.LaunchAsync(input with
            { RunId = new("replacement"), RoutingCandidate = change == "candidate" ? "candidate-b" : "candidate-a" }, default));
        Assert.Single((await f.Ledger.StateAsync()).Runs);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task HistoricalFaultOrLostFinalCheckpointStopsWithoutCognitiveRedispatch(bool lostCheckpoint)
    {
        using var f = await CognitiveFixture.OpenAsync(TaskStage.Research);
        var host = f.Ledger.Service();
        var owner = await host.AcquireCoordinationAsync(f.Ledger.TaskId, "host", TimeSpan.FromMinutes(1), default);
        var continuity = new DriverContinuity(host, owner); await continuity.LoadAsync(default);
        var input = new ProviderDispatchRequest(f.Ledger.TaskId, f.Ledger.Actor, f.Run, "codex")
            { SubjectActorId = f.Lead, CognitiveWork = CognitiveWorkKind.Recon };
        await continuity.BeginAsync(input, await f.Ledger.StateAsync(), default);
        var result = new AgentRunResult(f.Run, "codex", "session", AgentRunStatus.Failed,
            DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow, 37, null, [], "child startup refused", "fixture", [], false,
            "Input write failed", ProcessFailure: new(ProviderProcessFailureKind.InputWrite, "Broken pipe", 37, true, true, true));
        var path = lostCheckpoint ? await host.BindCoordination(owner).RetainProviderResultAsync(f.Ledger.TaskId, result, default) : null;
        await f.Ledger.ExecuteAsync(new CompleteRunCommand(f.Ledger.Actor, null, "end", f.Run, AgentRunStatus.Failed, "session"));
        var receipt = Receipt(input) with
        {
            Started = new(1, [], AgentRunStatus.Active), Completed = new((await f.Ledger.StateAsync()).Version, [], AgentRunStatus.Failed),
            CompletionRecording = LedgerRecordingStatus.Recorded,
            Retention = new(lostCheckpoint ? ResultRetentionStatus.Retained : ResultRetentionStatus.NotAttempted, path),
            Failure = lostCheckpoint ? null : new(DispatchFailureKind.ProviderFault, "Broken pipe")
        };
        await continuity.SaveAsync(input, receipt, null, default);
        var restarted = new DriverContinuity(host, owner); await restarted.LoadAsync(default);
        var recovered = await new DurableDispatch(new MustNotDispatch(), host, restarted)
            .LaunchAsync(input with { RunId = new("replacement") }, default);
        Assert.Equal(lostCheckpoint ? DispatchFailureKind.ProviderTransport : DispatchFailureKind.ProviderFault, recovered.Failure!.Kind);
        Assert.Equal(f.Run, recovered.RunId);
        Assert.Single((await f.Ledger.StateAsync()).Runs);
        Assert.Single(restarted.Journal.Intents);
    }

    private sealed class MustNotDispatch : IProviderDispatchService
    {
        public Task<ProviderDispatchResult> LaunchAsync(ProviderDispatchRequest request, CancellationToken token) =>
            throw new InvalidOperationException("A changed recovered judgment must not launch a provider.");
    }

    private static AddClaimCommand Claim(FindingsFixture f, string id) => new(f.Actor, null, id, new(id), id, null);
    private static ProviderDispatchResult Receipt(ProviderDispatchRequest input) => new(input.TaskId, input.RunId, DispatchPhase.Completion,
        null, null, null, LedgerRecordingStatus.Recorded, LedgerRecordingStatus.Unknown, new(ResultRetentionStatus.NotAttempted), null, null, null, null, AssurancePreparationStatus.NotConfigured);
    private sealed class CoordinationClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        internal void Advance(TimeSpan duration) => _now += duration;
    }
    private sealed class LoseReply(IGovernedTaskService inner) : IGovernedTaskService
    {
        public async Task<CommandOutcome> ExecuteAsync(TaskId task, LedgerCommand command, CancellationToken token)
        { await inner.ExecuteAsync(task, command, token); throw new IOException("Committed; reply lost"); }
        public Task<GovernedTaskState?> GetStateAsync(TaskId task, CancellationToken token) => inner.GetStateAsync(task, token);
        public IAsyncEnumerable<LedgerEvent> GetHistoryAsync(TaskId task, CancellationToken token) => inner.GetHistoryAsync(task, token);
    }
}
