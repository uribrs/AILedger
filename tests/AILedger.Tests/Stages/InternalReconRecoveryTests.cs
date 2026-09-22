using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Artifacts.Recon;

namespace AILedger.Tests.Stages;

public sealed class InternalReconRecoveryTests
{
    [Theory]
    [InlineData(AgentRunStatus.Completed)]
    [InlineData(AgentRunStatus.Failed)]
    [InlineData(AgentRunStatus.Cancelled)]
    public void R2_ResolvedInternalRecoveryRequiresFreshCompletedCurrentProducer(AgentRunStatus status)
    {
        var f = new InternalReconFixture();
        f.Eligible();
        f.Task.Transition(TaskStage.Design);
        f.Task.RecordPromptContract();
        f.Task.Transition(TaskStage.Scope);
        f.Resolve();
        Assert.Throws<GovernanceException>(() => f.Task.Transition(TaskStage.Research));
        foreach (var reason in new string?[] { null, "", " " })
            Assert.Throws<GovernanceException>(() => f.Task.Apply(new RequestStageTransitionCommand(
                f.Task.OperatorId, null, f.Task.NextCorrelation(), TaskStage.Design, Reason: reason)));
        f.Task.Transition(TaskStage.Design);
        Assert.Throws<GovernanceException>(() => f.Task.Transition(TaskStage.Scope));
        f.Start("refresh");
        f.File(id: "fresh", supersedes: "recon");
        Assert.Throws<GovernanceException>(() => f.Task.Transition(TaskStage.Scope));
        f.Complete(status);
        if (status == AgentRunStatus.Completed)
            f.Task.Transition(TaskStage.Scope);
        else
            Assert.Throws<GovernanceException>(() => f.Task.Transition(TaskStage.Scope));
        Assert.DoesNotContain(f.Task.State.Runs.Values, r => r.SubjectRole == RoleKind.Researcher);
        Assert.DoesNotContain(f.Task.State.Claims.Values, c => c.Status == ClaimStatus.Open);
        GovernedTaskState? replay = null;
        foreach (var e in f.Task.Events) replay = new TaskReducer().Apply(replay, e);
        Assert.Equal(f.Task.State.Stage, replay!.Stage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void R1_ExternalRecoveryNeedsResearchAndRefreshedRecon(bool resolveBeforeRetreat)
    {
        var f = new InternalReconFixture();
        f.Eligible();
        f.Task.Transition(TaskStage.Design);
        f.Task.RecordPromptContract();
        f.Task.Transition(TaskStage.Scope);
        f.Resolve();
        if (!resolveBeforeRetreat)
            f.Task.Apply(new AddClaimCommand(f.Task.OperatorId, null, f.Task.NextCorrelation(),
                new ClaimId("new-external"), "New external dependency", null));
        f.Task.Transition(TaskStage.Design);
        f.Start("external");
        f.File(f.Body("external"), "external", "recon");
        f.Complete();
        Assert.Throws<GovernanceException>(() => f.Task.Transition(TaskStage.Scope));
        f.Task.Transition(TaskStage.Research);
        Assert.Throws<GovernanceException>(() => f.Task.Transition(TaskStage.Design));
        f.Task.RecordResearcherPass();
        if (!resolveBeforeRetreat)
        {
            Assert.Throws<GovernanceException>(() => f.Task.Transition(TaskStage.Design));
            var claim = new ClaimId("new-external");
            var evidence = new EvidenceId("new-proof");
            f.Task.Apply(new AddEvidenceCommand(f.Task.OperatorId, null, f.Task.NextCorrelation(),
                evidence, "test-run", "ExternalProbe", "External result", [claim], []));
            f.Task.Apply(new ResolveClaimCommand(f.Task.OperatorId, null, f.Task.NextCorrelation(),
                claim, ClaimStatus.Validated, [evidence]));
        }
        f.Start("refresh");
        f.File(f.Body("external"), "fresh", "external");
        f.Complete();
        f.Task.Transition(TaskStage.Design);
        f.Task.Transition(TaskStage.Scope);
        Assert.Equal(TaskStage.Scope, f.Task.State.Stage);
    }

    [Fact]
    public void R1_ForwardScopeStillRequiresApproachAfterBackwardDesignEntry()
    {
        var f = new InternalReconFixture(approach: false);
        f.Eligible();
        Assert.Throws<GovernanceException>(() => f.Task.Transition(TaskStage.Design));
        // Legacy events are replay-compatible; no modern admission waiver is acceptance evidence.
        var prefix = f.Task.Events.ToList();
        var last = prefix.Last();
        prefix.Add(last with { EventId = new EventId("old-design"), Data =
            new StageTransitioned(TaskStage.Research, TaskStage.Design) });
        prefix.Add(last with { EventId = new EventId("old-scope"), Data =
            new StageTransitioned(TaskStage.Design, TaskStage.Scope) });
        GovernedTaskState? state = null;
        foreach (var e in prefix) state = new TaskReducer().Apply(state, e);
        var handler = new AILedger.Core.Application.CommandHandler(
            new TaskReducer(), new AuthorizationPolicy());
        state = handler.Handle(state!,
            new RequestStageTransitionCommand(f.Task.OperatorId, null, "retreat", TaskStage.Design,
                Reason: "Refresh legacy prerequisites"), DateTimeOffset.UtcNow).State;
        var error = Assert.Throws<GovernanceException>(() => handler.Handle(state!,
            new RequestStageTransitionCommand(f.Task.OperatorId, null, "exit", TaskStage.Scope),
            DateTimeOffset.UtcNow));
        Assert.Contains("alternative", error.Message, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void R4_LegacyEmptyTaskRecoversThroughFirstReconAndReplays()
    {
        var task = new AILedger.Tests.Support.TestTask(placeEntryStages: false);
        var events = task.Events.ToList();
        var opening = events.Last();
        foreach (var edge in new[] { (TaskStage.Discovery, TaskStage.Research),
                     (TaskStage.Research, TaskStage.Design), (TaskStage.Design, TaskStage.Scope) })
            events.Add(opening with { EventId = new EventId($"legacy-{edge.Item2}"),
                Data = new StageTransitioned(edge.Item1, edge.Item2) });
        var reducer = new TaskReducer();
        GovernedTaskState? state = null;
        foreach (var e in events) state = reducer.Apply(state, e);
        var handler = new AILedger.Core.Application.CommandHandler(reducer,
            new AuthorizationPolicy());
        var sequence = 0;
        void Apply(LedgerCommand command)
        {
            var result = handler.Handle(state!, command, DateTimeOffset.UtcNow);
            state = result.State;
            events.AddRange(result.Events);
        }
        string Next() => $"recovery-{++sequence}";
        RequestStageTransitionCommand Move(TaskStage stage) =>
            new(task.OperatorId, null, Next(), stage,
                Reason: stage < state!.Stage ? "Refresh historical prerequisites" : null);
        Apply(Move(TaskStage.Design));
        Assert.Throws<GovernanceException>(() => Apply(Move(TaskStage.Scope)));
        Apply(Move(TaskStage.Research)); // No fabricated Open claim for the empty set.
        Assert.Throws<GovernanceException>(() => Apply(Move(TaskStage.Design)));
        var run = new RunId("empty-recon");
        Apply(new StartRunCommand(task.OperatorId, null, Next(), run, null, "codex", null));
        var template = InternalReconDocuments.CreateTemplate(state!);
        var body = System.Text.Json.JsonSerializer.Serialize(template with { Report = "Empty claim set inspected." });
        Apply(AILedger.Tests.Support.ArtifactCommands.Record(task, task.OperatorId, "first-recon",
            GovernedArtifactKind.InternalRecon, body, producerRun: run));
        Apply(new CompleteRunCommand(task.OperatorId, null, Next(), run, AgentRunStatus.Completed, "session"));
        Apply(new RecordAlternativeCommand(task.OperatorId, null, Next(), new AlternativeId("alt"),
            "Skip recon", "Legacy recovery still needs current proof", null));
        Apply(Move(TaskStage.Design));
        Assert.Empty(state!.Claims);
        Apply(new StartRunCommand(task.OperatorId, null, Next(), new RunId("contract"), null, "codex", null));
        Apply(AILedger.Tests.Support.ArtifactCommands.Record(task, task.OperatorId, "contract",
            GovernedArtifactKind.PromptContract, producerRun: new RunId("contract")));
        Apply(new CompleteRunCommand(task.OperatorId, null, Next(), new RunId("contract"),
            AgentRunStatus.Completed, "contract-session"));
        Apply(Move(TaskStage.Scope));
        GovernedTaskState? replay = null;
        foreach (var e in events) replay = reducer.Apply(replay, e);
        Assert.Equal(TaskStage.Scope, replay!.Stage);
        Assert.Empty(replay.Claims);
    }

}
