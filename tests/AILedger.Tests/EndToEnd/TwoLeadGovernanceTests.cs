using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.EndToEnd;

public sealed class TwoLeadGovernanceTests
{
    [Fact]
    public async Task TwoLeadsCoordinateThroughDurableTaskStateRatherThanTranscript()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("two-lead-task");
        var operatorId = new ActorId("operator");
        var planningLead = new ActorId("codex-planning");
        var implementationLead = new ActorId("claude-implementation");
        var writer = Service(root.Path);

        await Execute(writer, taskId, new OpenTaskCommand(operatorId, null, "open", taskId, "Two leads", "Share governed truth"));
        await Execute(writer, taskId, new AssignRoleCommand(operatorId, null, "attach-codex", planningLead, RoleKind.PlanningLead,
            [Capability.AddClaim, Capability.ProposeDecision, Capability.BuildContext]));
        await Execute(writer, taskId, new AssignRoleCommand(operatorId, null, "attach-claude", implementationLead, RoleKind.ImplementationLead,
            [Capability.AddEvidence, Capability.RaiseChallenge, Capability.BuildContext]));
        await Execute(writer, taskId, new AddClaimCommand(planningLead, null, "codex-claim", new ClaimId("C1"), "The proposed API is stable", "Implementation may fail"));
        await Execute(writer, taskId, new ProposeDecisionCommand(planningLead, null, "codex-decision", new DecisionId("D1"), "Use proposed API", "Based on current evidence", [new ClaimId("C1")], null));

        // A new service instance models a separate provider process: no transcript or in-memory state is shared.
        var independentLeadProcess = Service(root.Path);
        await Execute(independentLeadProcess, taskId, new AddEvidenceCommand(implementationLead, null, "claude-evidence", new EvidenceId("E1"), "local probe", "cli --help", "Required switch is absent", [], [new ClaimId("C1")]));
        await Execute(independentLeadProcess, taskId, new RaiseChallengeCommand(implementationLead, null, "claude-challenge", new ChallengeId("CH1"), "decision", "D1", "Evidence refutes its dependency", [new EvidenceId("E1")]));

        var state = await Service(root.Path).GetStateAsync(taskId, CancellationToken.None);
        Assert.NotNull(state);
        Assert.Equal(RoleKind.PlanningLead, state.Roles[planningLead].Role);
        Assert.Equal(RoleKind.ImplementationLead, state.Roles[implementationLead].Role);
        Assert.Equal(planningLead, state.Claims[new ClaimId("C1")].Provenance.ActorId);
        Assert.Equal(implementationLead, state.Evidence[new EvidenceId("E1")].Provenance.ActorId);
        Assert.Equal(ChallengeStatus.Open, state.Challenges[new ChallengeId("CH1")].Status);

        var planningContext = new ContextAssembler().Build(state, planningLead, null, [], DateTimeOffset.UnixEpoch);
        var implementationContext = new ContextAssembler().Build(state, implementationLead, null, [], DateTimeOffset.UnixEpoch);
        Assert.Contains(planningContext.Artifacts, item => item.Id == "E1");
        Assert.Contains(implementationContext.Artifacts, item => item.Id == "D1");
    }

    private static FileGovernedTaskService Service(string root) =>
        new(root, new CommandHandler(), new TaskReducer());

    private static async Task Execute(IGovernedTaskService service, TaskId taskId, LedgerCommand command) =>
        await service.ExecuteAsync(taskId, command, CancellationToken.None);
}
