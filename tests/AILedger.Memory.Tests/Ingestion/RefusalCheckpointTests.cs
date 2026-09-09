using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Memory.Contracts;
using AILedger.Memory.Ingestion;
using AILedger.Memory.Tests.Support;
using AILedger.Storage;

namespace AILedger.Memory.Tests.Ingestion;

public sealed class RefusalCheckpointTests
{
    [Fact]
    public async Task R3_TornRefusalRowRemainsUncheckpointedUntilComplete()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "refusals.jsonl");
        var complete = Serialize(new RefusalRecord(
            DateTimeOffset.Parse("2026-09-08T12:00:00Z"),
            new ActorId("worker"),
            "work complete",
            RefusalSite.Service,
            7,
            "Verifier run required."));
        var second = Serialize(new RefusalRecord(
            DateTimeOffset.Parse("2026-09-08T12:01:00Z"),
            new ActorId("worker"),
            "work complete",
            RefusalSite.Service,
            8,
            "Code review required."));
        var tornLength = second.Length / 2;
        await File.WriteAllTextAsync(path, complete + "\n" + second[..tornLength]);

        var source = new SourceDescriptor
        {
            Id = "refusals", Kind = CanonicalSourceKind.RefusalJournal, CanonicalPath = path, TaskId = "T1"
        };
        var reader = new RefusalJournalSourceReader();
        var first = await reader.ReadAsync(source);
        var firstCheckpoint = Assert.IsType<RefusalJournalCheckpoint>(first.NextCheckpoint);

        Assert.Single(first.Upserts);
        Assert.Equal(complete.Length + 1, firstCheckpoint.CommittedByteOffset);
        Assert.Equal(1, firstCheckpoint.CommittedLineCount);

        await File.AppendAllTextAsync(path, second[tornLength..] + "\n");
        var resumed = await reader.ReadAsync(source, firstCheckpoint);
        var resumedCheckpoint = Assert.IsType<RefusalJournalCheckpoint>(resumed.NextCheckpoint);

        Assert.Single(resumed.Upserts);
        Assert.Equal(2, resumedCheckpoint.CommittedLineCount);
        Assert.Equal(new FileInfo(path).Length, resumedCheckpoint.CommittedByteOffset);
        Assert.Equal(2, resumed.Upserts[0].Citations.Single().LineNumber);
    }

    [Fact]
    public async Task CorruptCompleteRefusalFailsVisiblyWithoutReturningACheckpoint()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "refusals.jsonl");
        await File.WriteAllTextAsync(path, "{not-json}\n");
        var source = new SourceDescriptor
        {
            Id = "refusals", Kind = CanonicalSourceKind.RefusalJournal, CanonicalPath = path
        };

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new RefusalJournalSourceReader().ReadAsync(source));
        Assert.Contains("line 1", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string Serialize(RefusalRecord record) =>
        JsonSerializer.Serialize(record, LedgerJson.CreateOptions());
}
