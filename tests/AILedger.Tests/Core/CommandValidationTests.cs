using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

public sealed class CommandValidationTests
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

    [Fact]
    public void EvidenceCannotBothSupportAndRefuteSameClaim()
    {
        var task = new TestTask();
        var claim = new ClaimId("C1");
        task.Apply(new AddClaimCommand(task.OperatorId, null, task.NextCorrelation(), claim, "Claim", null));

        var exception = Assert.Throws<GovernanceException>(() => task.Apply(new AddEvidenceCommand(
            task.OperatorId, null, task.NextCorrelation(), new EvidenceId("E1"), "test", "citation", "summary", [claim], [claim])));

        Assert.Contains("both support and refute", exception.Message, StringComparison.Ordinal);
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

    [Fact]
    public void CompletionMustBeTerminalAndPreserveSessionIdentity()
    {
        var task = new TestTask();
        var run = new RunId("R1");
        task.Apply(new StartRunCommand(task.OperatorId, null, task.NextCorrelation(), run, null, "codex", "session-1"));

        Assert.Throws<GovernanceException>(() => task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), run, AgentRunStatus.Active, "session-1")));
        Assert.Throws<GovernanceException>(() => task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), run, AgentRunStatus.Completed, "different")));
        task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), run, AgentRunStatus.Completed, "session-1"));

        Assert.Equal(AgentRunStatus.Completed, task.State.Runs[run].Status);
    }

    [Fact]
    public void UndefinedCommandEnumsAreRejectedAtCoreBoundary()
    {
        var task = new TestTask();

        Assert.Throws<GovernanceException>(() => task.Apply(new AssignRoleCommand(
            task.OperatorId, null, task.NextCorrelation(), new ActorId("worker"), (RoleKind)999, [])));
        Assert.Throws<GovernanceException>(() => task.Apply(new AssignRoleCommand(
            task.OperatorId, null, task.NextCorrelation(), new ActorId("worker"), RoleKind.Worker, [(Capability)999])));
        Assert.Throws<GovernanceException>(() => task.Apply(new RequestStageTransitionCommand(
            task.OperatorId, null, task.NextCorrelation(), (TaskStage)999)));
    }
}
