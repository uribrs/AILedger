using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Tests.Support;

internal sealed class TestTask
{
    private static readonly DateTimeOffset Epoch = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    // The forward walk through the pipeline. Every backward edge is legal too, but a test that
    // stages its way to a later rule only ever goes forwards; the backward edges are pinned by the
    // tests whose subject they are.
    private static readonly TaskStage[] ForwardPipeline =
    [
        TaskStage.Research,
        TaskStage.Design,
        TaskStage.Scope,
        TaskStage.Ready,
        TaskStage.Execution,
        TaskStage.Verification,
        TaskStage.Review,
        TaskStage.Learn,
        TaskStage.Archive
    ];

    private readonly CommandHandler _handler = new();
    private int _commandNumber;

    public TestTask(string taskId = "task-1", string operatorId = "operator")
    {
        TaskId = new TaskId(taskId);
        OperatorId = new ActorId(operatorId);
        Apply(new OpenTaskCommand(OperatorId, null, NextCorrelation(), TaskId, "Test task", "Prove governed execution"));
    }

    public TaskId TaskId { get; }
    public ActorId OperatorId { get; }
    public GovernedTaskState State { get; private set; } = null!;

    // Work is code-bearing when any work item declares a directory area, whatever its status. The
    // status was in this predicate once and made it non-monotonic: Ready is read before the first
    // item exists and Learn after the scoped ones are completed, so a live-only reading was false at
    // exactly the two moments the kernel consults it. Nothing reads the spelling of a path.
    public bool IsCodeBearing => State.WorkItems.Values.Any(item => item.ResourceScope.Count > 0);

    // Adding work and launching a provider are refused until the acting actor has been briefed.
    // Staged here for the same reason ReachStage stages the stage arms: a test whose subject is
    // some other rule should not have to restate this one. The tests that pin the gate itself turn
    // it off and record the brief, or withhold it, deliberately.
    public bool AutoBuildContext { get; set; } = true;

    // The other half of the same staging. The gate compares the brief the ledger recorded against
    // what the cognitive layer serves the actor now, and a command carrying nothing to compare is
    // refused — a brief that cannot be checked is not a current brief. A test driving the handler
    // by hand has no cognitive layer, so this answers the way an unchanged one would: with the
    // digests the recorded brief already holds. The tests whose subject is the comparison itself
    // pass their own list, or turn this off to withhold one.
    public bool AutoServeSkills { get; set; } = true;

    public CommandOutcome Apply(LedgerCommand command)
    {
        if (AutoBuildContext && NeedsBrief(command))
        {
            BuildContext(command.ActorId);
        }

        if (AutoServeSkills)
        {
            command = WithSkillsServedNow(command);
        }

        var outcome = _handler.Handle(State, command, Epoch.AddMinutes(_commandNumber));
        State = outcome.State;
        return outcome;
    }

    // The two skills the operator's own role is defined by, which is what the cognitive layer
    // serves the seat that decomposes the work. The hashes stand in for the content: nothing in the
    // kernel reads a skill's text, only whether the digest still matches what the caller found.
    public CommandOutcome BuildContext(ActorId actorId, params string[] skills) =>
        Apply(new RecordContextBuiltCommand(
            actorId,
            null,
            NextCorrelation(),
            null,
            (skills.Length == 0 ? ["workflow-coordinator", "task-orchestrator"] : skills)
                .Select(name => new ContextSkill(name, $"hash-of-{name}"))
                .ToArray()));

    // Only for a command the gate reads, and only when it names nothing itself.
    private LedgerCommand WithSkillsServedNow(LedgerCommand command) =>
        command switch
        {
            AddWorkItemCommand { SkillsServedNow: null } add =>
                add with { SkillsServedNow = RecordedBrief(add.ActorId) },
            StartRunCommand { LaunchTokenHash: not null, SkillsServedNow: null } start =>
                start with { SkillsServedNow = RecordedBrief(start.ActorId) },
            _ => command
        };

    // Null when the actor has never been briefed, which leaves the gate's presence check to speak
    // first — the refusal that names the command to run.
    private IReadOnlyList<ContextSkill>? RecordedBrief(ActorId actorId) =>
        State.ContextBuilds.TryGetValue(actorId, out var build) ? build.Skills : null;

    private bool NeedsBrief(LedgerCommand command) =>
        command switch
        {
            AddWorkItemCommand => !State.ContextBuilds.ContainsKey(command.ActorId),
            StartRunCommand { LaunchTokenHash: not null } => !State.ContextBuilds.ContainsKey(command.ActorId),
            _ => false
        };

    public string NextCorrelation() => $"correlation-{++_commandNumber}";

    // Recall has no command: the durable service produces the event itself while opening a task, and
    // the replay rule accepts it only into a task that holds nothing but its opening role. So a test
    // that needs a recalled lesson reduces the event straight onto the opened state, and must do it
    // before anything else the test records.
    public void RecallLesson(Lesson lesson)
    {
        State = new TaskReducer().Apply(State, new LedgerEvent(
            GovernedTaskState.CurrentSchemaVersion,
            new EventId($"recall-{lesson.Id.Value}"),
            TaskId,
            OperatorId,
            Epoch.AddMinutes(_commandNumber),
            null,
            NextCorrelation(),
            new LessonRecalled(lesson)));
    }

    public CommandOutcome Transition(TaskStage stage) =>
        Apply(new RequestStageTransitionCommand(OperatorId, null, NextCorrelation(), stage));

    public void Assign(ActorId actorId, RoleKind role, params Capability[] capabilities) =>
        Apply(new AssignRoleCommand(
            OperatorId,
            null,
            NextCorrelation(),
            actorId,
            role,
            capabilities));

    // Every stage transition now requires state that only engagement with that stage produces, so
    // a test whose subject is a later rule stages the whole walk here rather than restating nine
    // transitions and the evidence each one asks for. Each step records only what its own arm
    // needs, so a test can stop one stage short and find exactly one prerequisite missing.
    public void ReachStage(TaskStage target)
    {
        var index = Array.IndexOf(ForwardPipeline, target);
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(target), target, "Repair and Discovery are reached by a backward edge, not by the forward walk.");
        }

        foreach (var stage in ForwardPipeline.Take(index + 1))
        {
            StageEvidenceFor(stage);
            Transition(stage);
        }
    }

    // What the arm for one stage asks for, recorded through the same commands an operator would
    // use. Archive is deliberately absent: its lesson mark is the subject of its own tests.
    public void StageEvidenceFor(TaskStage stage)
    {
        switch (stage)
        {
            case TaskStage.Research:
                RecordResearchTopic();
                break;
            case TaskStage.Design:
                RecordResearcherPass();
                RecordDiscardedAlternative();
                break;
            case TaskStage.Scope:
                RecordPromptContract();
                break;
            case TaskStage.Ready:
                RecordOrchestrationPlan();
                StaffRemainingStages();
                break;
            case TaskStage.Execution:
                StageWorkItem();
                RecordUserRequest();
                break;
            case TaskStage.Verification:
                EnsureCompletedWorkingRun();
                break;
            case TaskStage.Review:
                EnsureCompletedVerifierRun();
                break;
            case TaskStage.Learn:
                if (IsCodeBearing)
                {
                    RecordCodeReviewerPass(StageWorkItem());
                }

                break;
            default:
                break;
        }
    }

    // An unresolved claim is exactly what there is to research, so entering Research asks for one.
    public void RecordResearchTopic(string claimId = "C-topic")
    {
        if (State.Claims.Values.Any(claim => claim.Status == ClaimStatus.Open))
        {
            return;
        }

        Apply(new AddClaimCommand(
            OperatorId, null, NextCorrelation(), new ClaimId(claimId),
            "The stage arms are reachable from state the kernel already holds",
            "The arms would need records nothing writes"));
    }

    // Leaving Research asks what the research produced: an approach discarded, or a decision taken.
    public void RecordDiscardedAlternative(string alternativeId = "ALT-stage")
    {
        if (State.Alternatives.Count != 0 ||
            State.Decisions.Values.Any(decision => decision.Status == DecisionStatus.Accepted))
        {
            return;
        }

        Apply(new RecordAlternativeCommand(
            OperatorId, null, NextCorrelation(), new AlternativeId(alternativeId),
            "Walk the stages without recording what was considered",
            "The next actor would re-propose the approach this one discarded", null));
    }

    // A research pass holds no directory area, so its run names no work item. The Design arm reads
    // the role the run captured, which is what a task-wide run still records.
    public RunId RecordResearcherPass(string runId = "RR-stage", WorkItemId? workItemId = null)
    {
        var researcher = new ActorId("researcher");
        if (!State.Roles.ContainsKey(researcher))
        {
            Assign(researcher, RoleKind.Researcher, Capability.BuildContext, Capability.AddClaim, Capability.AddEvidence);
        }

        var run = new RunId(runId);
        Apply(new StartRunCommand(
            OperatorId, null, NextCorrelation(), run, workItemId, "codex", null, null, null, null, researcher));
        Apply(new CompleteRunCommand(
            OperatorId, null, NextCorrelation(), run, AgentRunStatus.Completed, $"session-{runId}"));
        return run;
    }

    // The roles the stages still to come will need, each on its own actor. The code reviewer is
    // staffed unconditionally: at Ready the task does not yet know whether it will touch code, and
    // staffing a role that goes unused costs one command.
    public void StaffRemainingStages()
    {
        if (!State.Roles.Values.Any(assignment => assignment.Role == RoleKind.Verifier))
        {
            Assign(new ActorId("verifier"), RoleKind.Verifier, Capability.BuildContext, Capability.RecordArtifact);
        }

        if (!State.Roles.Values.Any(assignment => assignment.Role == RoleKind.CodeReviewer))
        {
            Assign(new ActorId("reviewer"), RoleKind.CodeReviewer, Capability.BuildContext, Capability.RecordArtifact);
        }
    }

    // A work item can no longer be completed until a verifier has passed over it. Tests that pin
    // some other rule still have to get past that gate, so they record the pass the way the kernel
    // does: an operator dispatches a run to an actor holding the verifier role, and closes it.
    public RunId RecordVerifierPass(WorkItemId workItemId, string runId = "RV") =>
        RecordPass(workItemId, runId, new ActorId("verifier"), RoleKind.Verifier);

    // Completion also requires a completed run by a role that does the work, so an item nobody ever
    // worked on cannot be declared finished. Staged the same way, under a worker.
    public RunId RecordWorkingPass(WorkItemId workItemId, string runId = "RW") =>
        RecordPass(workItemId, runId, new ActorId("worker"), RoleKind.Worker);

    // The third pass, which only code-bearing work needs. It cannot start until a verifier run has
    // completed against the latest work on the item, so it is staged after the other two.
    public RunId RecordCodeReviewerPass(WorkItemId workItemId, string runId = "RCR") =>
        RecordPass(workItemId, runId, new ActorId("reviewer"), RoleKind.CodeReviewer);

    // Both runs a completion needs. Tests whose subject is some other rule call this and stop
    // caring how many gates completion has grown; only the tests that pin a gate stage one alone.
    public void RecordRequiredRuns(WorkItemId workItemId)
    {
        RecordWorkingPass(workItemId);
        RecordVerifierPass(workItemId);
    }

    // Entering Execution now requires a current user request, prompt contract and orchestration
    // plan, so a test walking the stages to reach some later rule records the three the operator
    // would have recorded. Only ArtifactGateTests stages them one at a time, because only it is
    // pinning the gate itself.
    public void RecordExecutionArtifacts()
    {
        RecordUserRequest();
        RecordPromptContract();
        RecordOrchestrationPlan();
    }

    public void RecordUserRequest(string artifactId = "A-request")
    {
        if (State.Artifacts.Values.Any(artifact => artifact.Kind == GovernedArtifactKind.UserRequest))
        {
            return;
        }

        Apply(ArtifactCommands.Record(this, OperatorId, artifactId, GovernedArtifactKind.UserRequest));
    }

    public void RecordPromptContract(string artifactId = "A-contract")
    {
        if (State.Artifacts.Values.Any(artifact => artifact.Kind == GovernedArtifactKind.PromptContract))
        {
            return;
        }

        var lead = GoverningLead();
        var run = StartGoverningRun("R-contract");
        Apply(ArtifactCommands.Record(this, lead, artifactId, GovernedArtifactKind.PromptContract, producerRun: run));
        Apply(new CompleteRunCommand(
            OperatorId, null, NextCorrelation(), run, AgentRunStatus.Completed, "session-R-contract"));
    }

    public void RecordOrchestrationPlan(string artifactId = "A-plan")
    {
        if (State.Artifacts.Values.Any(artifact => artifact.Kind == GovernedArtifactKind.OrchestrationPlan))
        {
            return;
        }

        var lead = GoverningLead();
        var run = StartGoverningRun("R-plan");
        Apply(ArtifactCommands.Record(
            this, lead, artifactId, GovernedArtifactKind.OrchestrationPlan,
            ArtifactCommands.PlanBody, producerRun: run));
        Apply(new CompleteRunCommand(
            OperatorId, null, NextCorrelation(), run, AgentRunStatus.Completed, "session-R-plan"));
    }

    // The lead that records the task-wide documents.
    public ActorId GoverningLead()
    {
        var lead = new ActorId("artifact-lead");
        if (!State.Roles.ContainsKey(lead))
        {
            Assign(lead, RoleKind.PlanningLead, Capability.BuildContext, Capability.RecordArtifact);
        }

        return lead;
    }

    // A producer run for the task-wide documents, left active because the artifact command requires
    // its producer run to still be open. It names no work item: the three documents govern the whole
    // task, and a run that held a directory area would make every task that records them
    // code-bearing.
    public RunId StartGoverningRun(string runId)
    {
        var run = new RunId(runId);
        Apply(new StartRunCommand(
            OperatorId, null, NextCorrelation(), run, null, "codex", null, null, null, null, GoverningLead()));
        return run;
    }

    // The item the walk's own runs are recorded against. It declares no directory area, so it never
    // decides on its own whether the task is code-bearing; the test's own work items do that.
    public WorkItemId StageWorkItem()
    {
        // The test's own item when it can carry a run: one nobody has released, that no run can be
        // refused on, and whose dependent claims a verifier output would have to dispose.
        var usable = State.WorkItems.Values
            .Where(item => item.Status is not (WorkItemStatus.Completed or WorkItemStatus.Stale or
                       WorkItemStatus.Abandoned or WorkItemStatus.Blocked) &&
                   item.DependsOnClaims.Count == 0)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .ToArray();
        if (usable.Length != 0)
        {
            return usable[0].Id;
        }

        var workItemId = new WorkItemId("W-stage");
        if (!State.WorkItems.ContainsKey(workItemId))
        {
            Apply(new AddWorkItemCommand(
                OperatorId, null, NextCorrelation(), workItemId, "Walk the governed stages", null, [], []));
        }

        return workItemId;
    }

    public RunId StartArtifactProducer(ActorId actor, RoleKind role, string runId)
    {
        if (!State.Roles.ContainsKey(actor))
        {
            Assign(actor, role, Capability.BuildContext, Capability.RecordArtifact);
        }

        var workItemId = new WorkItemId("W-artifacts");
        if (!State.WorkItems.ContainsKey(workItemId))
        {
            Apply(new AddWorkItemCommand(
                OperatorId, null, NextCorrelation(), workItemId, "Prepare workflow artifacts",
                actor, [], [Path.GetFullPath("artifact-preparation")]));
        }

        var run = new RunId(runId);
        Apply(new StartRunCommand(
            OperatorId, null, NextCorrelation(), run, workItemId, "codex",
            null, null, null, null, actor));
        return run;
    }

    // Entering Verification asks whether anyone has finished a pass that did the work. The two roles
    // the skill map gives contract-driven-execution answer that; a researcher's or a lead's planning
    // pass does not, which is why the walk records one of its own here.
    private void EnsureCompletedWorkingRun()
    {
        var worked = State.Runs.Values.Any(run =>
            run.Status == AgentRunStatus.Completed &&
            run.SubjectRole is RoleKind.Worker or RoleKind.ImplementationLead);
        if (!worked)
        {
            RecordWorkingPass(StageWorkItem(), "RW-stage");
        }
    }

    private void EnsureCompletedVerifierRun()
    {
        var verified = State.Runs.Values.Any(run =>
            run.Status == AgentRunStatus.Completed && run.SubjectRole == RoleKind.Verifier);
        if (!verified)
        {
            RecordVerifierPass(StageWorkItem(), "RV-stage");
        }
    }

    private RunId RecordPass(WorkItemId workItemId, string runId, ActorId subject, RoleKind role)
    {
        if (!State.Roles.ContainsKey(subject))
        {
            Assign(subject, role, Capability.BuildContext, Capability.RecordArtifact);
        }

        var run = new RunId(runId);
        var provider = role is RoleKind.Verifier ? "claude" : "codex";
        if (role == RoleKind.Verifier)
        {
            RecordExecutionArtifacts();
        }
        Apply(new StartRunCommand(
            OperatorId, null, NextCorrelation(), run, workItemId, provider, null, null, null, null, subject));
        // A verifier or code reviewer cannot close its run without the output artifact that run
        // produced, so the pass includes the finding it was dispatched to write. The run is still
        // active here, which is what makes the artifact's producer run a matching one.
        if (OutputKindFor(role) is { } outputKind)
        {
            var superseded = State.Artifacts.Values
                .Where(artifact => artifact.SupersedesArtifactId is not null)
                .Select(artifact => artifact.SupersedesArtifactId!.Value)
                .ToHashSet();
            var predecessor = State.Artifacts.Values.SingleOrDefault(artifact =>
                artifact.Kind == outputKind && artifact.WorkItemId == workItemId &&
                !superseded.Contains(artifact.ArtifactId));
            Apply(ArtifactCommands.Record(
                this, subject, $"A-{runId}", outputKind,
                role == RoleKind.Verifier ? ArtifactCommands.VerifierBody : ArtifactCommands.Body,
                workItem: workItemId, producerRun: run,
                supersedes: predecessor?.ArtifactId.Value));
        }

        Apply(new CompleteRunCommand(
            OperatorId, null, NextCorrelation(), run, AgentRunStatus.Completed, $"session-{runId}"));
        return run;
    }

    private static GovernedArtifactKind? OutputKindFor(RoleKind role) => role switch
    {
        RoleKind.Verifier => GovernedArtifactKind.VerifierOutput,
        RoleKind.CodeReviewer => GovernedArtifactKind.CodeReviewOutput,
        _ => null
    };
}
