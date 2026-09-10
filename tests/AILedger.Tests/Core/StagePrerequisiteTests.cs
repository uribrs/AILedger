using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

// The kernel used to hold the coordinator's eleven phases as stages and drive nothing from them, so
// a task could name a stage it had never engaged with. Each arm now asks for state that only
// engagement produces: a topic to research, an approach discarded, a contract, a plan and the roles
// the rest of the walk needs, work and its three documents, a pass that did the work, something to
// repair, a verifier's findings, a reviewer's, and a lesson worth carrying out.
//
// Every arm here is command-time. StagePrerequisiteReplayBoundaryTests pins that none of them
// reaches replay, because a methodology the operator is still changing must not make a history that
// was legal when written unreadable.
public sealed class StagePrerequisiteTests
{
    private static readonly DateTimeOffset When = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CommandTimeRefusesAStagePrerequisiteWaiverFromANonOperator()
    {
        var task = new TestTask();
        var lead = new ActorId("lead");
        task.Assign(lead, RoleKind.ImplementationLead, Capability.RequestTransition);
        var handler = new CommandHandler(new NonValidatingReducer(), new AuthorizationPolicy());

        var refusal = Assert.Throws<GovernanceException>(() => handler.Handle(
            task.State,
            new RequestStageTransitionCommand(
                lead, null, "waive-command-time", TaskStage.Research, "The arm is inapplicable"),
            When));

        Assert.Equal("Only an operator can transition stages without prerequisites.", refusal.Message);
    }

    [Fact]
    public void ReplayRefusesAStagePrerequisiteWaiverFromANonOperator()
    {
        var task = new TestTask();
        var lead = new ActorId("lead");
        task.Assign(lead, RoleKind.ImplementationLead, Capability.RequestTransition);
        var forged = new LedgerEvent(
            GovernedTaskState.CurrentSchemaVersion,
            new EventId($"{task.TaskId.Value}:{task.State.Version + 1:D10}"),
            task.TaskId,
            lead,
            When,
            null,
            "waive-replay",
            new StagePrerequisitesWaived(TaskStage.Research, "The arm is inapplicable"));

        var refusal = Assert.Throws<GovernanceException>(() => new TaskReducer().Apply(task.State, forged));

        Assert.Equal("Only an operator can transition stages without prerequisites.", refusal.Message);
    }

    [Fact]
    public void EnteringResearchRequiresAnOpenClaim()
    {
        var task = new TestTask();

        var refusal = Assert.Throws<GovernanceException>(() => task.Transition(TaskStage.Research));

        Assert.Contains("claim", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TaskStage.Discovery, task.State.Stage);

        task.Apply(new AddClaimCommand(
            task.OperatorId, null, task.NextCorrelation(), new ClaimId("C1"),
            "The adapter discards the provider session on failure", null));
        task.Transition(TaskStage.Research);

        Assert.Equal(TaskStage.Research, task.State.Stage);
    }

    // The arm asks for an open claim, not for a claim. A task whose every assumption is already
    // settled has nothing left to research, and this is the shape that breaks a walk which resolves
    // its one claim before it transitions.
    [Fact]
    public void AClaimAlreadyResolvedIsNotAResearchTopic()
    {
        var task = new TestTask();
        var claimId = new ClaimId("C1");
        var evidenceId = new EvidenceId("E1");
        task.Apply(new AddClaimCommand(
            task.OperatorId, null, task.NextCorrelation(), claimId, "The retry is bounded", null));
        task.Apply(new AddEvidenceCommand(
            task.OperatorId, null, task.NextCorrelation(), evidenceId, "test-run", "RetryTests.Bounded",
            "The production policy stopped after three attempts", [claimId], []));
        task.Apply(new ResolveClaimCommand(
            task.OperatorId, null, task.NextCorrelation(), claimId, ClaimStatus.Validated, [evidenceId]));

        var refusal = Assert.Throws<GovernanceException>(() => task.Transition(TaskStage.Research));

        Assert.Contains("claim", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TaskStage.Discovery, task.State.Stage);
    }

    // Discovery is the one target with nothing to prove. Research is where a task finds out that its
    // framing was wrong, and the way back must not demand evidence of the stage being left.
    [Fact]
    public void ReturningToDiscoveryFromResearchAsksForNothing()
    {
        var task = new TestTask();
        task.ReachStage(TaskStage.Research);

        task.Transition(TaskStage.Discovery);

        Assert.Equal(TaskStage.Discovery, task.State.Stage);
    }

    [Fact]
    public void EnteringDesignRequiresACompletedResearcherRunAndAnApproachOnTheRecord()
    {
        var task = new TestTask();
        task.RecordResearchTopic();
        task.Transition(TaskStage.Research);

        var withoutResearcher = Assert.Throws<GovernanceException>(() => task.Transition(TaskStage.Design));
        Assert.Contains(nameof(RoleKind.Researcher), withoutResearcher.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TaskStage.Research, task.State.Stage);

        task.RecordResearcherPass();
        var withoutApproach = Assert.Throws<GovernanceException>(() => task.Transition(TaskStage.Design));
        Assert.Contains("alternative", withoutApproach.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TaskStage.Research, task.State.Stage);

        task.RecordDiscardedAlternative();
        task.Transition(TaskStage.Design);

        Assert.Equal(TaskStage.Design, task.State.Stage);
    }

    // The other half of the Design arm. A task that took a decision rather than discarding an
    // approach has designed something too, and a proposal nobody accepted has not.
    [Fact]
    public void AnAcceptedDecisionSatisfiesDesignWithNothingDiscarded()
    {
        var task = new TestTask();
        task.RecordResearchTopic();
        task.Transition(TaskStage.Research);
        task.RecordResearcherPass();
        var decisionId = new DecisionId("D1");
        task.Apply(new ProposeDecisionCommand(
            task.OperatorId, null, task.NextCorrelation(), decisionId,
            "Gate the stages at command time",
            "Replay must accept every history that was legal when it was written", [], null));

        var proposedOnly = Assert.Throws<GovernanceException>(() => task.Transition(TaskStage.Design));
        Assert.Contains("alternative", proposedOnly.Message, StringComparison.OrdinalIgnoreCase);

        task.Apply(new ResolveDecisionCommand(
            task.OperatorId, null, task.NextCorrelation(), decisionId, DecisionStatus.Accepted));
        task.Transition(TaskStage.Design);

        Assert.Equal(TaskStage.Design, task.State.Stage);
        Assert.Empty(task.State.Alternatives);
    }

    [Fact]
    public void EnteringScopeRequiresACurrentPromptContract()
    {
        var task = new TestTask();
        task.ReachStage(TaskStage.Design);

        var refusal = Assert.Throws<GovernanceException>(() => task.Transition(TaskStage.Scope));

        Assert.Contains(nameof(GovernedArtifactKind.PromptContract), refusal.Message, StringComparison.Ordinal);
        Assert.Equal(TaskStage.Design, task.State.Stage);

        task.RecordPromptContract();
        task.Transition(TaskStage.Scope);

        Assert.Equal(TaskStage.Scope, task.State.Stage);
    }

    [Fact]
    public void EnteringReadyRequiresACurrentOrchestrationPlanAndTheRolesTheWalkStillNeeds()
    {
        var task = new TestTask();
        task.ReachStage(TaskStage.Design);
        task.RecordPromptContract();
        task.Transition(TaskStage.Scope);

        var withoutPlan = Assert.Throws<GovernanceException>(() => task.Transition(TaskStage.Ready));
        Assert.Contains(nameof(GovernedArtifactKind.OrchestrationPlan), withoutPlan.Message, StringComparison.Ordinal);
        Assert.Equal(TaskStage.Scope, task.State.Stage);

        task.RecordOrchestrationPlan();
        var withoutVerifier = Assert.Throws<GovernanceException>(() => task.Transition(TaskStage.Ready));
        Assert.Contains(nameof(RoleKind.Verifier), withoutVerifier.Message, StringComparison.Ordinal);
        Assert.Equal(TaskStage.Scope, task.State.Stage);

        // This task holds no work item at all, so it cannot yet be known to touch code — and the
        // reviewer is still required. R4 pins the same rule against work that is plainly prose.
        task.Assign(new ActorId("verifier"), RoleKind.Verifier, Capability.BuildContext, Capability.RecordArtifact);
        var withoutReviewer = Assert.Throws<GovernanceException>(() => task.Transition(TaskStage.Ready));
        Assert.Contains(nameof(RoleKind.CodeReviewer), withoutReviewer.Message, StringComparison.Ordinal);
        Assert.Equal(TaskStage.Scope, task.State.Stage);

        task.StaffRemainingStages();
        task.Transition(TaskStage.Ready);

        Assert.Equal(TaskStage.Ready, task.State.Stage);
        Assert.Empty(task.State.WorkItems);
    }

    // The arm asks whether the Execution step was engaged, so it reads the two roles that step is
    // given: Worker and ImplementationLead. It used to read "any role that is not a verifier or a
    // reviewer", and the researcher run the Design arm already demanded satisfied that, so a task
    // could enter Verification on planning alone with no execution having happened. This walk holds
    // exactly that history — a completed researcher pass and two completed planning-lead passes for
    // the task-wide documents — and nothing that did the work.
    [Fact]
    public void EnteringVerificationRequiresACompletedRunThatDidTheWork()
    {
        var task = new TestTask();
        task.ReachStage(TaskStage.Execution);
        var planningOnly = task.State.Runs.Values
            .Where(run => run.Status == AgentRunStatus.Completed)
            .Select(run => run.SubjectRole)
            .ToArray();
        Assert.NotEmpty(planningOnly);
        Assert.All(planningOnly, role => Assert.True(role is RoleKind.Researcher or RoleKind.PlanningLead));

        var refusal = Assert.Throws<GovernanceException>(() => task.Transition(TaskStage.Verification));
        Assert.Contains(nameof(RoleKind.Worker), refusal.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(RoleKind.ImplementationLead), refusal.Message, StringComparison.Ordinal);
        Assert.Equal(TaskStage.Execution, task.State.Stage);

        task.RecordWorkingPass(task.StageWorkItem(), "RW-execution");
        task.Transition(TaskStage.Verification);

        Assert.Equal(TaskStage.Verification, task.State.Stage);
        Assert.Equal(RoleKind.Worker, task.State.Runs[new RunId("RW-execution")].SubjectRole);
    }

    // The other half of the arm: an implementation lead is the second role contract-driven-execution
    // is given, so a task where the lead did the work rather than dispatching a worker passes too.
    [Fact]
    public void AnImplementationLeadRunAlsoSatisfiesVerification()
    {
        var task = new TestTask();
        task.ReachStage(TaskStage.Execution);
        var lead = new ActorId("impl-lead");
        task.Assign(lead, RoleKind.ImplementationLead, Capability.BuildContext);
        var run = new RunId("RI-execution");
        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), run, task.StageWorkItem(), "codex",
            null, null, null, null, lead));
        task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), run, AgentRunStatus.Completed, "session-RI"));

        task.Transition(TaskStage.Verification);

        Assert.Equal(TaskStage.Verification, task.State.Stage);
        Assert.DoesNotContain(task.State.Runs.Values, item => item.SubjectRole == RoleKind.Worker);
    }

    [Fact]
    public void EnteringRepairRequiresSomethingToRepair()
    {
        var task = new TestTask();
        task.ReachStage(TaskStage.Verification);

        var refusal = Assert.Throws<GovernanceException>(() => task.Transition(TaskStage.Repair));
        Assert.Contains("challenge", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TaskStage.Verification, task.State.Stage);

        // The verifier's findings are the other half of the arm: the pass that read the work is
        // itself the record of what there is to repair.
        task.RecordVerifierPass(task.StageWorkItem());
        task.Transition(TaskStage.Repair);

        Assert.Equal(TaskStage.Repair, task.State.Stage);
    }

    [Fact]
    public void AnOpenChallengeIsEnoughToEnterRepair()
    {
        var task = new TestTask();
        task.ReachStage(TaskStage.Verification);
        task.Apply(new RaiseChallengeCommand(
            task.OperatorId, null, task.NextCorrelation(), new ChallengeId("CH1"), "claim", "C-topic",
            "The topic was never settled", []));

        task.Transition(TaskStage.Repair);

        Assert.Equal(TaskStage.Repair, task.State.Stage);
        Assert.DoesNotContain(
            task.State.Artifacts.Values,
            artifact => artifact.Kind == GovernedArtifactKind.VerifierOutput);
    }

    [Fact]
    public void EnteringReviewRequiresACompletedVerifierRun()
    {
        var task = new TestTask();
        task.ReachStage(TaskStage.Verification);

        var refusal = Assert.Throws<GovernanceException>(() => task.Transition(TaskStage.Review));
        Assert.Contains(nameof(RoleKind.Verifier), refusal.Message, StringComparison.Ordinal);
        Assert.Equal(TaskStage.Verification, task.State.Stage);

        task.RecordVerifierPass(task.StageWorkItem());
        task.Transition(TaskStage.Review);

        Assert.Equal(TaskStage.Review, task.State.Stage);
    }

    [Fact]
    public void EnteringLearnRequiresACodeReviewerRunWhenAWorkItemDeclaresAnArea()
    {
        var task = CodeBearing();
        task.ReachStage(TaskStage.Review);

        var refusal = Assert.Throws<GovernanceException>(() => task.Transition(TaskStage.Learn));
        Assert.Contains(nameof(RoleKind.CodeReviewer), refusal.Message, StringComparison.Ordinal);
        Assert.Equal(TaskStage.Review, task.State.Stage);

        task.RecordCodeReviewerPass(task.StageWorkItem());
        task.Transition(TaskStage.Learn);

        Assert.Equal(TaskStage.Learn, task.State.Stage);
    }

    // The arm reads every work item, whatever its status, and this is the case that rule exists for.
    // A task reaches Learn after its work is finished, so a predicate over live items only would be
    // false here — the scoped item is Completed — and the gate that exists to force a code review
    // would be skipped by the ordinary act of completing the work first.
    [Fact]
    public void CompletingTheScopedWorkDoesNotRelieveLearnOfItsCodeReview()
    {
        var task = CodeBearing();
        var work = new WorkItemId("W1");
        task.ReachStage(TaskStage.Review);
        // The item carries both runs completion requires by now: the walk recorded a working pass
        // before Verification and a verifier pass before Review, both against this item.
        task.Apply(new CompleteWorkItemCommand(task.OperatorId, null, task.NextCorrelation(), work));

        var refusal = Assert.Throws<GovernanceException>(() => task.Transition(TaskStage.Learn));

        Assert.Contains(nameof(RoleKind.CodeReviewer), refusal.Message, StringComparison.Ordinal);
        Assert.Equal(TaskStage.Review, task.State.Stage);
        Assert.Equal(WorkItemStatus.Completed, task.State.WorkItems[work].Status);
        Assert.DoesNotContain(
            task.State.WorkItems.Values,
            item => item.ResourceScope.Count > 0 && item.Status is not
                (WorkItemStatus.Completed or WorkItemStatus.Stale or WorkItemStatus.Abandoned));
    }

    // Work is code-bearing when a work item declares a directory area, so a task whose subject is
    // prose closes without a code review. Requiring one anyway is how a documentation-only change
    // ends up waived, and a waiver in the log reads as a decision nobody made.
    [Fact]
    public void EnteringLearnNeedsNoCodeReviewerRunWhenNoWorkItemDeclaresAnArea()
    {
        var task = new TestTask();
        task.ReachStage(TaskStage.Review);

        task.Transition(TaskStage.Learn);

        Assert.Equal(TaskStage.Learn, task.State.Stage);
        Assert.DoesNotContain(task.State.WorkItems.Values, item => item.ResourceScope.Count > 0);
        Assert.DoesNotContain(task.State.Runs.Values, run => run.SubjectRole == RoleKind.CodeReviewer);
    }

    [Fact]
    public void EnteringArchiveRequiresEveryRunClosed()
    {
        var task = new TestTask();
        task.ReachStage(TaskStage.Learn);
        task.Apply(new MarkLessonBearingCommand(
            task.OperatorId, null, task.NextCorrelation(), LessonSourceKind.RejectedAlternative, "ALT-stage",
            Class: LessonClass.Refuted, Repo: "AILedger", Tags: ["stages"],
            Verify: "dotnet test --filter StagePrerequisiteTests",
            DoNot: "Do not bypass the governed stage walk", Actor: LessonActor.Verifier,
            VerifyExpects: VerifyExpectation.Present));
        var open = new RunId("R-open");
        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), open, task.StageWorkItem(), "codex", null));

        var refusal = Assert.Throws<GovernanceException>(() => task.Transition(TaskStage.Archive));
        Assert.Contains("active", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TaskStage.Learn, task.State.Stage);

        task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), open, AgentRunStatus.Completed, "session-R-open"));
        task.Transition(TaskStage.Archive);

        Assert.Equal(TaskStage.Archive, task.State.Stage);
        Assert.Single(task.State.Lessons);
    }

    // Constraint K23. The Archive target appends its waiver on a line of its own, after the lessons
    // it mints; every other target returns the waiver from the branch above, so a test that waives
    // into Research says nothing about this one. This is also the branch the operator archives real
    // tasks through, which made it the waiver path with the most use and the least cover.
    [Fact]
    public void WaivingTheArchiveArmRecordsTheReasonBesideTheLessonsItMints()
    {
        var task = new TestTask();
        task.ReachStage(TaskStage.Learn);
        task.Apply(new MarkLessonBearingCommand(
            task.OperatorId, null, task.NextCorrelation(), LessonSourceKind.RejectedAlternative, "ALT-stage",
            Class: LessonClass.Refuted, Repo: "AILedger", Tags: ["stages"],
            Verify: "dotnet test --filter StagePrerequisiteTests",
            DoNot: "Do not bypass the governed stage walk", Actor: LessonActor.Verifier,
            VerifyExpects: VerifyExpectation.Present));
        // An active run is what the Archive arm refuses on, so the waiver is what carries the
        // transition below rather than a prerequisite that was satisfied anyway.
        var open = new RunId("R-open");
        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), open, task.StageWorkItem(), "codex", null));
        var refusal = Assert.Throws<GovernanceException>(() => task.Transition(TaskStage.Archive));
        Assert.Contains("active", refusal.Message, StringComparison.OrdinalIgnoreCase);

        var outcome = task.Apply(new RequestStageTransitionCommand(
            task.OperatorId, null, task.NextCorrelation(), TaskStage.Archive,
            "The run died with its work landed and its session cannot be resumed"));

        var waiver = outcome.Events.Single(item => item.Data is StagePrerequisitesWaived);
        var transition = outcome.Events.Single(item => item.Data is StageTransitioned);
        var waived = (StagePrerequisitesWaived)waiver.Data;
        Assert.Equal(TaskStage.Archive, task.State.Stage);
        Assert.Equal("The run died with its work landed and its session cannot be resumed", waived.Reason);
        Assert.Equal(TaskStage.Archive, waived.TargetStage);
        // The waiver does not skip the lesson: minting still runs, and the waiver is appended after
        // the lessons, so the transition cites it. A reader following the causation chain back from
        // the archive finds which arm was skipped and why, not only that a transition happened.
        Assert.Single(outcome.Events.Where(item => item.Data is LessonMinted));
        Assert.Equal(waiver.EventId, transition.CausationId);
        Assert.Single(task.State.Lessons);
    }

    // R2 (superseded-stage-evidence). A withdrawn document must satisfy nothing, which is a rule
    // about how each arm reads state rather than about what it asks for: CurrentArtifacts, never
    // state.Artifacts.
    [Fact]
    public void R2_OnlyCurrentArtifactsSatisfyArtifactBackedStagePrerequisites()
    {
        // Through the commands: a revised contract satisfies Scope, so a task that rewrote its
        // contract is not held to the draft it replaced.
        var revised = new TestTask();
        revised.ReachStage(TaskStage.Design);
        revised.RecordPromptContract();
        var revisionRun = revised.StartGoverningRun("R-revision");
        revised.Apply(ArtifactCommands.Record(
            revised, revised.GoverningLead(), "A-contract-2", GovernedArtifactKind.PromptContract,
            "The revised contract", producerRun: revisionRun, supersedes: "A-contract"));

        revised.Transition(TaskStage.Scope);
        Assert.Equal(TaskStage.Scope, revised.State.Stage);

        // The direction that matters. No legal history can leave a kind with no current member —
        // command time and replay both refuse a revision that does not supersede the one in place —
        // so the withdrawn shape is built here and put through the real command.
        var withoutContract = Withdraw(
            revised.State with { Stage = TaskStage.Design },
            new ArtifactId("A-contract-2"),
            GovernedArtifactKind.UserRequest);
        var scopeRefusal = Assert.Throws<GovernanceException>(() => new CommandHandler().Handle(
            withoutContract,
            new RequestStageTransitionCommand(
                revised.OperatorId, null, "withdrawn-contract", TaskStage.Scope),
            When));
        Assert.Contains(nameof(GovernedArtifactKind.PromptContract), scopeRefusal.Message, StringComparison.Ordinal);
        Assert.Contains(new ArtifactId("A-contract-2"), withoutContract.Artifacts.Keys);

        // And the same for the findings the Repair arm reads.
        var repaired = new TestTask();
        repaired.ReachStage(TaskStage.Verification);
        repaired.RecordVerifierPass(repaired.StageWorkItem());
        var findings = repaired.State.Artifacts.Values.Single(artifact =>
            artifact.Kind == GovernedArtifactKind.VerifierOutput);
        var withoutFindings = Withdraw(
            repaired.State, findings.ArtifactId, GovernedArtifactKind.CodeReviewOutput);

        var repairRefusal = Assert.Throws<GovernanceException>(() => new CommandHandler().Handle(
            withoutFindings,
            new RequestStageTransitionCommand(
                repaired.OperatorId, null, "withdrawn-findings", TaskStage.Repair),
            When));
        Assert.Contains("challenge", repairRefusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    // R3 (fabricated-role-engagement). Engagement is a property of a run that finished, not of who
    // holds which role now.
    [Fact]
    public void R3_OnlyCompletedRunsWithCapturedRolesSatisfyEngagementPrerequisites()
    {
        var task = new TestTask();
        task.ReachStage(TaskStage.Verification);
        var work = task.StageWorkItem();
        var verifier = new ActorId("verifier");

        // A pass that died read nothing, so it verified nothing.
        var failed = new RunId("RV-failed");
        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), failed, work, "claude",
            null, null, null, null, verifier));
        task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), failed, AgentRunStatus.Failed, null));

        var afterFailure = Assert.Throws<GovernanceException>(() => task.Transition(TaskStage.Review));
        Assert.Contains(nameof(RoleKind.Verifier), afterFailure.Message, StringComparison.Ordinal);

        // Neither does reassigning the actor whose completed run was something else. The run holds
        // the role its subject had when it started; the present assignment answers another question.
        task.Assign(new ActorId("researcher"), RoleKind.Verifier, Capability.BuildContext, Capability.RecordArtifact);
        var afterReassignment = Assert.Throws<GovernanceException>(() => task.Transition(TaskStage.Review));
        Assert.Contains(nameof(RoleKind.Verifier), afterReassignment.Message, StringComparison.Ordinal);
        Assert.Equal(TaskStage.Verification, task.State.Stage);

        // The pass that did complete under the verifier role opens the gate, and keeps it open after
        // its subject is reassigned: what the arm reads is the run.
        task.RecordVerifierPass(work);
        task.Assign(verifier, RoleKind.Worker, Capability.BuildContext);
        task.Transition(TaskStage.Review);

        Assert.Equal(TaskStage.Review, task.State.Stage);
        Assert.Equal(RoleKind.Worker, task.State.Roles[verifier].Role);
        Assert.Equal(RoleKind.Verifier, task.State.Runs[new RunId("RV")].SubjectRole);
    }

    // R4 (ready-staffing-misclassification). The roles a task needs at Ready come from the stages it
    // must still walk, so nothing is declared and nothing can disagree with the sequence. The
    // reviewer is part of that staffing whatever the work turns out to touch: Ready is reached
    // before the first work item exists, so a conditional reading of it never fired.
    [Fact]
    public void R4_ReadyRequiresDistinctActorsAndACodeReviewerWhateverTheWorkTouches()
    {
        var code = CodeBearing();
        code.ReachStage(TaskStage.Scope);
        // The plan is the other half of the Ready arm and is pinned separately, so it is recorded
        // here to leave the staffing as the only thing missing.
        code.RecordOrchestrationPlan();

        var withoutVerifier = Assert.Throws<GovernanceException>(() => code.Transition(TaskStage.Ready));
        Assert.Contains(nameof(RoleKind.Verifier), withoutVerifier.Message, StringComparison.Ordinal);

        var checker = new ActorId("checker");
        code.Assign(checker, RoleKind.Verifier, Capability.BuildContext, Capability.RecordArtifact);
        var withoutReviewer = Assert.Throws<GovernanceException>(() => code.Transition(TaskStage.Ready));
        Assert.Contains(nameof(RoleKind.CodeReviewer), withoutReviewer.Message, StringComparison.Ordinal);

        // One actor cannot cover both roles: an assignment moves an actor rather than adding a role,
        // so the task that tries it loses the verifier it just had.
        code.Assign(checker, RoleKind.CodeReviewer, Capability.BuildContext, Capability.RecordArtifact);
        var notDistinct = Assert.Throws<GovernanceException>(() => code.Transition(TaskStage.Ready));
        Assert.Contains(nameof(RoleKind.Verifier), notDistinct.Message, StringComparison.Ordinal);
        Assert.Equal(TaskStage.Scope, code.State.Stage);

        code.Assign(new ActorId("second-checker"), RoleKind.Verifier, Capability.BuildContext, Capability.RecordArtifact);
        code.Transition(TaskStage.Ready);
        Assert.Equal(TaskStage.Ready, code.State.Stage);

        // Prose work is staffed the same way. A task at Ready has not yet done the work that would
        // tell it whether the work touches code, so the reviewer is required here too and the one
        // command it costs is the price of the gate at Learn being reachable at all.
        var prose = new TestTask();
        prose.Apply(Work(prose, new WorkItemId("W1"), []));
        prose.ReachStage(TaskStage.Scope);
        prose.RecordOrchestrationPlan();
        prose.Assign(new ActorId("checker"), RoleKind.Verifier, Capability.BuildContext, Capability.RecordArtifact);

        var proseWithoutReviewer = Assert.Throws<GovernanceException>(() => prose.Transition(TaskStage.Ready));
        Assert.Contains(nameof(RoleKind.CodeReviewer), proseWithoutReviewer.Message, StringComparison.Ordinal);
        Assert.Equal(TaskStage.Scope, prose.State.Stage);
        Assert.DoesNotContain(prose.State.WorkItems.Values, item => item.ResourceScope.Count > 0);

        prose.Assign(new ActorId("prose-reviewer"), RoleKind.CodeReviewer, Capability.BuildContext);
        prose.Transition(TaskStage.Ready);
        Assert.Equal(TaskStage.Ready, prose.State.Stage);

        // An area handed back is still an area this task declared, so the item that declared it
        // keeps the task code-bearing. Reading only live items would forget it.
        var released = CodeBearing();
        released.ReachStage(TaskStage.Scope);
        released.RecordOrchestrationPlan();
        released.Apply(new AbandonWorkItemCommand(
            released.OperatorId, null, released.NextCorrelation(), new WorkItemId("W1"),
            "Superseded by a narrower split; the area is now a separate item"));
        released.StaffRemainingStages();
        released.Transition(TaskStage.Ready);

        Assert.Equal(TaskStage.Ready, released.State.Stage);
        Assert.Equal(WorkItemStatus.Abandoned, released.State.WorkItems[new WorkItemId("W1")].Status);
    }

    // R5 (partial-stage-matrix). Every TaskStage is accounted for, and a refusal moves nothing: a
    // transition that fails must leave the task where it was, or the arm has already let the task
    // past the stage it was refusing.
    [Fact]
    public void R5_AllStagePrerequisitesUseProductionCommandsAndLeaveStageUnchangedOnRefusal()
    {
        // One entry per stage, so a TaskStage added later fails here rather than reaching the
        // pipeline with no arm behind it. A null arrangement is a stage this test cannot refuse and
        // names the test that pins it instead.
        var covered = new Dictionary<TaskStage, Func<TestTask>?>
        {
            // Nothing to prove: Discovery is where a task goes when its framing was wrong.
            [TaskStage.Discovery] = null,
            [TaskStage.Research] = () => new TestTask(),
            [TaskStage.Design] = () => AtStage(TaskStage.Research),
            [TaskStage.Scope] = () => AtStage(TaskStage.Design),
            [TaskStage.Ready] = () => AtStage(TaskStage.Scope),
            [TaskStage.Execution] = () => AtStage(TaskStage.Ready),
            // A task at Execution has a completed researcher run and two completed planning-lead
            // runs, and not one of them did the work, so the arm refuses it.
            [TaskStage.Verification] = () => AtStage(TaskStage.Execution),
            [TaskStage.Repair] = () => AtStage(TaskStage.Verification),
            [TaskStage.Review] = () => AtStage(TaskStage.Verification),
            [TaskStage.Learn] = () => Reached(CodeBearing(), TaskStage.Review),
            [TaskStage.Archive] = () => AtStage(TaskStage.Learn)
        };

        Assert.Equal(
            Enum.GetValues<TaskStage>().OrderBy(stage => stage),
            covered.Keys.OrderBy(stage => stage));

        foreach (var (stage, arrange) in covered.Where(entry => entry.Value is not null))
        {
            var task = arrange!();
            var before = task.State.Stage;

            var refusal = Assert.Throws<GovernanceException>(() => task.Transition(stage));

            Assert.False(
                string.IsNullOrWhiteSpace(refusal.Message),
                $"Entering {stage} was refused without saying what was missing.");
            Assert.Equal(before, task.State.Stage);
        }
    }

    private static TestTask AtStage(TaskStage stage) => Reached(new TestTask(), stage);

    private static TestTask Reached(TestTask task, TaskStage stage)
    {
        task.ReachStage(stage);
        return task;
    }

    // A task whose live work item declares a directory area, which is what makes work code-bearing.
    private static TestTask CodeBearing()
    {
        var task = new TestTask();
        task.Apply(Work(task, new WorkItemId("W1"), [Path.GetFullPath("src")]));
        return task;
    }

    private static AddWorkItemCommand Work(TestTask task, WorkItemId workItemId, string[] scope) =>
        new(task.OperatorId, null, task.NextCorrelation(), workItemId, "Build", null, [], scope);

    // The shape a withdrawal would take. The document stays on the record and something later names
    // it as its predecessor, so nothing current carries its kind.
    private static GovernedTaskState Withdraw(
        GovernedTaskState state,
        ArtifactId artifactId,
        GovernedArtifactKind withdrawingKind)
    {
        var taskWide = withdrawingKind is not
            (GovernedArtifactKind.VerifierOutput or GovernedArtifactKind.CodeReviewOutput);
        var predecessor = state.Artifacts[artifactId];
        var withdrawal = predecessor with
        {
            ArtifactId = new ArtifactId($"{artifactId.Value}-withdrawn"),
            Kind = withdrawingKind,
            WorkItemId = taskWide ? null : predecessor.WorkItemId,
            ProducerRunId = withdrawingKind == GovernedArtifactKind.UserRequest ? null : predecessor.ProducerRunId,
            SupersedesArtifactId = artifactId
        };

        return state with
        {
            Artifacts = state.Artifacts.Values.Append(withdrawal).ToDictionary(artifact => artifact.ArtifactId)
        };
    }

    private sealed class NonValidatingReducer : ITaskReducer
    {
        public GovernedTaskState Apply(GovernedTaskState? state, LedgerEvent @event) =>
            state! with { Version = state.Version + 1 };
    }
}
