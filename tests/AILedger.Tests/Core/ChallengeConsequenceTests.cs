using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

public sealed class ChallengeConsequenceTests
{
    [Fact]
    public void SupportingAChallengeAgainstADecisionOverturnsIt()
    {
        var task = Prepare();
        task.Apply(new ProposeDecisionCommand(task.OperatorId, null, task.NextCorrelation(),
            new DecisionId("D1"), "Use it", "Because C1", [new ClaimId("C1")], null));
        Raise(task, "CH1", "decision", "D1", [new EvidenceId("E-refutes")]);

        task.Apply(Dispose(task, "CH1", ChallengeStatus.Supported));

        Assert.Equal(DecisionStatus.Invalidated, task.State.Decisions[new DecisionId("D1")].Status);
    }

    [Fact]
    public void SupportingAChallengeAgainstWorkBlocksItWithAReasonNamingTheChallenge()
    {
        var task = Prepare();
        task.Apply(new AddWorkItemCommand(task.OperatorId, null, task.NextCorrelation(),
            new WorkItemId("W1"), "Build it", task.OperatorId, [], [Path.GetFullPath("src")]));
        Raise(task, "CH1", "work", "W1", [new EvidenceId("E-refutes")]);

        task.Apply(Dispose(task, "CH1", ChallengeStatus.Supported));

        var workItem = task.State.WorkItems[new WorkItemId("W1")];
        Assert.Equal(WorkItemStatus.Blocked, workItem.Status);
        Assert.Contains("CH1", workItem.BlockReason!, StringComparison.Ordinal);
    }

    [Fact]
    public void SupportingAChallengeAgainstAClaimRejectsItAndCascadesToDependentWork()
    {
        var task = Prepare();
        task.Apply(new AddWorkItemCommand(task.OperatorId, null, task.NextCorrelation(),
            new WorkItemId("W1"), "Build it", task.OperatorId, [new ClaimId("C1")], [Path.GetFullPath("src")]));
        Raise(task, "CH1", "claim", "C1", [new EvidenceId("E-refutes")]);

        task.Apply(Dispose(task, "CH1", ChallengeStatus.Supported));

        Assert.Equal(ClaimStatus.Rejected, task.State.Claims[new ClaimId("C1")].Status);
        Assert.Equal(WorkItemStatus.Stale, task.State.WorkItems[new WorkItemId("W1")].Status);
    }

    [Fact]
    public void AClaimChallengeCannotBeSupportedUnlessItsEvidenceRefutesTheClaim()
    {
        var task = Prepare();
        Raise(task, "CH1", "claim", "C1", [new EvidenceId("E-supports")]);

        var error = Assert.Throws<GovernanceException>(() => task.Apply(Dispose(task, "CH1", ChallengeStatus.Supported)));

        Assert.Contains("refutes that claim", error.Message, StringComparison.Ordinal);
        Assert.Equal(ChallengeStatus.Open, task.State.Challenges[new ChallengeId("CH1")].Status);
    }

    [Fact]
    public void RejectedAndWithdrawnDispositionsStillEmitOnlyTheDisposalEvent()
    {
        var task = Prepare();
        Raise(task, "CH1", "claim", "C1", [new EvidenceId("E-refutes")]);

        var outcome = task.Apply(Dispose(task, "CH1", ChallengeStatus.Rejected));

        Assert.Single(outcome.Events);
        Assert.Equal(ClaimStatus.Open, task.State.Claims[new ClaimId("C1")].Status);
    }

    // R1 (challenge-capability-replay-trap): the consequence event is authorised at replay against
    // its own capability, so an actor lacking it must be refused at command time or the task
    // becomes permanently unreplayable.
    [Fact]
    public void R1_SupportingAChallengeRequiresTheConsequenceCapabilityAtCommandTime()
    {
        var task = Prepare();
        var disposer = new ActorId("disposer");
        task.Assign(disposer, RoleKind.PlanningLead,
            Capability.RaiseChallenge, Capability.DisposeChallenge, Capability.AddEvidence);
        Raise(task, "CH1", "claim", "C1", [new EvidenceId("E-refutes")]);

        var error = Assert.Throws<GovernanceException>(() => task.Apply(new DisposeChallengeCommand(
            disposer, null, task.NextCorrelation(), new ChallengeId("CH1"), ChallengeStatus.Supported)));

        Assert.Contains("ResolveClaim", error.Message, StringComparison.Ordinal);
        Assert.Equal(ChallengeStatus.Open, task.State.Challenges[new ChallengeId("CH1")].Status);
    }

    // R3 (claim-evidence-overwrite): the reducer used to replace the claim's whole evidence list.
    [Fact]
    public void R3_ChallengeRejectionPreservesEvidenceAlreadyOnTheClaim()
    {
        var task = Prepare();
        task.Apply(new ResolveClaimCommand(task.OperatorId, null, task.NextCorrelation(),
            new ClaimId("C1"), ClaimStatus.Validated, [new EvidenceId("E-supports")]));
        Raise(task, "CH1", "claim", "C1", [new EvidenceId("E-refutes")]);

        task.Apply(Dispose(task, "CH1", ChallengeStatus.Supported));

        var claim = task.State.Claims[new ClaimId("C1")];
        Assert.Equal(ClaimStatus.Rejected, claim.Status);
        Assert.Contains(new EvidenceId("E-supports"), claim.EvidenceIds);
        Assert.Contains(new EvidenceId("E-refutes"), claim.EvidenceIds);
    }

    // R5 (consequence-already-true): refuse with a message about the consequence, not an
    // incidental failure from deep inside the claim-resolution rules.
    [Fact]
    public void R5_SupportingAChallengeIsRefusedWhenItsConsequenceIsAlreadyTrue()
    {
        var task = Prepare();
        Raise(task, "CH1", "claim", "C1", [new EvidenceId("E-refutes")]);
        task.Apply(new ResolveClaimCommand(task.OperatorId, null, task.NextCorrelation(),
            new ClaimId("C1"), ClaimStatus.Rejected, [new EvidenceId("E-refutes")]));

        var error = Assert.Throws<GovernanceException>(() => task.Apply(Dispose(task, "CH1", ChallengeStatus.Supported)));

        Assert.Contains("would change nothing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEvidenceFreeChallengeCannotBeSupportedAgainstAnyTarget()
    {
        var task = Prepare();
        task.Apply(new ProposeDecisionCommand(task.OperatorId, null, task.NextCorrelation(),
            new DecisionId("D1"), "Use it", "Because C1", [new ClaimId("C1")], null));
        task.Apply(new AddWorkItemCommand(task.OperatorId, null, task.NextCorrelation(),
            new WorkItemId("W1"), "Build it", task.OperatorId, [], [Path.GetFullPath("src")]));
        Raise(task, "CH-decision", "decision", "D1");
        Raise(task, "CH-work", "work", "W1");
        Raise(task, "CH-claim", "claim", "C1");

        foreach (var id in new[] { "CH-decision", "CH-work", "CH-claim" })
        {
            var error = Assert.Throws<GovernanceException>(() =>
                task.Apply(Dispose(task, id, ChallengeStatus.Supported)));
            Assert.Contains("no evidence", error.Message, StringComparison.Ordinal);
        }

        Assert.Equal(DecisionStatus.Proposed, task.State.Decisions[new DecisionId("D1")].Status);
        Assert.Equal(WorkItemStatus.Proposed, task.State.WorkItems[new WorkItemId("W1")].Status);
    }

    private static TestTask Prepare()
    {
        var task = new TestTask();
        task.Apply(new AddClaimCommand(task.OperatorId, null, task.NextCorrelation(), new ClaimId("C1"), "The API is stable", null));
        task.Apply(new AddEvidenceCommand(task.OperatorId, null, task.NextCorrelation(), new EvidenceId("E-supports"),
            "probe", "cite", "supports C1", [new ClaimId("C1")], []));
        task.Apply(new AddEvidenceCommand(task.OperatorId, null, task.NextCorrelation(), new EvidenceId("E-refutes"),
            "probe", "cite", "refutes C1", [], [new ClaimId("C1")]));
        return task;
    }

    private static void Raise(TestTask task, string id, string targetType, string targetId, IReadOnlyList<EvidenceId>? evidence = null) =>
        task.Apply(new RaiseChallengeCommand(task.OperatorId, null, task.NextCorrelation(),
            new ChallengeId(id), targetType, targetId, "It does not hold", evidence ?? []));

    private static DisposeChallengeCommand Dispose(TestTask task, string id, ChallengeStatus status) =>
        new(task.OperatorId, null, task.NextCorrelation(), new ChallengeId(id), status);
}
