using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Stages;

// The entry-action matrix is the dual of the stage arms: the record consumed on entry to a stage
// is produced in the stage immediately before it. These tests drive CommandHandler directly, with
// the stage-aware fixture used only to arrange the unrelated authority and producer-run state.
public sealed class EntryActionStageTests
{
    private static readonly DateTimeOffset When = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void R1_ProducerMatrixUsesThePrecedingStage()
    {
        var allStages = Enum.GetValues<TaskStage>();
        var probes = ProducerProbes();

        Assert.Equal(12, probes.Count);
        foreach (var probe in probes)
        {
            foreach (var stage in allStages)
            {
                var (state, command) = probe.Arrange();
                state = state with { Stage = stage };

                if (probe.Admitted.Contains(stage))
                {
                    var outcome = new CommandHandler().Handle(state, command, When);
                    Assert.NotEmpty(outcome.Events);
                    continue;
                }

                var refusal = Assert.Throws<GovernanceException>(() =>
                    new CommandHandler().Handle(state, command, When));
                Assert.Contains(probe.ActionName, refusal.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Contains($"'{stage}'", refusal.Message, StringComparison.Ordinal);
                Assert.All(probe.Admitted, admitted =>
                    Assert.Contains($"'{admitted}'", refusal.Message, StringComparison.Ordinal));
            }
        }
    }

    [Fact]
    public void R2_EpistemicAndCorrectiveCommandsRemainAvailable()
    {
        foreach (var stage in Enum.GetValues<TaskStage>())
        {
            var task = new TestTask();
            var handler = new CommandHandler();
            var claim = new ClaimId($"C-{stage}");
            var evidence = new EvidenceId($"E-{stage}");
            var state = task.State with { Stage = stage };

            state = handler.Handle(state, new AddClaimCommand(
                task.OperatorId, null, $"claim-{stage}", claim,
                "Epistemic records stay writable throughout the pipeline", null), When).State;
            state = handler.Handle(state, new AddEvidenceCommand(
                task.OperatorId, null, $"evidence-{stage}", evidence, "test-run",
                $"EntryActionStageTests:{stage}", "The production handler accepted the evidence", [claim], []),
                When.AddMinutes(1)).State;

            Assert.Contains(claim, state.Claims.Keys);
            Assert.Contains(evidence, state.Evidence.Keys);
        }

        // Exercise the other named setup and corrective channels at the most restrictive stage.
        // Each command is valid on its own terms, so success proves it was not swept into the entry
        // gate instead of merely proving that some earlier validation happened to speak first.
        var corrective = new TestTask();
        var direct = new CommandHandler();
        var current = corrective.State with { Stage = TaskStage.Archive };
        var claimId = new ClaimId("C-corrective");
        var evidenceId = new EvidenceId("E-corrective");

        current = direct.Handle(current, new AddClaimCommand(
            corrective.OperatorId, null, "claim", claimId, "A correction is needed", null), When).State;
        current = direct.Handle(current, new AddEvidenceCommand(
            corrective.OperatorId, null, "evidence", evidenceId, "test-run", "EntryActionStageTests:R2",
            "The correction was reproduced", [claimId], []), When.AddMinutes(1)).State;
        current = direct.Handle(current, new ResolveClaimCommand(
            corrective.OperatorId, null, "resolve", claimId, ClaimStatus.Validated, [evidenceId]),
            When.AddMinutes(2)).State;
        current = direct.Handle(current, new AssignRoleCommand(
            corrective.OperatorId, null, "assign", new ActorId("late-worker"), RoleKind.Worker,
            [Capability.BuildContext]), When.AddMinutes(3)).State;
        current = direct.Handle(current, new RecordContextBuiltCommand(
            corrective.OperatorId, null, "context", null,
            [new ContextSkill("workflow-coordinator", "hash")]), When.AddMinutes(4)).State;
        current = direct.Handle(current, new RecordAlternativeCommand(
            corrective.OperatorId, null, "alternative", new AlternativeId("ALT-corrective"),
            "Leave the finding outside the record", "The next actor could not recover it", null),
            When.AddMinutes(5)).State;
        current = direct.Handle(current, new RaiseChallengeCommand(
            corrective.OperatorId, null, "challenge", new ChallengeId("CH-corrective"), "claim",
            claimId.Value, "The conclusion needs an independent challenge", [evidenceId]),
            When.AddMinutes(6)).State;
        current = direct.Handle(current, new RaiseEscalationCommand(
            corrective.OperatorId, null, "escalate", new EscalationId("X-corrective"),
            EscalationKind.BusinessDecision, "Which governed option should be used?", null,
            ["Option A", "Option B"], "Option A", []), When.AddMinutes(7)).State;

        Assert.Equal(ClaimStatus.Validated, current.Claims[claimId].Status);
        Assert.Contains(new ActorId("late-worker"), current.Roles.Keys);
        Assert.Contains(new AlternativeId("ALT-corrective"), current.Alternatives.Keys);
        Assert.Contains(new ChallengeId("CH-corrective"), current.Challenges.Keys);
        Assert.Contains(new EscalationId("X-corrective"), current.Escalations.Keys);

        var completion = new TestTask();
        var run = new RunId("R-late");
        completion.Apply(new StartRunCommand(
            completion.OperatorId, null, completion.NextCorrelation(), run, null, "codex", null));
        var completed = direct.Handle(
            completion.State with { Stage = TaskStage.Archive },
            new CompleteRunCommand(
                completion.OperatorId, null, "complete", run, AgentRunStatus.Completed, "session-R-late"),
            When).State;
        Assert.Equal(AgentRunStatus.Completed, completed.Runs[run].Status);
    }

    [Fact]
    public void R3_BackwardLoopsAdmitTheirCorrectiveProducer()
    {
        var contract = new TestTask(placeEntryStages: false);
        contract.ReachStage(TaskStage.Execution);
        contract.Transition(TaskStage.Design);
        var contractRun = contract.StartGoverningRun("R-contract-revision");
        contract.Apply(ArtifactCommands.Record(
            contract, contract.GoverningLead(), "A-contract-2", GovernedArtifactKind.PromptContract,
            producerRun: contractRun, supersedes: "A-contract"));
        Assert.Equal(TaskStage.Design, contract.State.Stage);
        Assert.Equal(new ArtifactId("A-contract"),
            contract.State.Artifacts[new ArtifactId("A-contract-2")].SupersedesArtifactId);

        var plan = new TestTask(placeEntryStages: false);
        plan.ReachStage(TaskStage.Execution);
        plan.Transition(TaskStage.Scope);
        var planRun = plan.StartGoverningRun("R-plan-revision");
        plan.Apply(ArtifactCommands.Record(
            plan, plan.GoverningLead(), "A-plan-2", GovernedArtifactKind.OrchestrationPlan,
            ArtifactCommands.PlanBody, producerRun: planRun, supersedes: "A-plan"));
        Assert.Equal(TaskStage.Scope, plan.State.Stage);
        Assert.Equal(new ArtifactId("A-plan"),
            plan.State.Artifacts[new ArtifactId("A-plan-2")].SupersedesArtifactId);

        var repair = new TestTask(placeEntryStages: false);
        repair.ReachStage(TaskStage.Verification);
        var work = repair.StageWorkItem();
        repair.RecordVerifierPass(work, "RV-repair");
        repair.Transition(TaskStage.Repair);
        var repairRun = new RunId("RW-repair");
        repair.Apply(new StartRunCommand(
            repair.OperatorId, null, repair.NextCorrelation(), repairRun, work, "codex",
            null, null, null, null, new ActorId("worker")));

        Assert.Equal(TaskStage.Repair, repair.State.Stage);
        Assert.Equal(AgentRunStatus.Active, repair.State.Runs[repairRun].Status);
        Assert.Equal(RoleKind.Worker, repair.State.Runs[repairRun].SubjectRole);
    }

    private static IReadOnlyList<ProducerProbe> ProducerProbes() =>
    [
        new("artifact record --kind UserRequest",
            [TaskStage.Discovery, TaskStage.Research, TaskStage.Design, TaskStage.Scope, TaskStage.Ready],
            ArrangeUserRequest),
        new("artifact record --kind PromptContract", [TaskStage.Design], ArrangePromptContract),
        new("artifact record --kind OrchestrationPlan", [TaskStage.Design, TaskStage.Scope], ArrangePlan),
        new("work add", [TaskStage.Ready], ArrangeWork),
        new("run start for Researcher", [TaskStage.Research], () => ArrangeRun(RoleKind.Researcher)),
        new("run start for Worker", [TaskStage.Execution, TaskStage.Repair], () => ArrangeRun(RoleKind.Worker)),
        new("run start for Verifier", [TaskStage.Verification], () => ArrangeRun(RoleKind.Verifier)),
        new("run start for CodeReviewer", [TaskStage.Review], ArrangeReviewerRun),
        new("artifact record --kind VerifierOutput", [TaskStage.Verification], ArrangeVerifierOutput),
        new("artifact record --kind CodeReviewOutput", [TaskStage.Review], ArrangeCodeReviewOutput),
        new("lesson mark", [TaskStage.Learn], ArrangeLessonMark),
        new("workflow retrospective", [TaskStage.Archive], ArrangeRetrospective)
    ];

    private static (GovernedTaskState State, LedgerCommand Command) ArrangeUserRequest()
    {
        var task = new TestTask();
        return (task.State, ArtifactCommands.Record(
            task, task.OperatorId, "A-probe", GovernedArtifactKind.UserRequest));
    }

    private static (GovernedTaskState State, LedgerCommand Command) ArrangePromptContract()
    {
        var task = new TestTask();
        var run = task.StartGoverningRun("R-probe-contract");
        return (task.State, ArtifactCommands.Record(
            task, task.GoverningLead(), "A-probe-contract", GovernedArtifactKind.PromptContract,
            producerRun: run));
    }

    private static (GovernedTaskState State, LedgerCommand Command) ArrangePlan()
    {
        var task = new TestTask();
        var run = task.StartGoverningRun("R-probe-plan");
        return (task.State, ArtifactCommands.Record(
            task, task.GoverningLead(), "A-probe-plan", GovernedArtifactKind.OrchestrationPlan,
            ArtifactCommands.PlanBody, producerRun: run));
    }

    private static (GovernedTaskState State, LedgerCommand Command) ArrangeWork()
    {
        var task = new TestTask();
        task.BuildContext(task.OperatorId);
        return (task.State, new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W-probe"),
            "Prove the producer matrix", null, [], [],
            SkillsServedNow: task.State.ContextBuilds[task.OperatorId].Skills));
    }

    private static (GovernedTaskState State, LedgerCommand Command) ArrangeRun(RoleKind role)
    {
        var task = new TestTask();
        var subject = new ActorId("subject-" + role);
        task.Assign(subject, role, Capability.BuildContext, Capability.RecordArtifact);
        WorkItemId? work = null;
        if (role != RoleKind.Researcher)
        {
            work = new WorkItemId("W-probe");
            task.Apply(new AddWorkItemCommand(
                task.OperatorId, null, task.NextCorrelation(), work.Value, "Probe the run gate", null, [], []));
        }

        return (task.State, new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R-probe"), work, "codex",
            null, null, null, null, subject));
    }

    private static (GovernedTaskState State, LedgerCommand Command) ArrangeReviewerRun()
    {
        var task = new TestTask();
        var work = task.StageWorkItem();
        task.RecordWorkingPass(work, "RW-probe");
        task.RecordVerifierPass(work, "RV-probe");
        task.Assign(new ActorId("probe-reviewer"), RoleKind.CodeReviewer,
            Capability.BuildContext, Capability.RecordArtifact);
        return (task.State, new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("RCR-probe"), work, "codex",
            null, null, null, null, new ActorId("probe-reviewer")));
    }

    private static (GovernedTaskState State, LedgerCommand Command) ArrangeVerifierOutput()
    {
        var task = new TestTask();
        var work = task.StageWorkItem();
        task.RecordExecutionArtifacts();
        var verifier = new ActorId("probe-verifier");
        task.Assign(verifier, RoleKind.Verifier, Capability.BuildContext, Capability.RecordArtifact);
        var run = new RunId("RV-probe-output");
        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), run, work, "claude",
            null, null, null, null, verifier));
        return (task.State, ArtifactCommands.Record(
            task, verifier, "A-probe-verifier", GovernedArtifactKind.VerifierOutput,
            ArtifactCommands.VerifierBody, workItem: work, producerRun: run));
    }

    private static (GovernedTaskState State, LedgerCommand Command) ArrangeCodeReviewOutput()
    {
        var task = new TestTask();
        var work = task.StageWorkItem();
        task.RecordWorkingPass(work, "RW-probe-output");
        task.RecordVerifierPass(work, "RV-probe-output");
        var reviewer = new ActorId("probe-reviewer");
        task.Assign(reviewer, RoleKind.CodeReviewer, Capability.BuildContext, Capability.RecordArtifact);
        var run = new RunId("RCR-probe-output");
        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), run, work, "codex",
            null, null, null, null, reviewer));
        return (task.State, ArtifactCommands.Record(
            task, reviewer, "A-probe-review", GovernedArtifactKind.CodeReviewOutput,
            workItem: work, producerRun: run));
    }

    private static (GovernedTaskState State, LedgerCommand Command) ArrangeLessonMark()
    {
        var task = new TestTask();
        task.RecordDiscardedAlternative("ALT-probe");
        return (task.State, new MarkLessonBearingCommand(
            task.OperatorId, null, task.NextCorrelation(), LessonSourceKind.RejectedAlternative, "ALT-probe",
            Class: LessonClass.Refuted, Repo: "AILedger", Tags: ["stages"],
            Verify: "ailedger status --task task-1", DoNot: "Do not bypass the stage producer matrix",
            Actor: LessonActor.Verifier, Kind: LessonKind.Workflow, VerifyExpects: VerifyExpectation.Present));
    }

    private static (GovernedTaskState State, LedgerCommand Command) ArrangeRetrospective()
    {
        var task = new TestTask();
        task.ReachStage(TaskStage.Learn);
        foreach (var item in task.State.WorkItems.Values.ToArray())
        {
            task.Apply(new CompleteWorkItemCommand(task.OperatorId, null, task.NextCorrelation(), item.Id));
        }
        task.Apply(new MarkLessonBearingCommand(
            task.OperatorId, null, task.NextCorrelation(), LessonSourceKind.RejectedAlternative, "ALT-stage",
            Class: LessonClass.Refuted, Repo: "AILedger", Tags: ["stages"],
            Verify: "ailedger status --task task-1", DoNot: "Do not bypass the governed stage walk",
            Actor: LessonActor.Verifier, Kind: LessonKind.Workflow, VerifyExpects: VerifyExpectation.Present));
        task.Transition(TaskStage.Archive);

        var rows = string.Join("\n", Enumerable.Range(1, 10).Select(index =>
            $"| D{index} | 3 | high | yes | EntryActionStageTests:R1 |"));
        var body = "# Retrospective\n\n" +
            "| dimension | score | confidence | controllable | evidence |\n" +
            "| --- | --- | --- | --- | --- |\n" + rows + "\n";
        return (task.State, ArtifactCommands.Record(
            task, task.OperatorId, "A-probe-retrospective", GovernedArtifactKind.WorkflowRetrospective, body));
    }

    private sealed record ProducerProbe(
        string ActionName,
        IReadOnlyList<TaskStage> Admitted,
        Func<(GovernedTaskState State, LedgerCommand Command)> Arrange);
}
