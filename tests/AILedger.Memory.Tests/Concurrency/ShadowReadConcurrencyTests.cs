using System.Diagnostics;
using System.Text.Json;
using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Memory.Contracts;
using AILedger.Memory.Ingestion;
using AILedger.Memory.Tests.Support;
using AILedger.Storage;

namespace AILedger.Memory.Tests.Concurrency;

public sealed class ShadowReadConcurrencyTests
{
    [Fact]
    public async Task R2_IndexingSameTaskDoesNotAcquireWriterLockOrDelayCommand()
    {
        using var directory = new TemporaryDirectory();
        var taskDirectory = Path.Combine(directory.Path, "T1");
        var application = CreateApplication(TextWriter.Null, TextWriter.Null);
        var common = new[] { "--root", directory.Path, "--task", "T1", "--actor", "operator" };
        Assert.Equal(0, await application.RunAsync(
            ["task", "open", .. common, "--title", "Fixture", "--goal", "Concurrency proof"],
            CancellationToken.None));

        var source = new SourceDescriptor
        {
            Id = "events", Kind = CanonicalSourceKind.EventHistory,
            CanonicalPath = Path.Combine(taskDirectory, "events.jsonl"), TaskId = "T1"
        };

        await using (var heldWriterLock = new FileStream(
            Path.Combine(taskDirectory, ".writer.lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None))
        {
            var read = new EventHistorySourceReader().ReadAsync(source);
            var delta = await read.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(Assert.IsType<EventHistoryCheckpoint>(delta.NextCheckpoint).EventCount > 0);
        }

        var baseline = await RunStatusAsync(directory.Path);
        var stopwatch = Stopwatch.StartNew();
        var indexing = Task.WhenAll(Enumerable.Range(0, 25)
            .Select(_ => new EventHistorySourceReader().ReadAsync(source)));
        var concurrent = await RunStatusAsync(directory.Path);
        await indexing;
        stopwatch.Stop();

        Assert.Equal(0, concurrent.ExitCode);
        Assert.Equal(baseline.TaskVersion, concurrent.TaskVersion);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"A governed status command plus 25 same-task index reads took {stopwatch.Elapsed}.");
        Assert.True(File.Exists(Path.Combine(taskDirectory, ".writer.lock")));
    }

    private static async Task<(int ExitCode, long TaskVersion)> RunStatusAsync(string root)
    {
        var output = new StringWriter();
        var application = CreateApplication(output, TextWriter.Null);
        var exitCode = await application.RunAsync(
            ["status", "--root", root, "--task", "T1", "--actor", "operator"],
            CancellationToken.None);
        using var json = JsonDocument.Parse(output.ToString());
        return (exitCode, json.RootElement.GetProperty("version").GetInt64());
    }

    private static CliApplication CreateApplication(TextWriter output, TextWriter error)
    {
        var reducer = new TaskReducer();
        return new CliApplication(
            output,
            error,
            root => new FileGovernedTaskService(
                root,
                new CommandHandler(reducer, new AuthorizationPolicy()),
                reducer),
            _ => throw new InvalidOperationException("This test does not launch providers."),
            new ContextAssembler());
    }
}
