using System.Text.Json;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Artifacts;
using AILedger.Storage;
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
            recordedAt, null, null, null, null, null, RoleKind.Worker))));
        state = reducer.Apply(state, next(state, actor, new RunCompleted(
            new RunId("R1"), AgentRunStatus.Completed, "s1", recordedAt)));

        var debt = TaskDebt.Compute(state);

        Assert.Equal(1, debt.WorkItemsAwaitingVerification);
        // The two counts describe different items and never the same one: this one has done work
        // the gate accepts, so it is unverified rather than stuck.
        Assert.Equal(0, debt.WorkItemsRunByNoWorkingRole);
    }

    // KC1 from the code reviewer: the debt projection listed working roles positively while the
    // completion gate named the two judging roles negatively, so an item worked by any other role
    // read as clear while work complete refused it. The gate's list is positive again, deliberately,
    // and the defect has not come back with it: both readers ask WorkItemVerificationRules.HasCompletedWorkingRun
    // — which is internal for exactly that reason — and TaskDebt keeps no role list of its own.
    // Researcher is the fixture because it is the half of the new list a later refactor drops first.
    [Fact]
    public void AnItemWorkedByARoleTheGateCountsAsWorkIsCountedAsAwaitingVerification()
    {
        var state = Opened(out var reducer, out var next, out var actor, out var recordedAt);
        state = WithWorkItem(state, reducer, next, actor);
        state = WithCompletedRun(state, reducer, next, actor, recordedAt, "R1", RoleKind.Researcher, "claude");

        Assert.Equal(1, TaskDebt.Compute(state).WorkItemsAwaitingVerification);
    }

    // KC2, first half: verified, then worked again. The gate refuses this; the projection used to
    // call it clear, which is the repair cycle it exists to cover.
    [Fact]
    public void AnItemVerifiedBeforeItsLatestWorkIsStillCountedAsAwaitingVerification()
    {
        var state = Opened(out var reducer, out var next, out var actor, out var recordedAt);
        state = WithWorkItem(state, reducer, next, actor);
        state = WithCompletedRun(state, reducer, next, actor, recordedAt, "R1", RoleKind.Worker, "claude");
        state = WithCompletedRun(state, reducer, next, actor, recordedAt, "RV1", RoleKind.Verifier, "codex");
        state = WithCompletedRun(state, reducer, next, actor, recordedAt.AddHours(1), "R2", RoleKind.Worker, "claude");

        Assert.Equal(1, TaskDebt.Compute(state).WorkItemsAwaitingVerification);
    }

    // KC2, second half: verified by the provider that wrote it. Also refused by the gate.
    [Fact]
    public void AnItemVerifiedByTheProviderThatWroteItIsStillCountedAsAwaitingVerification()
    {
        var state = Opened(out var reducer, out var next, out var actor, out var recordedAt);
        state = WithWorkItem(state, reducer, next, actor);
        state = WithCompletedRun(state, reducer, next, actor, recordedAt, "R1", RoleKind.Worker, "claude");
        state = WithCompletedRun(state, reducer, next, actor, recordedAt.AddHours(1), "RV1", RoleKind.Verifier, "claude");

        Assert.Equal(1, TaskDebt.Compute(state).WorkItemsAwaitingVerification);
    }

    [Fact]
    public void AnItemVerifiedAfterItsLatestWorkByAnotherProviderIsNotCounted()
    {
        var state = Opened(out var reducer, out var next, out var actor, out var recordedAt);
        state = WithWorkItem(state, reducer, next, actor);
        state = WithCompletedRun(state, reducer, next, actor, recordedAt, "R1", RoleKind.Worker, "claude");
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
    //
    // The rows worked by a coordinating role, and the row whose run recorded no role at all, are
    // here rather than in a test of their own, and that is the repair: while this theory held only
    // roles the gate counts as work, its name was false. Narrowing HasCompletedWorkingRun to a
    // positive list made those items count zero on every debt figure while work complete refused
    // them outright, and a test that asserted agreement over the accepted roles alone could not see
    // it. The item this repository most wants flagged had become the one it reported nothing about.
    //
    // The two counts are read together because they are one answer in two parts: worked and
    // unverified, or run and not worked. Either is a reason work complete refuses, and their sum
    // being zero is what 'the gate accepts' means.
    [Theory]
    [InlineData(RoleKind.Worker, "claude", null, null, 1, 0)]
    [InlineData(RoleKind.Worker, "claude", RoleKind.Verifier, "claude", 1, 0)]
    [InlineData(RoleKind.Worker, "claude", RoleKind.Verifier, "codex", 0, 0)]
    [InlineData(RoleKind.Researcher, "claude", null, null, 1, 0)]
    [InlineData(RoleKind.Researcher, "claude", RoleKind.Verifier, "claude", 1, 0)]
    [InlineData(RoleKind.Researcher, "claude", RoleKind.Verifier, "codex", 0, 0)]
    [InlineData(RoleKind.Operator, "claude", null, null, 0, 1)]
    [InlineData(RoleKind.Operator, "claude", RoleKind.Verifier, "codex", 0, 1)]
    [InlineData(RoleKind.PlanningLead, "claude", RoleKind.Verifier, "codex", 0, 1)]
    [InlineData(RoleKind.ImplementationLead, "claude", RoleKind.Verifier, "codex", 0, 1)]
    [InlineData(RoleKind.CodeReviewer, "claude", RoleKind.Verifier, "codex", 0, 1)]
    [InlineData(null, "claude", RoleKind.Verifier, "codex", 0, 1)]
    public void TheDebtProjectionAgreesWithWhatWorkCompleteAccepts(
        RoleKind? workRole, string workProvider, RoleKind? verifyRole, string? verifyProvider,
        int expectedAwaitingVerification, int expectedRunByNoWorkingRole)
    {
        var state = Opened(out var reducer, out var next, out var actor, out var recordedAt);
        state = WithWorkItem(state, reducer, next, actor);
        state = WithCompletedRun(state, reducer, next, actor, recordedAt, "R1", workRole, workProvider);
        if (verifyRole is { } role)
        {
            state = WithCompletedRun(state, reducer, next, actor, recordedAt.AddHours(1), "RV1", role, verifyProvider!);
        }

        var debt = TaskDebt.Compute(state);
        Assert.Equal(expectedAwaitingVerification, debt.WorkItemsAwaitingVerification);
        Assert.Equal(expectedRunByNoWorkingRole, debt.WorkItemsRunByNoWorkingRole);

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

        // No debt on either count must mean the gate accepts, and any debt must mean it refuses.
        var owed = debt.WorkItemsAwaitingVerification + debt.WorkItemsRunByNoWorkingRole;
        Assert.Equal(owed == 0, accepted);
        // And the block an operator reads has to be there whenever the gate would refuse.
        if (!accepted)
        {
            Assert.False(debt.IsClear);
        }
    }

    // The far side of the narrowed list. A coordinating role's run is the plan being made, not work
    // a verifier would have anything to read, so the item has done nothing and work complete refuses
    // it for want of work rather than for want of verification. WorkItemsAwaitingVerification stays
    // at zero, correctly — it measures items that have worked and are still unverified — and the
    // item is counted by WorkItemsRunByNoWorkingRole instead, which is the whole reason that count
    // exists. For a while it was counted by neither: the debt read clear about an item the kernel
    // would not complete, and the shape this narrowing exists to flag was the one the report went
    // quiet about.
    //
    // The refusal message is asserted, not merely the refusal: a test that watched only the counts
    // would pass just as well if the gate had stopped refusing.
    //
    // The run is composed onto state rather than started, because run start now refuses a
    // coordinating role against a work item outright. The shape is still reachable on replay: this
    // ledger holds work items closed with a coordinating run as their only working run, and no rule
    // keyed on the role may reach the validator.
    [Theory]
    [InlineData(RoleKind.Operator)]
    [InlineData(RoleKind.PlanningLead)]
    [InlineData(RoleKind.ImplementationLead)]
    public void AnItemWhoseOnlyRunHeldACoordinatingRoleHasDoneNoWorkAndTheGateSaysSo(RoleKind role)
    {
        var state = Opened(out var reducer, out var next, out var actor, out var recordedAt);
        state = WithWorkItem(state, reducer, next, actor);
        state = WithCompletedRun(state, reducer, next, actor, recordedAt, "R1", role, "claude");
        state = WithCompletedRun(
            state, reducer, next, actor, recordedAt.AddHours(1), "RV1", RoleKind.Verifier, "codex");

        var debt = TaskDebt.Compute(state);
        Assert.Equal(0, debt.WorkItemsAwaitingVerification);
        Assert.Equal(1, debt.WorkItemsRunByNoWorkingRole);
        Assert.False(debt.IsClear);

        var refusal = Assert.Throws<GovernanceException>(() => new CommandHandler().Handle(
            state,
            new CompleteWorkItemCommand(actor, null, "c-complete", new WorkItemId("W1"), null),
            recordedAt.AddHours(2)));
        Assert.Contains("no completed run by a working role", refusal.Message);
    }

    // The defect in one assertion, isolated from every other debt. The fixture owes nothing else:
    // no open claim, no inherited lesson, not archived, and its stage matches what its runs imply,
    // so IsClear answers about the stuck item and about nothing else. Before the count existed this
    // read clear while work complete refused the item outright — the report and the gate saying
    // opposite things about the same state, which is KC1's shape and the reason the count is wired
    // into IsClear rather than merely reported.
    [Fact]
    public void TheDebtIsNotClearWhileAStuckItemIsTheOnlyThingTheTaskOwes()
    {
        var state = Opened(out var reducer, out var next, out var actor, out var recordedAt);
        state = WithWorkItem(state, reducer, next, actor);
        state = WithCompletedRun(
            state, reducer, next, actor, recordedAt, "R1", RoleKind.ImplementationLead, "claude");
        state = WithCompletedRun(
            state, reducer, next, actor, recordedAt.AddHours(1), "RV1", RoleKind.Verifier, "codex");
        // A verifier run implies Review, so the task sits where its own records put it and the stage
        // drift that would otherwise decide IsClear here is cleared.
        state = state with { Stage = TaskStage.Review };

        var debt = TaskDebt.Compute(state);

        Assert.Equal(0, debt.OpenClaims);
        Assert.Equal(0, debt.WorkItemsAwaitingVerification);
        Assert.Null(debt.StageBehindActivity);
        Assert.False(debt.RetrospectiveOwed);
        Assert.Equal(1, debt.WorkItemsRunByNoWorkingRole);
        Assert.False(debt.IsClear);

        Assert.Throws<GovernanceException>(() => new CommandHandler().Handle(
            state,
            new CompleteWorkItemCommand(actor, null, "c-complete", new WorkItemId("W1"), null),
            recordedAt.AddHours(2)));
    }

    // The half of the positive list that is not the obvious one, asserted against the gate as well
    // as the projection. A Researcher run does the work here while the Verification stage arm
    // accepts a Worker only; both decisions state that difference on purpose, so it is pinned rather
    // than tidied away by someone reading the two lists side by side.
    [Fact]
    public void AResearcherRunDoesTheWorkAndOpensTheCompletionGate()
    {
        var state = Opened(out var reducer, out var next, out var actor, out var recordedAt);
        state = WithWorkItem(state, reducer, next, actor);
        state = WithCompletedRun(state, reducer, next, actor, recordedAt, "R1", RoleKind.Researcher, "codex");
        state = WithCompletedRun(
            state, reducer, next, actor, recordedAt.AddHours(1), "RV1", RoleKind.Verifier, "claude");

        var debt = TaskDebt.Compute(state);
        Assert.Equal(0, debt.WorkItemsAwaitingVerification);
        // Researcher is work to the gate, so the item is finished rather than stuck.
        Assert.Equal(0, debt.WorkItemsRunByNoWorkingRole);
        new CommandHandler().Handle(
            state,
            new CompleteWorkItemCommand(actor, null, "c-complete", new WorkItemId("W1"), null),
            recordedAt.AddHours(2));
    }

    // A run recorded before SubjectRole existed never told the kernel who worked, so it cannot
    // satisfy the gate. The positive list makes that true by construction rather than by a clause of
    // its own, which is why it is asserted here: nothing in the predicate mentions null any more.
    //
    // Counted as stuck, like any other run the gate does not accept, and that is a decision rather
    // than a side effect. The kernel never saw the role and this count does not claim it did; what
    // it says is that work complete will refuse this item, which is true. Excusing null would need a
    // 'SubjectRole is null' test written into the projection — a second role model in the reader,
    // which is precisely the shape KC1 was — and would leave the report clear about an item that
    // cannot be completed.
    [Fact]
    public void ARunThatRecordedNoRoleDoesNotCountAsWorkAndLeavesTheItemStuck()
    {
        var state = Opened(out var reducer, out var next, out var actor, out var recordedAt);
        state = WithWorkItem(state, reducer, next, actor);
        state = WithCompletedRun(state, reducer, next, actor, recordedAt, "R1", null, "claude");
        state = WithCompletedRun(
            state, reducer, next, actor, recordedAt.AddHours(1), "RV1", RoleKind.Verifier, "codex");

        var debt = TaskDebt.Compute(state);
        Assert.Equal(0, debt.WorkItemsAwaitingVerification);
        Assert.Equal(1, debt.WorkItemsRunByNoWorkingRole);
        Assert.False(debt.IsClear);

        var refusal = Assert.Throws<GovernanceException>(() => new CommandHandler().Handle(
            state,
            new CompleteWorkItemCommand(actor, null, "c-complete", new WorkItemId("W1"), null),
            recordedAt.AddHours(2)));
        Assert.Contains("no completed run by a working role", refusal.Message);
    }

    // The distinction the stuck count has to keep, and the failure mode of fixing it carelessly: an
    // item nobody has run yet is ordinary pending work, not debt. If every un-started item counted,
    // the owed block would appear on every task the moment work was proposed and the count would
    // mean nothing.
    [Fact]
    public void AnItemThatHasRunNothingIsPendingRatherThanStuck()
    {
        var state = Opened(out var reducer, out var next, out var actor, out _);
        state = WithWorkItem(state, reducer, next, actor);

        var debt = TaskDebt.Compute(state);

        Assert.Equal(0, debt.WorkItemsAwaitingVerification);
        Assert.Equal(0, debt.WorkItemsRunByNoWorkingRole);
        // IsClear is not asserted in this file's composed fixtures: a task holding a work item while
        // sitting in Discovery owes the stage its activity implies, and that debt would carry the
        // assertion whatever the new count said. What is under test is the count.
    }

    // 'Has run something' is asked with DidWork rather than with a status test of the projection's
    // own, so a run that declared --provider none spawned no agent and leaves the item pending. The
    // alternative — counting any Completed run — would report a stuck item where nothing ever ran,
    // and it would be a second model of 'a run that did something' living in a reader.
    [Fact]
    public void AnItemWhoseOnlyRunDeclaredNoProviderIsPendingRatherThanStuck()
    {
        var state = Opened(out var reducer, out var next, out var actor, out var recordedAt);
        state = WithWorkItem(state, reducer, next, actor);
        state = WithCompletedRun(
            state, reducer, next, actor, recordedAt, "R1", RoleKind.Operator, AgentRun.NoProvider);

        var debt = TaskDebt.Compute(state);

        Assert.Equal(0, debt.WorkItemsRunByNoWorkingRole);
    }

    // Debt is about live work. A closed item is not going through the completion gate again, so
    // counting it would put a permanent block on every task that ever finished an item a
    // coordinating run touched — and this ledger holds sixteen of those. Stale is in the same
    // sentence because it is how the kernel releases an item's scope without completing it.
    [Theory]
    [InlineData(WorkItemStatus.Completed)]
    [InlineData(WorkItemStatus.Abandoned)]
    [InlineData(WorkItemStatus.Stale)]
    public void AClosedItemIsNotStuckWhateverItsOnlyRunHeld(WorkItemStatus status)
    {
        var state = Opened(out var reducer, out var next, out var actor, out var recordedAt);
        state = WithWorkItem(state, reducer, next, actor);
        state = WithCompletedRun(state, reducer, next, actor, recordedAt, "R1", RoleKind.Operator, "claude");
        state = WithItemStatus(state, status);

        var debt = TaskDebt.Compute(state);

        Assert.Equal(0, debt.WorkItemsRunByNoWorkingRole);
    }

    // The count has to reach the operator, and the CLI writes the owed block by serialising this
    // record whole (CliApplication.cs:672) rather than by naming its members. This asserts the half
    // that can break here: the field is a serialised member under the CLI's own options, so no CLI
    // change was needed for it to appear. IsClear stays out, as KC3 requires.
    [Fact]
    public void TheStuckCountIsSerialisedIntoTheOwedBlockTheCliWrites()
    {
        var state = Opened(out var reducer, out var next, out var actor, out var recordedAt);
        state = WithWorkItem(state, reducer, next, actor);
        state = WithCompletedRun(state, reducer, next, actor, recordedAt, "R1", RoleKind.Operator, "claude");
        var debt = TaskDebt.Compute(state);
        Assert.False(debt.IsClear);

        var owed = JsonSerializer.SerializeToNode(debt, LedgerJson.CreateOptions(indented: true))!.AsObject();

        Assert.Equal(1, owed["workItemsRunByNoWorkingRole"]!.GetValue<int>());
        Assert.False(owed.ContainsKey("isClear"));
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

    // The stage arms are command-time checks on a transition and nothing asks for one, so a task
    // can run a worker and a verifier while sitting in Discovery and be refused nothing. 271 of 484
    // provider runs in this ledger were dispatched from Discovery. This reports that drift as a
    // debt; it refuses nothing.
    [Fact]
    public void ATaskThatRecordsWorkWithoutTransitioningOwesTheStageItsActivityImplies()
    {
        var state = Opened(out var reducer, out var next, out var actor, out var recordedAt);
        state = WithClaim(state, reducer, next, actor, recordedAt, "C1");
        state = WithWorkItem(state, reducer, next, actor);
        state = WithCompletedRun(state, reducer, next, actor, recordedAt, "R1", RoleKind.Worker, "codex");
        state = WithCompletedRun(state, reducer, next, actor, recordedAt, "R2", RoleKind.Verifier, "claude");

        var debt = TaskDebt.Compute(state);

        // A completed verifier run is what the Review arm asks for, and the task is in Discovery.
        Assert.Equal(TaskStage.Review, debt.StageBehindActivity);
        Assert.False(debt.IsClear);
    }

    // Null is the cleared state, and it is what a task that has done nothing reports — the drift
    // must not read as debt on a task that simply has not started.
    [Fact]
    public void AFreshTaskOwesNoStageDrift()
    {
        var state = Opened(out _, out _, out _, out _);

        var debt = TaskDebt.Compute(state);

        Assert.Null(debt.StageBehindActivity);
        Assert.False(debt.CoordinatorSessionOpen);
    }

    // A waiver exercised with no bracket open records origin 'manual', correctly, because none
    // existed. This is the fact that says so before the waiver is made rather than after.
    [Fact]
    public void AnOpenCoordinatorSessionIsReportedAndAClosedOneIsNot()
    {
        var state = Opened(out _, out _, out var actor, out var recordedAt);
        Assert.False(TaskDebt.Compute(state).CoordinatorSessionOpen);

        var session = new CoordinatorSession(
            new CoordinatorSessionId("S1"), actor, "claude-code", "abc-123", recordedAt, null);
        var open = state with
        {
            CoordinatorSessions = new Dictionary<CoordinatorSessionId, CoordinatorSession> { [session.Id] = session }
        };
        Assert.True(TaskDebt.Compute(open).CoordinatorSessionOpen);

        var closed = open with
        {
            CoordinatorSessions = new Dictionary<CoordinatorSessionId, CoordinatorSession>
            {
                [session.Id] = session with { EndedAt = recordedAt }
            }
        };
        Assert.False(TaskDebt.Compute(closed).CoordinatorSessionOpen);
        // Deliberately not part of IsClear: an idle task with no session open owes nothing.
        Assert.True(TaskDebt.Compute(closed).IsClear);
    }

    // Composed rather than driven: the commands that close an item have gates of their own, and what
    // is under test is the count's treatment of a status, not the route to it.
    private static GovernedTaskState WithItemStatus(GovernedTaskState state, WorkItemStatus status)
    {
        var id = new WorkItemId("W1");
        var items = new Dictionary<WorkItemId, WorkItem>(state.WorkItems)
        {
            [id] = state.WorkItems[id] with { Status = status }
        };
        return state with { WorkItems = items };
    }

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
        ActorId actor, DateTimeOffset at, string runId, RoleKind? role, string provider)
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
