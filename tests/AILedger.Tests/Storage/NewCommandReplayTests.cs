using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Storage;

public sealed class NewCommandReplayTests
{
    // R1 (dual-kernel-rule-drift): every rule in this kernel is written twice — once in
    // CommandHandler, once in TaskTransitionValidator, which re-validates at replay. This drives
    // the real file store and then replays through a *fresh* service, so the two copies must
    // agree on real events, not on a test double's approximation of them.
    [Fact]
    public async Task R1_EveryNewCommandCommitsAndReplaysThroughTheFileStore()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("replay-task");
        var actor = new ActorId("operator");
        string Area(string name) => Directory.CreateDirectory(Path.Combine(root.Path, name)).FullName;
        var writer = Service(root.Path);

        await Run(writer, taskId, new OpenTaskCommand(actor, null, "c1", taskId, "Task", "Goal"));
        await Run(writer, taskId, new AddWorkItemCommand(actor, null, "c2", new WorkItemId("W1"), "Blocked work", actor, [], [Area("w1")]));
        await Run(writer, taskId, new AddWorkItemCommand(actor, null, "c3", new WorkItemId("W2"), "Finished work", actor, [], [Area("w2")]));
        await Run(writer, taskId, new AddConstraintCommand(actor, null, "c4", new ConstraintId("K1"), "Stay local", "dossier", [Area("k1")]));
        await Run(writer, taskId, new AddConstraintCommand(actor, null, "c5", new ConstraintId("K2"), "Old rule", "draft", []));
        await Run(writer, taskId, new SupersedeConstraintCommand(actor, null, "c6", new ConstraintId("K2")));
        await Run(writer, taskId, new AddEvidenceCommand(actor, null, "c7", new EvidenceId("E1"), "local-probe", "grep -r", "No answer in the repository", [], []));
        await Run(writer, taskId, new RaiseEscalationCommand(actor, null, "c8", new EscalationId("X1"), EscalationKind.TrueUnknown, "Which account owns this?", null, [], null, [new EvidenceId("E1")]));
        await Run(writer, taskId, new ResolveEscalationCommand(actor, null, "c9", new EscalationId("X1"), EscalationStatus.Resolved, "The platform account"));
        await Run(writer, taskId, new RaiseEscalationCommand(actor, null, "c10", new EscalationId("X2"), EscalationKind.BusinessDecision, "Ship now or harden first?", new WorkItemId("W1"), ["ship", "harden"], "ship", []));
        await Run(writer, taskId, new RecordAlternativeCommand(actor, null, "c11", new AlternativeId("ALT1"), "Use a database", "Files stay inspectable", null));
        await Run(writer, taskId, new BlockWorkItemCommand(actor, null, "c12", new WorkItemId("W1"), "Waiting on the operator", new EscalationId("X2")));
        await Run(writer, taskId, new AddWorkItemCommand(actor, null, "c12b", new WorkItemId("W3"), "Unblocked work", actor, [], [Area("w3")]));
        await Run(writer, taskId, new BlockWorkItemCommand(actor, null, "c12c", new WorkItemId("W3"), "Paused by the operator", null));
        await Run(writer, taskId, new UnblockWorkItemCommand(actor, null, "c12d", new WorkItemId("W3")));

        // A7: the lifecycle claim is about what a *real* run completion does to a work item, so
        // this drives run.start/run.complete through the file store rather than a fake adapter.
        await Run(writer, taskId, new StartRunCommand(actor, null, "c13", new RunId("R1"), new WorkItemId("W2"), "codex", null));
        await Run(writer, taskId, new CompleteRunCommand(actor, null, "c14", new RunId("R1"), AgentRunStatus.Completed, "session-1"));
        var afterRun = await Service(root.Path).GetStateAsync(taskId, CancellationToken.None);
        Assert.Equal(WorkItemStatus.Paused, afterRun?.WorkItems[new WorkItemId("W2")].Status);

        await Run(writer, taskId, new CompleteWorkItemCommand(actor, null, "c15", new WorkItemId("W2")));

        // A separate service instance models a separate process: nothing but events.jsonl carries over.
        var replayed = await Service(root.Path).GetStateAsync(taskId, CancellationToken.None);

        Assert.NotNull(replayed);
        Assert.Equal(ConstraintStatus.Active, replayed.Constraints[new ConstraintId("K1")].Status);
        Assert.Equal(ConstraintStatus.Superseded, replayed.Constraints[new ConstraintId("K2")].Status);
        Assert.Equal(EscalationStatus.Resolved, replayed.Escalations[new EscalationId("X1")].Status);
        Assert.Equal("The platform account", replayed.Escalations[new EscalationId("X1")].Resolution);
        Assert.Equal(EscalationStatus.Open, replayed.Escalations[new EscalationId("X2")].Status);
        Assert.Equal("ship", replayed.Escalations[new EscalationId("X2")].Recommendation);
        Assert.Equal("Files stay inspectable", replayed.Alternatives[new AlternativeId("ALT1")].RejectionRationale);
        Assert.Equal(WorkItemStatus.Blocked, replayed.WorkItems[new WorkItemId("W1")].Status);
        Assert.Equal("Waiting on the operator", replayed.WorkItems[new WorkItemId("W1")].BlockReason);
        Assert.Equal(WorkItemStatus.Completed, replayed.WorkItems[new WorkItemId("W2")].Status);
        Assert.Equal(WorkItemStatus.Paused, replayed.WorkItems[new WorkItemId("W3")].Status);
        Assert.Null(replayed.WorkItems[new WorkItemId("W3")].BlockReason);

        var projection = await File.ReadAllTextAsync(Path.Combine(root.Path, taskId.Value, "task.md"));
        Assert.Contains("Stay local", projection, StringComparison.Ordinal);
        Assert.Contains("Ship now or harden first?", projection, StringComparison.Ordinal);
        Assert.DoesNotContain("Old rule", projection, StringComparison.Ordinal);
        var decisions = await File.ReadAllTextAsync(Path.Combine(root.Path, taskId.Value, "decisions.md"));
        Assert.Contains("Use a database", decisions, StringComparison.Ordinal);
    }

    // A3: a log written before the state model grew must still replay. The new dictionaries are
    // appended, so an older events.jsonl carries no new event types and lands on empty collections.
    [Fact]
    public async Task AnEventLogWithoutTheNewEventTypesStillReplays()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("legacy-task");
        var actor = new ActorId("operator");
        var writer = Service(root.Path);
        await Run(writer, taskId, new OpenTaskCommand(actor, null, "c1", taskId, "Task", "Goal"));
        await Run(writer, taskId, new AddClaimCommand(actor, null, "c2", new ClaimId("C1"), "Still true", null));

        var eventsPath = Path.Combine(root.Path, taskId.Value, "events.jsonl");
        var lines = await File.ReadAllLinesAsync(eventsPath);
        File.Delete(Path.Combine(root.Path, taskId.Value, "state.json"));

        var replayed = await Service(root.Path).GetStateAsync(taskId, CancellationToken.None);

        Assert.NotNull(replayed);
        Assert.All(lines, line => Assert.DoesNotContain("escalation.", line, StringComparison.Ordinal));
        Assert.Equal("Still true", replayed.Claims[new ClaimId("C1")].Statement);
        Assert.Empty(replayed.Escalations);
        Assert.Empty(replayed.Alternatives);
        Assert.Empty(replayed.Constraints);
    }

    // R4 (refinement-repoint-replay-parity): the refinement derivation lives in both rule copies.
    // This drives both supersession outcomes and a supported challenge through the real store,
    // then reads state back from a fresh service so the replay copy has to agree.
    [Fact]
    public async Task R4_SupersessionAndChallengeConsequencesReplayThroughTheFileStore()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("supersession-task");
        var actor = new ActorId("operator");
        string Area(string name) => Directory.CreateDirectory(Path.Combine(root.Path, name)).FullName;
        var writer = Service(root.Path);

        await Run(writer, taskId, new OpenTaskCommand(actor, null, "s1", taskId, "Task", "Goal"));
        await Run(writer, taskId, new AddClaimCommand(actor, null, "s2", new ClaimId("C1"), "The API is stable", null));
        await Run(writer, taskId, new AddClaimCommand(actor, null, "s3", new ClaimId("C2"), "The API is stable below 200 rps", null));
        await Run(writer, taskId, new AddEvidenceCommand(actor, null, "s4", new EvidenceId("E2"), "probe", "cite", "supports C2", [new ClaimId("C2")], []));
        await Run(writer, taskId, new AddWorkItemCommand(actor, null, "s5", new WorkItemId("W1"), "Build it", actor, [new ClaimId("C1")], [Area("w1")]));
        await Run(writer, taskId, new ResolveClaimCommand(actor, null, "s6", new ClaimId("C2"), ClaimStatus.Validated, [new EvidenceId("E2")]));
        // Earned refinement: C2 is validated and nothing refutes C1.
        await Run(writer, taskId, new ResolveClaimCommand(actor, null, "s7", new ClaimId("C1"), ClaimStatus.Superseded, [], new ClaimId("C2")));

        // A correction on a second chain, driven by refuting evidence.
        await Run(writer, taskId, new AddClaimCommand(actor, null, "s8", new ClaimId("C3"), "Old belief", null));
        await Run(writer, taskId, new AddClaimCommand(actor, null, "s9", new ClaimId("C4"), "Replacement belief", null));
        await Run(writer, taskId, new AddEvidenceCommand(actor, null, "s10", new EvidenceId("E3"), "probe", "cite", "refutes C3", [], [new ClaimId("C3")]));
        await Run(writer, taskId, new AddWorkItemCommand(actor, null, "s11", new WorkItemId("W2"), "Other work", actor, [new ClaimId("C3")], [Area("w2")]));
        await Run(writer, taskId, new ResolveClaimCommand(actor, null, "s12", new ClaimId("C3"), ClaimStatus.Superseded, [], new ClaimId("C4")));

        // A supported challenge against a decision, whose consequence is a distinct event type.
        await Run(writer, taskId, new ProposeDecisionCommand(actor, null, "s13", new DecisionId("D1"), "Use it", "Because C2", [new ClaimId("C2")], null));
        await Run(writer, taskId, new AddEvidenceCommand(actor, null, "s13b", new EvidenceId("E4"), "probe", "cite", "the decision does not hold", [], []));
        await Run(writer, taskId, new RaiseChallengeCommand(actor, null, "s14", new ChallengeId("CH1"), "decision", "D1", "It does not hold", [new EvidenceId("E4")]));
        await Run(writer, taskId, new DisposeChallengeCommand(actor, null, "s15", new ChallengeId("CH1"), ChallengeStatus.Supported));

        var replayed = await Service(root.Path).GetStateAsync(taskId, CancellationToken.None);

        Assert.NotNull(replayed);
        Assert.Equal(new ClaimId("C2"), replayed.Claims[new ClaimId("C1")].SupersededByClaimId);
        Assert.Equal([new ClaimId("C2")], replayed.WorkItems[new WorkItemId("W1")].DependsOnClaims);
        Assert.Equal(WorkItemStatus.Proposed, replayed.WorkItems[new WorkItemId("W1")].Status);
        Assert.Equal([new ClaimId("C3")], replayed.WorkItems[new WorkItemId("W2")].DependsOnClaims);
        Assert.Equal(WorkItemStatus.Stale, replayed.WorkItems[new WorkItemId("W2")].Status);
        Assert.Equal(DecisionStatus.Invalidated, replayed.Decisions[new DecisionId("D1")].Status);

        var assumptions = await File.ReadAllTextAsync(Path.Combine(root.Path, taskId.Value, "assumptions.md"));
        Assert.Contains("Superseded by `C2`", assumptions, StringComparison.Ordinal);
    }

    private static FileGovernedTaskService Service(string root)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(root, new CommandHandler(reducer, new AuthorizationPolicy()), reducer);
    }

    private static Task<CommandOutcome> Run(IGovernedTaskService service, TaskId taskId, LedgerCommand command) =>
        service.ExecuteAsync(taskId, command, CancellationToken.None);
}
