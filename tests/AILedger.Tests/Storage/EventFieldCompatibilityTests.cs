using System.Text.Json;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;

namespace AILedger.Tests.Storage;

public sealed class EventFieldCompatibilityTests
{
    // R3 (legacy-identity-or-projection-loss): the envelope extension is optional for old JSON,
    // round-trips when present, and is stamped by the real command construction path.
    [Fact]
    public void R3_LegacyAndStampedKernelIdentityBothProject()
    {
        var options = LedgerJson.CreateOptions();
        const string legacyJson = """
            {"schemaVersion":1,"eventId":"legacy:0000000001","taskId":"legacy","actorId":"operator","recordedAt":"2026-09-14T12:00:00+00:00","correlationId":"legacy","data":{"eventType":"task.opened","title":"Legacy","goal":"Goal","tags":[]}}
            """;

        var legacy = JsonSerializer.Deserialize<LedgerEvent>(legacyJson, options);
        Assert.NotNull(legacy);
        Assert.Null(legacy.KernelIdentity);

        var identity = new KernelBuildIdentity(
            "2.0.123+abc1234", "abc1234", new DateTimeOffset(2026, 9, 14, 11, 30, 0, TimeSpan.Zero));
        var handler = new CommandHandler(new TaskReducer(), new AuthorizationPolicy(), identity);
        var outcome = handler.Handle(null, new OpenTaskCommand(
            new ActorId("operator"), null, "open", new TaskId("stamped"), "Stamped", "Goal"),
            new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero));

        Assert.All(outcome.Events, @event => Assert.Equal(identity, @event.KernelIdentity));
        var roundTripped = RoundTrip(outcome.Events[0], options);
        Assert.Equal(identity, roundTripped.KernelIdentity);
    }

    [Fact]
    public void LegacyEventsWithoutReasonSubjectRoleOrSerialJustificationReplay()
    {
        var taskId = new TaskId("legacy-nullable-event-fields");
        var actor = new ActorId("operator");
        var recordedAt = new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
        var reducer = new TaskReducer();
        GovernedTaskState? state = null;

        LedgerEvent Next(LedgerEventData data) => new(
            GovernedTaskState.CurrentSchemaVersion,
            new EventId($"{taskId.Value}:{(state?.Version ?? 0) + 1:D10}"),
            taskId,
            actor,
            recordedAt,
            null,
            "legacy-replay",
            data);

        void ReplaySerialized(LedgerEventData data, params string[] absentProperties)
        {
            var json = JsonSerializer.Serialize(Next(data), LedgerJson.CreateOptions());
            foreach (var property in absentProperties)
            {
                Assert.DoesNotContain($"\"{property}\"", json, StringComparison.Ordinal);
            }

            var deserialized = JsonSerializer.Deserialize<LedgerEvent>(json, LedgerJson.CreateOptions());
            Assert.NotNull(deserialized);
            state = reducer.Apply(state, deserialized);
        }

        ReplaySerialized(new TaskOpened("Legacy task", "Goal"));
        ReplaySerialized(new RoleAssigned(new RoleAssignment(
            actor,
            RoleKind.Operator,
            Enum.GetValues<Capability>(),
            new Provenance(actor, recordedAt, "task.open"))));
        ReplaySerialized(
            new StageTransitioned(TaskStage.Discovery, TaskStage.Research),
            "reason",
            "serialJustification");
        ReplaySerialized(
            new RunStarted(new AgentRun(
                new RunId("R1"),
                actor,
                null,
                "codex",
                null,
                AgentRunStatus.Active,
                recordedAt,
                null)),
            "subjectRole");

        Assert.NotNull(state);
        Assert.Equal(TaskStage.Research, state.Stage);
        Assert.Null(state.Runs[new RunId("R1")].SubjectRole);
    }

    [Fact]
    public void PresentReasonSubjectRoleAndSerialJustificationRoundTrip()
    {
        var taskId = new TaskId("new-nullable-event-fields");
        var actor = new ActorId("operator");
        var recordedAt = new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
        var options = LedgerJson.CreateOptions();

        var transition = RoundTrip(new LedgerEvent(
            GovernedTaskState.CurrentSchemaVersion,
            new EventId("new-nullable-event-fields:0000000001"),
            taskId,
            actor,
            recordedAt,
            null,
            "new-fields",
            new StageTransitioned(
                TaskStage.Execution,
                TaskStage.Verification,
                "Verification follows execution",
                new AlternativeId("ALT-serial"))), options);
        var transitioned = Assert.IsType<StageTransitioned>(transition.Data);
        Assert.Equal("Verification follows execution", transitioned.Reason);
        Assert.Equal(new AlternativeId("ALT-serial"), transitioned.SerialJustification);

        var startedEvent = RoundTrip(new LedgerEvent(
            GovernedTaskState.CurrentSchemaVersion,
            new EventId("new-nullable-event-fields:0000000002"),
            taskId,
            actor,
            recordedAt,
            null,
            "new-fields",
            new RunStarted(new AgentRun(
                new RunId("RW1"),
                actor,
                new WorkItemId("W1"),
                "codex",
                null,
                AgentRunStatus.Active,
                recordedAt,
                null,
                SubjectRole: RoleKind.Worker))), options);
        var started = Assert.IsType<RunStarted>(startedEvent.Data);
        Assert.Equal(RoleKind.Worker, started.Run.SubjectRole);
    }

    private static LedgerEvent RoundTrip(LedgerEvent @event, JsonSerializerOptions options)
    {
        var json = JsonSerializer.Serialize(@event, options);
        return JsonSerializer.Deserialize<LedgerEvent>(json, options)!;
    }
}
