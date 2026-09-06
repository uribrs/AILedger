using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

// F3b. Two disjoint areas in one work item is one agent holding what two could have held. The
// kernel cannot judge whether that was the right call — sometimes the areas really do only change
// together — but it can refuse to let the call go unrecorded. Claiming a second area now costs the
// operator a written alternative saying why the work was not split.
public sealed class ScopeJustificationTests
{
    [Fact]
    public void AWorkItemClaimingTwoAreasIsRefusedWhenNothingRecordsWhyItWasNotSplit()
    {
        var task = new TestTask();

        var error = Assert.Throws<GovernanceException>(() => Add(task, "W1", null, First, Second));

        Assert.Contains("more than one area", error.Message, StringComparison.Ordinal);
        Assert.Empty(task.State.WorkItems);
    }

    [Fact]
    public void NamingTheRecordedAlternativeAllowsItAndTheJustificationIsKeptOnTheWorkItem()
    {
        var task = new TestTask();
        RecordAlternative(task, "ALT1");

        Add(task, "W1", new AlternativeId("ALT1"), First, Second);

        var workItem = task.State.WorkItems[new WorkItemId("W1")];
        // The justification is on the work item, not only on the command, so a reader a month later
        // can get from "why does this item hold two areas" to the argument in one hop.
        Assert.Equal(new AlternativeId("ALT1"), workItem.NotSplitJustification);
        Assert.Equal(2, workItem.ResourceScope.Count);
    }

    [Fact]
    public void TheNamedJustificationMustBeAnAlternativeThatExists()
    {
        var task = new TestTask();

        // A justification nobody can read is not a justification. Naming an id that was never
        // recorded would leave the decision as undocumented as omitting it entirely.
        var error = Assert.Throws<GovernanceException>(() =>
            Add(task, "W1", new AlternativeId("ALT-missing"), First, Second));

        Assert.Contains("Unknown alternative 'ALT-missing'", error.Message, StringComparison.Ordinal);
        Assert.Empty(task.State.WorkItems);
    }

    [Fact]
    public void OneAreaNeedsNoJustification()
    {
        var task = new TestTask();

        Add(task, "W1", null, First);

        Assert.Equal(WorkItemStatus.Proposed, task.State.WorkItems[new WorkItemId("W1")].Status);
        Assert.Null(task.State.WorkItems[new WorkItemId("W1")].NotSplitJustification);
    }

    [Fact]
    public void NoAreaAtAllNeedsNoJustification()
    {
        var task = new TestTask();

        Add(task, "W1", null);

        Assert.Empty(task.State.WorkItems[new WorkItemId("W1")].ResourceScope);
    }

    // The most important test here. W10 in the live ledger holds two areas and carries no
    // justification, because it was added before this rule existed. The rule is command-time only
    // for that reason: it keys on the ABSENCE of a field, which every older event lacks, so
    // enforcing it at replay would reject a real work item that was legal when it was written.
    // If a later refactor adds this rule to the validator, this test is what stops it.
    [Fact]
    public void ReplayAcceptsAMultiScopeWorkItemCarryingNoJustification()
    {
        var state = Opened("legacy-multi-scope", out var reducer, out var next, out var actor);

        state = reducer.Apply(state, next(state, new WorkItemAdded(new WorkItem(
            new WorkItemId("W10"), "Two areas, recorded before the rule", actor, WorkItemStatus.Proposed,
            [], [First, Second]))));

        var workItem = state.WorkItems[new WorkItemId("W10")];
        Assert.Equal(2, workItem.ResourceScope.Count);
        Assert.Null(workItem.NotSplitJustification);
    }

    // The shape check is the safe half, and replay may hold it: it fires only when the field is
    // present, and no event written before the field existed can carry one.
    [Fact]
    public void ReplayRefusesAJustificationNamingAnAlternativeThatDoesNotExist()
    {
        var state = Opened("dangling-justification", out var reducer, out var next, out var actor);

        var forged = next(state, new WorkItemAdded(new WorkItem(
            new WorkItemId("W1"), "Two areas", actor, WorkItemStatus.Proposed, [], [First, Second],
            null, null, new AlternativeId("ALT-missing"))));

        var error = Assert.Throws<GovernanceException>(() => reducer.Apply(state, forged));
        Assert.Contains("Unknown alternative 'ALT-missing'", error.Message, StringComparison.Ordinal);
    }

    private static string First => Path.GetFullPath("src/AILedger.Core");
    private static string Second => Path.GetFullPath("src/AILedger.Cli");

    private static void Add(TestTask task, string id, AlternativeId? justification, params string[] scope) =>
        task.Apply(new AddWorkItemCommand(task.OperatorId, null, task.NextCorrelation(),
            new WorkItemId(id), $"Work {id}", task.OperatorId, [], scope, justification));

    private static void RecordAlternative(TestTask task, string id) =>
        task.Apply(new RecordAlternativeCommand(task.OperatorId, null, task.NextCorrelation(),
            new AlternativeId(id), "Split the two areas into separate work items",
            "The two areas only ever change together in this task", null));

    private static GovernedTaskState Opened(
        string taskId,
        out TaskReducer reducer,
        out Func<GovernedTaskState, LedgerEventData, LedgerEvent> next,
        out ActorId actor)
    {
        var id = new TaskId(taskId);
        var localActor = new ActorId("operator");
        var recordedAt = new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        var localReducer = new TaskReducer();
        actor = localActor;
        reducer = localReducer;
        next = (current, data) => new LedgerEvent(
            GovernedTaskState.CurrentSchemaVersion,
            new EventId($"{id.Value}:{(current?.Version ?? 0) + 1:D10}"),
            id, localActor, recordedAt, null, "replay", data);

        var state = localReducer.Apply(null, next(null!, new TaskOpened("Legacy", "Goal")));
        return localReducer.Apply(state, next(state, new RoleAssigned(new RoleAssignment(
            localActor, RoleKind.Operator, Enum.GetValues<Capability>(),
            new Provenance(localActor, recordedAt, "task.open")))));
    }
}
