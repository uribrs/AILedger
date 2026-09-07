using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

// Who may record which document. The kernel treats the five kinds differently because they are
// evidence of different things: a user request is what the operator asked for, a contract and a plan
// are what a lead settled, and a verifier's or reviewer's output is the product of a specific run.
// Without these rules an agent could write its own verdict and then close its own pass on it.
public sealed class ArtifactAuthorityTests
{
    [Fact]
    public void OnlyTheOperatorAuthorsTheUserRequest()
    {
        var task = new TestTask();
        var lead = new ActorId("lead");
        task.Assign(lead, RoleKind.ImplementationLead, Capability.RecordArtifact);

        // The lead holds RecordArtifact, so this is refused for who it is, not for what it may do:
        // a request the operator did not make is not a request.
        var error = Assert.Throws<GovernanceException>(() => task.Apply(ArtifactCommands.Record(
            task, lead, "A1", GovernedArtifactKind.UserRequest, "What I think was asked for")));

        Assert.Contains("operator", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(new ArtifactId("A1"), task.State.Artifacts.Keys);

        task.Apply(ArtifactCommands.Record(task, task.OperatorId, "A2", GovernedArtifactKind.UserRequest));
        Assert.Equal(GovernedArtifactKind.UserRequest, task.State.Artifacts[new ArtifactId("A2")].Kind);
    }

    [Theory]
    [InlineData(GovernedArtifactKind.PromptContract)]
    [InlineData(GovernedArtifactKind.OrchestrationPlan)]
    public void AContractAndAPlanComeFromAnOperatorOrALead(GovernedArtifactKind kind)
    {
        foreach (var (actor, role) in new[]
                 {
                     (new ActorId("planning-lead"), RoleKind.PlanningLead),
                     (new ActorId("implementation-lead"), RoleKind.ImplementationLead)
                 })
        {
            var task = new TestTask();
            var run = task.StartArtifactProducer(actor, role, $"R-{actor}");
            task.Apply(ArtifactCommands.Record(
                task, actor, "A1", kind, $"By the {role}", producerRun: run));

            Assert.Equal(actor, task.State.Artifacts[new ArtifactId("A1")].Provenance.ActorId);
            Assert.Equal(run, task.State.Artifacts[new ArtifactId("A1")].ProducerRunId);
        }
    }

    [Theory]
    [InlineData(GovernedArtifactKind.PromptContract)]
    [InlineData(GovernedArtifactKind.OrchestrationPlan)]
    public void AWorkerDoesNotWriteTheContractItWorksAgainst(GovernedArtifactKind kind)
    {
        var task = new TestTask();
        var worker = new ActorId("worker");
        task.Assign(worker, RoleKind.Worker, Capability.RecordArtifact);
        var run = task.StartArtifactProducer(worker, RoleKind.Worker, "RW");

        var error = Assert.Throws<GovernanceException>(() => task.Apply(ArtifactCommands.Record(
            task, worker, "A1", kind, "Scope I set for myself", producerRun: run)));

        Assert.Contains("operator", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(task.State.Artifacts);
    }

    [Fact]
    public void AVerifierOutputRequiresAnActiveVerifierRunToHaveProducedIt()
    {
        var task = new TestTask();
        var workItemId = new WorkItemId("W1");
        ArtifactRecordTests.AddWork(task, workItemId, "w1");
        var verifier = new ActorId("verifier");
        task.Assign(verifier, RoleKind.Verifier, Capability.BuildContext, Capability.RecordArtifact);

        // No run at all. A verdict nobody was dispatched to produce is an opinion.
        var error = Assert.Throws<GovernanceException>(() => task.Apply(ArtifactCommands.Record(
            task, verifier, "A1", GovernedArtifactKind.VerifierOutput, "It looks fine",
            workItem: workItemId)));
        Assert.Contains("run", error.Message, StringComparison.OrdinalIgnoreCase);

        ArtifactRecordTests.StartVerifierRun(task, workItemId, "RV", out var run);
        task.Apply(ArtifactCommands.Record(
            task, verifier, "A2", GovernedArtifactKind.VerifierOutput, ArtifactCommands.VerifierBody,
            workItem: workItemId, producerRun: run));

        Assert.Equal(run, task.State.Artifacts[new ArtifactId("A2")].ProducerRunId);
    }

    [Fact]
    public void ARunUnderAnotherRoleDoesNotProduceAVerifierOutput()
    {
        var task = new TestTask();
        var workItemId = new WorkItemId("W1");
        ArtifactRecordTests.AddWork(task, workItemId, "w1");
        var worker = new ActorId("worker");
        task.Assign(worker, RoleKind.Worker, Capability.BuildContext, Capability.RecordArtifact);
        var run = new RunId("RW");
        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), run, workItemId, "codex",
            null, null, null, null, worker));

        // The run is active and belongs to the recording actor. It is still not a verification,
        // because the subject was not dispatched as a verifier.
        var error = Assert.Throws<GovernanceException>(() => task.Apply(ArtifactCommands.Record(
            task, worker, "A1", GovernedArtifactKind.VerifierOutput, "I checked my own work",
            workItem: workItemId, producerRun: run)));

        Assert.Contains("verifier", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(task.State.Artifacts);
    }

    [Fact]
    public void ACodeReviewOutputRequiresAnActiveCodeReviewerRun()
    {
        var task = new TestTask();
        var workItemId = new WorkItemId("W1");
        ArtifactRecordTests.AddWork(task, workItemId, "w1");
        var reviewer = new ActorId("reviewer");
        task.Assign(reviewer, RoleKind.CodeReviewer, Capability.BuildContext, Capability.RecordArtifact);
        // A reviewer cannot even start until a verifier has finished, so the pass comes first.
        task.RecordWorkingPass(workItemId);
        task.RecordVerifierPass(workItemId);

        var error = Assert.Throws<GovernanceException>(() => task.Apply(ArtifactCommands.Record(
            task, reviewer, "A1", GovernedArtifactKind.CodeReviewOutput, "Findings",
            workItem: workItemId)));
        Assert.Contains("run", error.Message, StringComparison.OrdinalIgnoreCase);

        var run = new RunId("RR");
        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), run, workItemId, "claude",
            null, null, null, null, reviewer));
        task.Apply(ArtifactCommands.Record(
            task, reviewer, "A2", GovernedArtifactKind.CodeReviewOutput, "Findings",
            workItem: workItemId, producerRun: run));

        Assert.Equal(run, task.State.Artifacts[new ArtifactId("A2")].ProducerRunId);
    }

    // A run that has ended cannot acquire new findings afterwards. Allowing it would mean a verdict
    // written after the pass closed, attributed to a pass that never saw it.
    [Fact]
    public void ARunThatHasAlreadyEndedProducesNothingFurther()
    {
        var task = new TestTask();
        var workItemId = new WorkItemId("W1");
        ArtifactRecordTests.AddWork(task, workItemId, "w1");
        var verifier = ArtifactRecordTests.StartVerifierRun(task, workItemId, "RV", out var run);
        task.Apply(ArtifactCommands.Record(
            task, verifier, "A1", GovernedArtifactKind.VerifierOutput, ArtifactCommands.VerifierBody,
            workItem: workItemId, producerRun: run));
        task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), run, AgentRunStatus.Completed, "session-RV"));

        var error = Assert.Throws<GovernanceException>(() => task.Apply(ArtifactCommands.Record(
            task, verifier, "A2", GovernedArtifactKind.VerifierOutput, "An afterthought",
            workItem: workItemId, producerRun: run)));

        Assert.Contains("active", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(new ArtifactId("A2"), task.State.Artifacts.Keys);
    }

    [Fact]
    public void AWorkScopedOutputMustNameTheWorkItemItsRunWasDispatchedAgainst()
    {
        var task = new TestTask();
        var verified = new WorkItemId("W1");
        var other = new WorkItemId("W2");
        ArtifactRecordTests.AddWork(task, verified, "w1");
        ArtifactRecordTests.AddWork(task, other, "w2");
        var verifier = ArtifactRecordTests.StartVerifierRun(task, verified, "RV", out var run);

        // Findings filed against an item the run never looked at would satisfy that item's gate.
        var error = Assert.Throws<GovernanceException>(() => task.Apply(ArtifactCommands.Record(
            task, verifier, "A1", GovernedArtifactKind.VerifierOutput, "Findings",
            workItem: other, producerRun: run)));

        Assert.Contains("producer run", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(new ArtifactId("A1"), task.State.Artifacts.Keys);
    }

    [Fact]
    public void RecordingAnArtifactRequiresTheRecordArtifactCapability()
    {
        var task = new TestTask();
        var lead = new ActorId("lead");
        // Every other authority a lead ordinarily holds, and not this one.
        task.Assign(lead, RoleKind.ImplementationLead, Capability.AddClaim, Capability.ProposeDecision);

        var error = Assert.Throws<GovernanceException>(() => task.Apply(ArtifactCommands.Record(
            task, lead, "A1", GovernedArtifactKind.PromptContract, "Contract")));

        Assert.Contains(Capability.RecordArtifact.ToString(), error.Message, StringComparison.Ordinal);
        Assert.Empty(task.State.Artifacts);
    }

    [Fact]
    public void OpeningATaskGrantsTheOperatorTheNewCapability()
    {
        var task = new TestTask();

        Assert.Contains(Capability.RecordArtifact, task.State.Roles[task.OperatorId].Capabilities);
    }
}
