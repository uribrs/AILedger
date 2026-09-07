using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Tests.Core;

// The tags a task is opened with are what bounded lesson recall selects against, so they are the one
// field on the opening command that a later task's brief depends on. They arrived after the stage
// arms were planned and no work item owned a guard for them; these are that guard.
//
// The shape of the rule matters more than the normalisation: command time validates what is present
// and never requires presence, because every task already on disk was opened before tags existed and
// requiring them would reject a history that was legal when it was written.
public sealed class TaskOpeningTagTests
{
    private static readonly DateTimeOffset When = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OpeningATaskKeepsItsTagsInOrderAndTrimsThem()
    {
        var state = Open(["  stage-arms ", "kernel", "\treplay\t"]);

        Assert.Equal(["stage-arms", "kernel", "replay"], state.Tags);
    }

    // The order is the order the operator gave, not a sort. Recall reads the set, but the record is
    // what was written.
    [Fact]
    public void TheTagsAreKeptInTheOrderTheyWereGiven()
    {
        Assert.Equal(["zeta", "alpha", "mu"], Open(["zeta", "alpha", "mu"]).Tags);
    }

    [Fact]
    public void ATagRepeatedAfterTrimmingIsRefused()
    {
        var refusal = Assert.Throws<GovernanceException>(() => Open(["kernel", " kernel "]));

        Assert.Contains("tags", refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnEmptyOrWhitespaceTagIsRefused()
    {
        Assert.Throws<GovernanceException>(() => Open(["kernel", ""]));
        var refusal = Assert.Throws<GovernanceException>(() => Open(["kernel", "   "]));

        Assert.Contains("empty tag", refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    // "Untagged" has one representation, so recall has one thing to test. An explicitly empty list
    // and an omitted one are the same task.
    [Fact]
    public void AnExplicitlyEmptyTagListIsTheSameAsNoTagsAtAll()
    {
        Assert.Null(Open([]).Tags);
        Assert.Null(Open(null).Tags);
    }

    [Fact]
    public void TheOpeningEventCarriesTheNormalisedTags()
    {
        var opened = Assert.IsType<TaskOpened>(Outcome([" kernel", "arms "]).Events[0].Data);

        Assert.Equal(["kernel", "arms"], opened.Tags);
    }

    // The replay half, and the one that matters for a root that already holds tasks. Every task in
    // this repository was opened before the field existed, so the validator must accept a TaskOpened
    // that carries no tags — and it must not be reached by requiring them.
    [Fact]
    public void ReplayAcceptsATaskOpenedThatCarriesNoTags()
    {
        var replayed = Replay(new TaskOpened("Legacy task", "Goal"));

        Assert.Equal(TaskStage.Discovery, replayed.Stage);
        Assert.Null(replayed.Tags);
    }

    [Fact]
    public void ReplayAcceptsATaskOpenedThatCarriesTags()
    {
        var replayed = Replay(new TaskOpened("Tagged task", "Goal", ["kernel", "arms"]));

        Assert.Equal(["kernel", "arms"], replayed.Tags);
    }

    // Replay validates the shape of what is present. A forged opening with a repeated tag is refused
    // there as well, so the field cannot be corrupted by writing the log directly.
    [Fact]
    public void ReplayRefusesAForgedOpeningWhoseTagsRepeat()
    {
        var refusal = Assert.Throws<GovernanceException>(
            () => Replay(new TaskOpened("Forged", "Goal", ["kernel", "kernel"])));

        Assert.Contains("tags", refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReplayRefusesAForgedOpeningWithAnEmptyTag()
    {
        var refusal = Assert.Throws<GovernanceException>(
            () => Replay(new TaskOpened("Forged", "Goal", ["kernel", " "])));

        Assert.Contains("empty tag", refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static GovernedTaskState Open(IReadOnlyList<string>? tags) => Outcome(tags).State;

    private static CommandOutcome Outcome(IReadOnlyList<string>? tags) => new CommandHandler().Handle(
        null,
        new OpenTaskCommand(
            new ActorId("operator"), null, "open-1", new TaskId("tagged-task"),
            "Tagged task", "Prove the opening tags", null, tags),
        When);

    // Straight through the reducer, which re-validates every event it applies, so an opening
    // assembled here is held to the replay rules and nothing else.
    private static GovernedTaskState Replay(TaskOpened opened) => new TaskReducer().Apply(
        null,
        new LedgerEvent(
            GovernedTaskState.CurrentSchemaVersion,
            new EventId("tagged-task:0000000001"),
            new TaskId("tagged-task"),
            new ActorId("operator"),
            When,
            null,
            "replay",
            opened));
}
