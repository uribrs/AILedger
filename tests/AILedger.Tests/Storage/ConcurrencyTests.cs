using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Storage;

public sealed class ConcurrencyTests
{
    [Fact]
    public async Task R3_ConcurrentStartsAllowOnlyOneActiveRun()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("concurrent-task");
        var actor = new ActorId("operator");
        var service = CreateService(root.Path);
        await service.ExecuteAsync(taskId,
            new OpenTaskCommand(actor, null, "open", taskId, "Task", "Goal"), CancellationToken.None);
        await ContextBrief.RecordAsync(service, taskId.Value);
        await service.ExecuteAsync(taskId,
            new AddWorkItemCommand(actor, null, "work", new WorkItemId("W1"), "Work", actor, [], [root.Path]),
            CancellationToken.None);

        var first = CaptureAsync(() => CreateService(root.Path).ExecuteAsync(taskId,
            new StartRunCommand(actor, null, "start-1", new RunId("R1"), new WorkItemId("W1"), "codex", null),
            CancellationToken.None));
        var second = CaptureAsync(() => CreateService(root.Path).ExecuteAsync(taskId,
            new StartRunCommand(actor, null, "start-2", new RunId("R2"), new WorkItemId("W1"), "claude", null),
            CancellationToken.None));

        var results = await Task.WhenAll(first, second);

        Assert.Single(results.Where(result => result.Outcome is not null));
        var failure = Assert.Single(results.Where(result => result.Error is not null)).Error;
        Assert.IsType<GovernanceException>(failure);
        var state = await service.GetStateAsync(taskId, CancellationToken.None);
        Assert.NotNull(state);
        Assert.Single(state.Runs.Values.Where(run => run.Status == AgentRunStatus.Active));
        // Open, brief, work, one accepted start. The brief is the event the gate on 'work add'
        // requires, so it is part of every sequence that adds work.
        Assert.Equal(4 + 1, state.Version);
    }

    [Fact]
    public async Task ConcurrentDifferentWorkItemsBothCommitWithoutLosingEvents()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("parallel-task");
        var actor = new ActorId("operator");
        var service = CreateService(root.Path);
        await service.ExecuteAsync(taskId,
            new OpenTaskCommand(actor, null, "open", taskId, "Task", "Goal"), CancellationToken.None);
        await ContextBrief.RecordAsync(service, taskId.Value);
        foreach (var id in new[] { "W1", "W2" })
        {
            await service.ExecuteAsync(taskId,
                new AddWorkItemCommand(actor, null, $"work-{id}", new WorkItemId(id), id, actor, [], [Path.Combine(root.Path, id)]),
                CancellationToken.None);
        }

        await Task.WhenAll(
            CreateService(root.Path).ExecuteAsync(taskId,
                new StartRunCommand(actor, null, "start-1", new RunId("R1"), new WorkItemId("W1"), "codex", null),
                CancellationToken.None),
            CreateService(root.Path).ExecuteAsync(taskId,
                new StartRunCommand(actor, null, "start-2", new RunId("R2"), new WorkItemId("W2"), "claude", null),
                CancellationToken.None));

        var state = await service.GetStateAsync(taskId, CancellationToken.None);
        Assert.NotNull(state);
        Assert.Equal(2, state.Runs.Count);
        Assert.Equal(6 + 1, state.Version);
    }

    [Fact]
    public async Task PausedHistoryConsumerDoesNotHoldMutationLease()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("history-snapshot-task");
        var actor = new ActorId("operator");
        var service = CreateService(root.Path);
        await service.ExecuteAsync(taskId,
            new OpenTaskCommand(actor, null, "open", taskId, "Task", "Goal"), CancellationToken.None);
        await using var history = service.GetHistoryAsync(taskId, CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await history.MoveNextAsync());

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await CreateService(root.Path).ExecuteAsync(taskId,
            new AddClaimCommand(actor, null, "claim", new ClaimId("C1"), "Claim", null), timeout.Token);

        Assert.Equal(3, (await service.GetStateAsync(taskId, CancellationToken.None))?.Version);
    }

    private static FileGovernedTaskService CreateService(string root) =>
        new(root, new CommandHandler(), new TaskReducer());

    private static async Task<(CommandOutcome? Outcome, Exception? Error)> CaptureAsync(
        Func<Task<CommandOutcome>> action)
    {
        try
        {
            return (await action(), null);
        }
        catch (Exception exception)
        {
            return (null, exception);
        }
    }
}
