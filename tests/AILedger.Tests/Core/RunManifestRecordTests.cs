using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Tests.Core;

// LD1: a run records which manifest briefed it, and it records it at completion rather than at
// start, because the launcher builds the manifest after run.start (LC1) and hoisting that build
// would change what every launched agent reads (LC2).
//
// The pair is all-or-nothing on purpose. A hash with no count says a brief was handed over but not
// how large it was; a count with no hash cannot be matched to any manifest. Both fields absent is
// the third, meaningful state: no brief reached this run at all.
public sealed class RunManifestRecordTests
{
    private const string ValidHash = "9f2c4e1b8a7d6c5f4e3d2c1b0a9f8e7d6c5b4a39281706f5e4d3c2b1a0918273";

    [Fact]
    public void CompletionRecordsTheManifestThatBriefedTheRun()
    {
        var state = WithActiveRun(out var handler, out var actor, out var now);

        var outcome = handler.Handle(state, Complete(actor, ValidHash, 12), now);

        var run = outcome.State.Runs[new RunId("R1")];
        Assert.Equal(ValidHash, run.ManifestHash);
        Assert.Equal(12, run.ManifestArtifactCount);
    }

    // The absence is the measurement: a launch that failed before its manifest was built must stay
    // distinguishable from one briefed with an empty manifest.
    [Fact]
    public void ARunCompletedWithNoManifestRecordsNeitherField()
    {
        var state = WithActiveRun(out var handler, out var actor, out var now);

        var outcome = handler.Handle(state, Complete(actor, null, null), now);

        var run = outcome.State.Runs[new RunId("R1")];
        Assert.Null(run.ManifestHash);
        Assert.Null(run.ManifestArtifactCount);
    }

    [Fact]
    public void AnEmptyManifestIsRecordedAsZeroAndNotAsAbsence()
    {
        var state = WithActiveRun(out var handler, out var actor, out var now);

        var outcome = handler.Handle(state, Complete(actor, ValidHash, 0), now);

        Assert.Equal(0, outcome.State.Runs[new RunId("R1")].ManifestArtifactCount);
    }

    [Theory]
    [InlineData(ValidHash, null)]
    [InlineData(null, 12)]
    public void HalfAManifestRecordIsRefused(string? hash, int? count)
    {
        var state = WithActiveRun(out var handler, out var actor, out var now);

        var exception = Assert.Throws<GovernanceException>(() => handler.Handle(state, Complete(actor, hash, count), now));
        Assert.Contains("must be recorded together", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("tooshort")]
    [InlineData("9F2C4E1B8A7D6C5F4E3D2C1B0A9F8E7D6C5B4A39281706F5E4D3C2B1A0918273")]
    [InlineData("zzzc4e1b8a7d6c5f4e3d2c1b0a9f8e7d6c5b4a39281706f5e4d3c2b1a0918273")]
    public void AHashThatIsNotLowercaseHexOfTheRightLengthIsRefused(string hash)
    {
        var state = WithActiveRun(out var handler, out var actor, out var now);

        var exception = Assert.Throws<GovernanceException>(() => handler.Handle(state, Complete(actor, hash, 3), now));
        Assert.Contains("lowercase hexadecimal", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANegativeArtifactCountIsRefused()
    {
        var state = WithActiveRun(out var handler, out var actor, out var now);

        var exception = Assert.Throws<GovernanceException>(() => handler.Handle(state, Complete(actor, ValidHash, -1), now));
        Assert.Contains("cannot be negative", exception.Message, StringComparison.Ordinal);
    }

    // The replay boundary. Every run.completed already on disk carries neither field, so the twin
    // in TaskTransitionValidator must take its early return rather than demand a manifest that no
    // history can supply.
    [Fact]
    public void ReplayAcceptsARunCompletedBeforeTheManifestFieldsExisted()
    {
        var state = Replayed(out var reducer, out var next, out var actor, out var recordedAt);

        state = reducer.Apply(state, next(state, actor, new RunCompleted(
            new RunId("R1"), AgentRunStatus.Completed, "session-1", recordedAt)));

        Assert.Equal(AgentRunStatus.Completed, state.Runs[new RunId("R1")].Status);
        Assert.Null(state.Runs[new RunId("R1")].ManifestHash);
    }

    // Safe to validate at replay by construction rather than by exemption: the rule only fires on
    // events that carry the fields, and no event on disk does. So a forged log is still refused.
    [Fact]
    public void ReplayValidatesTheManifestRecordWhenTheEventCarriesOne()
    {
        var state = Replayed(out var reducer, out var next, out var actor, out var recordedAt);

        var forged = next(state, actor, new RunCompleted(
            new RunId("R1"), AgentRunStatus.Completed, "session-1", recordedAt, false, "not-a-hash", 4));

        Assert.Throws<GovernanceException>(() => reducer.Apply(state, forged));
    }

    private static CompleteRunCommand Complete(ActorId actor, string? hash, int? count) =>
        new(actor, null, "c-complete", new RunId("R1"), AgentRunStatus.Completed, "session-1", null, hash, count);

    private static GovernedTaskState WithActiveRun(
        out CommandHandler handler, out ActorId actor, out DateTimeOffset now)
    {
        var state = Replayed(out _, out var next, out var localActor, out var recordedAt);
        handler = new CommandHandler();
        actor = localActor;
        now = recordedAt;
        _ = next;
        return state;
    }

    private static GovernedTaskState Replayed(
        out TaskReducer reducer,
        out Func<GovernedTaskState, ActorId, LedgerEventData, LedgerEvent> next,
        out ActorId actor,
        out DateTimeOffset recordedAt)
    {
        var id = new TaskId("run-manifest");
        var localActor = new ActorId("operator");
        var localRecordedAt = new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
        var localReducer = new TaskReducer();
        actor = localActor;
        recordedAt = localRecordedAt;
        reducer = localReducer;
        next = (current, eventActor, data) => new LedgerEvent(
            GovernedTaskState.CurrentSchemaVersion,
            new EventId($"{id.Value}:{(current?.Version ?? 0) + 1:D10}"),
            id, eventActor, localRecordedAt, null, "replay", data);

        var state = localReducer.Apply(null, next(null!, localActor, new TaskOpened("Manifest", "Goal")));
        state = localReducer.Apply(state, next(state, localActor, new RoleAssigned(new RoleAssignment(
            localActor, RoleKind.Operator, Enum.GetValues<Capability>(),
            new Provenance(localActor, localRecordedAt, "task.open")))));
        return localReducer.Apply(state, next(state, localActor, new RunStarted(new AgentRun(
            new RunId("R1"), localActor, null, "claude", "session-1", AgentRunStatus.Active,
            localRecordedAt, null, null, null, null, null, RoleKind.Operator))));
    }
}
