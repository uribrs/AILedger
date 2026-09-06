using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

public sealed class DecisionReplacementTests
{
    [Fact]
    public void AcceptanceRejectsReplacementWhosePredecessorWasInvalidatedAfterProposal()
    {
        var task = new TestTask();
        var claimId = new ClaimId("C1");
        var evidenceId = new EvidenceId("E1");
        var predecessorId = new DecisionId("D1");
        var replacementId = new DecisionId("D2");
        task.Apply(new AddClaimCommand(
            task.OperatorId, null, task.NextCorrelation(), claimId, "Current assumption", null));
        task.Apply(new ProposeDecisionCommand(
            task.OperatorId, null, task.NextCorrelation(), predecessorId, "Original", "Rationale", [claimId], null));
        task.Apply(new ResolveDecisionCommand(
            task.OperatorId, null, task.NextCorrelation(), predecessorId, DecisionStatus.Accepted));
        task.Apply(new ProposeDecisionCommand(
            task.OperatorId, null, task.NextCorrelation(), replacementId, "Replacement", "Rationale", [], predecessorId));
        task.Apply(new AddEvidenceCommand(
            task.OperatorId, null, task.NextCorrelation(), evidenceId, "test", "citation", "refutes", [], [claimId]));
        task.Apply(new ResolveClaimCommand(
            task.OperatorId, null, task.NextCorrelation(), claimId, ClaimStatus.Rejected, [evidenceId]));

        var exception = Assert.Throws<GovernanceException>(() => task.Apply(new ResolveDecisionCommand(
            task.OperatorId, null, task.NextCorrelation(), replacementId, DecisionStatus.Accepted)));

        Assert.Contains("predecessor is current", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(DecisionStatus.Invalidated, task.State.Decisions[predecessorId].Status);
        Assert.Equal(DecisionStatus.Proposed, task.State.Decisions[replacementId].Status);
    }

    [Fact]
    public void AcceptanceRejectsCompetingReplacementAfterPredecessorWasSuperseded()
    {
        var task = new TestTask();
        var predecessorId = new DecisionId("D1");
        var acceptedReplacementId = new DecisionId("D2");
        var competingReplacementId = new DecisionId("D3");
        task.Apply(new ProposeDecisionCommand(
            task.OperatorId, null, task.NextCorrelation(), predecessorId, "Original", "Rationale", [], null));
        task.Apply(new ResolveDecisionCommand(
            task.OperatorId, null, task.NextCorrelation(), predecessorId, DecisionStatus.Accepted));
        task.Apply(new ProposeDecisionCommand(
            task.OperatorId, null, task.NextCorrelation(), acceptedReplacementId, "Replacement A", "Rationale", [], predecessorId));
        task.Apply(new ProposeDecisionCommand(
            task.OperatorId, null, task.NextCorrelation(), competingReplacementId, "Replacement B", "Rationale", [], predecessorId));
        task.Apply(new ResolveDecisionCommand(
            task.OperatorId, null, task.NextCorrelation(), acceptedReplacementId, DecisionStatus.Accepted));

        var exception = Assert.Throws<GovernanceException>(() => task.Apply(new ResolveDecisionCommand(
            task.OperatorId, null, task.NextCorrelation(), competingReplacementId, DecisionStatus.Accepted)));

        Assert.Contains("predecessor is current", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(DecisionStatus.Superseded, task.State.Decisions[predecessorId].Status);
        Assert.Equal(DecisionStatus.Accepted, task.State.Decisions[acceptedReplacementId].Status);
        Assert.Equal(DecisionStatus.Proposed, task.State.Decisions[competingReplacementId].Status);
    }
}
