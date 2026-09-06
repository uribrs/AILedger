using System.Text.Json;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Storage;

public sealed class RecoveryTests
{
    [Fact]
    public async Task R1_ReopenReplaysEventsWhenMaterializedStateIsStale()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("recovery-task");
        var actor = new ActorId("operator");
        var service = CreateService(root.Path);
        await OpenAsync(service, taskId, actor);
        await service.ExecuteAsync(
            taskId,
            new AddClaimCommand(actor, null, "claim-1", new ClaimId("C1"), "Durable truth", null),
            CancellationToken.None);

        var taskDirectory = Path.Combine(root.Path, taskId.Value);
        await File.WriteAllTextAsync(Path.Combine(taskDirectory, "state.json"), "{}\n");
        await File.WriteAllTextAsync(Path.Combine(taskDirectory, "assumptions.md"), "stale\n");

        var reopened = CreateService(root.Path);
        var state = await reopened.GetStateAsync(taskId, CancellationToken.None);

        Assert.NotNull(state);
        Assert.Equal(3, state.Version);
        Assert.Equal("Durable truth", state.Claims[new ClaimId("C1")].Statement);
        var stateJson = await File.ReadAllTextAsync(Path.Combine(taskDirectory, "state.json"));
        Assert.Contains("Durable truth", stateJson, StringComparison.Ordinal);
        var assumptions = await File.ReadAllTextAsync(Path.Combine(taskDirectory, "assumptions.md"));
        Assert.Contains("Durable truth", assumptions, StringComparison.Ordinal);
        Assert.DoesNotContain("stale", assumptions, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReopenUsesEventLogAsAuthorityAcrossServiceInstances()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("restart-task");
        var actor = new ActorId("operator");
        await OpenAsync(CreateService(root.Path), taskId, actor);

        var history = new List<LedgerEvent>();
        await foreach (var item in CreateService(root.Path).GetHistoryAsync(taskId, CancellationToken.None))
        {
            history.Add(item);
        }

        Assert.Collection(
            history,
            item => Assert.IsType<TaskOpened>(item.Data),
            item => Assert.IsType<RoleAssigned>(item.Data));
        Assert.Equal(history[0].EventId, history[1].CausationId);
    }

    [Fact]
    public async Task CorruptEventLineFailsClosedWithLineNumber()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("corrupt-task");
        var actor = new ActorId("operator");
        await OpenAsync(CreateService(root.Path), taskId, actor);
        await File.AppendAllTextAsync(Path.Combine(root.Path, taskId.Value, "events.jsonl"), "not-json\n");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            CreateService(root.Path).GetStateAsync(taskId, CancellationToken.None));

        Assert.Contains("line 3", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PersistedEventsRoundTripPolymorphicPayloadAndIdentifiers()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("roundtrip-task");
        var actor = new ActorId("operator");
        var service = CreateService(root.Path);
        await OpenAsync(service, taskId, actor);
        await service.ExecuteAsync(taskId,
            new AddClaimCommand(actor, null, "claim", new ClaimId("C1"), "Claim", null),
            CancellationToken.None);

        var lines = await File.ReadAllLinesAsync(Path.Combine(root.Path, taskId.Value, "events.jsonl"));
        using var document = JsonDocument.Parse(lines[^1]);

        Assert.Equal("claim.added", document.RootElement.GetProperty("data").GetProperty("eventType").GetString());
        Assert.Equal("C1", document.RootElement.GetProperty("data").GetProperty("claim").GetProperty("id").GetString());
    }

    [Fact]
    public async Task CurrentStateStillRepairsMissingProjection()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("projection-repair-task");
        var service = CreateService(root.Path);
        await OpenAsync(service, taskId, new ActorId("operator"));
        var projection = Path.Combine(root.Path, taskId.Value, "task.md");
        File.Delete(projection);

        var state = await service.GetStateAsync(taskId, CancellationToken.None);

        Assert.NotNull(state);
        Assert.True(File.Exists(projection));
        Assert.Contains("# Task", await File.ReadAllTextAsync(projection), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DerivedWriteFailureDoesNotMakeCommittedCommandAmbiguous()
    {
        using var root = new TemporaryDirectory();
        var writer = new FailOnceProjectionWriter();
        var taskId = new TaskId("derived-failure-task");
        var actor = new ActorId("operator");
        var service = new FileGovernedTaskService(root.Path, new CommandHandler(), new TaskReducer(), writer);

        var outcome = await service.ExecuteAsync(
            taskId, new OpenTaskCommand(actor, null, "open", taskId, "Task", "Goal"), CancellationToken.None);
        var recovered = await service.GetStateAsync(taskId, CancellationToken.None);

        Assert.Equal(2, outcome.State.Version);
        Assert.Equal(2, recovered?.Version);
        Assert.True(writer.SuccessfulWrites > 0);
    }

    [Fact]
    public async Task NonIoDerivedFailureDoesNotMakeCommittedCommandAmbiguous()
    {
        using var root = new TemporaryDirectory();
        var writer = new FailOnceProjectionWriter(new InvalidOperationException("Injected projection bug."));
        var taskId = new TaskId("derived-non-io-failure-task");
        var actor = new ActorId("operator");
        var service = new FileGovernedTaskService(root.Path, new CommandHandler(), new TaskReducer(), writer);

        var outcome = await service.ExecuteAsync(
            taskId, new OpenTaskCommand(actor, null, "open", taskId, "Task", "Goal"), CancellationToken.None);

        Assert.Equal(2, outcome.State.Version);
        Assert.Equal(2, (await service.GetStateAsync(taskId, CancellationToken.None))?.Version);
    }

    [Fact]
    public async Task StateReadSucceedsWhenDisposableProjectionRepairKeepsFailing()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("read-projection-failure-task");
        var service = new FileGovernedTaskService(
            root.Path, new CommandHandler(), new TaskReducer(), new AlwaysFailProjectionWriter());
        await OpenAsync(service, taskId, new ActorId("operator"));

        var state = await service.GetStateAsync(taskId, CancellationToken.None);

        Assert.NotNull(state);
        Assert.Equal(2, state.Version);
    }

    [Fact]
    public async Task ReplayRejectsSyntacticallyValidEventSequenceCorruption()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("sequence-task");
        await OpenAsync(CreateService(root.Path), taskId, new ActorId("operator"));
        var eventsPath = Path.Combine(root.Path, taskId.Value, "events.jsonl");
        var lines = await File.ReadAllLinesAsync(eventsPath);
        await File.WriteAllLinesAsync(eventsPath, [lines[0], lines[0]]);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            CreateService(root.Path).GetStateAsync(taskId, CancellationToken.None));

        Assert.Contains("Event sequence is invalid", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplayRejectsUnknownOrFutureCausation()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("causation-task");
        await OpenAsync(CreateService(root.Path), taskId, new ActorId("operator"));
        var eventsPath = Path.Combine(root.Path, taskId.Value, "events.jsonl");
        var lines = await File.ReadAllLinesAsync(eventsPath);
        var second = JsonSerializer.Deserialize<LedgerEvent>(lines[1], LedgerJson.CreateOptions())! with
        {
            CausationId = new EventId($"{taskId.Value}:0000009999")
        };
        lines[1] = JsonSerializer.Serialize(second, LedgerJson.CreateOptions());
        await File.WriteAllLinesAsync(eventsPath, lines);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            CreateService(root.Path).GetStateAsync(taskId, CancellationToken.None));

        Assert.Contains("unknown or non-prior cause", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MutationCannotCommitUnknownCausation()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("causation-write-task");
        var actor = new ActorId("operator");
        var service = CreateService(root.Path);
        await OpenAsync(service, taskId, actor);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.ExecuteAsync(
            taskId,
            new AddClaimCommand(
                actor, new EventId($"{taskId.Value}:0000009999"), "claim", new ClaimId("C1"), "Claim", null),
            CancellationToken.None));

        Assert.Equal(2, (await service.GetStateAsync(taskId, CancellationToken.None))?.Version);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("correlation")]
    [InlineData("timestamp")]
    public async Task ReplayRejectsMalformedProvenance(string field)
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId($"provenance-{field}");
        await OpenAsync(CreateService(root.Path), taskId, new ActorId("operator"));
        var eventsPath = Path.Combine(root.Path, taskId.Value, "events.jsonl");
        var lines = await File.ReadAllLinesAsync(eventsPath);
        var first = JsonSerializer.Deserialize<LedgerEvent>(lines[0], LedgerJson.CreateOptions())!;
        first = field switch
        {
            "actor" => first with { ActorId = new ActorId(string.Empty) },
            "correlation" => first with { CorrelationId = string.Empty },
            "timestamp" => first with { RecordedAt = default },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        lines[0] = JsonSerializer.Serialize(first, LedgerJson.CreateOptions());
        await File.WriteAllLinesAsync(eventsPath, lines);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            CreateService(root.Path).GetStateAsync(taskId, CancellationToken.None));

        Assert.Contains("invalid provenance", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplayRejectsNumericUndefinedEnumValues()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("numeric-enum-task");
        await OpenAsync(CreateService(root.Path), taskId, new ActorId("operator"));
        var eventsPath = Path.Combine(root.Path, taskId.Value, "events.jsonl");
        var lines = await File.ReadAllLinesAsync(eventsPath);
        lines[1] = lines[1].Replace("\"role\":\"operator\"", "\"role\":999", StringComparison.Ordinal);
        await File.WriteAllLinesAsync(eventsPath, lines);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            CreateService(root.Path).GetStateAsync(taskId, CancellationToken.None));

        Assert.Contains("Invalid event JSON", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplayRejectsForgedOpeningRolePayload()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("forged-opening-role-task");
        await OpenAsync(CreateService(root.Path), taskId, new ActorId("operator"));
        var eventsPath = Path.Combine(root.Path, taskId.Value, "events.jsonl");
        var lines = await File.ReadAllLinesAsync(eventsPath);
        var openingRoleEvent = JsonSerializer.Deserialize<LedgerEvent>(lines[1], LedgerJson.CreateOptions())!;
        var openingRole = Assert.IsType<RoleAssigned>(openingRoleEvent.Data);
        openingRoleEvent = openingRoleEvent with
        {
            Data = new RoleAssigned(openingRole.Assignment with { ActorId = new ActorId("attacker") })
        };
        lines[1] = JsonSerializer.Serialize(openingRoleEvent, LedgerJson.CreateOptions());
        await File.WriteAllLinesAsync(eventsPath, lines);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            CreateService(root.Path).GetStateAsync(taskId, CancellationToken.None));

        Assert.Contains("opening role", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReplayRejectsAuthorityChangingEventFromUnauthorizedActor()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("forged-authority-task");
        var operatorId = new ActorId("operator");
        var workerId = new ActorId("worker");
        var service = CreateService(root.Path);
        await OpenAsync(service, taskId, operatorId);
        await service.ExecuteAsync(
            taskId,
            new AssignRoleCommand(operatorId, null, "assign-worker", workerId, RoleKind.Worker, [Capability.AddClaim]),
            CancellationToken.None);
        var eventsPath = Path.Combine(root.Path, taskId.Value, "events.jsonl");
        var history = await File.ReadAllLinesAsync(eventsPath);
        var previous = JsonSerializer.Deserialize<LedgerEvent>(history[^1], LedgerJson.CreateOptions())!;
        var recordedAt = previous.RecordedAt.AddSeconds(1);
        var forged = new LedgerEvent(
            GovernedTaskState.CurrentSchemaVersion,
            new EventId($"{taskId.Value}:0000000004"),
            taskId,
            workerId,
            recordedAt,
            previous.EventId,
            "forged-role",
            new RoleAssigned(new RoleAssignment(
                new ActorId("accomplice"),
                RoleKind.Worker,
                [],
                new Provenance(workerId, recordedAt, "actor.assign-role"))));
        await File.AppendAllTextAsync(
            eventsPath,
            JsonSerializer.Serialize(forged, LedgerJson.CreateOptions()) + "\n");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            CreateService(root.Path).GetStateAsync(taskId, CancellationToken.None));

        Assert.Contains("Only an operator", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BoundedLedgerRejectsMutationBeforeExceedingEventLimit()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("bounded-task");
        var actor = new ActorId("operator");
        var service = new FileGovernedTaskService(
            root.Path, new CommandHandler(), new TaskReducer(), maximumEventsPerTask: 2);
        await OpenAsync(service, taskId, actor);

        var exception = await Assert.ThrowsAsync<GovernanceException>(() => service.ExecuteAsync(
            taskId,
            new AddClaimCommand(actor, null, "claim", new ClaimId("C1"), "Claim", null),
            CancellationToken.None));

        Assert.Contains("limit of 2 events", exception.Message, StringComparison.Ordinal);
        Assert.Equal(2, (await service.GetStateAsync(taskId, CancellationToken.None))?.Version);
    }

    [Fact]
    public async Task BoundedLedgerRejectsMutationBeforeExceedingByteLimit()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("bounded-bytes-task");
        var service = new FileGovernedTaskService(
            root.Path, new CommandHandler(), new TaskReducer(), maximumEventLogBytes: 1);

        var exception = await Assert.ThrowsAsync<GovernanceException>(() => OpenAsync(
            service, taskId, new ActorId("operator")));

        Assert.Contains("limit of 1 bytes", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root.Path, taskId.Value, "events.jsonl")));
    }

    private static FileGovernedTaskService CreateService(string root) =>
        new(root, new CommandHandler(), new TaskReducer());

    private static async Task OpenAsync(IGovernedTaskService service, TaskId taskId, ActorId actor) =>
        await service.ExecuteAsync(
            taskId,
            new OpenTaskCommand(actor, null, "open", taskId, "Task", "Goal"),
            CancellationToken.None);

    private sealed class FailOnceProjectionWriter(Exception? failure = null) : ITaskProjectionWriter
    {
        private readonly Exception _failure = failure ?? new IOException("Injected projection failure.");
        private bool _failed;
        public int SuccessfulWrites { get; private set; }

        public Task WriteAsync(string taskDirectory, GovernedTaskState state, CancellationToken cancellationToken)
        {
            if (!_failed)
            {
                _failed = true;
                throw _failure;
            }

            SuccessfulWrites++;
            return Task.CompletedTask;
        }
    }

    private sealed class AlwaysFailProjectionWriter : ITaskProjectionWriter
    {
        public Task WriteAsync(string taskDirectory, GovernedTaskState state, CancellationToken cancellationToken) =>
            throw new IOException("Injected persistent projection failure.");
    }
}
