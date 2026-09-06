using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

public sealed class ClaimSupersessionTests
{
    [Fact]
    public void SupersedingRequiresANamedReplacementThatIsNotItselfAndIsCurrent()
    {
        var task = Prepare();
        task.Apply(new AddClaimCommand(task.OperatorId, null, task.NextCorrelation(), new ClaimId("C3"), "Dead", null));
        task.Apply(new AddEvidenceCommand(task.OperatorId, null, task.NextCorrelation(), new EvidenceId("E9"),
            "probe", "cite", "refutes C3", [], [new ClaimId("C3")]));
        task.Apply(new ResolveClaimCommand(task.OperatorId, null, task.NextCorrelation(),
            new ClaimId("C3"), ClaimStatus.Rejected, [new EvidenceId("E9")]));

        Assert.Throws<GovernanceException>(() => Supersede(task, "C1", null));
        Assert.Throws<GovernanceException>(() => Supersede(task, "C1", "C1"));
        Assert.Throws<GovernanceException>(() => Supersede(task, "C1", "C3"));
        Assert.Throws<GovernanceException>(() => Supersede(task, "C1", "C-missing"));
    }

    [Fact]
    public void ValidatedAndRejectedResolutionsRejectASuppliedReplacement()
    {
        var task = Prepare();
        var error = Assert.Throws<GovernanceException>(() => task.Apply(new ResolveClaimCommand(
            task.OperatorId, null, task.NextCorrelation(), new ClaimId("C1"), ClaimStatus.Validated,
            [new EvidenceId("E1")], new ClaimId("C2"))));
        Assert.Contains("cannot name a superseding claim", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEarnedRefinementRepointsDependentsInsteadOfInvalidatingThem()
    {
        var task = Prepare();
        var workItemId = new WorkItemId("W1");
        var decisionId = new DecisionId("D1");
        task.Apply(new AddWorkItemCommand(task.OperatorId, null, task.NextCorrelation(), workItemId,
            "Build it", task.OperatorId, [new ClaimId("C1")], [Path.GetFullPath("src")]));
        task.Apply(new ProposeDecisionCommand(task.OperatorId, null, task.NextCorrelation(), decisionId,
            "Use it", "Because C1", [new ClaimId("C1")], null));
        // C2 is Validated and nothing refutes C1, so the refinement path is earned.
        task.Apply(new ResolveClaimCommand(task.OperatorId, null, task.NextCorrelation(),
            new ClaimId("C2"), ClaimStatus.Validated, [new EvidenceId("E2")]));

        var outcome = Supersede(task, "C1", "C2");

        var resolved = Assert.IsType<ClaimResolved>(outcome.Events[0].Data);
        Assert.Equal(SupersessionOutcome.Refinement, resolved.Outcome);
        Assert.IsType<ClaimDependenciesRepointed>(outcome.Events[1].Data);
        Assert.Equal(WorkItemStatus.Proposed, task.State.WorkItems[workItemId].Status);
        Assert.Equal(DecisionStatus.Proposed, task.State.Decisions[decisionId].Status);
        Assert.Equal([new ClaimId("C2")], task.State.WorkItems[workItemId].DependsOnClaims);
        Assert.Equal([new ClaimId("C2")], task.State.Decisions[decisionId].DependsOnClaims);
    }

    [Fact]
    public void EvidenceRefutingTheOriginalForcesACorrectionEvenWhenTheReplacementIsValidated()
    {
        var task = Prepare();
        var workItemId = new WorkItemId("W1");
        task.Apply(new AddWorkItemCommand(task.OperatorId, null, task.NextCorrelation(), workItemId,
            "Build it", task.OperatorId, [new ClaimId("C1")], [Path.GetFullPath("src")]));
        task.Apply(new ResolveClaimCommand(task.OperatorId, null, task.NextCorrelation(),
            new ClaimId("C2"), ClaimStatus.Validated, [new EvidenceId("E2")]));
        // This is what separates a refinement from a correction: something contradicts the original.
        task.Apply(new AddEvidenceCommand(task.OperatorId, null, task.NextCorrelation(), new EvidenceId("E3"),
            "probe", "vendor changelog", "The original no longer holds", [], [new ClaimId("C1")]));

        var outcome = Supersede(task, "C1", "C2");

        var resolved = Assert.IsType<ClaimResolved>(outcome.Events[0].Data);
        Assert.Equal(SupersessionOutcome.Correction, resolved.Outcome);
        Assert.Equal(WorkItemStatus.Stale, task.State.WorkItems[workItemId].Status);
    }

    [Fact]
    public void ARefinementLeavesTerminalDependentsPointingAtWhatTheyWereActuallyBuiltOn()
    {
        var task = Prepare();
        var doneId = new WorkItemId("W-done");
        task.Apply(new AddWorkItemCommand(task.OperatorId, null, task.NextCorrelation(), doneId,
            "Already finished", task.OperatorId, [new ClaimId("C1")], [Path.GetFullPath("src")]));
        // Completion is only scaffolding here — the subject is what refinement does to a terminal
        // dependent — so the operator waives the verifier pass instead of staging one.
        task.Apply(new CompleteWorkItemCommand(task.OperatorId, null, task.NextCorrelation(), doneId,
            "This test is about claim refinement, not verification"));
        task.Apply(new ResolveClaimCommand(task.OperatorId, null, task.NextCorrelation(),
            new ClaimId("C2"), ClaimStatus.Validated, [new EvidenceId("E2")]));

        Supersede(task, "C1", "C2");

        // A completed item keeps the audit record of the claim it was actually built on. Note the
        // correction path does still stale a Completed dependent — the two paths are not symmetric.
        Assert.Equal(WorkItemStatus.Completed, task.State.WorkItems[doneId].Status);
        Assert.Equal([new ClaimId("C1")], task.State.WorkItems[doneId].DependsOnClaims);
    }

    private static CommandOutcome Supersede(TestTask task, string claimId, string? replacementId) =>
        task.Apply(new ResolveClaimCommand(
            task.OperatorId, null, task.NextCorrelation(), new ClaimId(claimId), ClaimStatus.Superseded, [],
            replacementId is null ? null : new ClaimId(replacementId)));

    private static TestTask Prepare()
    {
        var task = new TestTask();
        task.Apply(new AddClaimCommand(task.OperatorId, null, task.NextCorrelation(), new ClaimId("C1"), "The API is stable", null));
        task.Apply(new AddClaimCommand(task.OperatorId, null, task.NextCorrelation(), new ClaimId("C2"), "The API is stable below 200 rps", null));
        task.Apply(new AddEvidenceCommand(task.OperatorId, null, task.NextCorrelation(), new EvidenceId("E1"),
            "probe", "cite", "supports C1", [new ClaimId("C1")], []));
        task.Apply(new AddEvidenceCommand(task.OperatorId, null, task.NextCorrelation(), new EvidenceId("E2"),
            "probe", "cite", "supports C2", [new ClaimId("C2")], []));
        return task;
    }
}
