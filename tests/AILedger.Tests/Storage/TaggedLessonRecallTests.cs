using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Storage;

// Recall against a tagged opening: which lessons ten slots are spent on, and how many source tasks
// they reach. The lessons are seeded straight into the cross-repository store rather than earned by
// archiving a source task. Selection is a function of three fields a store row already carries —
// tags, provenance recency and source task — so fourteen archive walks would prove the walk, not the
// selection. The walk itself is pinned in LessonRecallTests.
//
// One recall used to be able to fill every slot from one source task: ordering was by recency alone,
// so a single broad tag shared with the opening let ten lessons minted twelve minutes earlier
// displace the four that matched two of the requested tags.
public sealed class TaggedLessonRecallTests
{
    private static readonly DateTimeOffset Base = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    // Today's actual failure, and the one that must fail against the old ordering. The ten newest
    // lessons match one requested tag each; the oldest lesson matches two. Overlap decides first, so
    // it takes a slot from the oldest of the ten rather than being excluded by them.
    [Fact]
    public async Task ALessonMatchingTwoRequestedTagsOutranksNewerLessonsMatchingOne()
    {
        var narrow = PublishedLesson("2026-09-07_0957-stage-arms", "C1", ["kernel", "recall"], Base);
        // One lesson each, so the ten occupy ten source tasks and the diversity cap never binds.
        // This test pins the ordering and nothing else.
        var broad = Enumerable.Range(1, 10)
            .Select(index => PublishedLesson($"2026-09-08_10{index:D2}-broad", "C1", ["kernel"], Base.AddMinutes(index)))
            .ToArray();

        var recalled = await RecallAsync([narrow, .. broad], ["kernel", "recall"]);

        Assert.Equal(Ids([narrow, .. broad.Skip(1)]), recalled);
        Assert.DoesNotContain(broad[0].Id.Value, recalled);
    }

    // The cap. Ten of the fourteen matching lessons come from one task and they are the newest, so
    // before the cap that task took every slot.
    [Fact]
    public async Task NoSourceTaskContributesMoreThanThreeLessonsToOneRecall()
    {
        var quiet = new[] { "alpha", "bravo", "charlie", "delta" }
            .Select((name, index) => PublishedLesson($"2026-09-07_09{index:D2}-{name}", "C1", ["kernel"], Base.AddMinutes(index)))
            .ToArray();
        var loud = Enumerable.Range(1, 10)
            .Select(index => PublishedLesson("2026-09-08_1048-loud", $"C{index}", ["kernel"], Base.AddHours(index)))
            .ToArray();

        var recalled = await RecallAsync([.. quiet, .. loud], ["kernel"]);

        // Three from the loud task — its three newest — and every quiet task reached.
        Assert.Equal(Ids([.. quiet, .. loud.TakeLast(3)]), recalled);
        Assert.Equal(3, recalled.Count(id => id.StartsWith("2026-09-08_1048-loud:", StringComparison.Ordinal)));
    }

    // Recency is demoted, not discarded. Between lessons matching the same number of requested tags
    // it is still what decides, because the newest lesson about a subject is the one that supersedes
    // the others in practice.
    [Fact]
    public async Task BetweenLessonsMatchingTheSameTagsTheNewerOneWins()
    {
        // Twelve source tasks, one lesson each, one tag each: overlap ties everywhere and the cap
        // binds nowhere, so only recency can decide which two are left out.
        var lessons = Enumerable.Range(1, 12)
            .Select(index => PublishedLesson($"2026-09-08_11{index:D2}-source", "C1", ["kernel"], Base.AddMinutes(index)))
            .ToArray();

        var recalled = await RecallAsync(lessons, ["kernel"]);

        Assert.Equal(Ids(lessons.TakeLast(10)), recalled);
        Assert.DoesNotContain(lessons[0].Id.Value, recalled);
        Assert.DoesNotContain(lessons[1].Id.Value, recalled);
    }

    // The cap is hard rather than a preference. A store holding only two matching source tasks
    // returns six lessons, not ten: backfilling the remaining slots from a task that already gave
    // three is exactly the single-task recall the cap exists to prevent.
    [Fact]
    public async Task AStoreWithTwoMatchingSourceTasksReturnsSixLessonsRatherThanBackfillingToTen()
    {
        var first = Enumerable.Range(1, 6)
            .Select(index => PublishedLesson("2026-09-08_1200-first", $"C{index}", ["kernel"], Base.AddMinutes(index)))
            .ToArray();
        var second = Enumerable.Range(1, 6)
            .Select(index => PublishedLesson("2026-09-08_1300-second", $"C{index}", ["kernel"], Base.AddHours(index)))
            .ToArray();

        var recalled = await RecallAsync([.. first, .. second], ["kernel"]);

        Assert.Equal(Ids([.. first.TakeLast(3), .. second.TakeLast(3)]), recalled);
        Assert.Equal(6, recalled.Length);
    }

    // The untagged path is untouched. An opening with no tags still takes every lesson from the
    // three most recently archived source tasks, with no tag filter, no ten-lesson budget and no
    // per-task cap — the behaviour every task on disk was opened under.
    [Fact]
    public async Task AnOpeningWithNoTagsStillRecallsEverythingFromTheThreeMostRecentSourceTasks()
    {
        var lessons = new[] { "oldest", "older", "newer", "newest" }
            .SelectMany((name, index) => new[]
            {
                PublishedLesson($"2026-09-08_14{index:D2}-{name}", "C1", ["kernel"], Base.AddHours(index)),
                PublishedLesson($"2026-09-08_14{index:D2}-{name}", "C2", ["unrequested"], Base.AddHours(index))
            })
            .ToArray();

        var recalled = await RecallAsync(lessons, null);

        // Six lessons from three tasks, including the ones tagged with something nobody asked for.
        Assert.Equal(Ids(lessons.Skip(2)), recalled);
        Assert.DoesNotContain(recalled, id => id.Contains("-oldest:", StringComparison.Ordinal));
    }

    private static async Task<string[]> RecallAsync(
        IReadOnlyList<Lesson> published,
        IReadOnlyList<string>? openingTags)
    {
        using var root = new TemporaryDirectory();
        using var lessonRoot = new TemporaryDirectory();
        // The task root holds no archived sibling, so the store is the only source of candidates and
        // the recall under test reads exactly what this test seeded.
        await new FileLessonStore(lessonRoot.Path).PublishAsync(published, CancellationToken.None);

        var actor = new ActorId("operator");
        var target = new TaskId("2026-09-08_1430-target");
        var opened = await Service(root.Path, lessonRoot.Path).ExecuteAsync(
            target,
            new OpenTaskCommand(actor, null, "target-open", target, "Target", "Recall", null, openingTags),
            CancellationToken.None);

        return Ids(opened.State.Lessons.Values);
    }

    private static string[] Ids(IEnumerable<Lesson> lessons) => lessons
        .Select(lesson => lesson.Id.Value)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();

    private static Lesson PublishedLesson(
        string sourceTaskId,
        string sourceRecordId,
        IReadOnlyList<string> tags,
        DateTimeOffset recordedAt) =>
        new(
            // The identifier a recalled lesson must carry: source task, source kind, source record.
            new LessonId($"{sourceTaskId}:validatedclaim:{sourceRecordId}"),
            new TaskId(sourceTaskId),
            LessonSourceKind.ValidatedClaim,
            sourceRecordId,
            $"{sourceTaskId} established {sourceRecordId}",
            "Validated",
            [$"src/AILedger.Storage/FileGovernedTaskService.cs:{sourceRecordId}"],
            // The close-out a recalled lesson must carry: minted by its source task's archive, at
            // the moment recall orders on.
            new Provenance(new ActorId("operator"), recordedAt, "stage.archive"),
            Class: LessonClass.Refuted,
            Repo: "AILedger",
            Tags: tags,
            Verify: "grep -n SelectRecalled src/AILedger.Storage/FileGovernedTaskService.cs",
            DoNot: "Do not order recall by recency alone",
            Actor: LessonActor.Verifier);

    private static FileGovernedTaskService Service(string root, string lessonRoot)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(
            root,
            new CommandHandler(reducer, new AuthorizationPolicy()),
            reducer,
            lessonStore: new FileLessonStore(lessonRoot));
    }
}
