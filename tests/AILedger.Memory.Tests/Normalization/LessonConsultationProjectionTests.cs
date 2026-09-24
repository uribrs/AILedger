using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Memory.Contracts;
using AILedger.Memory.Ingestion;
using AILedger.Memory.Tests.Support;
using AILedger.Storage;

namespace AILedger.Memory.Tests.Normalization;

// lesson.consulted is a new event for the third reader of the log. Without an arm the projector's
// switch throws and the index stops rebuilding every task that ever consulted (PD3).
public sealed class LessonConsultationProjectionTests
{
    // R1 (memory-projector-arm): a history carrying two consultations rebuilds through the production
    // reader. The first imports the lesson; the second re-serves it without importing it again. The
    // lesson document must cite the consultation that brought it in, and only that one: a re-serve is
    // not where the lesson came from.
    [Fact]
    public async Task R1_ConsultedLessonHistoryProjectsAndAttributes()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "events.jsonl");
        var lesson = ConsultedLesson();
        var run = new RunId("R1");
        await File.WriteAllTextAsync(path, Rows([
            Event(1, new TaskOpened("Fixture", "Replay a consulting task")),
            Event(2, new RunStarted(new AgentRun(
                run, new ActorId("operator"), null, "claude", null,
                AgentRunStatus.Active, DateTimeOffset.UnixEpoch.AddSeconds(2), null))),
            Event(3, new LessonsConsulted(
                run, LessonConsultationPurpose.Recon, "What do earlier recons say?", ["recon"], [],
                new string('a', 64), [lesson.Id], [lesson])),
            Event(4, new LessonsConsulted(
                run, LessonConsultationPurpose.Reconsideration, "Does the new strategy repeat a refuted one?",
                ["recon"], [], new string('a', 64), [lesson.Id], []))
        ]));

        var delta = await new EventHistorySourceReader().ReadAsync(Source(path));

        var document = Assert.Single(delta.Upserts, item => item.Kind == MemoryDocumentKind.Lesson);
        Assert.Equal(lesson.Id.Value, document.Citations.Single().RecordId);
        Assert.Equal("EV3", Assert.Single(document.Citations).EventId);
        Assert.Contains("Recon consultations must precede filing", document.Text, StringComparison.Ordinal);
    }

    // A consultation that served nothing still rebuilds, and produces no lesson document.
    [Fact]
    public async Task AZeroLessonConsultationRebuildsWithoutALessonDocument()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "events.jsonl");
        var run = new RunId("R1");
        await File.WriteAllTextAsync(path, Rows([
            Event(1, new TaskOpened("Fixture", "Replay an empty consultation")),
            Event(2, new RunStarted(new AgentRun(
                run, new ActorId("operator"), null, "claude", null,
                AgentRunStatus.Active, DateTimeOffset.UnixEpoch.AddSeconds(2), null))),
            Event(3, new LessonsConsulted(
                run, LessonConsultationPurpose.Recon, "What do earlier recons say?", ["recon"], [],
                new string('a', 64), [], []))
        ]));

        var delta = await new EventHistorySourceReader().ReadAsync(Source(path));

        Assert.DoesNotContain(delta.Upserts, item => item.Kind == MemoryDocumentKind.Lesson);
        Assert.Contains(delta.Upserts, item => item.Kind == MemoryDocumentKind.RunSummary);
    }

    private static Lesson ConsultedLesson() => new(
        new LessonId("earlier-task:validatedclaim:C1"),
        new TaskId("earlier-task"),
        LessonSourceKind.ValidatedClaim,
        "C1",
        "Recon consultations must precede filing",
        "Validated",
        ["src/AILedger.Core/Artifacts/Logic/InternalReconRules.cs:94"],
        new Provenance(new ActorId("operator"), DateTimeOffset.UnixEpoch, "stage.archive"),
        Class: LessonClass.Refuted,
        Repo: "AILedger",
        Tags: ["recon"]);

    private static string Rows(IEnumerable<LedgerEvent> events) =>
        string.Join('\n', events.Select(item => JsonSerializer.Serialize(item, LedgerJson.CreateOptions()))) + "\n";

    private static SourceDescriptor Source(string path) => new()
    {
        Id = "events",
        Kind = CanonicalSourceKind.EventHistory,
        CanonicalPath = path,
        TaskId = "T1"
    };

    private static LedgerEvent Event(long sequence, LedgerEventData data) => new(
        1,
        new EventId($"EV{sequence}"),
        new TaskId("T1"),
        new ActorId("operator"),
        DateTimeOffset.UnixEpoch.AddSeconds(sequence),
        null,
        $"correlation-{sequence}",
        data);
}
