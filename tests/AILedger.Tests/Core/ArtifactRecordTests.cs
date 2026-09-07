using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

// The prompt contract, the orchestration plan, the verifier's findings and the review used to live
// in markdown files beside the ledger, so a replay of the log produced a task whose governing
// documents were somewhere else and could have changed since. They are events now: the body travels
// inline, every revision is retained, and one artifact supersedes another rather than overwriting
// it. These tests pin the record and its revision chain; authority is in ArtifactAuthorityTests and
// the two enforcement points are in ArtifactGateTests.
public sealed class ArtifactRecordTests
{
    [Fact]
    public void TheBodyIsCarriedIntoTheEventLogVerbatim()
    {
        var task = new TestTask();

        var outcome = task.Apply(ArtifactCommands.Record(
            task, task.OperatorId, "A1", GovernedArtifactKind.UserRequest, ArtifactCommands.Body));

        var recorded = Assert.IsType<ArtifactRecorded>(Assert.Single(outcome.Events).Data).Artifact;
        // Not trimmed, not normalised, not re-wrapped. The front matter, the blank lines and the
        // leading spaces are the document, and 'artifact show' has to write back what went in.
        Assert.Equal(ArtifactCommands.Body, recorded.Content);
        Assert.Equal(ArtifactCommands.Body, task.State.Artifacts[new ArtifactId("A1")].Content);
        Assert.Equal(GovernedArtifactKind.UserRequest, recorded.Kind);
        Assert.Equal(task.OperatorId, recorded.Provenance.ActorId);
        Assert.Null(recorded.SupersedesArtifactId);
    }

    [Fact]
    public void AnArtifactIdIsTakenOnce()
    {
        var task = new TestTask();
        task.Apply(ArtifactCommands.Record(task, task.OperatorId, "A1", GovernedArtifactKind.UserRequest));

        var error = Assert.Throws<GovernanceException>(() => task.Apply(ArtifactCommands.Record(
            task, task.OperatorId, "A1", GovernedArtifactKind.UserRequest, "A second request")));

        Assert.Contains("already exists", error.Message, StringComparison.Ordinal);
        Assert.Equal(ArtifactCommands.Body, task.State.Artifacts[new ArtifactId("A1")].Content);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnArtifactWithNoBodyIsNotADocument(string content)
    {
        var task = new TestTask();

        Assert.Throws<GovernanceException>(() => task.Apply(ArtifactCommands.Record(
            task, task.OperatorId, "A1", GovernedArtifactKind.UserRequest, content)));
        Assert.Empty(task.State.Artifacts);
    }

    [Fact]
    public void AnArtifactWithNoIdOrNoTitleIsRefused()
    {
        var task = new TestTask();

        Assert.Throws<GovernanceException>(() => task.Apply(ArtifactCommands.Record(
            task, task.OperatorId, "  ", GovernedArtifactKind.UserRequest)));
        Assert.Throws<GovernanceException>(() => task.Apply(ArtifactCommands.Record(
            task, task.OperatorId, "A1", GovernedArtifactKind.UserRequest, title: "  ")));
        Assert.Empty(task.State.Artifacts);
    }

    // Supersession is the whole reason a contract can be revised without the ledger losing what the
    // work was actually done against. The predecessor stays readable; only the successor is current.
    [Fact]
    public void ARevisedContractSupersedesItsPredecessorAndBothStayOnTheRecord()
    {
        var task = new TestTask();
        var run = task.StartArtifactProducer(task.OperatorId, RoleKind.Operator, "RP");
        task.Apply(ArtifactCommands.Record(
            task, task.OperatorId, "A1", GovernedArtifactKind.PromptContract, "First contract",
            producerRun: run));

        task.Apply(ArtifactCommands.Record(
            task, task.OperatorId, "A2", GovernedArtifactKind.PromptContract, "Revised contract",
            producerRun: run, supersedes: "A1"));

        Assert.Equal("First contract", task.State.Artifacts[new ArtifactId("A1")].Content);
        Assert.Equal("Revised contract", task.State.Artifacts[new ArtifactId("A2")].Content);
        Assert.Equal(new ArtifactId("A1"), task.State.Artifacts[new ArtifactId("A2")].SupersedesArtifactId);
        Assert.Equal(2, task.State.Artifacts.Count);
    }

    [Fact]
    public void ARevisionChainKeepsEveryRevisionAndOnlyTheLastIsUnsuperseded()
    {
        var task = new TestTask();
        var run = task.StartArtifactProducer(task.OperatorId, RoleKind.Operator, "RP");
        task.Apply(ArtifactCommands.Record(task, task.OperatorId, "A1", GovernedArtifactKind.OrchestrationPlan, "One", producerRun: run));
        task.Apply(ArtifactCommands.Record(task, task.OperatorId, "A2", GovernedArtifactKind.OrchestrationPlan, "Two", producerRun: run, supersedes: "A1"));
        task.Apply(ArtifactCommands.Record(task, task.OperatorId, "A3", GovernedArtifactKind.OrchestrationPlan, "Three", producerRun: run, supersedes: "A2"));

        var superseded = task.State.Artifacts.Values
            .Where(artifact => artifact.SupersedesArtifactId is { } predecessor &&
                               task.State.Artifacts.ContainsKey(predecessor))
            .Select(artifact => artifact.SupersedesArtifactId!.Value)
            .ToHashSet();
        var current = task.State.Artifacts.Values
            .Where(artifact => !superseded.Contains(artifact.ArtifactId))
            .ToArray();

        Assert.Equal(3, task.State.Artifacts.Count);
        Assert.Equal(new ArtifactId("A3"), Assert.Single(current).ArtifactId);
    }

    // A fork is two documents each claiming to replace the same one, and nothing in the record says
    // which the work was done against. The kernel refuses the second rather than picking.
    [Fact]
    public void APredecessorMayHaveOnlyOneSuccessor()
    {
        var task = new TestTask();
        var run = task.StartArtifactProducer(task.OperatorId, RoleKind.Operator, "RP");
        task.Apply(ArtifactCommands.Record(task, task.OperatorId, "A1", GovernedArtifactKind.PromptContract, "First", producerRun: run));
        task.Apply(ArtifactCommands.Record(task, task.OperatorId, "A2", GovernedArtifactKind.PromptContract, "Second", producerRun: run, supersedes: "A1"));

        var error = Assert.Throws<GovernanceException>(() => task.Apply(ArtifactCommands.Record(
            task, task.OperatorId, "A3", GovernedArtifactKind.PromptContract, "Third", producerRun: run, supersedes: "A1")));

        Assert.Contains("supersede", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(new ArtifactId("A3"), task.State.Artifacts.Keys);
    }

    [Fact]
    public void OneKindCannotSupersedeAnother()
    {
        var task = new TestTask();
        var run = task.StartArtifactProducer(task.OperatorId, RoleKind.Operator, "RP");
        task.Apply(ArtifactCommands.Record(task, task.OperatorId, "A1", GovernedArtifactKind.PromptContract, "Contract", producerRun: run));

        // A plan replacing a contract would leave the task with a current plan and no contract at
        // all, and the Execution gate would then be asking after a document nobody withdrew.
        var error = Assert.Throws<GovernanceException>(() => task.Apply(ArtifactCommands.Record(
            task, task.OperatorId, "A2", GovernedArtifactKind.OrchestrationPlan, "Plan", producerRun: run, supersedes: "A1")));

        Assert.Contains("supersede", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(new ArtifactId("A2"), task.State.Artifacts.Keys);
    }

    [Fact]
    public void AnArtifactCannotSupersedeOneBelongingToAnotherWorkItem()
    {
        var task = new TestTask();
        var first = new WorkItemId("W1");
        var second = new WorkItemId("W2");
        AddWork(task, first, "w1");
        AddWork(task, second, "w2");
        var verifier = StartVerifierRun(task, first, "RV1", out var firstRun);
        task.Apply(ArtifactCommands.Record(
            task, verifier, "A1", GovernedArtifactKind.VerifierOutput, ArtifactCommands.VerifierBody,
            workItem: first, producerRun: firstRun));
        task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), firstRun, AgentRunStatus.Completed, "session-RV1"));
        StartVerifierRun(task, second, "RV2", out var secondRun);

        var error = Assert.Throws<GovernanceException>(() => task.Apply(ArtifactCommands.Record(
            task, verifier, "A2", GovernedArtifactKind.VerifierOutput, ArtifactCommands.VerifierBody,
            workItem: second, producerRun: secondRun, supersedes: "A1")));

        Assert.Contains("supersede", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(new ArtifactId("A2"), task.State.Artifacts.Keys);
    }

    [Fact]
    public void SupersedingAnArtifactTheTaskDoesNotHoldIsRefused()
    {
        var task = new TestTask();
        var run = task.StartArtifactProducer(task.OperatorId, RoleKind.Operator, "RP");

        var error = Assert.Throws<GovernanceException>(() => task.Apply(ArtifactCommands.Record(
            task, task.OperatorId, "A2", GovernedArtifactKind.PromptContract, "Revision", producerRun: run, supersedes: "A1")));

        Assert.Contains("A1", error.Message, StringComparison.Ordinal);
        Assert.Empty(task.State.Artifacts);
    }

    internal static void AddWork(TestTask task, WorkItemId workItemId, string area) =>
        task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), workItemId, $"Work {workItemId}",
            task.OperatorId, [], [Path.GetFullPath(Path.Combine("src", area))]));

    // A verifier's output has to name the run that produced it, and that run has to still be open.
    // Every test that needs one starts here rather than restating six commands.
    internal static ActorId StartVerifierRun(TestTask task, WorkItemId workItemId, string runId, out RunId run)
    {
        task.RecordExecutionArtifacts();
        var verifier = new ActorId("verifier");
        if (!task.State.Roles.ContainsKey(verifier))
        {
            task.Assign(verifier, RoleKind.Verifier, Capability.BuildContext, Capability.RecordArtifact);
        }

        run = new RunId(runId);
        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), run, workItemId, "claude",
            null, null, null, null, verifier));
        return verifier;
    }
}
