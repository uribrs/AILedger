using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Storage;

public sealed class LessonRecallTests
{
    [Fact]
    public async Task OpeningTaskRecallsLessonArchivedInAnotherRepositoryRoot()
    {
        using var sourceRoot = new TemporaryDirectory();
        using var targetRoot = new TemporaryDirectory();
        using var lessonRoot = new TemporaryDirectory();
        var actor = new ActorId("operator");
        var source = new TaskId("source-from-another-repo");
        await ArchiveAlternativeAsync(Service(sourceRoot.Path, lessonRoot.Path), source, actor);

        // A fresh store and service model the next repository opening in another process. The
        // target root has no archived sibling from which it could have learned this lesson.
        var target = new TaskId("target-in-this-repo");
        var opened = await Service(targetRoot.Path, lessonRoot.Path).ExecuteAsync(
            target,
            new OpenTaskCommand(actor, null, "target-open", target, "Target", "Recall"),
            CancellationToken.None);

        var recalled = Assert.Single(opened.State.Lessons.Values);
        Assert.Equal(source, recalled.SourceTaskId);
        Assert.Equal("AILedger", recalled.Repo);
        Assert.Equal(LessonClass.Refuted, recalled.Class);
        Assert.Equal(["state", "isolation"], recalled.Tags);
        Assert.DoesNotContain(
            Directory.EnumerateDirectories(targetRoot.Path),
            path => Path.GetFileName(path) == source.Value);
        Assert.True(File.Exists(Path.Combine(lessonRoot.Path, FileLessonStore.LessonsFileName)));
    }

    [Fact]
    public async Task OpeningTaskRecallsLessonsMintedByArchivedSibling()
    {
        using var root = new TemporaryDirectory();
        var service = Service(root.Path);
        var actor = new ActorId("operator");
        var source = new TaskId("source");
        var target = new TaskId("target");
        await ArchiveAlternativeAsync(service, source, actor);

        var opened = await service.ExecuteAsync(
            target,
            new OpenTaskCommand(actor, null, "target-open", target, "Target", "Recall"),
            CancellationToken.None);

        var recalled = Assert.Single(opened.Events, item => item.Data is LessonRecalled);
        var lesson = Assert.IsType<LessonRecalled>(recalled.Data).Lesson;
        Assert.Equal(source, lesson.SourceTaskId);
        Assert.Equal("ALT1", lesson.SourceRecordId);
        AssertLessonEqual(lesson, opened.State.Lessons[lesson.Id]);

        var replayed = await Service(root.Path).GetStateAsync(target, CancellationToken.None);
        AssertLessonEqual(lesson, replayed!.Lessons[lesson.Id]);
        var manifest = new ContextAssembler().Build(
            replayed, actor, null, [], DateTimeOffset.UtcNow);
        var artifact = Assert.Single(manifest.Artifacts, artifact =>
            artifact.Kind == ContextArtifactKind.Lesson && artifact.Id == lesson.Id.Value);
        Assert.Contains("Unverified prior evidence", artifact.Content, StringComparison.Ordinal);
        Assert.Contains("re-establish before relying on it", artifact.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpeningTaskDoesNotFollowSiblingSymlinkOutsideLedgerRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var root = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        var actor = new ActorId("operator");
        await ArchiveAlternativeAsync(Service(outside.Path), new TaskId("source"), actor);
        Directory.CreateSymbolicLink(
            Path.Combine(root.Path, "linked-source"),
            Path.Combine(outside.Path, "source"));

        var target = new TaskId("target");
        var opened = await Service(root.Path).ExecuteAsync(
            target,
            new OpenTaskCommand(actor, null, "target-open", target, "Target", "Recall"),
            CancellationToken.None);

        Assert.DoesNotContain(opened.Events, item => item.Data is LessonRecalled);
        Assert.Empty(opened.State.Lessons);
    }

    [Fact]
    public async Task OpeningTaskRecallsLessonsFromOnlyTheThreeMostRecentlyArchivedSiblings()
    {
        using var root = new TemporaryDirectory();
        var service = Service(root.Path);
        var actor = new ActorId("operator");
        // Undated ids, archived in descending id order. Ordering by task id would keep delta,
        // charlie and bravo — the three oldest archives — so only archive recency passes here.
        TaskId[] archiveOrder =
        [
            new("delta"),
            new("charlie"),
            new("bravo"),
            new("alpha")
        ];
        foreach (var source in archiveOrder)
        {
            await ArchiveAlternativeAsync(service, source, actor);
        }

        var target = new TaskId("target");
        var opened = await service.ExecuteAsync(
            target,
            new OpenTaskCommand(actor, null, "target-open", target, "Target", "Recall"),
            CancellationToken.None);

        var recalledSources = opened.State.Lessons.Values
            .Select(lesson => lesson.SourceTaskId.Value)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["alpha", "bravo", "charlie"], recalledSources);
        Assert.DoesNotContain("delta", recalledSources);
    }

    [Fact]
    public async Task MintedLessonCarriesTheArchiveTimestampThatRecallOrdersBy()
    {
        using var root = new TemporaryDirectory();
        var service = Service(root.Path);
        var actor = new ActorId("operator");
        var source = new TaskId("source");
        var correlation = new CorrelationSequence();
        await MarkAlternativeLessonBearingAsync(service, source, actor, correlation);

        // Everything the lesson is derived from is already recorded. Recall orders on the
        // provenance stamped when the source task archives, not on when its records were written.
        var beforeArchive = DateTimeOffset.UtcNow;
        await ArchiveAsync(service, source, actor, correlation);

        var lesson = Assert.Single(
            (await service.GetStateAsync(source, CancellationToken.None))!.Lessons.Values);
        Assert.True(
            lesson.Provenance.RecordedAt >= beforeArchive,
            $"lesson provenance {lesson.Provenance.RecordedAt:O} predates the archive transition at {beforeArchive:O}");
    }

    [Fact]
    public async Task OpeningTaskFiltersARecalledLessonSupersededByANewerLesson()
    {
        using var root = new TemporaryDirectory();
        var service = Service(root.Path);
        var actor = new ActorId("operator");
        var originalSource = new TaskId("2026-09-01_1200-original");
        await ArchiveAlternativeAsync(service, originalSource, actor);
        var original = Assert.Single(
            (await service.GetStateAsync(originalSource, CancellationToken.None))!.Lessons.Values);

        var replacementSource = new TaskId("2026-09-02_1200-replacement");
        await ArchiveAlternativeAsync(service, replacementSource, actor, original.Id);
        var target = new TaskId("2026-09-03_1200-target");
        var opened = await service.ExecuteAsync(
            target,
            new OpenTaskCommand(actor, null, "target-open", target, "Target", "Recall"),
            CancellationToken.None);

        var replacement = Assert.Single(opened.State.Lessons.Values);
        Assert.Equal(replacementSource, replacement.SourceTaskId);
        Assert.Equal(original.Id, replacement.SupersedesLessonId);
        Assert.DoesNotContain(original.Id, opened.State.Lessons.Keys);
    }

    private sealed class CorrelationSequence
    {
        private int _next;

        public string Next() => $"c{++_next}";
    }

    private static async Task ArchiveAlternativeAsync(
        FileGovernedTaskService service,
        TaskId source,
        ActorId actor,
        LessonId? supersedesLessonId = null)
    {
        var correlation = new CorrelationSequence();
        await MarkAlternativeLessonBearingAsync(service, source, actor, correlation, supersedesLessonId);
        await ArchiveAsync(service, source, actor, correlation);
    }

    private static async Task MarkAlternativeLessonBearingAsync(
        FileGovernedTaskService service,
        TaskId source,
        ActorId actor,
        CorrelationSequence correlation,
        LessonId? supersedesLessonId = null)
    {
        await Run(service, source, new OpenTaskCommand(actor, null, correlation.Next(), source, "Source", "Learn"));
        await Run(service, source, new RecordAlternativeCommand(
            actor, null, correlation.Next(), new AlternativeId("ALT1"), "Use global mutable state", "Tasks must remain isolated", null));
        await Run(service, source, new MarkLessonBearingCommand(
            actor, null, correlation.Next(), LessonSourceKind.RejectedAlternative, "ALT1", supersedesLessonId,
            LessonClass.Refuted, "AILedger", ["state", "isolation"]));
        await Run(service, source, new AddWorkItemCommand(
            actor, null, correlation.Next(), new WorkItemId("W1"), "Work", null, [], []));
    }

    private static async Task ArchiveAsync(
        FileGovernedTaskService service,
        TaskId source,
        ActorId actor,
        CorrelationSequence correlation)
    {
        foreach (var stage in new[]
                 {
                     TaskStage.Research, TaskStage.Design, TaskStage.Scope, TaskStage.Ready,
                     TaskStage.Execution, TaskStage.Verification, TaskStage.Review, TaskStage.Learn,
                     TaskStage.Archive
                 })
        {
            await Run(service, source, new RequestStageTransitionCommand(actor, null, correlation.Next(), stage));
        }
    }

    private static FileGovernedTaskService Service(string root)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(root, new CommandHandler(reducer, new AuthorizationPolicy()), reducer);
    }

    private static FileGovernedTaskService Service(string root, string lessonRoot)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(
            root,
            new CommandHandler(reducer, new AuthorizationPolicy()),
            reducer,
            lessonStore: new FileLessonStore(lessonRoot));
    }

    private static async Task Run(FileGovernedTaskService service, TaskId taskId, LedgerCommand command) =>
        _ = await service.ExecuteAsync(taskId, command, CancellationToken.None);

    private static void AssertLessonEqual(Lesson expected, Lesson actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.SourceTaskId, actual.SourceTaskId);
        Assert.Equal(expected.SourceKind, actual.SourceKind);
        Assert.Equal(expected.SourceRecordId, actual.SourceRecordId);
        Assert.Equal(expected.Statement, actual.Statement);
        Assert.Equal(expected.Outcome, actual.Outcome);
        Assert.Equal(expected.Citations, actual.Citations);
        Assert.Equal(expected.Provenance, actual.Provenance);
        Assert.Equal(expected.SupersedesLessonId, actual.SupersedesLessonId);
        Assert.Equal(expected.Class, actual.Class);
        Assert.Equal(expected.Repo, actual.Repo);
        Assert.Equal(expected.Tags, actual.Tags);
    }
}
