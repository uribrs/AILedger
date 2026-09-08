using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Tests.Core;

// A claim, decision or alternative may name the lesson that prompted it. The citation is checked
// against this task's own lessons, never against the store at <home>/lessons: a foreign store
// changes after the event is written, so a replay rule keyed on it would reject old history for a
// reason unrelated to what happened.
//
// Refusing a citation of a lesson the task never recalled is correct on its own terms — an agent
// cannot have been influenced by a lesson it was never shown — and it makes recall a precondition
// for citation, which is a real constraint rather than a discovered one.
public sealed class LessonCitationTests
{
    // A recalled lesson's id is derived from its source: "<task>:<kind>:<record>". The kernel
    // refuses a free-form one, which is what makes a citation traceable back to where it was earned.
    private static readonly LessonId RecalledId = new("earlier-task:imported:C9");

    [Fact]
    public void AClaimMayCiteALessonTheTaskRecalled()
    {
        var state = WithRecalledLesson(out var handler, out var actor, out var now);

        var outcome = handler.Handle(state, new AddClaimCommand(
            actor, null, "c1", new ClaimId("C1"), "The parser drops the session", null,
            RecalledId), now);

        Assert.Equal(RecalledId, outcome.State.Claims[new ClaimId("C1")].FromLesson);
    }

    [Fact]
    public void ADecisionMayCiteALessonTheTaskRecalled()
    {
        var state = WithRecalledLesson(out var handler, out var actor, out var now);

        var outcome = handler.Handle(state, new ProposeDecisionCommand(
            actor, null, "c1", new DecisionId("D1"), "Keep the session", "Because the lesson says so",
            [], null, RecalledId), now);

        Assert.Equal(RecalledId, outcome.State.Decisions[new DecisionId("D1")].FromLesson);
    }

    [Fact]
    public void AnAlternativeMayCiteALessonTheTaskRecalled()
    {
        var state = WithRecalledLesson(out var handler, out var actor, out var now);

        var outcome = handler.Handle(state, new RecordAlternativeCommand(
            actor, null, "c1", new AlternativeId("ALT1"), "Cache it", "The lesson refuted this",
            null, RecalledId), now);

        Assert.Equal(RecalledId, outcome.State.Alternatives[new AlternativeId("ALT1")].FromLesson);
    }

    // Optional on purpose. A task that cites nothing is not doing anything wrong; it may simply
    // have recalled nothing relevant.
    [Fact]
    public void CitingNoLessonIsAlwaysAllowed()
    {
        var state = WithRecalledLesson(out var handler, out var actor, out var now);

        var outcome = handler.Handle(state, new AddClaimCommand(
            actor, null, "c1", new ClaimId("C1"), "Unprompted claim", null, null), now);

        Assert.Null(outcome.State.Claims[new ClaimId("C1")].FromLesson);
    }

    [Fact]
    public void CitingALessonTheTaskNeverRecalledIsRefused()
    {
        var state = WithRecalledLesson(out var handler, out var actor, out var now);

        var exception = Assert.Throws<GovernanceException>(() => handler.Handle(state, new AddClaimCommand(
            actor, null, "c1", new ClaimId("C1"), "Borrowed authority", null,
            new LessonId("L-never-seen")), now));

        Assert.Contains("recalled lesson", exception.Message, StringComparison.Ordinal);
    }

    // The replay boundary. No claim, decision or alternative on disk carries FromLesson, so every
    // existing history passes the null branch untouched.
    [Fact]
    public void ReplayAcceptsAClaimRecordedBeforeCitationsExisted()
    {
        var state = Opened(out var reducer, out var next, out var actor, out var recordedAt);

        state = reducer.Apply(state, next(state, actor, new ClaimAdded(new Claim(
            new ClaimId("C-old"), "Legacy claim", ClaimStatus.Open, [], null,
            new Provenance(actor, recordedAt, "claim.add")))));

        Assert.Null(state.Claims[new ClaimId("C-old")].FromLesson);
    }

    [Fact]
    public void ReplayRefusesAForgedCitationOfALessonTheTaskNeverHeld()
    {
        var state = Opened(out var reducer, out var next, out var actor, out var recordedAt);

        var forged = next(state, actor, new ClaimAdded(new Claim(
            new ClaimId("C-forged"), "Borrowed authority", ClaimStatus.Open, [], null,
            new Provenance(actor, recordedAt, "claim.add"), null, new LessonId("L-never-seen"))));

        Assert.Throws<GovernanceException>(() => reducer.Apply(state, forged));
    }

    private static GovernedTaskState WithRecalledLesson(
        out CommandHandler handler, out ActorId actor, out DateTimeOffset now)
    {
        var state = Opened(out var reducer, out var next, out var localActor, out var recordedAt);
        state = reducer.Apply(state, next(state, localActor, new LessonRecalled(new Lesson(
            RecalledId, new TaskId("earlier-task"), LessonSourceKind.Imported, "C9",
            "The adapter discards the session on failure",
            "A failed run could not be resumed and the work was redone",
            ["adapter.cs:104"], new Provenance(localActor, recordedAt, "lesson.import"), null,
            LessonClass.Refuted, "AILedger", ["adapter"], "grep -n session adapter.cs", "Do not drop the session",
            LessonActor.Verifier))));
        handler = new CommandHandler();
        actor = localActor;
        now = recordedAt;
        return state;
    }

    private static GovernedTaskState Opened(
        out TaskReducer reducer,
        out Func<GovernedTaskState, ActorId, LedgerEventData, LedgerEvent> next,
        out ActorId actor,
        out DateTimeOffset recordedAt)
    {
        var id = new TaskId("lesson-citation");
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

        var state = localReducer.Apply(null, next(null!, localActor, new TaskOpened("Citations", "Goal")));
        return localReducer.Apply(state, next(state, localActor, new RoleAssigned(new RoleAssignment(
            localActor, RoleKind.Operator, Enum.GetValues<Capability>(),
            new Provenance(localActor, localRecordedAt, "task.open")))));
    }
}
