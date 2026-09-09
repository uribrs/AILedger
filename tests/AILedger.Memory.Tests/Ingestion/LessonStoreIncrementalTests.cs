using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Memory.Contracts;
using AILedger.Memory.Ingestion;
using AILedger.Memory.Tests.Support;
using AILedger.Storage;

namespace AILedger.Memory.Tests.Ingestion;

public sealed class LessonStoreIncrementalTests
{
    [Fact]
    public async Task AppendedLessonSupersessionReemitsPriorLessonLikeRebuild()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "lessons.jsonl");
        var source = new SourceDescriptor
        {
            Id = "lessons",
            Kind = CanonicalSourceKind.PublishedLessons,
            CanonicalPath = path,
            Repository = "AILedger"
        };
        var original = Lesson("L1", "Use the original rule");
        await WriteLessonsAsync(path, [original], append: false);
        var reader = new LessonStoreSourceReader();
        var first = await reader.ReadAsync(source);

        var revision = Lesson("L2", "Use the revised rule", original.Id);
        await WriteLessonsAsync(path, [revision], append: true);

        var incremental = await reader.ReadAsync(source, first.NextCheckpoint);
        var rebuilt = await reader.ReadAsync(source);
        Assert.Equal(MemoryLifecycle.Superseded,
            Assert.Single(incremental.Upserts, item => item.SourceVersion == "L1").Lifecycle);
        Assert.Equal(MemoryLifecycle.Active,
            Assert.Single(incremental.Upserts, item => item.SourceVersion == "L2").Lifecycle);
        Assert.Equal(2, incremental.Upserts.Count);
        foreach (var changed in incremental.Upserts)
        {
            Assert.Equal(
                JsonSerializer.Serialize(changed),
                JsonSerializer.Serialize(Assert.Single(rebuilt.Upserts, item => item.Id == changed.Id)));
        }
    }

    private static Lesson Lesson(string id, string statement, LessonId? supersedes = null) => new(
        new LessonId(id),
        new TaskId("source-task"),
        LessonSourceKind.ValidatedClaim,
        "C1",
        statement,
        "The durable outcome",
        ["events.jsonl:1"],
        new Provenance(
            new ActorId("executor"),
            DateTimeOffset.Parse("2026-09-08T12:00:00Z"),
            "stage.archive"),
        supersedes,
        LessonClass.Drifted,
        "AILedger",
        ["memory"],
        "dotnet test",
        "Do not regress the rule",
        LessonActor.Executor);

    private static async Task WriteLessonsAsync(string path, IEnumerable<Lesson> lessons, bool append)
    {
        var rows = string.Join('\n', lessons.Select(item =>
            JsonSerializer.Serialize(item, LedgerJson.CreateOptions()))) + "\n";
        if (append)
        {
            await File.AppendAllTextAsync(path, rows);
        }
        else
        {
            await File.WriteAllTextAsync(path, rows);
        }
    }
}
