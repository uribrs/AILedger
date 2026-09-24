using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Lessons;
using AILedger.Tests.Support;

namespace AILedger.Tests.Storage;

// The durable service injects the store's candidates into a consultation, through the same selection
// opening recall uses (PC5). These tests drive the real FileGovernedTaskService and FileLessonStore.
public sealed class LessonConsultationServiceTests
{
    private static readonly ActorId Operator = new("operator");
    private static readonly TaskId Target = new("2026-09-24_1200-target");
    private static readonly RunId Run = new("R-consult");

    // S1 through the service: a store lesson matching a consultation tag is served and imported; one
    // matching no tag, and one minted by the consulting task itself, are not. The imported lesson
    // survives a replay from the event log alone.
    [Fact]
    public async Task TheServiceServesStoreLessonsMatchingTheConsultationTags()
    {
        using var root = new TemporaryDirectory();
        using var lessonRoot = new TemporaryDirectory();
        var service = Service(root.Path, lessonRoot.Path);
        await OpenAtResearchWithRunAsync(service);
        var matching = LessonConsultationTests.Lesson("2026-09-20_1200-source", "C1", null) with { Tags = ["recon"] };
        var unrelated = LessonConsultationTests.Lesson("2026-09-20_1200-source", "C2", null) with { Tags = ["billing"] };
        var own = LessonConsultationTests.Lesson(Target.Value, "C3", null) with { Tags = ["recon"] };
        await new FileLessonStore(lessonRoot.Path).PublishAsync([matching, unrelated, own], CancellationToken.None);

        var outcome = await service.ExecuteAsync(Target, Consult(["recon"]), CancellationToken.None);

        var consulted = Assert.IsType<LessonsConsulted>(Assert.Single(outcome.Events).Data);
        Assert.Equal([matching.Id], consulted.ServedLessonIds);
        Assert.Equal([matching.Id], consulted.NewLessons.Select(lesson => lesson.Id));
        File.Delete(Path.Combine(root.Path, Target.Value, "state.json"));
        var replayed = (await Service(root.Path, lessonRoot.Path).GetStateAsync(Target, CancellationToken.None))!;
        Assert.Equal([matching.Id], replayed.Lessons.Keys);
        Assert.Equal([matching.Id], Assert.Single(replayed.LessonConsultations).ServedLessonIds);
    }

    // S2 through the service: nothing matches, and the consultation is still recorded.
    [Fact]
    public async Task AConsultationMatchingNothingIsRecordedWithNothingServed()
    {
        using var root = new TemporaryDirectory();
        using var lessonRoot = new TemporaryDirectory();
        var service = Service(root.Path, lessonRoot.Path);
        await OpenAtResearchWithRunAsync(service);
        await new FileLessonStore(lessonRoot.Path).PublishAsync(
            [LessonConsultationTests.Lesson("2026-09-20_1200-source", "C1", null) with { Tags = ["billing"] }],
            CancellationToken.None);

        var outcome = await service.ExecuteAsync(Target, Consult(["recon"]), CancellationToken.None);

        var consulted = Assert.IsType<LessonsConsulted>(Assert.Single(outcome.Events).Data);
        Assert.Empty(consulted.ServedLessonIds);
        Assert.Empty(outcome.State.Lessons);
        Assert.Single(outcome.State.LessonConsultations);
    }

    // Plan A3: a store that cannot be read fails the consultation loudly and records nothing, the
    // same way it already fails task open.
    [Fact]
    public async Task AnUnreadableStoreFailsTheConsultationAndRecordsNothing()
    {
        using var root = new TemporaryDirectory();
        using var lessonRoot = new TemporaryDirectory();
        var service = Service(root.Path, lessonRoot.Path);
        await OpenAtResearchWithRunAsync(service);
        var before = (await service.GetStateAsync(Target, CancellationToken.None))!.Version;
        await File.WriteAllTextAsync(Path.Combine(lessonRoot.Path, FileLessonStore.LessonsFileName), "{not json\n");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.ExecuteAsync(Target, Consult(["recon"]), CancellationToken.None));

        var after = (await service.GetStateAsync(Target, CancellationToken.None))!;
        Assert.Equal(before, after.Version);
        Assert.Empty(after.LessonConsultations);
    }

    // The service replaces whatever candidates a caller put on the command: only the store decides.
    [Fact]
    public async Task CallerSuppliedCandidatesAreReplacedByTheStoreSelection()
    {
        using var root = new TemporaryDirectory();
        using var lessonRoot = new TemporaryDirectory();
        var service = Service(root.Path, lessonRoot.Path);
        await OpenAtResearchWithRunAsync(service);
        var smuggled = LessonConsultationTests.Lesson("2026-09-20_1200-source", "C9", null);

        var outcome = await service.ExecuteAsync(
            Target, Consult(["recon"]) with { Candidates = [smuggled] }, CancellationToken.None);

        Assert.Empty(Assert.IsType<LessonsConsulted>(Assert.Single(outcome.Events).Data).ServedLessonIds);
        Assert.Empty(outcome.State.Lessons);
    }

    private static async Task OpenAtResearchWithRunAsync(FileGovernedTaskService service)
    {
        // A tag nothing in the store carries keeps opening recall from importing what the test seeds.
        await service.ExecuteAsync(Target,
            new OpenTaskCommand(Operator, null, "open", Target, "Target", "Consult", null, ["opening-only"]),
            CancellationToken.None);
        await service.ExecuteAsync(Target,
            new AddClaimCommand(Operator, null, "claim", new ClaimId("C1"), "Something to research", null),
            CancellationToken.None);
        await service.ExecuteAsync(Target,
            new RequestStageTransitionCommand(Operator, null, "research", TaskStage.Research),
            CancellationToken.None);
        await service.ExecuteAsync(Target,
            new StartRunCommand(Operator, null, "run", Run, null, "codex", null),
            CancellationToken.None);
    }

    private static ConsultLessonsCommand Consult(IReadOnlyList<string> tags) =>
        new(Operator, null, "consult", Run, LessonConsultationPurpose.Recon,
            "What do earlier recons say?", tags, [new ClaimId("C1")]);

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
