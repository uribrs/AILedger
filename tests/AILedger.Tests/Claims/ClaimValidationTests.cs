using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Claims;

public sealed class ClaimValidationTests
{
    [Fact]
    public void ValidatedClaimRequiresExistingEvidence()
    {
        var task = new TestTask();
        task.Apply(new AddClaimCommand(task.OperatorId, null, task.NextCorrelation(), new ClaimId("C1"), "Claim", null));

        Assert.Throws<GovernanceException>(() => task.Apply(new ResolveClaimCommand(
            task.OperatorId, null, task.NextCorrelation(), new ClaimId("C1"), ClaimStatus.Validated, [])));
        Assert.Throws<GovernanceException>(() => task.Apply(new ResolveClaimCommand(
            task.OperatorId, null, task.NextCorrelation(), new ClaimId("C1"), ClaimStatus.Validated, [new EvidenceId("missing")])));
    }

    [Theory]
    [InlineData(ClaimStatus.Validated)]
    [InlineData(ClaimStatus.Rejected)]
    public void ClaimResolutionRejectsDirectionallyUnrelatedEvidence(ClaimStatus status)
    {
        var task = new TestTask();
        var target = new ClaimId("C1");
        var unrelated = new ClaimId("C2");
        var evidenceId = new EvidenceId("E1");
        task.Apply(new AddClaimCommand(task.OperatorId, null, task.NextCorrelation(), target, "Target", null));
        task.Apply(new AddClaimCommand(task.OperatorId, null, task.NextCorrelation(), unrelated, "Other", null));
        task.Apply(new AddEvidenceCommand(
            task.OperatorId, null, task.NextCorrelation(), evidenceId, "test", "citation", "summary",
            [unrelated], []));

        var exception = Assert.Throws<GovernanceException>(() => task.Apply(new ResolveClaimCommand(
            task.OperatorId, null, task.NextCorrelation(), target, status, [evidenceId])));

        Assert.Contains(status == ClaimStatus.Validated ? "does not support" : "does not refute",
            exception.Message, StringComparison.Ordinal);
    }
}
