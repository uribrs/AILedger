using System.Text.Json;
using System.Text.Json.Nodes;
using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

// A door that takes its justification and drops it before anything durable is written is a hole,
// not a door. The waiver event always carried the reason (GC1), but nothing projected it, so
// `status` — which reads state and never the log — could not say which live work was decomposed
// unbriefed, by whom, or why. These pin the projection that joins each waiver to the work item or
// the run it carried through, and the reader that answers the question from it.
//
// The justification stays in exactly one place, the `context.brief-waived` event (GX1). Copying it
// onto the item and the run was the first repair and was wrong: two records of one fact can
// disagree, and the join the log already carried is all the reader was missing.
public sealed class BriefWaiverRecordTests
{
    // The operator door on 'work add'. The waiver says why, the projection says what it bought.
    [Fact]
    public void AnItemAddedThroughTheOperatorDoorRecordsTheReasonAndTheDecider()
    {
        var task = new TestTask { AutoBuildContext = false, AutoServeSkills = false };

        var outcome = task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Work", null, [], [],
            WithoutBriefReason: "The skills are mid-rewrite and this item does not touch them"));

        // The event carries the justification, and the item it caused carries none: a later replay
        // rebuilds the pair from the waiver and the causationId that names it.
        var waived = Assert.IsType<ContextBriefWaived>(outcome.Events[0].Data);
        Assert.Equal(
            "The skills are mid-rewrite and this item does not touch them", waived.OperatorReason);
        Assert.IsType<WorkItemAdded>(outcome.Events[1].Data);
        Assert.Equal(outcome.Events[0].EventId, outcome.Events[1].CausationId);

        var waiver = Assert.Single(task.State.ContextBriefWaivers);
        Assert.Equal(ContextBriefWaiver.WorkItemKind, waiver.Kind);
        Assert.Equal("W1", waiver.TargetId);
        Assert.Equal(task.OperatorId, waiver.Actor);
        Assert.Equal(
            "The skills are mid-rewrite and this item does not touch them", waiver.OperatorReason);
        Assert.Null(waiver.StaleBriefEvidenceId);
    }

    [Fact]
    public void AnItemAddedThroughTheStaleDoorNamesTheEvidenceItCited()
    {
        var task = new TestTask { AutoBuildContext = false, AutoServeSkills = false };
        task.BuildContext(task.OperatorId);
        RecordEvidence(task, "E1");

        task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Work", null, [], [],
            SkillsServedNow: EditedSince,
            StaleBriefEvidenceId: new EvidenceId("E1")));

        var waiver = Assert.Single(task.State.ContextBriefWaivers);
        Assert.Equal("W1", waiver.TargetId);
        Assert.Equal(new EvidenceId("E1"), waiver.StaleBriefEvidenceId);
        Assert.Null(waiver.OperatorReason);
        Assert.Equal(task.OperatorId, waiver.Actor);
    }

    // The other half of the same rule, and the one that keeps the projection honest: an item added
    // on a current brief must be indistinguishable from every item this ledger recorded before the
    // doors existed. One event, and nothing in the waiver projection to name it.
    [Fact]
    public void AnItemAddedOnACurrentBriefCarriesNoWaiverAtAll()
    {
        var task = new TestTask();

        var outcome = task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Work", null, [], []));

        Assert.IsType<WorkItemAdded>(Assert.Single(outcome.Events).Data);
        Assert.Empty(task.State.ContextBriefWaivers);
    }

    // The join is the causationId chain and nothing looser. A waiver whose command died after the
    // waiver was appended must not attach itself to whatever arrives next, which would name work as
    // unbriefed that was in fact added on a current brief.
    [Fact]
    public void AWaiverJoinsOnlyTheEventItCaused()
    {
        var task = new TestTask { AutoBuildContext = false, AutoServeSkills = false };
        task.BuildContext(task.OperatorId);
        var reducer = new TaskReducer();
        var waived = Event(task, "task-1:0000009001", null, new ContextBriefWaived(
            "add work", "The cognitive layer is mid-rewrite", null));
        var state = reducer.Apply(task.State, waived);

        // An orphan: this item was added by a later command and names no cause at all.
        state = reducer.Apply(state, Event(task, "task-1:0000009002", null, new WorkItemAdded(
            new WorkItem(new WorkItemId("W1"), "Work", null, WorkItemStatus.Proposed, [], []))));

        Assert.Contains(new WorkItemId("W1"), state.WorkItems.Keys);
        Assert.Empty(state.ContextBriefWaivers);
    }

    // A provider launch through the operator door. No field for who on the run: the waiver event's
    // actor is the authorising actor and that is the one the gate was checked against.
    [Fact]
    public void ARunLaunchedThroughTheOperatorDoorRecordsTheReasonAgainstTheRun()
    {
        var task = new TestTask { AutoBuildContext = false, AutoServeSkills = false };
        task.BuildContext(task.OperatorId);
        task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Work", null, [], [],
            SkillsServedNow: ContextBrief.Served));

        var outcome = task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), new WorkItemId("W1"), "codex",
            null, null, null, CommandHandler.HashLaunchToken("a-launch-token"), null, null,
            WithoutBriefReason: "Dispatching the agent that is rewriting the skills the hashes are over"));

        Assert.IsType<RunStarted>(outcome.Events[1].Data);
        Assert.Equal(outcome.Events[0].EventId, outcome.Events[1].CausationId);

        var waiver = Assert.Single(task.State.ContextBriefWaivers);
        Assert.Equal(ContextBriefWaiver.ProviderLaunchKind, waiver.Kind);
        Assert.Equal("R1", waiver.TargetId);
        Assert.Equal(task.OperatorId, waiver.Actor);
        Assert.Equal(
            "Dispatching the agent that is rewriting the skills the hashes are over",
            waiver.OperatorReason);
        Assert.Null(waiver.StaleBriefEvidenceId);
    }

    // Completing a run rebuilds it with `with`, and the waiver is projected beside the run rather
    // than onto it, so neither can drop the other. A launch answerable only while its agent is still
    // running answers nothing: the question is asked afterwards.
    [Fact]
    public void TheDoorOnARunSurvivesTheRunBeingCompleted()
    {
        var task = new TestTask { AutoBuildContext = false, AutoServeSkills = false };
        task.BuildContext(task.OperatorId);
        RecordEvidence(task, "E1");
        task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Work", null, [], [],
            SkillsServedNow: ContextBrief.Served));
        const string token = "a-launch-token";
        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), new WorkItemId("W1"), "codex",
            null, null, null, CommandHandler.HashLaunchToken(token), null, EditedSince,
            StaleBriefEvidenceId: new EvidenceId("E1")));

        task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), AgentRunStatus.Completed,
            "session-1", token));

        var run = task.State.Runs[new RunId("R1")];
        Assert.Equal(AgentRunStatus.Completed, run.Status);
        var waiver = Assert.Single(task.State.ContextBriefWaivers);
        Assert.Equal("R1", waiver.TargetId);
        Assert.Equal(new EvidenceId("E1"), waiver.StaleBriefEvidenceId);
    }

    // The reader. Without it the fields are present and unanswerable, which is the defect this task
    // was opened to fix in another form.
    [Fact]
    public void StatusNamesTheLiveWorkThatCameThroughADoorAndWhoOpenedIt()
    {
        var task = new TestTask { AutoBuildContext = false, AutoServeSkills = false };
        task.BuildContext(task.OperatorId);
        RecordEvidence(task, "E1");
        task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Briefed", null, [], [],
            SkillsServedNow: ContextBrief.Served));
        task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W2"), "No brief", null, [], [],
            WithoutBriefReason: "The cognitive layer is mid-rewrite"));
        task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W3"), "Stale brief", null, [], [],
            SkillsServedNow: EditedSince, StaleBriefEvidenceId: new EvidenceId("E1")));

        var waivers = BriefWaiver.Compute(task.State);

        // W1 came through no door and is absent. A block listing every item would be the JSON dump
        // the operator already has.
        Assert.Equal(2, waivers.Count);
        Assert.Equal(["W2", "W3"], waivers.Select(waiver => waiver.Id).ToArray());
        Assert.All(waivers, waiver => Assert.Equal("work item", waiver.Kind));
        Assert.All(waivers, waiver => Assert.Equal("operator", waiver.Actor));
        Assert.Equal("The cognitive layer is mid-rewrite", waivers[0].OperatorReason);
        Assert.Null(waivers[0].StaleBriefEvidence);
        Assert.Equal(new EvidenceId("E1"), waivers[1].StaleBriefEvidence);
        Assert.Null(waivers[1].OperatorReason);
    }

    // Live work only. An abandoned item is history, and the question `status` is being asked is
    // which of the work in front of the operator now was decomposed unbriefed.
    [Fact]
    public void AWaivedItemLeavesTheBlockOnceItIsAbandoned()
    {
        var task = new TestTask { AutoBuildContext = false, AutoServeSkills = false };
        task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Work", null, [], [],
            WithoutBriefReason: "The cognitive layer is mid-rewrite"));
        Assert.Single(BriefWaiver.Compute(task.State));

        task.Apply(new AbandonWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Superseded"));

        Assert.Empty(BriefWaiver.Compute(task.State));
    }

    // A launch is a thing that happened, so it stays in the block after its run ends. Filtering runs
    // the way live work is filtered would hide the dispatch the moment the agent exits.
    [Fact]
    public void ALaunchThroughADoorStaysInTheBlockAfterItsRunEnds()
    {
        var task = new TestTask { AutoBuildContext = false, AutoServeSkills = false };
        task.BuildContext(task.OperatorId);
        task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Work", null, [], [],
            SkillsServedNow: ContextBrief.Served));
        const string token = "a-launch-token";
        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), new WorkItemId("W1"), "codex",
            null, null, null, CommandHandler.HashLaunchToken(token), null, null,
            WithoutBriefReason: "Dispatching the agent that is rewriting the skills"));
        task.Apply(new CompleteRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), AgentRunStatus.Completed,
            "session-1", token));

        var waiver = Assert.Single(BriefWaiver.Compute(task.State));
        Assert.Equal("provider launch", waiver.Kind);
        Assert.Equal("R1", waiver.Id);
        Assert.Equal("operator", waiver.Actor);
    }

    // The block is absent when no door was opened, for the reason the owed block is absent when
    // nothing is owed: a task that opened no door reads exactly as it did before the doors existed.
    [Fact]
    public async Task StatusOmitsTheBlockOnATaskThatOpenedNoDoor()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var application = Application(output);
        Assert.Equal(0, await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--title", "Task", "--goal", "Goal"], CancellationToken.None));
        output.GetStringBuilder().Clear();

        Assert.Equal(0, await application.RunAsync(
            ["status", "--root", root.Path, "--task", "T1"], CancellationToken.None));

        Assert.Null(Parse(output)["briefsWaived"]);
    }

    // End to end, because a field the kernel records and the command line cannot print is the same
    // unanswerable record in a different place.
    [Fact]
    public async Task StatusPrintsTheWaiverBlockAfterTheOperatorDoorIsUsed()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var application = Application(output);
        Assert.Equal(0, await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--title", "Task", "--goal", "Goal"], CancellationToken.None));
        Assert.Equal(0, await application.RunAsync(
            ["work", "add", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--id", "W1", "--title", "Work",
             "--without-brief", "The cognitive layer is being rewritten by another agent"],
            CancellationToken.None));
        output.GetStringBuilder().Clear();

        Assert.Equal(0, await application.RunAsync(
            ["status", "--root", root.Path, "--task", "T1"], CancellationToken.None));

        var waived = Assert.IsType<JsonArray>(Parse(output)["briefsWaived"]);
        var row = Assert.IsType<JsonObject>(Assert.Single(waived));
        Assert.Equal("work item", (string?)row["kind"]);
        Assert.Equal("W1", (string?)row["id"]);
        Assert.Equal("operator", (string?)row["actor"]);
        Assert.Equal(
            "The cognitive layer is being rewritten by another agent", (string?)row["operatorReason"]);
    }

    // The twin rule, in the direction that has already made this ledger unreadable twice. Command
    // time may require a reason when a door is used; replay may only read one. A work.added payload
    // carrying none of the three fields — which is every one in this ledger — must still replay.
    [Fact]
    public async Task ReplayAcceptsAWorkAddedPayloadThatCarriesNoWaiverFields()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("legacy-task");
        var actor = new ActorId("operator");
        var writer = Service(root.Path);
        await writer.ExecuteAsync(taskId, new OpenTaskCommand(
            actor, null, "l1", taskId, "Task", "Goal"), CancellationToken.None);
        await writer.ExecuteAsync(taskId, new RecordContextBuiltCommand(
            actor, null, "l2", null, ContextBrief.Served), CancellationToken.None);
        await writer.ExecuteAsync(taskId, new AddWorkItemCommand(
            actor, null, "l3", new WorkItemId("W1"), "Work", null, [], [],
            SkillsServedNow: ContextBrief.Served), CancellationToken.None);

        // No waiver event and no waiver field on the payload, exactly as every work.added this
        // ledger recorded before the doors existed reads.
        var events = await File.ReadAllTextAsync(Path.Combine(root.Path, taskId.Value, "events.jsonl"));
        Assert.DoesNotContain("withoutBriefReason", events, StringComparison.Ordinal);
        Assert.DoesNotContain("context.brief-waived", events, StringComparison.Ordinal);

        var replayed = await Service(root.Path).GetStateAsync(taskId, CancellationToken.None);

        Assert.NotNull(replayed);
        Assert.Empty(replayed.ContextBriefWaivers);
        Assert.Empty(BriefWaiver.Compute(replayed));
    }

    // The reducer is driven directly for the orphan case above, so the events have to be built by
    // hand. Everything but the data is what CommandHandler would have written.
    private static LedgerEvent Event(
        TestTask task,
        string eventId,
        EventId? causationId,
        LedgerEventData data) =>
        new(GovernedTaskState.CurrentSchemaVersion, new EventId(eventId), task.TaskId, task.OperatorId,
            new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero), causationId, "orphan", data);

    private static readonly IReadOnlyList<ContextSkill> EditedSince =
    [
        new ContextSkill("workflow-coordinator", "hash-of-workflow-coordinator"),
        new ContextSkill("task-orchestrator", "an-edited-orchestrator")
    ];

    private static void RecordEvidence(TestTask task, string evidenceId) =>
        task.Apply(new AddEvidenceCommand(
            task.OperatorId, null, task.NextCorrelation(), new EvidenceId(evidenceId), "source-read",
            "cognitive/skills/task-orchestrator/SKILL.md",
            "The edited section is the one this work item does not read", [], []));

    private static CliApplication Application(TextWriter output) =>
        new(output, TextWriter.Null, Service,
            _ => throw new InvalidOperationException("No provider is launched here."),
            new ContextAssembler());

    private static JsonObject Parse(StringWriter output) =>
        JsonNode.Parse(output.ToString()) as JsonObject
        ?? throw new JsonException("Status did not print a JSON object.");

    private static IGovernedTaskService Service(string root)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(root, new CommandHandler(reducer, new AuthorizationPolicy()), reducer);
    }
}
