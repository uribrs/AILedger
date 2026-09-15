using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Escalations;

public sealed class EscalationRulesTests
{
    [Fact]
    public void BusinessDecisionRequiresAtLeastTwoOptionsAndARecommendationNamingOne()
    {
        var task = EscalationCommands.PreparePlanningTask(out var lead);

        Assert.Throws<GovernanceException>(() => EscalationCommands.Raise(
            task, lead, "X1", EscalationKind.BusinessDecision,
            options: ["only-one"], recommendation: "only-one"));
        Assert.Throws<GovernanceException>(() => EscalationCommands.Raise(
            task, lead, "X2", EscalationKind.BusinessDecision,
            options: ["a", "b"], recommendation: null));
        Assert.Throws<GovernanceException>(() => EscalationCommands.Raise(
            task, lead, "X3", EscalationKind.BusinessDecision,
            options: ["a", "b"], recommendation: "c"));

        EscalationCommands.Raise(
            task, lead, "X4", EscalationKind.BusinessDecision,
            question: " Which way? ", options: [" a ", " b "], recommendation: " b ");

        var escalation = task.State.Escalations[new EscalationId("X4")];
        Assert.Equal(EscalationStatus.Open, escalation.Status);
        Assert.Equal("Which way?", escalation.Question);
        Assert.Equal(["a", "b"], escalation.Options);
        Assert.Equal("b", escalation.Recommendation);
    }

    [Fact]
    public void TrueUnknownRequiresEvidenceOfTheAttemptThatFailedToAnswerIt()
    {
        var task = EscalationCommands.PreparePlanningTask(out var lead);

        Assert.Throws<GovernanceException>(() => EscalationCommands.Raise(
            task, lead, "X1", EscalationKind.TrueUnknown));

        task.Apply(new AddEvidenceCommand(lead, null, task.NextCorrelation(), new EvidenceId("E1"),
            "local-probe", "grep over src", "The credential is not present anywhere in the repository", [], []));
        EscalationCommands.Raise(
            task, lead, "X2", EscalationKind.TrueUnknown, evidence: [new EvidenceId("E1")]);

        Assert.Equal(EscalationKind.TrueUnknown, task.State.Escalations[new EscalationId("X2")].Kind);
    }

    [Fact]
    public void OnlyAnOperatorResolvesAnEscalationAndResolutionRecordsTheAnswer()
    {
        var task = EscalationCommands.PreparePlanningTask(out var lead);
        EscalationCommands.Raise(
            task, lead, "X1", EscalationKind.BusinessDecision,
            options: ["a", "b"], recommendation: "a");

        // The lead holds RaiseEscalation but never ResolveEscalation.
        Assert.Throws<GovernanceException>(() => task.Apply(new ResolveEscalationCommand(
            lead, null, task.NextCorrelation(), new EscalationId("X1"), EscalationStatus.Resolved, "pick a")));

        Assert.Throws<GovernanceException>(() => task.Apply(new ResolveEscalationCommand(
            task.OperatorId, null, task.NextCorrelation(), new EscalationId("X1"), EscalationStatus.Resolved, null)));

        task.Apply(new ResolveEscalationCommand(
            task.OperatorId, null, task.NextCorrelation(), new EscalationId("X1"),
            EscalationStatus.Resolved, "pick a"));

        var escalation = task.State.Escalations[new EscalationId("X1")];
        Assert.Equal(EscalationStatus.Resolved, escalation.Status);
        Assert.Equal("pick a", escalation.Resolution);
        Assert.Equal(task.OperatorId, escalation.ResolvedBy);
    }

    [Fact]
    public void AnOperatorMayWithdrawAnEscalationWithoutAnAnswer()
    {
        var task = EscalationCommands.PreparePlanningTask(out var lead);
        EscalationCommands.Raise(
            task, lead, "X1", EscalationKind.BusinessDecision,
            options: ["a", "b"], recommendation: "a");

        task.Apply(new ResolveEscalationCommand(
            task.OperatorId, null, task.NextCorrelation(), new EscalationId("X1"),
            EscalationStatus.Withdrawn, null));

        var escalation = task.State.Escalations[new EscalationId("X1")];
        Assert.Equal(EscalationStatus.Withdrawn, escalation.Status);
        Assert.Null(escalation.Resolution);
        Assert.Equal(task.OperatorId, escalation.ResolvedBy);
    }
}
