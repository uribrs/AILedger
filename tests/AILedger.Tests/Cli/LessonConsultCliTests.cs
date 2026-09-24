using System.Text.Json;
using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Lessons;
using AILedger.Tests.Support;

namespace AILedger.Tests.Cli;

// `lesson consult` through the CLI and the durable service it composes: the output JSON PPC2 freezes,
// with the full served lesson records, and the honest empty result.
[Collection(StandardInput.Collection)]
public sealed class LessonConsultCliTests
{
    [Fact]
    public async Task ConsultPrintsTheServedLessonsInFullAndOnlyThoseTheRoleMaySee()
    {
        using var root = new TemporaryDirectory();
        using var lessonRoot = new TemporaryDirectory();
        var output = new StringWriter();
        var error = new StringWriter();
        var cli = Cli(output, error, lessonRoot.Path);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await PrepareAsync(cli, common);
        var visible = LessonConsultationTests.Lesson("2026-09-20_1200-source", "C1", null) with { Tags = ["recon"] };
        var excluded = LessonConsultationTests.Lesson("2026-09-20_1200-source", "C2", [RoleKind.Verifier]) with
        {
            Tags = ["recon"]
        };
        await new FileLessonStore(lessonRoot.Path).PublishAsync([visible, excluded], CancellationToken.None);
        output.GetStringBuilder().Clear();

        var exit = await cli.RunAsync(
            ["lesson", "consult", .. common, "--run", "R1", "--purpose", "recon",
             "--question", "What do earlier recons say?", "--tag", "recon", "--claim", "C1"],
            CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, error.ToString());
        using var document = JsonDocument.Parse(output.ToString());
        var json = document.RootElement;
        Assert.Equal("T1", json.GetProperty("taskId").GetString());
        Assert.Equal("research", json.GetProperty("stage").GetString());
        Assert.Equal(1, json.GetProperty("events").GetArrayLength());
        Assert.Equal("recon", json.GetProperty("purpose").GetString());
        Assert.Equal(["C1"], Strings(json.GetProperty("claimIds")));
        Assert.Equal([visible.Id.Value], Strings(json.GetProperty("servedLessonIds")));
        Assert.Equal([visible.Id.Value], Strings(json.GetProperty("newLessonIds")));
        var printed = Assert.Single(json.GetProperty("lessons").EnumerateArray().ToArray());
        var lesson = printed.Deserialize<Lesson>(LedgerJson.CreateOptions())!;
        Assert.Equal(visible.Id, lesson.Id);
        Assert.Equal(visible.Statement, lesson.Statement);
        Assert.Equal(visible.Citations, lesson.Citations);
        Assert.Equal(visible.Provenance, lesson.Provenance);
        Assert.Equal(visible.Tags, lesson.Tags);
        Assert.DoesNotContain(excluded.Id.Value, output.ToString(), StringComparison.Ordinal);

        var state = (await CliApplicationTestSupport.Service(root.Path).GetStateAsync(new TaskId("T1"),
            CancellationToken.None))!;
        Assert.Equal(json.GetProperty("version").GetInt64(), state.Version);
        Assert.Equal([visible.Id], state.Lessons.Keys);
    }

    [Fact]
    public async Task AZeroLessonConsultPrintsEmptyLists()
    {
        using var root = new TemporaryDirectory();
        using var lessonRoot = new TemporaryDirectory();
        var output = new StringWriter();
        var error = new StringWriter();
        var cli = Cli(output, error, lessonRoot.Path);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await PrepareAsync(cli, common);
        output.GetStringBuilder().Clear();

        var exit = await cli.RunAsync(
            ["lesson", "consult", .. common, "--run", "R1", "--purpose", "recon",
             "--question", "What do earlier recons say?", "--tag", "recon"],
            CancellationToken.None);

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(output.ToString());
        var json = document.RootElement;
        Assert.Empty(Strings(json.GetProperty("claimIds")));
        Assert.Empty(Strings(json.GetProperty("servedLessonIds")));
        Assert.Empty(Strings(json.GetProperty("newLessonIds")));
        Assert.Equal(0, json.GetProperty("lessons").GetArrayLength());
    }

    [Theory]
    [InlineData("--purpose", "bogus")]
    [InlineData("--purpose", "research")]
    [InlineData("--question", " ")]
    public async Task AnInvalidConsultExitsNonzeroAndRecordsNothing(string option, string value)
    {
        using var root = new TemporaryDirectory();
        using var lessonRoot = new TemporaryDirectory();
        var output = new StringWriter();
        var error = new StringWriter();
        var cli = Cli(output, error, lessonRoot.Path);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await PrepareAsync(cli, common);
        var service = CliApplicationTestSupport.Service(root.Path);
        var version = (await service.GetStateAsync(new TaskId("T1"), CancellationToken.None))!.Version;
        var arguments = new Dictionary<string, string>
        {
            ["--purpose"] = "recon",
            ["--question"] = "What do earlier recons say?"
        };
        arguments[option] = value;

        var exit = await cli.RunAsync(
            ["lesson", "consult", .. common, "--run", "R1", "--tag", "recon",
             .. arguments.SelectMany(pair => new[] { pair.Key, pair.Value })],
            CancellationToken.None);

        Assert.NotEqual(0, exit);
        Assert.NotEqual(string.Empty, error.ToString());
        var state = (await service.GetStateAsync(new TaskId("T1"), CancellationToken.None))!;
        Assert.Equal(version, state.Version);
        Assert.Empty(state.LessonConsultations);
    }

    private static async Task PrepareAsync(CliApplication cli, string[] common)
    {
        foreach (var arguments in new[]
                 {
                     new[] { "task", "open", "--title", "Consult", "--goal", "Test" },
                     ["claim", "add", "--id", "C1", "--statement", "Something to research"],
                     ["stage", "transition", "--stage", "research"],
                     ["run", "start", "--run", "R1", "--provider", "codex"]
                 })
        {
            Assert.Equal(0, await cli.RunAsync([.. arguments, .. common], CancellationToken.None));
        }
    }

    private static CliApplication Cli(TextWriter output, TextWriter error, string lessonRoot) => new(
        output,
        error,
        root =>
        {
            var reducer = new TaskReducer();
            return new FileGovernedTaskService(root, new CommandHandler(reducer, new AuthorizationPolicy()),
                reducer, lessonStore: new FileLessonStore(lessonRoot));
        },
        _ => throw new InvalidOperationException("No provider dispatch in a consult test."),
        new ContextAssembler());

    private static string[] Strings(JsonElement array) =>
        array.EnumerateArray().Select(item => item.GetString()!).ToArray();
}
