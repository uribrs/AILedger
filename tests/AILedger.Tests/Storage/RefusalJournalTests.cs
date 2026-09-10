using System.Text.Json;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Storage;

// The journal is telemetry beside the event log: a retrospective reads which gates fired and which
// an agent could not satisfy. Every test here drives the real FileGovernedTaskService, because the
// question is what the kernel's own refusal path writes, not what a re-reading of a gate would say
// (lesson status-owed:LC11). No test asserts an absence on its own — an empty journal is what a
// completely dead write path also produces, so the two absence checks the contract asks for each
// sit beside a refusal in the same task that proves the path is alive.
public sealed class RefusalJournalTests
{
    [Fact]
    public async Task ARefusedWorkCompletionAppendsOneRowCarryingTheGatesOwnMessage()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("refusal-row-task");
        var actor = new ActorId("operator");
        var service = await StageWorkItemWithACompletedWorkingRunAsync(root.Path, taskId, actor);
        var versionBeforeTheAttempt = (await service.GetStateAsync(taskId, CancellationToken.None))!.Version;

        var refusal = await Assert.ThrowsAsync<GovernanceException>(() => Run(
            service, taskId, new CompleteWorkItemCommand(actor, null, "c5", new WorkItemId("W1"))));

        var row = Assert.Single(ReadJournal(root.Path, taskId));
        Assert.Equal("operator", row.ActorId);
        Assert.Equal("CompleteWorkItemCommand", row.Command);
        Assert.Equal("service", row.Site);
        // Verbatim, not a category derived from it: a derived category is a second, looser model of
        // what the gate requires, which is the defect recorded twice in TaskDebt.
        Assert.Equal(refusal.Message, row.Message);
        Assert.Equal(
            "Work item 'W1' has no completed verifier run and cannot be completed. " +
            "An operator may complete it without one by recording why.",
            row.Message);
        // The version the refused attempt was made against, which a refusal does not advance.
        Assert.Equal(versionBeforeTheAttempt, row.TaskVersion);
        Assert.NotEqual(default, row.RecordedAt);
    }

    [Fact]
    public async Task TheRowCarriesTheSixSpecifiedFieldsAndNothingElse()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("refusal-shape-task");
        var actor = new ActorId("operator");
        var service = await StageWorkItemWithACompletedWorkingRunAsync(root.Path, taskId, actor);

        await Assert.ThrowsAsync<GovernanceException>(() => Run(
            service, taskId, new CompleteWorkItemCommand(actor, null, "c5", new WorkItemId("W1"))));

        Assert.Equal(
            ["recordedAt", "actorId", "command", "site", "taskVersion", "message"],
            FieldNamesOfFirstRow(root.Path, taskId));
    }

    [Fact]
    public async Task ACommandThatSucceedsAppendsNoRowWhileARefusalInTheSameTaskStillDoes()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("refusal-success-task");
        var actor = new ActorId("operator");

        // Five commands, all accepted: open, brief the operator, add work, start a run, complete it.
        var service = await StageWorkItemWithACompletedWorkingRunAsync(root.Path, taskId, actor);

        Assert.False(File.Exists(JournalPath(root.Path, taskId)));

        // The same assertion is worthless without this: a journal that is never written is also
        // empty after a successful command.
        await Assert.ThrowsAsync<GovernanceException>(() => Run(
            service, taskId, new CompleteWorkItemCommand(actor, null, "c5", new WorkItemId("W1"))));

        Assert.Single(ReadJournal(root.Path, taskId));
    }

    [Fact]
    public async Task TwoRefusalsAgainstOneTaskAppendTwoRowsInTheOrderTheyHappened()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("refusal-order-task");
        var actor = new ActorId("operator");
        var service = await StageWorkItemWithACompletedWorkingRunAsync(root.Path, taskId, actor);

        await Assert.ThrowsAsync<GovernanceException>(() => Run(
            service, taskId, new CompleteWorkItemCommand(actor, null, "c5", new WorkItemId("W1"))));
        // A different command, refused for a different reason, by a different actor — so the row's
        // command and actor fields cannot be passing on a constant.
        var unauthorised = await Assert.ThrowsAsync<GovernanceException>(() => Run(
            service,
            taskId,
            new AddWorkItemCommand(
                new ActorId("worker"), null, "c6", new WorkItemId("W2"), "Unauthorised work", null, [], [])));

        var rows = ReadJournal(root.Path, taskId);
        Assert.Equal(2, rows.Count);
        Assert.Equal("CompleteWorkItemCommand", rows[0].Command);
        Assert.Equal("operator", rows[0].ActorId);
        Assert.Equal("AddWorkItemCommand", rows[1].Command);
        Assert.Equal("worker", rows[1].ActorId);
        Assert.Equal(unauthorised.Message, rows[1].Message);
        Assert.True(rows[1].RecordedAt >= rows[0].RecordedAt);
    }

    // K2: command time writes the journal, replay ignores it entirely. Deleting the file must leave
    // nothing to notice, so the materialised state is compared byte for byte and not field by field.
    [Fact]
    public async Task DeletingTheJournalLeavesTheMaterializedStateByteIdentical()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("refusal-k2-delete-task");
        var actor = new ActorId("operator");
        var service = await StageTaskWithTwoRefusalsAsync(root.Path, taskId, actor);

        var before = await service.GetStateAsync(taskId, CancellationToken.None);
        var expectedStateBytes = await File.ReadAllBytesAsync(StatePath(root.Path, taskId));
        // Without this the test would pass against a feature that never wrote a row.
        Assert.Equal(2, ReadJournal(root.Path, taskId).Count);

        File.Delete(JournalPath(root.Path, taskId));
        File.Delete(StatePath(root.Path, taskId));
        var replayed = await Service(root.Path).GetStateAsync(taskId, CancellationToken.None);

        Assert.Equal(before!.Version, replayed!.Version);
        Assert.Equal(expectedStateBytes, await File.ReadAllBytesAsync(StatePath(root.Path, taskId)));
    }

    // The other half of K2: with the journal present, a fresh replay of the events alone produces
    // the same state.json, and the replay leaves the journal untouched rather than consuming it.
    [Fact]
    public async Task ATaskWithRefusalsMaterializesTheSameStateAsAFreshReplayOfItsEvents()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("refusal-k2-replay-task");
        var actor = new ActorId("operator");
        var service = await StageTaskWithTwoRefusalsAsync(root.Path, taskId, actor);

        await service.GetStateAsync(taskId, CancellationToken.None);
        var expectedStateBytes = await File.ReadAllBytesAsync(StatePath(root.Path, taskId));
        var journalBefore = await File.ReadAllBytesAsync(JournalPath(root.Path, taskId));
        Assert.Equal(2, ReadJournal(root.Path, taskId).Count);

        File.Delete(StatePath(root.Path, taskId));
        await Service(root.Path).GetStateAsync(taskId, CancellationToken.None);

        Assert.Equal(expectedStateBytes, await File.ReadAllBytesAsync(StatePath(root.Path, taskId)));
        Assert.Equal(journalBefore, await File.ReadAllBytesAsync(JournalPath(root.Path, taskId)));
    }

    // Open, a brief for the operator, one work item, one completed run by a working role. That
    // leaves work complete refused
    // on the verifier gate — the second of its four, so the refusal is a real rule and not the
    // easiest one to reach.
    private static async Task<FileGovernedTaskService> StageWorkItemWithACompletedWorkingRunAsync(
        string root,
        TaskId taskId,
        ActorId actor)
    {
        var service = Service(root);
        await Run(service, taskId, new OpenTaskCommand(actor, null, "c1", taskId, "Task", "Goal"));
        await ContextBrief.RecordAsync(service, taskId.Value);
        await Run(service, taskId, new AddWorkItemCommand(
            actor, null, "c2", new WorkItemId("W1"), "Journalled work", actor, [], []));
        await Run(service, taskId, new StartRunCommand(
            actor, null, "c3", new RunId("R1"), new WorkItemId("W1"), "codex", null));
        await Run(service, taskId, new CompleteRunCommand(
            actor, null, "c4", new RunId("R1"), AgentRunStatus.Completed, "session-1"));
        return service;
    }

    private static async Task<FileGovernedTaskService> StageTaskWithTwoRefusalsAsync(
        string root,
        TaskId taskId,
        ActorId actor)
    {
        var service = await StageWorkItemWithACompletedWorkingRunAsync(root, taskId, actor);
        await Assert.ThrowsAsync<GovernanceException>(() => Run(
            service, taskId, new CompleteWorkItemCommand(actor, null, "c5", new WorkItemId("W1"))));
        await Assert.ThrowsAsync<GovernanceException>(() => Run(
            service,
            taskId,
            new AddWorkItemCommand(
                new ActorId("worker"), null, "c6", new WorkItemId("W2"), "Unauthorised work", null, [], [])));
        return service;
    }

    private static string JournalPath(string root, TaskId taskId) =>
        Path.Combine(root, taskId.Value, "refusals.jsonl");

    private static string StatePath(string root, TaskId taskId) =>
        Path.Combine(root, taskId.Value, "state.json");

    // Read through the property names the row is specified with, so a rename fails here instead of
    // deserializing quietly into a default.
    private static IReadOnlyList<JournalRow> ReadJournal(string root, TaskId taskId)
    {
        var path = JournalPath(root, taskId);
        if (!File.Exists(path))
        {
            return [];
        }

        return File.ReadAllLines(path).Select(line =>
        {
            using var document = JsonDocument.Parse(line);
            var element = document.RootElement;
            return new JournalRow(
                element.GetProperty("recordedAt").GetDateTimeOffset(),
                element.GetProperty("actorId").GetString()!,
                element.GetProperty("command").GetString()!,
                element.GetProperty("site").GetString()!,
                element.GetProperty("taskVersion").GetInt64(),
                element.GetProperty("message").GetString()!);
        }).ToArray();
    }

    private static IReadOnlyList<string> FieldNamesOfFirstRow(string root, TaskId taskId)
    {
        using var document = JsonDocument.Parse(File.ReadAllLines(JournalPath(root, taskId))[0]);
        return document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
    }

    private static FileGovernedTaskService Service(string root)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(root, new CommandHandler(reducer, new AuthorizationPolicy()), reducer);
    }

    private static Task<CommandOutcome> Run(IGovernedTaskService service, TaskId taskId, LedgerCommand command) =>
        service.ExecuteAsync(taskId, ContextBrief.WithServedSkills(command), CancellationToken.None);

    private sealed record JournalRow(
        DateTimeOffset RecordedAt,
        string ActorId,
        string Command,
        string Site,
        long TaskVersion,
        string Message);
}
