using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Cli;

// LessonCitationTests pins the rule; these pin the option that reaches it. The two are not the same
// check: a field the kernel accepts and the parser never passes is a field no task can use, and no
// kernel test can see that. The lesson is put in the store directly rather than minted by an archive
// walk, because what is under test here is the option, not recall.
public sealed class LessonCitationCliTests
{
    private static readonly LessonId RecalledId = new("2026-09-01_1200-source:validatedclaim:C1");

    [Fact]
    public async Task ClaimDecisionAndAlternativeMayEachCiteARecalledLessonThroughTheCli()
    {
        using var root = new TemporaryDirectory();
        using var lessonRoot = new TemporaryDirectory();
        await PublishRecalledLessonAsync(lessonRoot.Path);
        var error = new StringWriter();
        var application = Create(error);
        var common = Common(root.Path, lessonRoot.Path);

        var exits = new[]
        {
            await application.RunAsync(
                ["task", "open", .. common, "--title", "Target", "--goal", "Cite"],
                CancellationToken.None),
            await application.RunAsync(
                ["claim", "add", .. common, "--id", "C1",
                 "--statement", "The adapter discards the session on failure",
                 "--from-lesson", RecalledId.Value], CancellationToken.None),
            await application.RunAsync(
                ["decision", "propose", .. common, "--id", "D1", "--statement", "Keep the session",
                 "--rationale", "The recalled lesson says a discarded session cannot be resumed",
                 "--from-lesson", RecalledId.Value], CancellationToken.None),
            await application.RunAsync(
                ["alternative", "record", .. common, "--id", "ALT1", "--statement", "Discard the session",
                 "--rejected-because", "The recalled lesson refuted it",
                 "--from-lesson", RecalledId.Value], CancellationToken.None)
        };

        var state = await Service(root.Path, lessonRoot.Path)
            .GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.All(exits, exit => Assert.Equal(0, exit));
        Assert.Equal(string.Empty, error.ToString());
        Assert.Contains(RecalledId, state!.Lessons.Keys);
        Assert.Equal(RecalledId, state.Claims[new ClaimId("C1")].FromLesson);
        Assert.Equal(RecalledId, state.Decisions[new DecisionId("D1")].FromLesson);
        Assert.Equal(RecalledId, state.Alternatives[new AlternativeId("ALT1")].FromLesson);
    }

    // Optional, so a task that cites nothing is not doing anything wrong. Asserted through the CLI
    // as well as the kernel because omission is the common case: if the parser turned an absent
    // option into something other than null, every uncited record would carry a wrong citation.
    [Fact]
    public async Task OmittingTheCitationLeavesTheRecordUncited()
    {
        using var root = new TemporaryDirectory();
        using var lessonRoot = new TemporaryDirectory();
        await PublishRecalledLessonAsync(lessonRoot.Path);
        var error = new StringWriter();
        var application = Create(error);
        var common = Common(root.Path, lessonRoot.Path);
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Target", "--goal", "Cite"], CancellationToken.None);

        var exit = await application.RunAsync(
            ["claim", "add", .. common, "--id", "C1", "--statement", "An unprompted claim"],
            CancellationToken.None);

        var state = await Service(root.Path, lessonRoot.Path)
            .GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Null(state!.Claims[new ClaimId("C1")].FromLesson);
    }

    // The refusal an operator actually sees. The lesson store holds a lesson, so the store is not
    // what is missing — this task was never shown the one being cited.
    [Fact]
    public async Task CitingALessonTheTaskNeverRecalledIsRefusedByTheCli()
    {
        using var root = new TemporaryDirectory();
        using var lessonRoot = new TemporaryDirectory();
        await PublishRecalledLessonAsync(lessonRoot.Path);
        var error = new StringWriter();
        var application = Create(error);
        var common = Common(root.Path, lessonRoot.Path);
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Target", "--goal", "Cite"], CancellationToken.None);

        var exit = await application.RunAsync(
            ["claim", "add", .. common, "--id", "C1", "--statement", "Borrowed authority",
             "--from-lesson", "another-task:validatedclaim:C9"], CancellationToken.None);

        var state = await Service(root.Path, lessonRoot.Path)
            .GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(1, exit);
        Assert.Contains("recalled lesson", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(new ClaimId("C1"), state!.Claims.Keys);
    }

    private static string[] Common(string root, string lessonRoot) =>
    [
        "--root", root, "--lesson-root", lessonRoot, "--task", "T1", "--actor", "operator"
    ];

    private static Task PublishRecalledLessonAsync(string lessonRoot) =>
        new FileLessonStore(lessonRoot).PublishAsync(
            [
                new Lesson(
                    RecalledId,
                    new TaskId("2026-09-01_1200-source"),
                    LessonSourceKind.ValidatedClaim,
                    "C1",
                    "The adapter discards the provider session on failure",
                    "Validated",
                    ["src/AILedger.Providers/Adapters/AgentAdapterBase.cs:104"],
                    new Provenance(
                        new ActorId("operator"),
                        new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
                        "stage.archive"),
                    null,
                    LessonClass.Refuted,
                    "AILedger",
                    ["adapter", "session"],
                    "grep -n session AgentAdapterBase.cs",
                    "Do not assume a failed run can be resumed",
                    LessonActor.Verifier)
            ],
            CancellationToken.None);

    private static CliApplication Create(TextWriter error) => new(
        TextWriter.Null,
        error,
        Service,
        _ => throw new InvalidOperationException("These tests launch no provider."),
        new ContextAssembler());

    private static IGovernedTaskService Service(string root, string lessonRoot)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(
            root, new CommandHandler(reducer, new AuthorizationPolicy()), reducer,
            lessonStore: new FileLessonStore(lessonRoot));
    }
}
