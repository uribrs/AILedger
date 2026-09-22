using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Artifacts.Recon;

namespace AILedger.Tests.Stages;

public sealed class InternalReconGateTests
{
    [Fact]
    public void InternalOnlyDesignNeedsNoResearcherOrWaiver()
    {
        var f = new InternalReconFixture();
        f.Eligible();
        f.Task.Transition(TaskStage.Design);
        Assert.Equal(TaskStage.Design, f.Task.State.Stage);
        Assert.DoesNotContain(f.Task.State.Runs.Values, r => r.SubjectRole == RoleKind.Researcher);
        Assert.All(f.Task.State.Claims.Values, c => Assert.Equal(ClaimStatus.Open, c.Status));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void R1_DesignRequiresBothArmsForExternalClaims(bool resolved, bool researcher, bool accepted)
    {
        var f = new InternalReconFixture();
        if (researcher) f.Task.RecordResearcherPass();
        if (resolved) f.Resolve();
        f.Eligible("external");
        if (accepted)
            Assert.Equal(TaskStage.Design, f.Task.Transition(TaskStage.Design).State.Stage);
        else
        {
            Assert.Throws<GovernanceException>(() => f.Task.Transition(TaskStage.Design));
            Assert.Equal(TaskStage.Research, f.Task.State.Stage);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingReconFailsEvenWithResearcher(bool researcher)
    {
        var f = new InternalReconFixture();
        if (researcher) f.Task.RecordResearcherPass();
        var ex = Assert.Throws<GovernanceException>(() => f.Task.Transition(TaskStage.Design));
        Assert.Contains("InternalRecon", ex.Message);
    }
    [Fact]
    public void ReconDoesNotReplaceTheApproachPrerequisite()
    {
        var f = new InternalReconFixture(approach: false);
        f.Eligible();
        var error = Assert.Throws<GovernanceException>(() => f.Task.Transition(TaskStage.Design));
        Assert.Contains("alternative", error.Message, StringComparison.OrdinalIgnoreCase);
        f.Task.RecordDiscardedAlternative();
        f.Task.Transition(TaskStage.Design);
    }

}
