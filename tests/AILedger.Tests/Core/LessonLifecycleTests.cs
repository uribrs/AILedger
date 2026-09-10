using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

public sealed class LessonLifecycleTests
{
    [Fact]
    public void ArchiveMintsLessonsFromEachGovernedSourceKind()
    {
        var task = new TestTask("source-task");
        var claimId = new ClaimId("C1");
        var evidenceId = new EvidenceId("E1");
        task.Apply(new AddClaimCommand(
            task.OperatorId, null, task.NextCorrelation(), claimId, "The retry is bounded", null));
        task.Apply(new AddEvidenceCommand(
            task.OperatorId, null, task.NextCorrelation(), evidenceId, "test-run", "RetryTests.Bounded",
            "The production policy stopped after three attempts", [claimId], []));
        task.Apply(new ResolveClaimCommand(
            task.OperatorId, null, task.NextCorrelation(), claimId, ClaimStatus.Validated, [evidenceId]));
        task.Apply(new MarkLessonBearingCommand(
            task.OperatorId, null, task.NextCorrelation(), LessonSourceKind.ValidatedClaim, claimId.Value,
            Class: LessonClass.Untested, Repo: "AILedger", Tags: ["retry", "bounded"],
            Verify: "dotnet test --filter RetryTests.Bounded",
            DoNot: "Do not assume retries are bounded without rerunning the test", Actor: LessonActor.Verifier,
            Kind: LessonKind.Domain, VerifyExpects: VerifyExpectation.Present));
        task.Apply(new RecordAlternativeCommand(
            task.OperatorId, null, task.NextCorrelation(), new AlternativeId("ALT1"),
            "Retry forever", "It prevents terminal failure", null));
        task.Apply(new MarkLessonBearingCommand(
            task.OperatorId, null, task.NextCorrelation(), LessonSourceKind.RejectedAlternative, "ALT1",
            Class: LessonClass.Refuted, Repo: "AILedger", Tags: ["retry"],
            Verify: "dotnet test --filter RetryTests.Bounded",
            DoNot: "Do not retry forever", Actor: LessonActor.Executor,
            Kind: LessonKind.Workflow, Audience: [RoleKind.Verifier, RoleKind.Worker],
            VerifyExpects: VerifyExpectation.Absent));
        task.Apply(new RaiseEscalationCommand(
            task.OperatorId, null, task.NextCorrelation(), new EscalationId("X1"),
            EscalationKind.BusinessDecision, "Ship narrow or broad?", null,
            ["narrow", "broad"], "narrow", []));
        task.Apply(new ResolveEscalationCommand(
            task.OperatorId, null, task.NextCorrelation(), new EscalationId("X1"),
            EscalationStatus.Resolved, "narrow"));
        task.Apply(new MarkLessonBearingCommand(
            task.OperatorId, null, task.NextCorrelation(), LessonSourceKind.ResolvedEscalation, "X1",
            Class: LessonClass.Drifted, Repo: "AILedger", Tags: [],
            Verify: "dotnet test --filter LessonLifecycleTests",
            DoNot: "Do not broaden the shipped change without resolving the tradeoff", Actor: LessonActor.Researcher,
            VerifyExpects: VerifyExpectation.Present));
        AddWorkAndReachLearn(task);

        var outcome = task.Apply(new RequestStageTransitionCommand(
            task.OperatorId, null, task.NextCorrelation(), TaskStage.Archive));

        Assert.Equal(TaskStage.Archive, outcome.State.Stage);
        Assert.Equal(3, outcome.Events.Count(item => item.Data is LessonMinted));
        Assert.IsType<StageTransitioned>(outcome.Events[^1].Data);
        Assert.Collection(
            outcome.State.Lessons.Values.OrderBy(item => item.SourceKind),
            lesson =>
            {
                Assert.Equal(LessonSourceKind.ValidatedClaim, lesson.SourceKind);
                Assert.Equal(["RetryTests.Bounded"], lesson.Citations);
                Assert.Equal(LessonClass.Untested, lesson.Class);
                Assert.Equal("AILedger", lesson.Repo);
                Assert.Equal(["retry", "bounded"], lesson.Tags);
                Assert.Equal("dotnet test --filter RetryTests.Bounded", lesson.Verify);
                Assert.Equal(LessonActor.Verifier, lesson.Actor);
            },
            lesson =>
            {
                Assert.Equal(LessonSourceKind.RejectedAlternative, lesson.SourceKind);
                Assert.Equal(LessonClass.Refuted, lesson.Class);
                Assert.Equal(["retry"], lesson.Tags);
                // The three routing fields survive minting alongside the rest of the metadata.
                Assert.Equal(LessonKind.Workflow, lesson.Kind);
                Assert.Equal([RoleKind.Verifier, RoleKind.Worker], lesson.Audience);
                Assert.Equal(VerifyExpectation.Absent, lesson.VerifyExpects);
            },
            lesson =>
            {
                Assert.Equal(LessonSourceKind.ResolvedEscalation, lesson.SourceKind);
                Assert.Equal(LessonClass.Drifted, lesson.Class);
                Assert.Empty(lesson.Tags!);
            });
    }

    [Fact]
    public void ArchiveMintsOnlySourcesExplicitlyMarkedLessonBearing()
    {
        var task = new TestTask();
        task.Apply(new RecordAlternativeCommand(
            task.OperatorId, null, task.NextCorrelation(), new AlternativeId("ALT1"),
            "Keep the first local workaround", "It does not generalise", null));
        task.Apply(new RecordAlternativeCommand(
            task.OperatorId, null, task.NextCorrelation(), new AlternativeId("ALT2"),
            "Repeat the cross-task mistake", "Future tasks need this warning", null));
        task.Apply(new MarkLessonBearingCommand(
            task.OperatorId, null, task.NextCorrelation(), LessonSourceKind.RejectedAlternative, "ALT2",
            Class: LessonClass.Refuted, Repo: "AILedger", Tags: ["repeat"],
            Verify: "dotnet test --filter LessonLifecycleTests",
            DoNot: "Do not repeat the cross-task mistake", Actor: LessonActor.Recon,
            VerifyExpects: VerifyExpectation.Present));
        AddWorkAndReachLearn(task);

        var outcome = task.Apply(new RequestStageTransitionCommand(
            task.OperatorId, null, task.NextCorrelation(), TaskStage.Archive));

        var minted = Assert.Single(outcome.Events, item => item.Data is LessonMinted);
        Assert.Equal("ALT2", Assert.IsType<LessonMinted>(minted.Data).Lesson.SourceRecordId);
        Assert.DoesNotContain(task.State.Lessons.Values, lesson => lesson.SourceRecordId == "ALT1");
    }

    [Fact]
    public void WorkerCannotMarkASourceLessonBearing()
    {
        var task = new TestTask();
        var worker = new ActorId("worker");
        task.Assign(worker, RoleKind.Worker, Capability.AddClaim);
        task.Apply(new RecordAlternativeCommand(
            task.OperatorId, null, task.NextCorrelation(), new AlternativeId("ALT1"),
            "Carry this into every later task", "Only a lead can make that scope decision", null));

        var error = Assert.Throws<GovernanceException>(() => task.Apply(new MarkLessonBearingCommand(
            worker, null, task.NextCorrelation(), LessonSourceKind.RejectedAlternative, "ALT1",
            Class: LessonClass.Refuted, Repo: "AILedger", Tags: [],
            Verify: "dotnet test --filter LessonLifecycleTests",
            DoNot: "Do not let a worker make task-wide lesson decisions", Actor: LessonActor.Executor)));

        Assert.Contains("operator or lead", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(task.State.LessonMarks);
    }

    [Fact]
    public void NewLessonMarkRequiresClassAndRepository()
    {
        var task = new TestTask();
        task.Apply(new RecordAlternativeCommand(
            task.OperatorId, null, task.NextCorrelation(), new AlternativeId("ALT1"),
            "Repeat a stale workaround", "The environment changed", null));

        var missingClass = Assert.Throws<GovernanceException>(() => task.Apply(new MarkLessonBearingCommand(
            task.OperatorId, null, task.NextCorrelation(), LessonSourceKind.RejectedAlternative, "ALT1",
            Repo: "AILedger", Tags: [], Verify: "dotnet test --filter LessonLifecycleTests",
            DoNot: "Do not repeat a stale workaround", Actor: LessonActor.Executor)));
        Assert.Contains("class", missingClass.Message, StringComparison.OrdinalIgnoreCase);

        var missingRepo = Assert.Throws<GovernanceException>(() => task.Apply(new MarkLessonBearingCommand(
            task.OperatorId, null, task.NextCorrelation(), LessonSourceKind.RejectedAlternative, "ALT1",
            Class: LessonClass.Drifted, Tags: [], Verify: "dotnet test --filter LessonLifecycleTests",
            DoNot: "Do not repeat a stale workaround", Actor: LessonActor.Executor)));
        Assert.Contains("repo", missingRepo.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(task.State.LessonMarks);
    }

    [Fact]
    public void ReplayAcceptsLegacyLessonWithoutPortedMetadata()
    {
        var task = new TestTask("target-task");
        var legacyLesson = new Lesson(
            new LessonId("source-task:rejectedalternative:ALT1"),
            new TaskId("source-task"),
            LessonSourceKind.RejectedAlternative,
            "ALT1",
            "Use global mutable state",
            "Tasks must remain isolated",
            [],
            new Provenance(task.OperatorId, new DateTimeOffset(2026, 9, 6, 14, 0, 0, TimeSpan.Zero), "stage.archive"));
        var legacyRecall = new LedgerEvent(
            GovernedTaskState.CurrentSchemaVersion,
            new EventId("legacy-lesson"),
            task.TaskId,
            task.OperatorId,
            new DateTimeOffset(2026, 9, 6, 14, 1, 0, TimeSpan.Zero),
            null,
            "legacy",
            new LessonRecalled(legacyLesson));

        var replayed = new TaskReducer().Apply(task.State, legacyRecall);
        var recalled = replayed.Lessons[legacyLesson.Id];

        Assert.Null(recalled.Class);
        Assert.Null(recalled.Repo);
        Assert.Null(recalled.Tags);
        Assert.Null(recalled.Verify);
        Assert.Null(recalled.DoNot);
        Assert.Null(recalled.Actor);
        // The twin rule: a routing field required at command time is never required at replay, so
        // a lesson minted before the field existed still reads back.
        Assert.Null(recalled.Kind);
        Assert.Null(recalled.Audience);
        Assert.Null(recalled.VerifyExpects);
    }

    [Fact]
    public void NewLessonMarkRequiresVerifyDoNotAndActor()
    {
        var task = LessonMarkTask();

        var missingVerify = Assert.Throws<GovernanceException>(() => task.Apply(LessonMarkCommand(task,
            DoNot: "Do not repeat the rejected approach", Actor: LessonActor.Executor)));
        Assert.Contains("re-establishes", missingVerify.Message, StringComparison.OrdinalIgnoreCase);

        var missingDoNot = Assert.Throws<GovernanceException>(() => task.Apply(LessonMarkCommand(task,
            Verify: "dotnet test --filter LessonLifecycleTests", Actor: LessonActor.Executor)));
        Assert.Contains("prohibits", missingDoNot.Message, StringComparison.OrdinalIgnoreCase);

        var missingActor = Assert.Throws<GovernanceException>(() => task.Apply(LessonMarkCommand(task,
            Verify: "dotnet test --filter LessonLifecycleTests",
            DoNot: "Do not repeat the rejected approach")));
        Assert.Contains("actor", missingActor.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(task.State.LessonMarks);
    }

    [Theory]
    [InlineData("none - source citation cannot be checked automatically")]
    [InlineData("none — source citation cannot be checked automatically")]
    [InlineData("none – source citation cannot be checked automatically")]
    [InlineData("none: source citation cannot be checked automatically")]
    public void VerifyAcceptsNoneWithASeparatorAndReason(string verify)
    {
        var task = LessonMarkTask();

        task.Apply(LessonMarkCommand(task, Verify: verify,
            DoNot: "Do not treat an uncheckable citation as current evidence", Actor: LessonActor.Researcher));

        Assert.Equal(verify, Assert.Single(task.State.LessonMarks).Value.Verify);
    }

    [Fact]
    public void VerifyRefusesBareNone()
    {
        var task = LessonMarkTask();

        var error = Assert.Throws<GovernanceException>(() => task.Apply(LessonMarkCommand(task,
            Verify: "none", DoNot: "Do not treat an uncheckable citation as current evidence",
            Actor: LessonActor.Researcher)));

        Assert.Contains("runnable command", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(task.State.LessonMarks);
    }

    [Fact]
    public void ReplayAcceptsLegacyLessonMarkWithoutVerifyDoNotOrActor()
    {
        var task = LessonMarkTask();
        var recordedAt = new DateTimeOffset(2026, 9, 6, 14, 0, 0, TimeSpan.Zero);
        var legacyMark = new LessonMark(
            new LessonMarkId("rejectedalternative:ALT1"), LessonSourceKind.RejectedAlternative, "ALT1", null,
            new Provenance(task.OperatorId, recordedAt, "lesson.mark"),
            LessonClass.Refuted, "AILedger", ["legacy"]);
        var legacyEvent = new LedgerEvent(
            GovernedTaskState.CurrentSchemaVersion, new EventId("legacy-mark"), task.TaskId,
            task.OperatorId, recordedAt, null, "legacy", new LessonMarked(legacyMark));

        var replayed = new TaskReducer().Apply(task.State, legacyEvent);
        var mark = replayed.LessonMarks[legacyMark.Id];

        Assert.Null(mark.Verify);
        Assert.Null(mark.DoNot);
        Assert.Null(mark.Actor);
        Assert.Null(mark.Kind);
        Assert.Null(mark.Audience);
        Assert.Null(mark.VerifyExpects);
    }

    [Fact]
    public void ArchiveIsRefusedWhenTheTaskHasNothingLessonBearing()
    {
        var task = new TestTask();
        AddWorkAndReachLearn(task);

        var error = Assert.Throws<GovernanceException>(() => task.Apply(
            new RequestStageTransitionCommand(
                task.OperatorId, null, task.NextCorrelation(), TaskStage.Archive)));

        // The message names the mark, not the lesson: after LD6 a lesson exists only
        // because a source was marked lesson-bearing, so the mark is what is missing.
        Assert.Contains("lesson-bearing mark", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TaskStage.Learn, task.State.Stage);
    }

    [Fact]
    public void ReplayAcceptsLegacyArchiveTransitionWithoutLessonEvents()
    {
        var task = new TestTask();
        AddWorkAndReachLearn(task);
        var reducer = new TaskReducer();
        var legacyArchive = new LedgerEvent(
            GovernedTaskState.CurrentSchemaVersion,
            new EventId("legacy-archive"),
            task.TaskId,
            task.OperatorId,
            new DateTimeOffset(2026, 9, 6, 14, 0, 0, TimeSpan.Zero),
            null,
            "legacy",
            new StageTransitioned(TaskStage.Learn, TaskStage.Archive));

        var replayed = reducer.Apply(task.State, legacyArchive);

        Assert.Equal(TaskStage.Archive, replayed.Stage);
        Assert.Empty(replayed.Lessons);
    }

    internal static void AddWorkAndReachLearn(TestTask task)
    {
        task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Build", null, [], []));
        // Each stage on the way requires state that only engagement with it produces: a topic, an
        // approach discarded, the contract, the plan and the roles, the work and its documents, a
        // pass that did the work, and a verifier's findings. The item declares no directory area, so
        // the work is not code-bearing and Learn asks for no code review.
        task.ReachStage(TaskStage.Learn);
    }

    private static TestTask LessonMarkTask()
    {
        var task = new TestTask();
        task.Apply(new RecordAlternativeCommand(
            task.OperatorId, null, task.NextCorrelation(), new AlternativeId("ALT1"),
            "Repeat the rejected approach", "The evidence no longer supports it", null));
        return task;
    }

    private static MarkLessonBearingCommand LessonMarkCommand(
        TestTask task,
        string? Verify = null,
        string? DoNot = null,
        LessonActor? Actor = null,
        LessonKind? Kind = null,
        IReadOnlyList<RoleKind>? Audience = null,
        VerifyExpectation? VerifyExpects = null) =>
        new(
            task.OperatorId, null, task.NextCorrelation(), LessonSourceKind.RejectedAlternative, "ALT1",
            Class: LessonClass.Refuted, Repo: "AILedger", Tags: ["lesson"],
            Verify: Verify, DoNot: DoNot, Actor: Actor,
            Kind: Kind, Audience: Audience, VerifyExpects: VerifyExpects);
}
