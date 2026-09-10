using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Storage;

public sealed class NewCommandReplayTests
{
    // R1 (dual-kernel-rule-drift): every rule in this kernel is written twice — once in
    // CommandHandler, once in TaskTransitionValidator, which re-validates at replay. This drives
    // the real file store and then replays through a *fresh* service, so the two copies must
    // agree on real events, not on a test double's approximation of them.
    [Fact]
    public async Task R1_EveryNewCommandCommitsAndReplaysThroughTheFileStore()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("replay-task");
        var actor = new ActorId("operator");
        var reviewer = new ActorId("reviewer");
        var verifier = new ActorId("verifier");
        var worker = new ActorId("worker");
        string Area(string name) => Directory.CreateDirectory(Path.Combine(root.Path, name)).FullName;
        var writer = Service(root.Path);

        await Run(writer, taskId, new OpenTaskCommand(actor, null, "c1", taskId, "Task", "Goal"));
        // The brief the gate on 'work add' requires, and itself one of the new commands this test
        // exists to commit and replay.
        await Run(writer, taskId, new RecordContextBuiltCommand(
            actor, null, "c1b", null,
            [new ContextSkill("workflow-coordinator", "hash-of-workflow-coordinator"),
             new ContextSkill("task-orchestrator", "hash-of-task-orchestrator")]));
        await Run(writer, taskId, new AddWorkItemCommand(actor, null, "c2", new WorkItemId("W1"), "Blocked work", actor, [], [Area("w1")]));
        await Run(writer, taskId, new AddWorkItemCommand(actor, null, "c3", new WorkItemId("W2"), "Finished work", actor, [], [Area("w2")]));
        await Run(writer, taskId, new AddConstraintCommand(actor, null, "c4", new ConstraintId("K1"), "Stay local", "dossier", [Area("k1")]));
        await Run(writer, taskId, new AddConstraintCommand(actor, null, "c5", new ConstraintId("K2"), "Old rule", "draft", []));
        await Run(writer, taskId, new SupersedeConstraintCommand(actor, null, "c6", new ConstraintId("K2")));
        await Run(writer, taskId, new AddEvidenceCommand(actor, null, "c7", new EvidenceId("E1"), "local-probe", "grep -r", "No answer in the repository", [], []));
        await Run(writer, taskId, new RaiseEscalationCommand(actor, null, "c8", new EscalationId("X1"), EscalationKind.TrueUnknown, "Which account owns this?", null, [], null, [new EvidenceId("E1")]));
        await Run(writer, taskId, new ResolveEscalationCommand(actor, null, "c9", new EscalationId("X1"), EscalationStatus.Resolved, "The platform account"));
        await Run(writer, taskId, new RaiseEscalationCommand(actor, null, "c10", new EscalationId("X2"), EscalationKind.BusinessDecision, "Ship now or harden first?", new WorkItemId("W1"), ["ship", "harden"], "ship", []));
        await Run(writer, taskId, new RecordAlternativeCommand(actor, null, "c11", new AlternativeId("ALT1"), "Use a database", "Files stay inspectable", null));
        await Run(writer, taskId, new BlockWorkItemCommand(actor, null, "c12", new WorkItemId("W1"), "Waiting on the operator", new EscalationId("X2")));
        await Run(writer, taskId, new AddWorkItemCommand(actor, null, "c12b", new WorkItemId("W3"), "Unblocked work", actor, [], [Area("w3")]));
        await Run(writer, taskId, new BlockWorkItemCommand(actor, null, "c12c", new WorkItemId("W3"), "Paused by the operator", null));
        await Run(writer, taskId, new UnblockWorkItemCommand(actor, null, "c12d", new WorkItemId("W3")));

        // A7: the lifecycle claim is about what a *real* run completion does to a work item, so
        // this drives run.start/run.complete through the file store rather than a fake adapter.
        await Run(writer, taskId, new StartRunCommand(actor, null, "c13", new RunId("R1"), new WorkItemId("W2"), "codex", null));
        await Run(writer, taskId, new RecordArtifactCommand(
            actor, null, "c13a", new ArtifactId("A-request"), GovernedArtifactKind.UserRequest,
            "Request", "The request", null, null, null));
        await Run(writer, taskId, new RecordArtifactCommand(
            actor, null, "c13b", new ArtifactId("A-contract"), GovernedArtifactKind.PromptContract,
            "Contract", ArtifactCommands.Body, null, new RunId("R1"), null));
        await Run(writer, taskId, new RecordArtifactCommand(
            actor, null, "c13c", new ArtifactId("A-plan"), GovernedArtifactKind.OrchestrationPlan,
            "Plan", ArtifactCommands.PlanBody, null, new RunId("R1"), null));
        await Run(writer, taskId, new CompleteRunCommand(actor, null, "c14", new RunId("R1"), AgentRunStatus.Completed, "session-1"));
        var afterRun = await Service(root.Path).GetStateAsync(taskId, CancellationToken.None);
        Assert.Equal(WorkItemStatus.Paused, afterRun?.WorkItems[new WorkItemId("W2")].Status);

        // Dispatch: the operator authorises, the reviewer is the run's subject. The rule is written
        // twice, so this has to survive a real commit and a real replay, not just the handler.
        await Run(writer, taskId, new AssignRoleCommand(actor, null, "c14a", reviewer, RoleKind.CodeReviewer, [Capability.BuildContext, Capability.RecordArtifact]));
        await Run(writer, taskId, new AssignRoleCommand(actor, null, "c14a2", verifier, RoleKind.Verifier, [Capability.BuildContext, Capability.RecordArtifact]));
        await Run(writer, taskId, new AddWorkItemCommand(actor, null, "c14b", new WorkItemId("W4"), "Reviewed work", actor, [], [Area("w4")]));
        // A reviewer only starts after a verifier has finished with the item, so the sequence has
        // to hold through a real commit and a real replay, not only inside the handler. Each of the
        // two inspecting roles also has to file its output before its run can close as completed.
        await Run(writer, taskId, new StartRunCommand(actor, null, "c14b2", new RunId("RV0"), new WorkItemId("W4"), "codex", null, null, null, null, verifier));
        await Run(writer, taskId, Artifact(verifier, "c14b2a", "A-RV0", GovernedArtifactKind.VerifierOutput, new WorkItemId("W4"), new RunId("RV0")));
        await Run(writer, taskId, new CompleteRunCommand(actor, null, "c14b3", new RunId("RV0"), AgentRunStatus.Completed, "session-v0"));
        await Run(writer, taskId, new StartRunCommand(actor, null, "c14c", new RunId("R2"), new WorkItemId("W4"), "claude", null, null, null, null, reviewer));
        await Run(writer, taskId, Artifact(reviewer, "c14c1", "A-R2", GovernedArtifactKind.CodeReviewOutput, new WorkItemId("W4"), new RunId("R2")));
        await Run(writer, taskId, new CompleteRunCommand(actor, null, "c14d", new RunId("R2"), AgentRunStatus.Completed, "session-2"));

        await Run(writer, taskId, new StartRunCommand(actor, null, "c14e", new RunId("RV1"), new WorkItemId("W2"), "claude", null, null, null, null, verifier));
        await Run(writer, taskId, Artifact(verifier, "c14e1", "A-RV1", GovernedArtifactKind.VerifierOutput, new WorkItemId("W2"), new RunId("RV1")));
        await Run(writer, taskId, new CompleteRunCommand(actor, null, "c14f", new RunId("RV1"), AgentRunStatus.Completed, "session-3"));
        await Run(writer, taskId, new CompleteWorkItemCommand(actor, null, "c15", new WorkItemId("W2")));

        // F2: a released area really is free again, proven by a second work item taking it after a
        // real commit rather than by asking the handler what it thinks.
        await Run(writer, taskId, new AddWorkItemCommand(actor, null, "c16", new WorkItemId("W5"), "Wrong split", actor, [], [Area("shared")]));
        await Run(writer, taskId, new AbandonWorkItemCommand(actor, null, "c17", new WorkItemId("W5"), "The split was wrong"));
        await Run(writer, taskId, new AddWorkItemCommand(actor, null, "c18", new WorkItemId("W6"), "Better split", actor, [], [Area("shared")]));
        await Run(writer, taskId, new AssignRoleCommand(actor, null, "c18a", worker, RoleKind.Worker, [Capability.BuildContext]));
        await Run(writer, taskId, new StartRunCommand(actor, null, "c18b", new RunId("RW1"), new WorkItemId("W6"), "codex", null, null, null, null, worker));
        await Run(writer, taskId, new CompleteRunCommand(actor, null, "c18c", new RunId("RW1"), AgentRunStatus.Completed, "session-w1"));
        await Run(writer, taskId, new StartRunCommand(actor, null, "c19", new RunId("RV2"), new WorkItemId("W6"), "claude", null, null, null, null, verifier));
        await Run(writer, taskId, Artifact(verifier, "c19a", "A-RV2", GovernedArtifactKind.VerifierOutput, new WorkItemId("W6"), new RunId("RV2")));
        await Run(writer, taskId, new CompleteRunCommand(actor, null, "c20", new RunId("RV2"), AgentRunStatus.Completed, "session-4"));
        await Run(writer, taskId, new CompleteWorkItemCommand(actor, null, "c21", new WorkItemId("W6")));

        // The waived completion: its reason is a new field on an existing event, so it has to
        // serialise, come back, and pass the replay copy of the rules.
        await Run(writer, taskId, new AddWorkItemCommand(actor, null, "c22", new WorkItemId("W7"), "Unverifiable work", actor, [], [Area("w7")]));
        await Run(writer, taskId, new CompleteWorkItemCommand(actor, null, "c23", new WorkItemId("W7"), "No verifier is attached to this task"));

        // A separate service instance models a separate process: nothing but events.jsonl carries over.
        var replayed = await Service(root.Path).GetStateAsync(taskId, CancellationToken.None);

        Assert.NotNull(replayed);
        Assert.Equal(ConstraintStatus.Active, replayed.Constraints[new ConstraintId("K1")].Status);
        Assert.Equal(ConstraintStatus.Superseded, replayed.Constraints[new ConstraintId("K2")].Status);
        Assert.Equal(EscalationStatus.Resolved, replayed.Escalations[new EscalationId("X1")].Status);
        Assert.Equal("The platform account", replayed.Escalations[new EscalationId("X1")].Resolution);
        Assert.Equal(EscalationStatus.Open, replayed.Escalations[new EscalationId("X2")].Status);
        Assert.Equal("ship", replayed.Escalations[new EscalationId("X2")].Recommendation);
        Assert.Equal("Files stay inspectable", replayed.Alternatives[new AlternativeId("ALT1")].RejectionRationale);
        Assert.Equal(WorkItemStatus.Blocked, replayed.WorkItems[new WorkItemId("W1")].Status);
        Assert.Equal("Waiting on the operator", replayed.WorkItems[new WorkItemId("W1")].BlockReason);
        Assert.Equal(WorkItemStatus.Completed, replayed.WorkItems[new WorkItemId("W2")].Status);
        Assert.Equal(WorkItemStatus.Paused, replayed.WorkItems[new WorkItemId("W3")].Status);
        Assert.Null(replayed.WorkItems[new WorkItemId("W3")].BlockReason);
        // The dispatched run survives replay with both identities intact, and the subject still
        // holds no run authority of its own.
        Assert.Equal(reviewer, replayed.Runs[new RunId("R2")].ActorId);
        Assert.Equal(actor, replayed.Runs[new RunId("R2")].LaunchedBy);
        Assert.Null(replayed.Runs[new RunId("R1")].LaunchedBy);
        Assert.DoesNotContain(Capability.ManageRuns, replayed.Roles[reviewer].Capabilities);
        // Each run carries the role it ran under, dispatched or not, and that is what the
        // completion gate reads back after a replay.
        Assert.Equal(RoleKind.Verifier, replayed.Runs[new RunId("RV1")].SubjectRole);
        Assert.Equal(RoleKind.CodeReviewer, replayed.Runs[new RunId("R2")].SubjectRole);
        Assert.Equal(RoleKind.Operator, replayed.Runs[new RunId("R1")].SubjectRole);
        // Each inspecting run's output came back with the run and the work item it belongs to.
        Assert.Equal(new RunId("RV0"), replayed.Artifacts[new ArtifactId("A-RV0")].ProducerRunId);
        Assert.Equal(new WorkItemId("W4"), replayed.Artifacts[new ArtifactId("A-R2")].WorkItemId);
        Assert.Equal(GovernedArtifactKind.CodeReviewOutput, replayed.Artifacts[new ArtifactId("A-R2")].Kind);
        Assert.Equal(WorkItemStatus.Abandoned, replayed.WorkItems[new WorkItemId("W5")].Status);
        Assert.Equal("The split was wrong", replayed.WorkItems[new WorkItemId("W5")].AbandonReason);
        Assert.Equal(WorkItemStatus.Completed, replayed.WorkItems[new WorkItemId("W6")].Status);
        Assert.Equal(WorkItemStatus.Completed, replayed.WorkItems[new WorkItemId("W7")].Status);

        // The waiver reason is not projected into state, so the log itself is the only place it can
        // be read back — and it is the only record that a completion skipped verification.
        var events = await File.ReadAllTextAsync(Path.Combine(root.Path, taskId.Value, "events.jsonl"));
        Assert.Contains("work.abandoned", events, StringComparison.Ordinal);
        Assert.Contains("No verifier is attached to this task", events, StringComparison.Ordinal);

        var projection = await File.ReadAllTextAsync(Path.Combine(root.Path, taskId.Value, "task.md"));
        Assert.Contains("Stay local", projection, StringComparison.Ordinal);
        Assert.Contains("Ship now or harden first?", projection, StringComparison.Ordinal);
        Assert.DoesNotContain("Old rule", projection, StringComparison.Ordinal);
        var decisions = await File.ReadAllTextAsync(Path.Combine(root.Path, taskId.Value, "decisions.md"));
        Assert.Contains("Use a database", decisions, StringComparison.Ordinal);
    }

    // A3: a log written before the state model grew must still replay. The new dictionaries are
    // appended, so an older events.jsonl carries no new event types and lands on empty collections.
    [Fact]
    public async Task AnEventLogWithoutTheNewEventTypesStillReplays()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("legacy-task");
        var actor = new ActorId("operator");
        var writer = Service(root.Path);
        await Run(writer, taskId, new OpenTaskCommand(actor, null, "c1", taskId, "Task", "Goal"));
        await Run(writer, taskId, new AddClaimCommand(actor, null, "c2", new ClaimId("C1"), "Still true", null));

        var eventsPath = Path.Combine(root.Path, taskId.Value, "events.jsonl");
        var lines = await File.ReadAllLinesAsync(eventsPath);
        File.Delete(Path.Combine(root.Path, taskId.Value, "state.json"));

        var replayed = await Service(root.Path).GetStateAsync(taskId, CancellationToken.None);

        Assert.NotNull(replayed);
        Assert.All(lines, line => Assert.DoesNotContain("escalation.", line, StringComparison.Ordinal));
        Assert.Equal("Still true", replayed.Claims[new ClaimId("C1")].Statement);
        Assert.Empty(replayed.Escalations);
        Assert.Empty(replayed.Alternatives);
        Assert.Empty(replayed.Constraints);
    }

    // R4 (refinement-repoint-replay-parity): the refinement derivation lives in both rule copies.
    // This drives both supersession outcomes and a supported challenge through the real store,
    // then reads state back from a fresh service so the replay copy has to agree.
    [Fact]
    public async Task R4_SupersessionAndChallengeConsequencesReplayThroughTheFileStore()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("supersession-task");
        var actor = new ActorId("operator");
        string Area(string name) => Directory.CreateDirectory(Path.Combine(root.Path, name)).FullName;
        var writer = Service(root.Path);

        await Run(writer, taskId, new OpenTaskCommand(actor, null, "s1", taskId, "Task", "Goal"));
        await ContextBrief.RecordAsync(writer, taskId.Value);
        await Run(writer, taskId, new AddClaimCommand(actor, null, "s2", new ClaimId("C1"), "The API is stable", null));
        await Run(writer, taskId, new AddClaimCommand(actor, null, "s3", new ClaimId("C2"), "The API is stable below 200 rps", null));
        await Run(writer, taskId, new AddEvidenceCommand(actor, null, "s4", new EvidenceId("E2"), "probe", "cite", "supports C2", [new ClaimId("C2")], []));
        await Run(writer, taskId, new AddWorkItemCommand(actor, null, "s5", new WorkItemId("W1"), "Build it", actor, [new ClaimId("C1")], [Area("w1")]));
        await Run(writer, taskId, new ResolveClaimCommand(actor, null, "s6", new ClaimId("C2"), ClaimStatus.Validated, [new EvidenceId("E2")]));
        // Earned refinement: C2 is validated and nothing refutes C1.
        await Run(writer, taskId, new ResolveClaimCommand(actor, null, "s7", new ClaimId("C1"), ClaimStatus.Superseded, [], new ClaimId("C2")));

        // A correction on a second chain, driven by refuting evidence.
        await Run(writer, taskId, new AddClaimCommand(actor, null, "s8", new ClaimId("C3"), "Old belief", null));
        await Run(writer, taskId, new AddClaimCommand(actor, null, "s9", new ClaimId("C4"), "Replacement belief", null));
        await Run(writer, taskId, new AddEvidenceCommand(actor, null, "s10", new EvidenceId("E3"), "probe", "cite", "refutes C3", [], [new ClaimId("C3")]));
        await Run(writer, taskId, new AddWorkItemCommand(actor, null, "s11", new WorkItemId("W2"), "Other work", actor, [new ClaimId("C3")], [Area("w2")]));
        await Run(writer, taskId, new ResolveClaimCommand(actor, null, "s12", new ClaimId("C3"), ClaimStatus.Superseded, [], new ClaimId("C4")));

        // A supported challenge against a decision, whose consequence is a distinct event type.
        await Run(writer, taskId, new ProposeDecisionCommand(actor, null, "s13", new DecisionId("D1"), "Use it", "Because C2", [new ClaimId("C2")], null));
        await Run(writer, taskId, new AddEvidenceCommand(actor, null, "s13b", new EvidenceId("E4"), "probe", "cite", "the decision does not hold", [], []));
        await Run(writer, taskId, new RaiseChallengeCommand(actor, null, "s14", new ChallengeId("CH1"), "decision", "D1", "It does not hold", [new EvidenceId("E4")]));
        await Run(writer, taskId, new DisposeChallengeCommand(actor, null, "s15", new ChallengeId("CH1"), ChallengeStatus.Supported));

        var replayed = await Service(root.Path).GetStateAsync(taskId, CancellationToken.None);

        Assert.NotNull(replayed);
        Assert.Equal(new ClaimId("C2"), replayed.Claims[new ClaimId("C1")].SupersededByClaimId);
        Assert.Equal([new ClaimId("C2")], replayed.WorkItems[new WorkItemId("W1")].DependsOnClaims);
        Assert.Equal(WorkItemStatus.Proposed, replayed.WorkItems[new WorkItemId("W1")].Status);
        Assert.Equal([new ClaimId("C3")], replayed.WorkItems[new WorkItemId("W2")].DependsOnClaims);
        Assert.Equal(WorkItemStatus.Stale, replayed.WorkItems[new WorkItemId("W2")].Status);
        Assert.Equal(DecisionStatus.Invalidated, replayed.Decisions[new DecisionId("D1")].Status);

        var assumptions = await File.ReadAllTextAsync(Path.Combine(root.Path, taskId.Value, "assumptions.md"));
        Assert.Contains("Superseded by `C2`", assumptions, StringComparison.Ordinal);
    }

    // A verifier's or reviewer's output, the way the agent inside the run files it: it names the
    // run that produced it and the work item that run was dispatched against.
    private static RecordArtifactCommand Artifact(
        ActorId actor,
        string correlation,
        string artifactId,
        GovernedArtifactKind kind,
        WorkItemId workItemId,
        RunId producerRun) =>
        new(
            actor, null, correlation, new ArtifactId(artifactId), kind, "Governed document",
            kind == GovernedArtifactKind.VerifierOutput
                ? ArtifactCommands.VerifierBody
                : $"Findings from {producerRun}",
            workItemId, producerRun, null);

    private static FileGovernedTaskService Service(string root)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(root, new CommandHandler(reducer, new AuthorizationPolicy()), reducer);
    }

    private static Task<CommandOutcome> Run(IGovernedTaskService service, TaskId taskId, LedgerCommand command) =>
        service.ExecuteAsync(taskId, ContextBrief.WithServedSkills(command), CancellationToken.None);
}
