using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Storage;

public sealed class PathAndProjectionTests
{
    [Theory]
    [InlineData("../escape")]
    [InlineData("nested/task")]
    [InlineData(".")]
    [InlineData("..")]
    public void UnsafeTaskIdentifierIsRejected(string value)
    {
        using var root = new TemporaryDirectory();

        Assert.Throws<ArgumentException>(() => new TaskWorkspacePathResolver(root.Path).Resolve(new TaskId(value)));
    }

    [Fact]
    public async Task ProjectionsAreReadableStableViewsOfCommittedState()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("projection-task");
        var actor = new ActorId("operator");
        var service = new FileGovernedTaskService(root.Path, new CommandHandler(), new TaskReducer());
        await service.ExecuteAsync(taskId,
            new OpenTaskCommand(actor, null, "open", taskId, "Line one\nLine two", "Goal\ncontinued"),
            CancellationToken.None);
        await service.ExecuteAsync(taskId,
            new AddClaimCommand(actor, null, "claim", new ClaimId("C1"), "Claim\ncontinued", "Breakage\npossible"),
            CancellationToken.None);
        await service.ExecuteAsync(taskId,
            new ProposeDecisionCommand(actor, null, "decision", new DecisionId("D1"), "Choose files", "Auditable", [new ClaimId("C1")], null),
            CancellationToken.None);

        var directory = Path.Combine(root.Path, taskId.Value);
        var taskProjection = await File.ReadAllTextAsync(Path.Combine(directory, "task.md"));
        var assumptionsProjection = await File.ReadAllTextAsync(Path.Combine(directory, "assumptions.md"));
        var decisionsProjection = await File.ReadAllTextAsync(Path.Combine(directory, "decisions.md"));

        Assert.Contains("# Line one Line two", taskProjection, StringComparison.Ordinal);
        Assert.Contains("Goal continued", taskProjection, StringComparison.Ordinal);
        Assert.Contains("`C1` — **Open** — Claim continued", assumptionsProjection, StringComparison.Ordinal);
        Assert.Contains("`D1` — **Proposed** — Choose files", decisionsProjection, StringComparison.Ordinal);
        Assert.All(new[] { taskProjection, assumptionsProjection, decisionsProjection }, text => Assert.EndsWith("\n", text, StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnknownTaskReturnsNullStateAndEmptyHistory()
    {
        using var root = new TemporaryDirectory();
        var service = new FileGovernedTaskService(root.Path, new CommandHandler(), new TaskReducer());

        Assert.Null(await service.GetStateAsync(new TaskId("missing"), CancellationToken.None));
        var history = new List<LedgerEvent>();
        await foreach (var item in service.GetHistoryAsync(new TaskId("missing"), CancellationToken.None))
        {
            history.Add(item);
        }

        Assert.Empty(history);
    }
}
