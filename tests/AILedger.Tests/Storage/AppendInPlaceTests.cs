using System.Text.Json;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;
using Xunit.Abstractions;

namespace AILedger.Tests.Storage;

public sealed class AppendInPlaceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task AnAppendThatWouldExceedTheCapIsStillRefused()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("append-byte-cap");
        var actor = new ActorId("operator");
        var service = CreateService(root.Path);
        await OpenAsync(service, taskId, actor);

        var eventsPath = Path.Combine(root.Path, taskId.Value, "events.jsonl");
        var committedLength = new FileInfo(eventsPath).Length;
        var committedVersion = (await service.GetStateAsync(taskId, CancellationToken.None))!.Version;
        var capped = CreateService(root.Path, maximumEventLogBytes: committedLength);

        var exception = await Assert.ThrowsAsync<GovernanceException>(() => capped.ExecuteAsync(
            taskId,
            new AddClaimCommand(actor, null, "over-cap", new ClaimId("C1"), "Claim", null),
            CancellationToken.None));

        Assert.Contains("Archive it and open a successor", exception.Message, StringComparison.Ordinal);
        Assert.Equal(committedLength, new FileInfo(eventsPath).Length);
        Assert.Equal(committedVersion, (await service.GetStateAsync(taskId, CancellationToken.None))!.Version);
    }

    [Fact]
    public async Task TwoConcurrentAppendsProduceTwoWholeLines()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("concurrent-appends");
        var actor = new ActorId("operator");
        var service = CreateService(root.Path);
        await OpenAsync(service, taskId, actor);
        var startingVersion = (await service.GetStateAsync(taskId, CancellationToken.None))!.Version;

        await Task.WhenAll(
            CreateService(root.Path).ExecuteAsync(
                taskId,
                new AddClaimCommand(actor, null, "one", new ClaimId("C1"), "One", null),
                CancellationToken.None),
            CreateService(root.Path).ExecuteAsync(
                taskId,
                new AddClaimCommand(actor, null, "two", new ClaimId("C2"), "Two", null),
                CancellationToken.None));

        var eventsPath = Path.Combine(root.Path, taskId.Value, "events.jsonl");
        var lines = await File.ReadAllLinesAsync(eventsPath);
        Assert.Equal(startingVersion + 2, lines.Length);
        Assert.All(lines, line => Assert.NotNull(
            JsonSerializer.Deserialize<LedgerEvent>(line, LedgerJson.CreateOptions())));
        Assert.Equal(lines.Length, (await service.GetStateAsync(taskId, CancellationToken.None))!.Version);
    }

    [Fact]
    public async Task AnUnterminatedFinalFragmentIsIgnoredAndRemovedByTheNextAppend()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("torn-append");
        var actor = new ActorId("operator");
        var service = CreateService(root.Path);
        await OpenAsync(service, taskId, actor);

        var eventsPath = Path.Combine(root.Path, taskId.Value, "events.jsonl");
        var committedVersion = (await service.GetStateAsync(taskId, CancellationToken.None))!.Version;
        await File.AppendAllTextAsync(eventsPath, "{\"eventId\":\"torn");

        Assert.Equal(
            committedVersion,
            (await CreateService(root.Path).GetStateAsync(taskId, CancellationToken.None))!.Version);

        await CreateService(root.Path).ExecuteAsync(
            taskId,
            new AddClaimCommand(actor, null, "after-torn", new ClaimId("C1"), "After torn append", null),
            CancellationToken.None);

        var bytes = await File.ReadAllBytesAsync(eventsPath);
        Assert.Equal((byte)'\n', bytes[^1]);
        var lines = await File.ReadAllLinesAsync(eventsPath);
        Assert.Equal(committedVersion + 1, lines.Length);
        Assert.All(lines, line => Assert.NotNull(
            JsonSerializer.Deserialize<LedgerEvent>(line, LedgerJson.CreateOptions())));
        Assert.Equal(lines.Length, (await service.GetStateAsync(taskId, CancellationToken.None))!.Version);
    }

    [Fact]
    public async Task AThousandAppendsWriteRoughlyTheLogSizeNotItsSquare()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("thousand-appends");
        var actor = new ActorId("operator");
        var service = new FileGovernedTaskService(
            root.Path,
            new SyntheticHandler(taskId, actor),
            new SyntheticReducer(),
            maximumEventsPerTask: 2_000);

        // 575 approximately 700-byte events reproduce the measured ledger-learning fixture.
        for (var index = 0; index < 575; index++)
        {
            await service.ExecuteAsync(
                taskId,
                new SyntheticCommand(actor, $"seed-{index}"),
                CancellationToken.None);
        }

        var taskDirectory = Path.Combine(root.Path, taskId.Value);
        var eventsPath = Path.Combine(taskDirectory, "events.jsonl");
        long copiedBytes = 0;
        long appendedBytes = 0;
        var temporaryAppendFiles = 0;
        using var watcher = new FileSystemWatcher(taskDirectory, "*.append");
        watcher.Created += (_, _) => Interlocked.Increment(ref temporaryAppendFiles);
        watcher.EnableRaisingEvents = true;

        for (var index = 0; index < 1_000; index++)
        {
            var before = new FileInfo(eventsPath).Length;
            await service.ExecuteAsync(
                taskId,
                new SyntheticCommand(actor, $"measured-{index}"),
                CancellationToken.None);
            var after = new FileInfo(eventsPath).Length;
            copiedBytes += after;
            appendedBytes += after - before;
        }

        await Task.Delay(100);
        output.WriteLine($"copy-and-rename logical bytes: {copiedBytes:N0}");
        output.WriteLine($"append-in-place logical bytes: {appendedBytes:N0}");
        output.WriteLine($"reduction: {(double)copiedBytes / appendedBytes:N2}x");
        Assert.Equal(0, Volatile.Read(ref temporaryAppendFiles));
        Assert.True(copiedBytes > appendedBytes * 500);
        Assert.Equal(1_575, File.ReadLines(eventsPath).Count());
    }

    private static FileGovernedTaskService CreateService(
        string root,
        long maximumEventLogBytes = FileGovernedTaskService.DefaultMaximumEventLogBytes) =>
        new(
            root,
            new CommandHandler(),
            new TaskReducer(),
            maximumEventLogBytes: maximumEventLogBytes);

    private static Task OpenAsync(IGovernedTaskService service, TaskId taskId, ActorId actor) =>
        service.ExecuteAsync(
            taskId,
            new OpenTaskCommand(actor, null, "open", taskId, "Task", "Goal"),
            CancellationToken.None);

    private sealed record SyntheticCommand(ActorId ActorId, string CorrelationId) :
        LedgerCommand(ActorId, null, CorrelationId);

    private sealed class SyntheticHandler(TaskId taskId, ActorId actor) : ICommandHandler
    {
        private static readonly string Payload = new('x', 500);

        public CommandOutcome Handle(GovernedTaskState? state, LedgerCommand command, DateTimeOffset now)
        {
            var nextVersion = (state?.Version ?? 0) + 1;
            var nextState = state is null
                ? new GovernedTaskState
                {
                    TaskId = taskId,
                    Title = "Synthetic",
                    Goal = "Measure event-log writes",
                    Version = nextVersion
                }
                : state with { Version = nextVersion };
            var envelope = new LedgerEvent(
                GovernedTaskState.CurrentSchemaVersion,
                new EventId($"{taskId.Value}:{nextVersion:D10}"),
                taskId,
                actor,
                now,
                null,
                command.CorrelationId,
                new TaskOpened("Synthetic", Payload));
            return new CommandOutcome(nextState, [envelope]);
        }
    }

    private sealed class SyntheticReducer : ITaskReducer
    {
        public GovernedTaskState Apply(GovernedTaskState? state, LedgerEvent @event) =>
            state is null
                ? new GovernedTaskState
                {
                    TaskId = @event.TaskId,
                    Title = "Synthetic",
                    Goal = "Measure event-log writes",
                    Version = 1
                }
                : state with { Version = state.Version + 1 };
    }
}
