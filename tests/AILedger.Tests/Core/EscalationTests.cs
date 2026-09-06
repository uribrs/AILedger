using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

public sealed class EscalationTests
{
    [Fact]
    public void BusinessDecisionRequiresAtLeastTwoOptionsAndARecommendationNamingOne()
    {
        var task = Prepare(out var lead);

        Assert.Throws<GovernanceException>(() => Raise(task, lead, "X1", EscalationKind.BusinessDecision,
            options: ["only-one"], recommendation: "only-one"));
        Assert.Throws<GovernanceException>(() => Raise(task, lead, "X2", EscalationKind.BusinessDecision,
            options: ["a", "b"], recommendation: null));
        Assert.Throws<GovernanceException>(() => Raise(task, lead, "X3", EscalationKind.BusinessDecision,
            options: ["a", "b"], recommendation: "c"));

        Raise(task, lead, "X4", EscalationKind.BusinessDecision, options: ["a", "b"], recommendation: "b");

        var escalation = task.State.Escalations[new EscalationId("X4")];
        Assert.Equal(EscalationStatus.Open, escalation.Status);
        Assert.Equal("b", escalation.Recommendation);
    }

    [Fact]
    public void TrueUnknownRequiresEvidenceOfTheAttemptThatFailedToAnswerIt()
    {
        var task = Prepare(out var lead);

        Assert.Throws<GovernanceException>(() => Raise(task, lead, "X1", EscalationKind.TrueUnknown));

        task.Apply(new AddEvidenceCommand(lead, null, task.NextCorrelation(), new EvidenceId("E1"),
            "local-probe", "grep over src", "The credential is not present anywhere in the repository", [], []));
        Raise(task, lead, "X2", EscalationKind.TrueUnknown, evidence: [new EvidenceId("E1")]);

        Assert.Equal(EscalationKind.TrueUnknown, task.State.Escalations[new EscalationId("X2")].Kind);
    }

    [Fact]
    public void OnlyAnOperatorResolvesAnEscalationAndResolutionRecordsTheAnswer()
    {
        var task = Prepare(out var lead);
        Raise(task, lead, "X1", EscalationKind.BusinessDecision, options: ["a", "b"], recommendation: "a");

        // The lead holds RaiseEscalation but never ResolveEscalation.
        Assert.Throws<GovernanceException>(() => task.Apply(new ResolveEscalationCommand(
            lead, null, task.NextCorrelation(), new EscalationId("X1"), EscalationStatus.Resolved, "pick a")));

        Assert.Throws<GovernanceException>(() => task.Apply(new ResolveEscalationCommand(
            task.OperatorId, null, task.NextCorrelation(), new EscalationId("X1"), EscalationStatus.Resolved, null)));

        task.Apply(new ResolveEscalationCommand(
            task.OperatorId, null, task.NextCorrelation(), new EscalationId("X1"), EscalationStatus.Resolved, "pick a"));

        var escalation = task.State.Escalations[new EscalationId("X1")];
        Assert.Equal(EscalationStatus.Resolved, escalation.Status);
        Assert.Equal("pick a", escalation.Resolution);
        Assert.Equal(task.OperatorId, escalation.ResolvedBy);
    }

    internal static TestTask Prepare(out ActorId lead)
    {
        var task = new TestTask();
        lead = new ActorId("planning-lead");
        task.Assign(lead, RoleKind.PlanningLead,
            Capability.AddClaim, Capability.AddEvidence, Capability.RaiseEscalation,
            Capability.RecordAlternative, Capability.BuildContext);
        return task;
    }

    internal static void Raise(
        TestTask task,
        ActorId actor,
        string id,
        EscalationKind kind,
        string question = "Which way?",
        WorkItemId? workItemId = null,
        IReadOnlyList<string>? options = null,
        string? recommendation = null,
        IReadOnlyList<EvidenceId>? evidence = null) =>
        task.Apply(new RaiseEscalationCommand(
            actor, null, task.NextCorrelation(), new EscalationId(id), kind, question, workItemId,
            options ?? [], recommendation, evidence ?? []));
}
