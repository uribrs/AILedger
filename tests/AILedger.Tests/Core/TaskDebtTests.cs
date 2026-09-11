using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

// TaskDebt counts what a task still owes. It refuses nothing and scores nothing, so these tests are
// about the counts being honest rather than about any gate.
public sealed class TaskDebtTests
{
    private static readonly LessonId Inherited = new("earlier-task:imported:C9");

    [Fact]
    public void AnOpenClaimWithSupportingEvidenceAndNoRefutationIsCountedAsClearable()
    {
        var state = Opened(out var reducer, out var next, out var actor, out var recordedAt);
        state = WithClaim(state, reducer, next, actor, recordedAt, "C1");
        state = WithClaim(state, reducer, next, actor, recordedAt, "C2");
        state = reducer.Apply(state, next(state, actor, new EvidenceAdded(new Evidence(
            new EvidenceId("E1"), "source-read", "a.cs:1", "supports", [new ClaimId("C1")], [],
            new Provenance(actor, recordedAt, "evidence.add")))));

        var debt = TaskDebt.Compute(state);

        Assert.Equal(2, debt.OpenClaims);
        Assert.Equal(1, debt.OpenClaimsWithSupportingEvidence);
    }

    // A claim with evidence pointing both ways needs a judgement, so it is not offered as clearable.
    [Fact]
    public void AClaimWithEvidenceOnBothSidesIsNotCountedAsClearable()
    {
        var state = Opened(out var reducer, out var next, out var actor, out var recordedAt);
        state = WithClaim(state, reducer, next, actor, recordedAt, "C1");
        state = reducer.Apply(state, next(state, actor, new EvidenceAdded(new Evidence(
            new EvidenceId("E1"), "source-read", "a.cs:1", "supports", [new ClaimId("C1")], [],
            new Provenance(actor, recordedAt, "evidence.add")))));
        state = reducer.Apply(state, next(state, actor, new EvidenceAdded(new Evidence(
            new EvidenceId("E2"), "source-read", "b.cs:2", "refutes", [], [new ClaimId("C1")],
            new Provenance(actor, recordedAt, "evidence.add")))));

        Assert.Equal(0, TaskDebt.Compute(state).OpenClaimsWithSupportingEvidence);
    }

    // The bug live data caught: state.Lessons holds lessons this task minted as well as lessons it
    // inherited, and only the inherited ones can be cited. Counting both made every archived task
    // look like it owed citations forever.
    [Fact]
    public void ALessonThisTaskMintedIsNotCountedAsSomethingItOwesACitationFor()
    {
        var state = Opened(out _, out _, out var actor, out var recordedAt);
        // Minting legally requires the Learn stage and a whole close-out. TaskDebt is a pure
        // projection over state, so the state is composed directly rather than driven there: what
        // is under test is the count, not the route.
        var minted = new Lesson(
            new LessonId("debt-task:validatedclaim:C1"), new TaskId("debt-task"),
            LessonSourceKind.ValidatedClaim, "C1", "Minted here", "Outcome", ["a.cs:1"],
            new Provenance(actor, recordedAt, "stage.archive"), null, LessonClass.Refuted,
            "AILedger", ["tag"], "grep -n x a.cs", "Do not", LessonActor.Verifier);
        state = state with { Lessons = new Dictionary<LessonId, Lesson> { [minted.Id] = minted } };

        var debt = TaskDebt.Compute(state);

        Assert.Equal(0, debt.LessonsRecalled);
        Assert.True(debt.IsClear);
    }

    [Fact]
    public void AnInheritedLessonThatNothingCitesIsCountedAsOwed()
    {
        var state = WithRecalledLesson(out var reducer, out var next, out var actor, out var recordedAt);

        var debt = TaskDebt.Compute(state);

        Assert.Equal(1, debt.LessonsRecalled);
        Assert.Equal(0, debt.LessonsCited);
        Assert.False(debt.IsClear);
    }

    // VC1 from the codex verifier: citing one of several recalled lessons used to clear the block
    // entirely, hiding the rest.
    [Fact]
    public void CitingOneOfSeveralInheritedLessonsDoesNotClearTheRest()
    {
        var state = WithRecalledLesson(out var reducer, out var next, out var actor, out var recordedAt);
        var second = new Lesson(
            new LessonId("earlier-task:imported:C10"), new TaskId("earlier-task"),
            LessonSourceKind.Imported, "C10", "Second inherited belief", "Outcome", ["b.cs:1"],
            new Provenance(actor, recordedAt, "lesson.import"), null, LessonClass.Refuted,
            "AILedger", ["adapter"], "grep -n x b.cs", "Do not", LessonActor.Verifier);
        var lessons = new Dictionary<LessonId, Lesson>(state.Lessons) { [second.Id] = second };
        state = state with { Lessons = lessons };
        state = reducer.Apply(state, next(state, actor, new ClaimAdded(new Claim(
            new ClaimId("C1"), "Prompted by the first lesson", ClaimStatus.Open, [], null,
            new Provenance(actor, recordedAt, "claim.add"), null, Inherited))));

        var debt = TaskDebt.Compute(state);

        Assert.Equal(2, debt.LessonsRecalled);
        Assert.Equal(1, debt.LessonsCited);
        Assert.False(debt.IsClear);
    }

    [Fact]
    public void CitingAnInheritedLessonClearsThatDebt()
    {
        var state = WithRecalledLesson(out var reducer, out var next, out var actor, out var recordedAt);
        state = reducer.Apply(state, next(state, actor, new ClaimAdded(new Claim(
            new ClaimId("C1"), "Prompted by the lesson", ClaimStatus.Open, [], null,
            new Provenance(actor, recordedAt, "claim.add"), null, Inherited))));

        var debt = TaskDebt.Compute(state);

        Assert.Equal(1, debt.LessonsCited);
        Assert.Equal(1, debt.OpenClaims);
    }

    [Fact]
    public void AWorkItemWithAWorkingRunAndNoVerifierRunIsCountedAsAwaitingVerification()
    {
        var state = Opened(out var reducer, out var next, out var actor, out var recordedAt);
        state = reducer.Apply(state, next(state, actor, new WorkItemAdded(new WorkItem(
            new WorkItemId("W1"), "Work", actor, WorkItemStatus.Proposed, [], [Path.GetFullPath("src")]))));
        state = reducer.Apply(state, next(state, actor, new RunStarted(new AgentRun(
            new RunId("R1"), actor, new WorkItemId("W1"), "claude", "s1", AgentRunStatus.Active,
            recordedAt, null, null, null, null, null, RoleKind.ImplementationLead))));
        state = reducer.Apply(state, next(state, actor, new RunCompleted(
            new RunId("R1"), AgentRunStatus.Completed, "s1", recordedAt)));

        Assert.Equal(1, TaskDebt.Compute(state).WorkItemsAwaitingVerification);
    }

    // KC1 from the code reviewer: the debt projection listed working roles positively while the
    // completion gate names the two judging roles negatively, so an item worked by any other role
    // read as clear while work complete refused it.
    [Fact]
    public void AnItemWorkedByARoleTheGateCountsAsWorkIsCountedAsAwaitingVerification()
    {
        var state = Opened(out var reducer, out var next, out var actor, out var recordedAt);
        state = WithWorkItem(state, reducer, next, actor);
        state = WithCompletedRun(state, reducer, next, actor, recordedAt, "R1", RoleKind.PlanningLead, "claude");

        Assert.Equal(1, TaskDebt.Compute(state).WorkItemsAwaitingVerification);
    }

    // KC2, first half: verified, then worked again. The gate refuses this; the projection used to
    // call it clear, which is the repair cycle it exists to cover.
    [Fact]
    public void AnItemVerifiedBeforeItsLatestWorkIsStillCountedAsAwaitingVerification()
    {
        var state = Opened(out var reducer, out var next, out var actor, out var recordedAt);
        state = WithWorkItem(state, reducer, next, actor);
        state = WithCompletedRun(state, reducer, next, actor, recordedAt, "R1", RoleKind.ImplementationLead, "claude");
        state = WithCompletedRun(state, reducer, next, actor, recordedAt, "RV1", RoleKind.Verifier, "codex");
        state = WithCompletedRun(state, reducer, next, actor, recordedAt.AddHours(1), "R2", RoleKind.ImplementationLead, "claude");

        Assert.Equal(1, TaskDebt.Compute(state).WorkItemsAwaitingVerification);
    }

    // KC2, second half: verified by the provider that wrote it. Also refused by the gate.
    [Fact]
    public void AnItemVerifiedByTheProviderThatWroteItIsStillCountedAsAwaitingVerification()
    {
        var state = Opened(out var reducer, out var next, out var actor, out var recordedAt);
        state = WithWorkItem(state, reducer, next, actor);
        state = WithCompletedRun(state, reducer, next, actor, recordedAt, "R1", RoleKind.ImplementationLead, "claude");
        state = WithCompletedRun(state, reducer, next, actor, recordedAt.AddHours(1), "RV1", RoleKind.Verifier, "claude");

        Assert.Equal(1, TaskDebt.Compute(state).WorkItemsAwaitingVerification);
    }

    [Fact]
    public void AnItemVerifiedAfterItsLatestWorkByAnotherProviderIsNotCounted()
    {
        var state = Opened(out var reducer, out var next, out var actor, out var recordedAt);
        state = WithWorkItem(state, reducer, next, actor);
        state = WithCompletedRun(state, reducer, next, actor, recordedAt, "R1", RoleKind.ImplementationLead, "claude");
        state = WithCompletedRun(state, reducer, next, actor, recordedAt.AddHours(1), "RV1", RoleKind.Verifier, "codex");

        Assert.Equal(0, TaskDebt.Compute(state).WorkItemsAwaitingVerification);
    }

    // KC4 from the code reviewer: the cited set was unfiltered while the recalled count excluded
    // minted lessons, so citing a lesson this task produced paid off an inherited lesson's debt.
    [Fact]
    public void CitingALessonThisTaskMintedDoesNotPayOffAnInheritedLessonsDebt()
    {
        var state = WithRecalledLesson(out var reducer, out var next, out var actor, out var recordedAt);
        var mine = new Lesson(
            new LessonId("debt-task:validatedclaim:C9"), new TaskId("debt-task"),
            LessonSourceKind.ValidatedClaim, "C9", "Minted here", "Outcome", ["a.cs:1"],
            new Provenance(actor, recordedAt, "stage.archive"), null, LessonClass.Refuted,
            "AILedger", ["tag"], "grep -n x a.cs", "Do not", LessonActor.Verifier);
        state = state with { Lessons = new Dictionary<LessonId, Lesson>(state.Lessons) { [mine.Id] = mine } };
        state = reducer.Apply(state, next(state, actor, new ClaimAdded(new Claim(
            new ClaimId("C1"), "Cites the task's own lesson", ClaimStatus.Open, [], null,
            new Provenance(actor, recordedAt, "claim.add"), null, mine.Id))));

        var debt = TaskDebt.Compute(state);

        Assert.Equal(1, debt.LessonsRecalled);
        Assert.Equal(0, debt.LessonsCited);
    }

    // KC5: the projection answers the same question work complete does, and nothing asserted the two
    // agree. This drives the real command rather than re-reading the predicates, so a divergence
    // fails here rather than being noticed by an operator meeting a refusal after a clean report.
    [Theory]
    [InlineData(RoleKind.ImplementationLead, "claude", null, null, 1)]
    [InlineData(RoleKind.ImplementationLead, "claude", RoleKind.Verifier, "claude", 1)]
    [InlineData(RoleKind.ImplementationLead, "claude", RoleKind.Verifier, "codex", 0)]
    public void TheDebtProjectionAgreesWithWhatWorkCompleteAccepts(
        RoleKind workRole, string workProvider, RoleKind? verifyRole, string? verifyProvider, int expectedDebt)
    {
        var state = Opened(out var reducer, out var next, out var actor, out var recordedAt);
        state = WithWorkItem(state, reducer, next, actor);
        state = WithCompletedRun(state, reducer, next, actor, recordedAt, "R1", workRole, workProvider);
        if (verifyRole is { } role)
        {
            state = WithCompletedRun(state, reducer, next, actor, recordedAt.AddHours(1), "RV1", role, verifyProvider!);
        }

        var debt = TaskDebt.Compute(state).WorkItemsAwaitingVerification;
        Assert.Equal(expectedDebt, debt);

        var accepted = true;
        try
        {
            new CommandHandler().Handle(state, new CompleteWorkItemCommand(
                actor, null, "c-complete", new WorkItemId("W1"), null), recordedAt.AddHours(2));
        }
        catch (GovernanceException)
        {
            accepted = false;
        }

        // Debt of zero must mean the gate accepts, and any debt must mean it refuses.
        Assert.Equal(debt == 0, accepted);
    }

    // IC1: the retrospective debt is unconditional. An archived task that carries no retrospective
    // owes one on that ground alone, which today is every archived task in this repository.
    [Fact]
    public void AnArchivedTaskWithNoRetrospectiveOwesOne()
    {
        var task = Archived();

        var debt = TaskDebt.Compute(task.State);

        Assert.True(debt.RetrospectiveOwed);
        Assert.False(debt.IsClear);
    }

    [Fact]
    public void AnArchivedTaskThatCarriesARetrospectiveOwesNothing()
    {
        var task = Archived();
        task.Apply(WorkflowRetrospectiveArtifactTests.Retrospective(task, task.OperatorId, "A-retro"));

        var debt = TaskDebt.Compute(task.State);

        Assert.False(debt.RetrospectiveOwed);
        Assert.True(debt.IsClear);
    }

    // Before Archive the kernel refuses a retrospective outright, so a task that has not reached
    // closeout cannot be in arrears for one, whatever else it owes.
    [Fact]
    public void ATaskBeforeArchiveOwesNoRetrospectiveWhateverElseItOwes()
    {
        var state = WithRecalledLesson(out _, out _, out _, out _);

        var debt = TaskDebt.Compute(state);

        Assert.False(debt.RetrospectiveOwed);
        Assert.Equal(1, debt.LessonsRecalled);
        Assert.False(debt.IsClear);
    }

    // The rest of the block is untouched: a task that owes something else still reports exactly what
    // it reported before, and a filed retrospective neither adds to that debt nor clears it.
    [Fact]
    public void ADebtOtherThanTheRetrospectiveIsReportedUnchanged()
    {
        var task = Archived(InheritedLesson());
        task.Apply(WorkflowRetrospectiveArtifactTests.Retrospective(task, task.OperatorId, "A-retro"));

        var debt = TaskDebt.Compute(task.State);

        Assert.False(debt.RetrospectiveOwed);
        Assert.Equal(1, debt.LessonsRecalled);
        Assert.Equal(0, debt.LessonsCited);
        Assert.False(debt.IsClear);
    }

    // IC2: a supersession must carry its predecessor's kind, so a revised retrospective leaves the
    // kind present and the debt stays paid across the revision. This is why the projection asks
    // whether any retrospective exists rather than whether a current one does.
    [Fact]
    public void ARevisedRetrospectiveLeavesTheDebtPaid()
    {
        var task = Archived();
        task.Apply(WorkflowRetrospectiveArtifactTests.Retrospective(task, task.OperatorId, "A-retro"));
        task.Apply(ArtifactCommands.Record(
            task, task.OperatorId, "A-retro-2", GovernedArtifactKind.WorkflowRetrospective,
            WorkflowRetrospectiveArtifactTests.Table(), "How this task was governed",
            supersedes: "A-retro"));

        Assert.False(TaskDebt.Compute(task.State).RetrospectiveOwed);
    }

    // An archived task built by the commands an operator would issue, rather than by composing the
    // stage and the artifact onto an opened state. The kernel refuses a retrospective anywhere but
    // Archive and refuses Archive itself without the walk, so a composed state would let these cases
    // pass against a task the kernel can never produce. Everything the walk itself owes is settled
    // here — its open claim resolved on evidence, its work items completed — so that IsClear answers
    // about the retrospective and not about what the walk left behind.
    private static TestTask Archived(Lesson? recalled = null)
    {
        var task = new TestTask("debt-task");
        // Recall has no command: the durable service emits the event while opening the task, and the
        // replay rule accepts it only into a task holding nothing but its opening role. So an
        // inherited lesson goes on before the walk records anything.
        if (recalled is not null)
        {
            task.RecallLesson(recalled);
        }

        task.ReachStage(TaskStage.Learn);
        foreach (var item in task.State.WorkItems.Values
                     .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
                     .ToArray())
        {
            task.Apply(new CompleteWorkItemCommand(
                task.OperatorId, null, task.NextCorrelation(), item.Id));
        }

        // The claim the walk opens to enter Research is the only debt it leaves besides the
        // retrospective, so it is earned and resolved rather than left to decide IsClear.
        task.Apply(new AddEvidenceCommand(
            task.OperatorId, null, task.NextCorrelation(), new EvidenceId("E-topic"),
            "source-read", "TaskDebt.cs:96", "The stage arms read state the kernel already holds",
            [new ClaimId("C-topic")], []));
        task.Apply(new ResolveClaimCommand(
            task.OperatorId, null, task.NextCorrelation(), new ClaimId("C-topic"),
            ClaimStatus.Validated, [new EvidenceId("E-topic")]));
        task.Apply(new MarkLessonBearingCommand(
            task.OperatorId, null, task.NextCorrelation(), LessonSourceKind.RejectedAlternative,
            "ALT-stage", Class: LessonClass.Refuted, Repo: "AILedger", Tags: ["retrospective"],
            Verify: "ailedger status --task debt-task",
            DoNot: "Do not walk the stages without recording what was considered",
            Actor: LessonActor.Verifier, Kind: LessonKind.Workflow,
            VerifyExpects: VerifyExpectation.Present));
        task.Transition(TaskStage.Archive);
        return task;
    }

    // The same lesson WithRecalledLesson reduces on, for the archived cases, which reach their state
    // through TestTask rather than through this file's own reducer helpers.
    private static Lesson InheritedLesson() => new(
        Inherited, new TaskId("earlier-task"), LessonSourceKind.Imported, "C9",
        "Inherited belief", "Outcome", ["adapter.cs:104"],
        new Provenance(new ActorId("operator"), new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero),
            "lesson.import"),
        null, LessonClass.Refuted, "AILedger", ["adapter"], "grep -n session adapter.cs",
        "Do not drop it", LessonActor.Verifier);

    private static GovernedTaskState WithWorkItem(
        GovernedTaskState state, TaskReducer reducer,
        Func<GovernedTaskState, ActorId, LedgerEventData, LedgerEvent> next, ActorId actor) =>
        reducer.Apply(state, next(state, actor, new WorkItemAdded(new WorkItem(
            new WorkItemId("W1"), "Work", actor, WorkItemStatus.Proposed, [], [Path.GetFullPath("src")]))));

    // Composed directly rather than driven through the reducer: a started run must begin at its own
    // event timestamp, so a legal history cannot place two runs at different times through one
    // fixed-clock helper. TaskDebt is a pure projection over state, and what is under test is the
    // count, not the route.
    private static GovernedTaskState WithCompletedRun(
        GovernedTaskState state, TaskReducer reducer,
        Func<GovernedTaskState, ActorId, LedgerEventData, LedgerEvent> next,
        ActorId actor, DateTimeOffset at, string runId, RoleKind role, string provider)
    {
        _ = reducer; _ = next;
        var run = new AgentRun(
            new RunId(runId), actor, new WorkItemId("W1"), provider, "s-" + runId,
            AgentRunStatus.Completed, at, at, null, null, null, null, role);
        var runs = new Dictionary<RunId, AgentRun>(state.Runs) { [run.Id] = run };
        return state with { Runs = runs };
    }

    private static GovernedTaskState WithClaim(
        GovernedTaskState state, TaskReducer reducer,
        Func<GovernedTaskState, ActorId, LedgerEventData, LedgerEvent> next,
        ActorId actor, DateTimeOffset recordedAt, string id) =>
        reducer.Apply(state, next(state, actor, new ClaimAdded(new Claim(
            new ClaimId(id), $"Claim {id}", ClaimStatus.Open, [], null,
            new Provenance(actor, recordedAt, "claim.add")))));

    private static GovernedTaskState WithRecalledLesson(
        out TaskReducer reducer,
        out Func<GovernedTaskState, ActorId, LedgerEventData, LedgerEvent> next,
        out ActorId actor,
        out DateTimeOffset recordedAt)
    {
        var state = Opened(out reducer, out next, out actor, out recordedAt);
        return reducer.Apply(state, next(state, actor, new LessonRecalled(new Lesson(
            Inherited, new TaskId("earlier-task"), LessonSourceKind.Imported, "C9",
            "Inherited belief", "Outcome", ["adapter.cs:104"],
            new Provenance(actor, recordedAt, "lesson.import"), null, LessonClass.Refuted,
            "AILedger", ["adapter"], "grep -n session adapter.cs", "Do not drop it",
            LessonActor.Verifier))));
    }

    private static GovernedTaskState Opened(
        out TaskReducer reducer,
        out Func<GovernedTaskState, ActorId, LedgerEventData, LedgerEvent> next,
        out ActorId actor,
        out DateTimeOffset recordedAt)
    {
        var id = new TaskId("debt-task");
        var localActor = new ActorId("operator");
        var localRecordedAt = new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
        var localReducer = new TaskReducer();
        actor = localActor;
        recordedAt = localRecordedAt;
        reducer = localReducer;
        next = (current, eventActor, data) => new LedgerEvent(
            GovernedTaskState.CurrentSchemaVersion,
            new EventId($"{id.Value}:{(current?.Version ?? 0) + 1:D10}"),
            id, eventActor, localRecordedAt, null, "replay", data);

        var state = localReducer.Apply(null, next(null!, localActor, new TaskOpened("Debt", "Goal")));
        return localReducer.Apply(state, next(state, localActor, new RoleAssigned(new RoleAssignment(
            localActor, RoleKind.Operator, Enum.GetValues<Capability>(),
            new Provenance(localActor, localRecordedAt, "task.open")))));
    }
}
