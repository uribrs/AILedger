using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Tests.Support;

namespace AILedger.Tests.Artifacts;

public sealed class TaskCloseoutEvidenceTests
{
    [Fact]
    public void SupersededAndCurrentAssuranceRevisionsAreBothReportedInLogOrder()
    {
        var report = Build(WithTwoVerifierRevisions());

        Assert.Equal(["A-v1", "A-v2"], report.AssuranceRevisions
            .Select(revision => revision.ArtifactId).ToArray());
        Assert.False(report.AssuranceRevisions[0].IsCurrent);
        Assert.True(report.AssuranceRevisions[1].IsCurrent);
        Assert.Equal("A-v1", report.AssuranceRevisions[1].SupersedesArtifactId);
        Assert.True(report.AssuranceRevisions[0].EventIndex < report.AssuranceRevisions[1].EventIndex);
        Assert.Equal(2, report.Completeness.AssuranceRevisions);
        Assert.Equal(1, report.Completeness.CurrentAssuranceRevisions);
    }

    [Fact]
    public void ARevisionCarriesProducerIdentityRoleAndProviderFacts()
    {
        var revision = Build(WithTwoVerifierRevisions()).AssuranceRevisions[0];

        Assert.Equal(64, revision.ContentSha256.Length);
        Assert.True(revision.ContentBytes > 0);
        Assert.NotNull(revision.Producer);
        Assert.Equal("R-v1", revision.Producer!.RunId);
        Assert.Equal("verifier", revision.Producer.ActorId);
        Assert.Equal(RoleKind.Verifier, revision.Producer.SubjectRole);
        Assert.Equal("verification-provider", revision.Producer.Provider);
        Assert.Equal(AgentRunStatus.Completed, revision.Producer.Status);
    }

    [Fact]
    public void LegacyRevisionsWithoutCandidateBindingsAreCounted()
    {
        var report = Build(WithTwoVerifierRevisions());

        Assert.Equal(2, report.Completeness.RevisionsWithoutCandidateBinding);
    }

    [Fact]
    public void TheDeterministicProjectionCarriesNoVerdictFields()
    {
        var names = typeof(CloseoutAssuranceRevision).GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("Severity", names);
        Assert.DoesNotContain("Opportunity", names);
        Assert.DoesNotContain("Repair", names);
        Assert.DoesNotContain("Disposition", names);
    }

    [Fact]
    public void WorkItemRunsAndGovernanceRecordsRetainOrderAndAttribution()
    {
        var report = Build(WithTwoVerifierRevisions());
        var work = Assert.Single(report.WorkItems, item => item.WorkItemId == "W-stage");

        Assert.Equal(["R-v1", "R-v2"], work.Runs
            .Where(run => run.SubjectRole == RoleKind.Verifier)
            .Select(run => run.RunId).ToArray());
        Assert.NotEmpty(report.Records.Claims);
        Assert.All(report.Records.Claims,
            claim => Assert.False(string.IsNullOrWhiteSpace(claim.RecordedBy)));
    }

    private static TaskCloseoutEvidenceReport Build(TestTask task) =>
        TaskCloseoutEvidence.Build(task.State, task.Events);

    private static TestTask WithTwoVerifierRevisions()
    {
        var task = new TestTask();
        task.ReachStage(TaskStage.Verification);
        var workItem = task.State.WorkItems.Values.Single().Id;

        ArtifactCommands.StartVerifierRun(task, workItem, "R-v1", out var first);
        task.Apply(ArtifactCommands.Record(
            task, new ActorId("verifier"), "A-v1", GovernedArtifactKind.VerifierOutput,
            ArtifactCommands.VerifierBody, "Verifier 1", workItem, first));
        task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), first,
            AgentRunStatus.Completed, "session-v1"));

        ArtifactCommands.StartVerifierRun(task, workItem, "R-v2", out var second);
        task.Apply(ArtifactCommands.Record(
            task, new ActorId("verifier"), "A-v2", GovernedArtifactKind.VerifierOutput,
            ArtifactCommands.VerifierBody + "\nLater pass.\n", "Verifier 2", workItem, second,
            supersedes: "A-v1"));
        task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), second,
            AgentRunStatus.Completed, "session-v2"));
        return task;
    }
}
