using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.WorkItems.Lifecycle;

public sealed class WorkLifecycleTests
{
    [Fact]
    public void CompletingWorkIsRefusedWhileARunIsStillActive()
    {
        var task = Prepare(out var workItemId);
        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), workItemId, "codex", null,
            SubjectActorId: Worker));

        var error = Assert.Throws<GovernanceException>(() => task.Apply(new CompleteWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), workItemId)));

        Assert.Contains("run is still active", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASuccessfulRunPausesItsWorkItemAndOnlyAnExplicitCommandCompletesIt()
    {
        var task = Prepare(out var workItemId);
        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), workItemId, "codex", null,
            SubjectActorId: Worker));

        task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), AgentRunStatus.Completed, "session-1"));

        // A provider exiting zero is a finished process, not finished work.
        Assert.Equal(WorkItemStatus.Paused, task.State.WorkItems[workItemId].Status);

        task.RecordVerifierPass(workItemId);
        task.Apply(new CompleteWorkItemCommand(task.OperatorId, null, task.NextCorrelation(), workItemId));
        Assert.Equal(WorkItemStatus.Completed, task.State.WorkItems[workItemId].Status);
    }

    [Fact]
    public void CompletingWorkIsRefusedWhileAnEscalationOnItIsOpen()
    {
        var task = Prepare(out var workItemId);
        EscalationCommands.Raise(task, task.OperatorId, "X1", EscalationKind.BusinessDecision,
            workItemId: workItemId, options: ["a", "b"], recommendation: "a");

        var error = Assert.Throws<GovernanceException>(() => task.Apply(new CompleteWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), workItemId)));
        Assert.Contains("escalation on it is open", error.Message, StringComparison.Ordinal);

        task.Apply(new ResolveEscalationCommand(
            task.OperatorId, null, task.NextCorrelation(), new EscalationId("X1"), EscalationStatus.Resolved, "a"));
        task.RecordRequiredRuns(workItemId);
        task.Apply(new CompleteWorkItemCommand(task.OperatorId, null, task.NextCorrelation(), workItemId));

        Assert.Equal(WorkItemStatus.Completed, task.State.WorkItems[workItemId].Status);
    }

    [Fact]
    public void BlockingRecordsAReasonAndMayCiteAnOpenEscalation()
    {
        var task = Prepare(out var workItemId);
        EscalationCommands.Raise(task, task.OperatorId, "X1", EscalationKind.BusinessDecision,
            options: ["a", "b"], recommendation: "a");

        task.Apply(new BlockWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), workItemId, "Waiting on the operator", new EscalationId("X1")));

        var workItem = task.State.WorkItems[workItemId];
        Assert.Equal(WorkItemStatus.Blocked, workItem.Status);
        Assert.Equal("Waiting on the operator", workItem.BlockReason);
        Assert.Throws<GovernanceException>(() => task.Apply(new CompleteWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), workItemId)));
    }

    // R3 (workitem-blocked-conflation): an operator block and a claim-caused block are the
    // same status member, so invalidation must not treat a manual block as already handled.
    [Fact]
    public void R3_ManuallyBlockedItemStillReactsWhenItsClaimIsRejected()
    {
        var task = new TestTask();
        var claimId = new ClaimId("C1");
        var workItemId = new WorkItemId("W1");
        task.Apply(new AddClaimCommand(task.OperatorId, null, task.NextCorrelation(), claimId, "The API is stable", "Work is wasted"));
        task.Apply(new AddWorkItemCommand(task.OperatorId, null, task.NextCorrelation(), workItemId, "Build it",
            task.OperatorId, [claimId], [Path.GetFullPath("src")]));
        task.Apply(new BlockWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), workItemId, "Operator paused this", null));
        Assert.Equal(WorkItemStatus.Blocked, task.State.WorkItems[workItemId].Status);

        task.Apply(new AddEvidenceCommand(task.OperatorId, null, task.NextCorrelation(), new EvidenceId("E1"),
            "probe", "vendor changelog", "The API changed", [], [claimId]));
        var outcome = task.Apply(new ResolveClaimCommand(
            task.OperatorId, null, task.NextCorrelation(), claimId, ClaimStatus.Rejected, [new EvidenceId("E1")]));

        Assert.Contains(outcome.Events.Select(item => item.Data), item => item is WorkItemInvalidated);
        Assert.Equal(WorkItemStatus.Stale, task.State.WorkItems[workItemId].Status);
    }

    [Fact]
    public void AManuallyBlockedItemUnblocksAndCanThenRunAndComplete()
    {
        var task = Prepare(out var workItemId);
        EscalationCommands.Raise(task, task.OperatorId, "X1", EscalationKind.BusinessDecision,
            workItemId: workItemId, options: ["a", "b"], recommendation: "a");
        task.Apply(new BlockWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), workItemId, "Waiting on the operator", new EscalationId("X1")));
        task.Apply(new ResolveEscalationCommand(
            task.OperatorId, null, task.NextCorrelation(), new EscalationId("X1"), EscalationStatus.Resolved, "a"));

        task.Apply(new UnblockWorkItemCommand(task.OperatorId, null, task.NextCorrelation(), workItemId));

        var workItem = task.State.WorkItems[workItemId];
        Assert.Equal(WorkItemStatus.Paused, workItem.Status);
        Assert.Null(workItem.BlockReason);

        // The exit is real: the item takes a run, is verified, and then completes.
        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), workItemId, "codex", null,
            SubjectActorId: Worker));
        task.Apply(new CompleteRunCommand(task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), AgentRunStatus.Completed, "s1"));
        task.RecordVerifierPass(workItemId);
        task.Apply(new CompleteWorkItemCommand(task.OperatorId, null, task.NextCorrelation(), workItemId));
        Assert.Equal(WorkItemStatus.Completed, task.State.WorkItems[workItemId].Status);
    }

    [Fact]
    public void UnblockingCannotUndoCausalInvalidation()
    {
        var task = new TestTask();
        var claimId = new ClaimId("C1");
        var workItemId = new WorkItemId("W1");
        task.Apply(new AddClaimCommand(task.OperatorId, null, task.NextCorrelation(), claimId, "The API is stable", null));
        task.Apply(new AddWorkItemCommand(task.OperatorId, null, task.NextCorrelation(), workItemId, "Build it",
            task.OperatorId, [claimId], [Path.GetFullPath("src")]));
        task.Assign(Worker, RoleKind.Worker, Capability.BuildContext, Capability.RecordArtifact);
        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), workItemId, "codex", null,
            SubjectActorId: Worker));
        task.Apply(new AddEvidenceCommand(task.OperatorId, null, task.NextCorrelation(), new EvidenceId("E1"),
            "probe", "vendor changelog", "The API changed", [], [claimId]));
        task.Apply(new ResolveClaimCommand(
            task.OperatorId, null, task.NextCorrelation(), claimId, ClaimStatus.Rejected, [new EvidenceId("E1")]));
        Assert.Equal(WorkItemStatus.Blocked, task.State.WorkItems[workItemId].Status);

        var error = Assert.Throws<GovernanceException>(() => task.Apply(new UnblockWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), workItemId)));

        Assert.Contains("replacement work item", error.Message, StringComparison.Ordinal);
        Assert.Equal(WorkItemStatus.Blocked, task.State.WorkItems[workItemId].Status);
    }

    // The first live governed run closed its own run with no session id, erasing the identity that
    // exact-session resume depends on. A completed run must stay resumable; a failed one need not.
    [Fact]
    public void ARunCannotBeRecordedAsCompletedWithoutASessionIdentity()
    {
        var task = Prepare(out var workItemId);
        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), workItemId, "claude", null,
            SubjectActorId: Worker));

        var error = Assert.Throws<GovernanceException>(() => task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), AgentRunStatus.Completed, null)));
        Assert.Contains("must stay resumable", error.Message, StringComparison.Ordinal);
        // The refusal names the declaration that exempts a run with no provider, because the caller
        // that hits this is usually holding a run that never had one.
        Assert.Contains("--provider none", error.Message, StringComparison.Ordinal);
        Assert.Equal(AgentRunStatus.Active, task.State.Runs[new RunId("R1")].Status);

        // A run that died before its session existed has no identity to record.
        task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), AgentRunStatus.Failed, null));
        Assert.Equal(AgentRunStatus.Failed, task.State.Runs[new RunId("R1")].Status);
    }

    // An operator opens a run to hold a record — an artifact needs a producer run — and no provider
    // process is ever spawned. Refusing that run Completed for lacking a session forced every such
    // filing to be closed Cancelled: 27 of the 111 unsuccessful runs in this ledger are successful
    // filings recorded as failures. Nothing is lost by completing them, because there is no session
    // to resume.
    //
    // The run names no work item, which is not incidental: an operator may hold a run only to file
    // task-wide artifacts, and that is exactly the filing this exemption exists for.
    [Fact]
    public void ARunThatDeclaresNoProviderCompletesWithoutASessionIdentity()
    {
        var task = new TestTask();
        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("RP"), null, AgentRun.NoProvider, null));

        task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("RP"), AgentRunStatus.Completed, null));

        var run = task.State.Runs[new RunId("RP")];
        Assert.Equal(AgentRunStatus.Completed, run.Status);
        Assert.Null(run.ProviderSessionId);
        Assert.True(run.HasNoProviderSessionByDeclaration);
    }

    // The exemption is a declaration made at run start, not a claim made at the end. A run that
    // named a real provider cannot reach Completed without the session it was supposed to record,
    // which is the whole rule and is unchanged.
    //
    // Deliberately the same shape as the test above — the same actor, the same absent work item,
    // the same absent session — so the provider name is the only thing that differs between the run
    // that takes the exemption and the run that cannot.
    [Fact]
    public void TheNoProviderExemptionCannotBeClaimedByARunThatNamedAProvider()
    {
        var task = new TestTask();
        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), null, "codex", null));

        var error = Assert.Throws<GovernanceException>(() => task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), AgentRunStatus.Completed, null)));

        Assert.Contains("must stay resumable", error.Message, StringComparison.Ordinal);
        Assert.Equal(AgentRunStatus.Active, task.State.Runs[new RunId("R1")].Status);
    }

    // And a launcher-managed run is never exempt however it names its provider: the token hash on
    // run.started is the second half of the declaration, so a launch cannot route around the rule
    // by passing 'none'.
    [Fact]
    public void ALauncherManagedRunIsNeverExemptEvenWhenItNamesNoProvider()
    {
        var task = Prepare(out var workItemId);
        var subject = new ActorId("worker");
        task.Assign(subject, RoleKind.Worker, Capability.AddClaim, Capability.BuildContext);
        // Named, because LaunchTokenHash is the tenth positional argument and getting it wrong here
        // silently tests nothing: the run comes back exempt and the assertion below reads as a
        // product defect rather than a miswired test.
        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("RL"), workItemId, AgentRun.NoProvider,
            ProviderSessionId: null,
            LaunchTokenHash: CommandHandler.HashLaunchToken("token"),
            SubjectActorId: subject));

        // The behaviour, not the helper: a code review pointed out that asserting the property
        // alone would keep passing if the completion rule stopped consulting it.
        var error = Assert.Throws<GovernanceException>(() => task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("RL"), AgentRunStatus.Completed, null)));
        // A launcher-managed run is refused before the resumability rule is reached — the launcher
        // holds the token and the operator is not presenting it. Either refusal proves the run did
        // not take the exemption; the property assertion below pins which of the two halves failed.
        Assert.Equal(AgentRunStatus.Active, task.State.Runs[new RunId("RL")].Status);
        Assert.NotEmpty(error.Message);
        Assert.False(task.State.Runs[new RunId("RL")].HasNoProviderSessionByDeclaration);
    }

    // The review's Major finding. Exempting a no-provider run from the resumability rule let it
    // reach Completed, and five other gates decide whether work happened by asking exactly that.
    // Two commands with no agent must not stand in for a working run.
    [Fact]
    public void ANoProviderRunDoesNotSatisfyTheWorkCompletionGates()
    {
        var task = Prepare(out var workItemId);
        // A Worker subject, so the role the gate asks about is already the right one and the only
        // thing left for it to refuse is the declaration. An operator subject would be refused for
        // holding the run at all, and the test would prove nothing about the exemption.
        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("RP"), workItemId, AgentRun.NoProvider, null,
            SubjectActorId: Worker));
        task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("RP"), AgentRunStatus.Completed, null));
        Assert.Equal(AgentRunStatus.Completed, task.State.Runs[new RunId("RP")].Status);
        Assert.Equal(RoleKind.Worker, task.State.Runs[new RunId("RP")].SubjectRole);

        var error = Assert.Throws<GovernanceException>(() => task.Apply(new CompleteWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), workItemId)));

        // The gate still reports no completed working run, which is the truth: nothing ran.
        Assert.Contains("no completed run by a working role", error.Message, StringComparison.Ordinal);
        Assert.NotEqual(WorkItemStatus.Completed, task.State.WorkItems[workItemId].Status);
    }

    // And the same run staffs no role for the stage arms.
    [Fact]
    public void ANoProviderRunStaffsNoRoleForAStageArm()
    {
        var task = Prepare(out var workItemId);
        var researcher = new ActorId("researcher");
        task.Assign(researcher, RoleKind.Researcher, Capability.AddClaim, Capability.BuildContext);
        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("RN"), workItemId, AgentRun.NoProvider,
            ProviderSessionId: null, SubjectActorId: researcher));
        task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("RN"), AgentRunStatus.Completed, null));

        task.Apply(new AddClaimCommand(
            task.OperatorId, null, task.NextCorrelation(), new ClaimId("C9"), "Something to research", null));
        task.Apply(new RequestStageTransitionCommand(
            task.OperatorId, null, task.NextCorrelation(), TaskStage.Research));

        var error = Assert.Throws<GovernanceException>(() => task.Apply(new RequestStageTransitionCommand(
            task.OperatorId, null, task.NextCorrelation(), TaskStage.Design)));

        Assert.Contains("Design requires a completed Researcher run", error.Message, StringComparison.Ordinal);
    }

    // Three live agents closed their own runs despite a briefing forbidding it. A launched agent
    // shares the run's actor identity, so only a secret the launcher holds can tell them apart.
    [Fact]
    public void AnAgentInsideALauncherManagedRunCannotCompleteIt()
    {
        var task = Prepare(out var workItemId);
        var token = "launcher-secret";
        task.Apply(new StartRunCommand(task.OperatorId, null, task.NextCorrelation(), new RunId("R1"),
            workItemId, "claude", null, null, "2.1.261", CommandHandler.HashLaunchToken(token),
            SubjectActorId: Worker));

        // The agent knows its own actor and session, and neither is enough. The attempt is made by
        // the operator rather than by the run's worker subject, and deliberately: a worker holds no
        // ManageRuns and would be refused for that instead, which proves nothing about the secret.
        // An actor that may complete runs is the only one this rule has to stop.
        var error = Assert.Throws<GovernanceException>(() => task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), AgentRunStatus.Completed, "s1")));
        Assert.Contains("closed by that launcher", error.Message, StringComparison.Ordinal);
        Assert.Throws<GovernanceException>(() => task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), AgentRunStatus.Completed, "s1", "guessed")));
        Assert.Equal(AgentRunStatus.Active, task.State.Runs[new RunId("R1")].Status);

        // And the launcher, which holds the secret, closes it.
        task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), AgentRunStatus.Completed, "s1", token));
        Assert.Equal(AgentRunStatus.Completed, task.State.Runs[new RunId("R1")].Status);
    }

    // The launching process can die and take its secret with it. An operator may then close the
    // orphan, but only as a failure: a run nobody watched finish is not a success.
    [Fact]
    public void AnOrphanedLauncherManagedRunMayBeClosedByAnOperatorOnlyAsAFailure()
    {
        var task = Prepare(out var workItemId);
        // The operator dispatches and later closes the orphan; it never holds the run itself, which
        // is a different act and one a coordinating role may not perform against a work item.
        task.Apply(new StartRunCommand(task.OperatorId, null, task.NextCorrelation(), new RunId("R1"),
            workItemId, "claude", null, null, "2.1.261", CommandHandler.HashLaunchToken("lost"),
            SubjectActorId: Worker));

        Assert.Throws<GovernanceException>(() => task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), AgentRunStatus.Completed, "s1")));

        task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), AgentRunStatus.Cancelled, null));
        Assert.Equal(AgentRunStatus.Cancelled, task.State.Runs[new RunId("R1")].Status);
    }

    // The subject every run against a work item below is dispatched to. A coordinating role may
    // hold a run only for filing task-wide artifacts, so an operator cannot be the subject of a run
    // that names an item; it stays the dispatching actor and a worker holds the run.
    private static readonly ActorId Worker = new("worker");

    private static TestTask Prepare(out WorkItemId workItemId)
    {
        var task = new TestTask();
        workItemId = new WorkItemId("W1");
        task.Apply(new AddWorkItemCommand(task.OperatorId, null, task.NextCorrelation(), workItemId, "Build it",
            task.OperatorId, [], [Path.GetFullPath("src")]));
        task.Assign(Worker, RoleKind.Worker, Capability.BuildContext, Capability.RecordArtifact);
        return task;
    }
}
