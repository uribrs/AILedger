using AILedger.Core.Contracts;
using AILedger.Tests.Support;

namespace AILedger.Tests.Challenges.Lifecycle;

public sealed class ChallengeLifecycleTests
{
    [Theory]
    [InlineData(ChallengeStatus.Rejected)]
    [InlineData(ChallengeStatus.Withdrawn)]
    public void TerminalDispositionsWithoutConsequencesEmitOnlyTheDisposalEvent(
        ChallengeStatus disposition)
    {
        var task = new TestTask();
        task.Apply(new AddClaimCommand(
            task.OperatorId,
            null,
            task.NextCorrelation(),
            new ClaimId("C1"),
            "The API is stable",
            null));
        task.Apply(new RaiseChallengeCommand(
            task.OperatorId,
            null,
            task.NextCorrelation(),
            new ChallengeId("CH1"),
            "claim",
            "C1",
            "It does not hold",
            []));

        var outcome = task.Apply(new DisposeChallengeCommand(
            task.OperatorId,
            null,
            task.NextCorrelation(),
            new ChallengeId("CH1"),
            disposition));

        Assert.Single(outcome.Events);
        Assert.Equal(disposition, task.State.Challenges[new ChallengeId("CH1")].Status);
        Assert.Equal(ClaimStatus.Open, task.State.Claims[new ClaimId("C1")].Status);
    }
}
