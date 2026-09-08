using System.Text.Json;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

// D3 and D4: a completed run records what it cost — turns, output tokens, three input buckets, and
// how long it took to reach the ledger for the first time — and it records them at completion,
// because none of them exists until the run has ended. D2: the model the provider actually served
// wins over the one asked for.
//
// The six cost fields are independent, unlike the manifest pair. A run can report turns and tokens
// and still never touch the ledger, a provider that states no turn count reports none, and each
// absence says something on its own.
public sealed class RunCostRecordTests
{
    // The property names the six fields serialise under. Used to check the log and the projection
    // by text, because the guarantee this record has to keep is a byte-level one.
    private static readonly string[] CostPropertyNames =
    [
        "\"turns\":", "\"outputTokens\":", "\"millisecondsToFirstLedgerWrite\":",
        "\"tokensInUncached\":", "\"tokensInCacheWrite\":", "\"tokensInCacheRead\":"
    ];

    [Fact]
    public void CompletionRecordsWhatTheRunCost()
    {
        var state = Replayed(out _, out _, out var actor, out var now);

        var outcome = new CommandHandler().Handle(state, Complete(actor, 125, 33110, 4213), now);

        var run = outcome.State.Runs[new RunId("R1")];
        Assert.Equal(125, run.Turns);
        Assert.Equal(33110, run.OutputTokens);
        Assert.Equal(4213, run.MillisecondsToFirstLedgerWrite);
    }

    // D3: the three input buckets land as three separate numbers and are never added into one. C7
    // is why — they are billed at roughly 1x, 1.25x and 0.1x, so a sum is not proportional to cost.
    [Fact]
    public void CompletionRecordsTheThreeInputBucketsSeparately()
    {
        var state = Replayed(out _, out _, out var actor, out var now);

        var outcome = new CommandHandler().Handle(
            state,
            Complete(actor, 125, 33110, 4213) with
            {
                TokensInUncached = 2, TokensInCacheWrite = 41368, TokensInCacheRead = 16285
            },
            now);

        var run = outcome.State.Runs[new RunId("R1")];
        Assert.Equal(2, run.TokensInUncached);
        Assert.Equal(41368, run.TokensInCacheWrite);
        Assert.Equal(16285, run.TokensInCacheRead);
    }

    // The absence is the measurement, and it is only meaningful because the tests above show the
    // write path is live: a run completed before these fields existed, or one whose provider stream
    // died before its terminal event, must stay distinguishable from a run that measured zero.
    [Fact]
    public void ARunCompletedWithNoCostRecordsNone()
    {
        var state = Replayed(out _, out _, out var actor, out var now);

        var outcome = new CommandHandler().Handle(state, Complete(actor, null, null, null), now);

        var run = outcome.State.Runs[new RunId("R1")];
        Assert.Null(run.Turns);
        Assert.Null(run.OutputTokens);
        Assert.Null(run.MillisecondsToFirstLedgerWrite);
        Assert.Null(run.TokensInUncached);
        Assert.Null(run.TokensInCacheWrite);
        Assert.Null(run.TokensInCacheRead);
    }

    // The distinction the field exists for. A run that never reached the ledger is the observability
    // hole; a run that reached it inside the first millisecond is the opposite. Null and zero must
    // not collapse into each other.
    [Fact]
    public void NeverReachingTheLedgerIsNullAndReachingItImmediatelyIsZero()
    {
        var state = Replayed(out _, out _, out var actor, out var now);
        var handler = new CommandHandler();

        var neverWrote = handler.Handle(state, Complete(actor, 3, 100, null), now)
            .State.Runs[new RunId("R1")].MillisecondsToFirstLedgerWrite;
        var wroteAtOnce = handler.Handle(state, Complete(actor, 3, 100, 0), now)
            .State.Runs[new RunId("R1")].MillisecondsToFirstLedgerWrite;

        Assert.Null(neverWrote);
        Assert.Equal(0, wroteAtOnce);
    }

    // Turns is absent for a provider that states no turn count of its own — codex, per D4 and IC1 —
    // so it must stay independently omittable rather than being forced into an all-or-nothing group
    // with the numbers that same run does report.
    [Fact]
    public void ACodexShapedCompletionRecordsEveryCostExceptTheTurnCount()
    {
        var state = Replayed(out _, out _, out var actor, out var now);

        var outcome = new CommandHandler().Handle(
            state,
            Complete(actor, null, 28399, 900) with
            {
                TokensInUncached = 156775, TokensInCacheWrite = 151552, TokensInCacheRead = 8639744
            },
            now);

        var run = outcome.State.Runs[new RunId("R1")];
        Assert.Null(run.Turns);
        Assert.Equal(28399, run.OutputTokens);
        Assert.Equal(900, run.MillisecondsToFirstLedgerWrite);
        Assert.Equal(156775, run.TokensInUncached);
        Assert.Equal(151552, run.TokensInCacheWrite);
        Assert.Equal(8639744, run.TokensInCacheRead);
    }

    // RC2, and the test that pins the persisted representation: the token counters are 64-bit
    // through every layer. A count above int.MaxValue passes the command rule, reaches the log at
    // full width, survives a fresh replay of that log through the validator and the reducer, and
    // reads back as the number the provider stated rather than as nothing.
    //
    // Turns is deliberately not widened — K11 keeps it 32-bit, because a turn count is a provider's
    // own iteration count and nothing aggregates it — so the completion below carries a real one.
    [Fact]
    public async Task ATokenCountAboveTheThirtyTwoBitLimitSurvivesTheLogAndAFreshReplay()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("run-cost-wide-tokens");
        var actor = new ActorId("operator");
        await StageCompletedRunAsync(root.Path, taskId, actor, cost: WideCost);

        Assert.Contains(
            "3000000000", PersistedRunCompletedLine(root.Path, taskId), StringComparison.Ordinal);

        File.Delete(StatePath(root.Path, taskId));
        var replayed = await Service(root.Path).GetStateAsync(taskId, CancellationToken.None);

        var run = replayed!.Runs[new RunId("R1")];
        Assert.Equal(125, run.Turns);
        Assert.Equal(3000000000L, run.OutputTokens);
        Assert.Equal(4294967296L, run.TokensInUncached);
        Assert.Equal(2147483649L, run.TokensInCacheWrite);
        Assert.Equal(2147483648L, run.TokensInCacheRead);
    }

    [Theory]
    [InlineData(-1, null, null, "turn count")]
    [InlineData(null, -1L, null, "output token count")]
    [InlineData(null, null, -1L, "time to first ledger write")]
    public void ANegativeCostIsRefused(int? turns, long? outputTokens, long? milliseconds, string expected)
    {
        var state = Replayed(out _, out _, out var actor, out var now);

        var exception = Assert.Throws<GovernanceException>(() =>
            new CommandHandler().Handle(state, Complete(actor, turns, outputTokens, milliseconds), now));

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    // The uncached bucket is the one a mapping can drive negative, because for codex it is a
    // subtraction (C6). RunCostReader records nothing rather than a negative, so a negative reaching
    // the command is a caller that did its own arithmetic — and the rule refuses it by name.
    [Theory]
    [InlineData(-1L, null, null, "uncached input token count")]
    [InlineData(null, -1L, null, "cache write input token count")]
    [InlineData(null, null, -1L, "cache read input token count")]
    public void ANegativeInputBucketIsRefused(long? uncached, long? cacheWrite, long? cacheRead, string expected)
    {
        var state = Replayed(out _, out _, out var actor, out var now);

        var exception = Assert.Throws<GovernanceException>(() =>
            new CommandHandler().Handle(
                state,
                Complete(actor, 1, 1, 1) with
                {
                    TokensInUncached = uncached, TokensInCacheWrite = cacheWrite, TokensInCacheRead = cacheRead
                },
                now));

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    // C15, and the only test that states the invariant the three symptoms shared: RunCostReader is
    // read where the launcher closes a run, so every value it returns has to satisfy the rule the
    // two theories above enforce. Where it does not, the completion throws over optional telemetry
    // and a finished run stays active, which blocks the next run on its work item and blocks
    // Archive.
    //
    // Each row is one degradation path the reader has, driven from a provider stream through the
    // real completion command rather than asserted as a null at the reader: text that is not JSON,
    // a root that is valid JSON but not an object, numbers outside the range their fields hold, an
    // inverted subtraction, and directly stated negatives on both provider paths. RC1, IC6 and RC3
    // were three of these found one at a time; what this asserts is the property they all broke.
    [Theory]
    [InlineData("result", "{not json")]
    [InlineData("result", "[]")]
    [InlineData("turn.completed", "\"turn.completed\"")]
    [InlineData("result", """{"type":"result","num_turns":2147483648,"usage":{"output_tokens":184467440737095516160}}""")]
    [InlineData("turn.completed", """{"type":"turn.completed","usage":{"input_tokens":100,"cached_input_tokens":500}}""")]
    [InlineData(
        "result",
        """
        {"type":"result","num_turns":-1,"usage":{"input_tokens":-2,
         "cache_creation_input_tokens":-3,"cache_read_input_tokens":-4,"output_tokens":-5}}
        """)]
    [InlineData(
        "turn.completed",
        """
        {"type":"turn.completed","usage":{"input_tokens":8796519,"cached_input_tokens":-1,
         "cache_write_input_tokens":-2,"output_tokens":-3}}
        """)]
    public void EveryDegradedProviderStreamStillClosesTheRun(string type, string rawJson)
    {
        var state = Replayed(out _, out _, out var actor, out var now);
        var cost = RunCostReader.Read([new ProviderEvent(1, type, rawJson, "session-1", true, false)]);

        var outcome = new CommandHandler().Handle(state, Complete(actor, cost), now);

        Assert.Equal(AgentRunStatus.Completed, outcome.State.Runs[new RunId("R1")].Status);
    }

    // What stops the theory above passing on a reader that returns five nulls for everything: the
    // same two lines against R13's real claude result event from E4 close the run and record every
    // number it states.
    [Fact]
    public void AWellFormedProviderStreamClosesTheRunWithTheCostItStated()
    {
        var state = Replayed(out _, out _, out var actor, out var now);
        var cost = RunCostReader.Read([
            new ProviderEvent(1, "result", RunCostReaderTests.ClaudeResult, "session-1", true, false)
        ]);

        var outcome = new CommandHandler().Handle(state, Complete(actor, cost), now);

        var run = outcome.State.Runs[new RunId("R1")];
        Assert.Equal(AgentRunStatus.Completed, run.Status);
        Assert.Equal(125, run.Turns);
        Assert.Equal(33110, run.OutputTokens);
        Assert.Equal(2, run.TokensInUncached);
        Assert.Equal(41368, run.TokensInCacheWrite);
        Assert.Equal(16285, run.TokensInCacheRead);
    }

    // D2: the record should say which cognition ran, not which was asked for.
    [Fact]
    public void TheModelTheProviderServedWinsOverTheOneRequested()
    {
        var state = Replayed(out _, out _, out var actor, out var now);

        var outcome = new CommandHandler().Handle(
            state, Complete(actor, 1, 1, 1) with { Model = "claude-opus-5-served" }, now);

        Assert.Equal("claude-opus-5-served", outcome.State.Runs[new RunId("R1")].Model);
    }

    // A provider that reports no model must not erase the one the launcher resolved and recorded at
    // run.started, which per D4 is the value AgentRun.Model carries until the launcher item
    // establishes whether a served-model field exists at all.
    [Fact]
    public void ACompletionThatReportsNoModelLeavesTheRequestedOneStanding()
    {
        var state = Replayed(out _, out _, out var actor, out var now);

        var outcome = new CommandHandler().Handle(state, Complete(actor, 1, 1, 1), now);

        Assert.Equal("claude-opus-5-requested", outcome.State.Runs[new RunId("R1")].Model);
    }

    // The replay boundary, and the first rule this task writes into the validator. Every
    // run.completed already on disk carries none of the six, so the twin must take its early
    // return rather than demand a measurement no history can supply.
    [Fact]
    public void ReplayAcceptsARunCompletedBeforeTheCostFieldsExisted()
    {
        var state = Replayed(out var reducer, out var next, out var actor, out var recordedAt);

        state = reducer.Apply(state, next(state, actor, new RunCompleted(
            new RunId("R1"), AgentRunStatus.Completed, "session-1", recordedAt)));

        var run = state.Runs[new RunId("R1")];
        Assert.Equal(AgentRunStatus.Completed, run.Status);
        Assert.Null(run.Turns);
        Assert.Null(run.OutputTokens);
        Assert.Null(run.MillisecondsToFirstLedgerWrite);
        Assert.Null(run.TokensInUncached);
        Assert.Null(run.TokensInCacheWrite);
        Assert.Null(run.TokensInCacheRead);
        Assert.Equal("claude-opus-5-requested", run.Model);
    }

    // Safe by construction rather than by exemption, on the terms the manifest pair set: the rule
    // fires only on an event that carries the field, and no event on disk does. A forged log is
    // still refused, and by the same message the command path uses.
    [Fact]
    public void ReplayValidatesATurnCountTheEventCarries()
    {
        var state = Replayed(out var reducer, out var next, out var actor, out var recordedAt);

        var forged = next(state, actor, new RunCompleted(
            new RunId("R1"), AgentRunStatus.Completed, "session-1", recordedAt, Turns: -4));

        var exception = Assert.Throws<GovernanceException>(() => reducer.Apply(state, forged));
        Assert.Contains("turn count", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplayValidatesAnInputBucketTheEventCarries()
    {
        var state = Replayed(out var reducer, out var next, out var actor, out var recordedAt);

        var forged = next(state, actor, new RunCompleted(
            new RunId("R1"), AgentRunStatus.Completed, "session-1", recordedAt, TokensInUncached: -1));

        var exception = Assert.Throws<GovernanceException>(() => reducer.Apply(state, forged));
        Assert.Contains("uncached input token count", exception.Message, StringComparison.Ordinal);
    }

    // Replay records the same numbers the command path does, so a state rebuilt from the log is the
    // state the handler produced.
    [Fact]
    public void ReplayRecordsACostTheEventCarries()
    {
        var state = Replayed(out var reducer, out var next, out var actor, out var recordedAt);

        state = reducer.Apply(state, next(state, actor, new RunCompleted(
            new RunId("R1"), AgentRunStatus.Completed, "session-1", recordedAt, false, null, null,
            96, 28399, 512, "codex-served", 156775, 151552, 8639744)));

        var run = state.Runs[new RunId("R1")];
        Assert.Equal(96, run.Turns);
        Assert.Equal(28399, run.OutputTokens);
        Assert.Equal(512, run.MillisecondsToFirstLedgerWrite);
        Assert.Equal("codex-served", run.Model);
        Assert.Equal(156775, run.TokensInUncached);
        Assert.Equal(151552, run.TokensInCacheWrite);
        Assert.Equal(8639744, run.TokensInCacheRead);
    }

    // K6 item five, as written. A task whose run.completed predates the cost fields is materialised,
    // its state.json is captured, the file is deleted, the events alone are replayed by a fresh
    // service, and the two files are compared byte for byte.
    //
    // The comparison is only worth making because of the two assertions around it. The first shows
    // the log genuinely holds the pre-cost shape rather than today's with nulls in it. The last
    // shows the projection of that history carries none of the six property names — which is the
    // thing that could have broken when the record grew, and it is checked as text, not as a
    // nullable property read back through the record that changed.
    [Fact]
    public async Task ATaskWhoseRunPredatesTheCostFieldsReplaysToAByteIdenticalStateFile()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("run-cost-legacy-replay");
        var actor = new ActorId("operator");
        var service = await StageCompletedRunAsync(root.Path, taskId, actor, cost: null);

        var loggedCompletion = PersistedRunCompletedLine(root.Path, taskId);
        foreach (var property in CostPropertyNames)
        {
            Assert.DoesNotContain(property, loggedCompletion, StringComparison.Ordinal);
        }

        var before = await service.GetStateAsync(taskId, CancellationToken.None);
        var expected = await File.ReadAllBytesAsync(StatePath(root.Path, taskId));

        File.Delete(StatePath(root.Path, taskId));
        var replayed = await Service(root.Path).GetStateAsync(taskId, CancellationToken.None);

        Assert.Equal(before!.Version, replayed!.Version);
        Assert.Equal(expected, await File.ReadAllBytesAsync(StatePath(root.Path, taskId)));

        var materialised = await File.ReadAllTextAsync(StatePath(root.Path, taskId));
        foreach (var property in CostPropertyNames)
        {
            Assert.DoesNotContain(property, materialised, StringComparison.Ordinal);
        }
    }

    // The other half of the test above, and what stops it passing against a dead write path: the
    // same staging with a cost recorded puts all six property names in both the log and the
    // projection, and the projection still survives a fresh replay byte for byte.
    [Fact]
    public async Task ATaskWhoseRunRecordedACostWritesAllSixPropertiesAndStillReplaysByteIdentically()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("run-cost-measured-replay");
        var actor = new ActorId("operator");
        var service = await StageCompletedRunAsync(root.Path, taskId, actor, cost: MeasuredCost);

        var loggedCompletion = PersistedRunCompletedLine(root.Path, taskId);
        var before = await service.GetStateAsync(taskId, CancellationToken.None);
        var expected = await File.ReadAllBytesAsync(StatePath(root.Path, taskId));
        var materialised = await File.ReadAllTextAsync(StatePath(root.Path, taskId));
        foreach (var property in CostPropertyNames)
        {
            Assert.Contains(property, loggedCompletion, StringComparison.Ordinal);
            Assert.Contains(property, materialised, StringComparison.Ordinal);
        }

        File.Delete(StatePath(root.Path, taskId));
        var replayed = await Service(root.Path).GetStateAsync(taskId, CancellationToken.None);

        Assert.Equal(before!.Version, replayed!.Version);
        Assert.Equal(expected, await File.ReadAllBytesAsync(StatePath(root.Path, taskId)));
        var run = replayed.Runs[new RunId("R1")];
        Assert.Equal(125, run.Turns);
        Assert.Equal(156775, run.TokensInUncached);
    }

    // The root of byte-identical replay. The ledger writer omits nulls, so a run that measured
    // nothing serialises exactly as it did before these fields existed — the projection of an old
    // history cannot change just because the record grew.
    [Fact]
    public void ARunThatMeasuredNothingSerialisesWithNoCostProperties()
    {
        var run = new AgentRun(
            new RunId("R1"), new ActorId("operator"), null, "claude", "session-1",
            AgentRunStatus.Completed, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

        var json = JsonSerializer.Serialize(run, LedgerJson.CreateOptions());

        foreach (var property in CostPropertyNames)
        {
            Assert.DoesNotContain(property, json, StringComparison.Ordinal);
        }
    }

    // The same guarantee for the event itself: a run.completed written today with no measurement is
    // the same bytes as one written before the fields existed.
    [Fact]
    public void ARunCompletedCarryingNoCostSerialisesWithNoCostProperties()
    {
        var completed = new RunCompleted(
            new RunId("R1"), AgentRunStatus.Completed, "session-1", DateTimeOffset.UnixEpoch);

        var json = JsonSerializer.Serialize<LedgerEventData>(completed, LedgerJson.CreateOptions());

        foreach (var property in CostPropertyNames)
        {
            Assert.DoesNotContain(property, json, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("\"model\":", json, StringComparison.Ordinal);
    }

    // R13's claude numbers and R8's codex buckets from E4 and E5, in one completion. The provider
    // mix is not the point here — the point is that all six properties are present.
    private static readonly Func<ActorId, CompleteRunCommand> MeasuredCost = actor =>
        Complete(actor, 125, 33110, 4213) with
        {
            TokensInUncached = 156775, TokensInCacheWrite = 151552, TokensInCacheRead = 8639744
        };

    // Every token count above int.MaxValue, so a 32-bit field anywhere on the path records none of
    // them. The turn count is R13's real one, because Turns stays 32-bit by decision.
    private static readonly Func<ActorId, CompleteRunCommand> WideCost = actor =>
        Complete(actor, 125, 3000000000L, 4213) with
        {
            TokensInUncached = 4294967296L,
            TokensInCacheWrite = 2147483649L,
            TokensInCacheRead = 2147483648L
        };

    // The seam the launcher will use, spelled out once: everything RunCostReader read, and nothing
    // else, handed to the completion command. The time to first ledger write is not the reader's to
    // measure, so it is null here.
    private static CompleteRunCommand Complete(ActorId actor, RunCost cost) =>
        Complete(actor, cost.Turns, cost.OutputTokens, null) with
        {
            TokensInUncached = cost.TokensInUncached,
            TokensInCacheWrite = cost.TokensInCacheWrite,
            TokensInCacheRead = cost.TokensInCacheRead
        };

    private static CompleteRunCommand Complete(
        ActorId actor, int? turns, long? outputTokens, long? milliseconds) =>
        new(actor, null, "c-complete", new RunId("R1"), AgentRunStatus.Completed, "session-1",
            null, null, null, turns, outputTokens, milliseconds);

    private static async Task<FileGovernedTaskService> StageCompletedRunAsync(
        string root,
        TaskId taskId,
        ActorId actor,
        Func<ActorId, CompleteRunCommand>? cost)
    {
        var service = Service(root);
        await service.ExecuteAsync(
            taskId,
            new OpenTaskCommand(actor, null, "c1", taskId, "Run cost", "Goal"),
            CancellationToken.None);
        await service.ExecuteAsync(
            taskId,
            new StartRunCommand(
                actor, null, "c2", new RunId("R1"), null, "claude", "session-1", "claude-opus-5-requested"),
            CancellationToken.None);
        await service.ExecuteAsync(
            taskId,
            cost is null
                ? new CompleteRunCommand(
                    actor, null, "c3", new RunId("R1"), AgentRunStatus.Completed, "session-1")
                : cost(actor) with { CorrelationId = "c3" },
            CancellationToken.None);
        return service;
    }

    private static string PersistedRunCompletedLine(string root, TaskId taskId) =>
        File.ReadAllLines(Path.Combine(root, taskId.Value, "events.jsonl"))
            .Single(line => line.Contains("\"run.completed\"", StringComparison.Ordinal));

    private static string StatePath(string root, TaskId taskId) =>
        Path.Combine(root, taskId.Value, "state.json");

    private static FileGovernedTaskService Service(string root)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(root, new CommandHandler(reducer, new AuthorizationPolicy()), reducer);
    }

    private static GovernedTaskState Replayed(
        out TaskReducer reducer,
        out Func<GovernedTaskState, ActorId, LedgerEventData, LedgerEvent> next,
        out ActorId actor,
        out DateTimeOffset recordedAt)
    {
        var id = new TaskId("run-cost");
        var localActor = new ActorId("operator");
        var localRecordedAt = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        var localReducer = new TaskReducer();
        actor = localActor;
        recordedAt = localRecordedAt;
        reducer = localReducer;
        next = (current, eventActor, data) => new LedgerEvent(
            GovernedTaskState.CurrentSchemaVersion,
            new EventId($"{id.Value}:{(current?.Version ?? 0) + 1:D10}"),
            id, eventActor, localRecordedAt, null, "replay", data);

        var state = localReducer.Apply(null, next(null!, localActor, new TaskOpened("Run cost", "Goal")));
        state = localReducer.Apply(state, next(state, localActor, new RoleAssigned(new RoleAssignment(
            localActor, RoleKind.Operator, Enum.GetValues<Capability>(),
            new Provenance(localActor, localRecordedAt, "task.open")))));
        return localReducer.Apply(state, next(state, localActor, new RunStarted(new AgentRun(
            new RunId("R1"), localActor, null, "claude", "session-1", AgentRunStatus.Active,
            localRecordedAt, null, "claude-opus-5-requested", null, null, null, RoleKind.Operator))));
    }
}
