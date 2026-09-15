using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Evidences;

public sealed class EvidenceRulesTests
{
    [Fact]
    public void EvidenceCannotBothSupportAndRefuteTheSameClaim()
    {
        var task = new TestTask();
        var claim = AddClaim(task);

        var exception = Assert.Throws<GovernanceException>(() => task.Apply(new AddEvidenceCommand(
            task.OperatorId,
            null,
            task.NextCorrelation(),
            new EvidenceId("E1"),
            "test",
            "citation",
            "summary",
            [claim],
            [claim])));

        Assert.Contains("both support and refute", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddedEvidencePreservesDirectionsAndTrimsDescriptiveFields()
    {
        var task = new TestTask();
        var claim = AddClaim(task);
        var evidenceId = new EvidenceId("E1");

        var outcome = task.Apply(new AddEvidenceCommand(
            task.OperatorId,
            null,
            task.NextCorrelation(),
            evidenceId,
            "  source-read  ",
            "  src/file.cs:12  ",
            "  The implementation supports the claim  ",
            [claim],
            []));

        Assert.IsType<EvidenceAdded>(Assert.Single(outcome.Events).Data);
        var evidence = task.State.Evidence[evidenceId];
        Assert.Equal("source-read", evidence.SourceType);
        Assert.Equal("src/file.cs:12", evidence.Citation);
        Assert.Equal("The implementation supports the claim", evidence.Summary);
        Assert.Equal(new[] { claim }, evidence.Supports);
        Assert.Empty(evidence.Refutes);
    }

    private static ClaimId AddClaim(TestTask task)
    {
        var claim = new ClaimId("C1");
        task.Apply(new AddClaimCommand(
            task.OperatorId,
            null,
            task.NextCorrelation(),
            claim,
            "Claim",
            null));
        return claim;
    }
}
