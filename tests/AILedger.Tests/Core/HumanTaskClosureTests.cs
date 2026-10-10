using System.Text.Json;
using AILedger.Core.Application;
using AILedger.Core.Authority;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.TaskClosure;
using AILedger.Storage;
using AILedger.Tests.Findings;

namespace AILedger.Tests.Core;

public sealed class HumanTaskClosureTests
{
    internal static CloseTaskByHumanCommand Confirm(GovernedTaskState state, string reason = "The requested result is sufficient; end the detour.") =>
        new(new("operator"), null, "human-close", reason)
        {
            Admission = new(state.TaskId, new("operator"), state.Version, reason, DateTimeOffset.UtcNow,
                "macos-local-authentication", "fixture-human", state.Roles[new("operator")].AssignedBy)
        };

    [Theory]
    [InlineData(TaskStage.Discovery)]
    [InlineData(TaskStage.Research)]
    [InlineData(TaskStage.Design)]
    [InlineData(TaskStage.Scope)]
    [InlineData(TaskStage.Ready)]
    [InlineData(TaskStage.Execution)]
    [InlineData(TaskStage.Verification)]
    [InlineData(TaskStage.Repair)]
    [InlineData(TaskStage.Review)]
    [InlineData(TaskStage.Learn)]
    public async Task HumanCanCloseSuccessfullyAtEveryOpenStageWithoutWorkflowPrerequisites(TaskStage stage)
    {
        using var f = new FindingsFixture();
        await f.OpenAsync();
        var state = (await f.StateAsync()) with { Stage = stage };
        var result = new CommandHandler().Handle(state, Confirm(state), DateTimeOffset.UtcNow);
        Assert.Equal(TaskStage.Archive, result.State.Stage);
        Assert.Equal("succeeded_by_human_override", result.State.HumanClosure!.Outcome);
        Assert.Equal(stage, result.State.HumanClosure.PreviousStage);
        Assert.Empty(result.State.Lessons);
        Assert.Single(result.Events);
    }

    [Fact]
    public async Task ClosurePreservesFindingsAndCancelsActiveWorkWithoutFabricatingVerification()
    {
        using var f = new FindingsFixture();
        await f.OpenAsync();
        await f.ExecuteAsync(new AddClaimCommand(f.Actor, null, "claim", new("C1"), "Unproven result", null));
        await f.ExecuteAsync(new RaiseEscalationCommand(f.Actor, null, "escalate", new("X1"),
            EscalationKind.BusinessDecision, "Which scope?", null, ["Narrow", "Broad"], "Narrow", []));
        var state = await f.StateAsync();
        state = state with
        {
            WorkItems = new Dictionary<WorkItemId, WorkItem>
            {
                [new("W1")] = new(new("W1"), "Detour", null, WorkItemStatus.Blocked, [], ["/fixture"], "open question"),
                [new("W2")] = new(new("W2"), "Done", null, WorkItemStatus.Completed, [], ["/done"])
            },
            Runs = new Dictionary<RunId, AgentRun>
            {
                [new("R1")] = new(new("R1"), f.Actor, new("W1"), "codex", null, AgentRunStatus.Active, DateTimeOffset.UtcNow, null)
            }
        };
        var result = new CommandHandler().Handle(state, Confirm(state), DateTimeOffset.UtcNow).State;
        Assert.Same(state.Claims, result.Claims);
        Assert.Same(state.Escalations, result.Escalations);
        Assert.Same(state.Evidence, result.Evidence);
        Assert.Equal(WorkItemStatus.Abandoned, result.WorkItems[new("W1")].Status);
        Assert.Equal(WorkItemStatus.Completed, result.WorkItems[new("W2")].Status);
        Assert.Equal(AgentRunStatus.Cancelled, result.Runs[new("R1")].Status);
        Assert.Equal([new RunId("R1")], result.HumanClosure!.InterruptedRuns);
        Assert.Equal([new WorkItemId("W1")], result.HumanClosure.ExcludedWorkItems);
    }

    [Fact]
    public async Task OperatorRoleAndSerializedConfirmationCannotCreateHumanAuthority()
    {
        using var f = new FindingsFixture();
        await f.OpenAsync();
        var state = await f.StateAsync();
        var command = Confirm(state);
        var json = JsonSerializer.Serialize<LedgerCommand>(command, LedgerJson.CreateOptions());
        Assert.DoesNotContain("admission", json, StringComparison.OrdinalIgnoreCase);
        var roundTrip = JsonSerializer.Deserialize<LedgerCommand>(json, LedgerJson.CreateOptions())!;
        Assert.Throws<GovernanceException>(() => new CommandHandler().Handle(state, roundTrip, DateTimeOffset.UtcNow));
        Assert.Throws<GovernanceException>(() => new RoutineOrchestrationCommandHandler(
            new(state, f.Actor, DateTimeOffset.UtcNow.AddHours(1)), new CommandHandler())
            .Handle(state, command, DateTimeOffset.UtcNow));
        var agentState = state with { Runs = new Dictionary<RunId, AgentRun>
        { [new("R1")] = new(new("R1"), f.Actor, null, "codex", null, AgentRunStatus.Active, DateTimeOffset.UtcNow, null) } };
        var agent = new AgentSessionCommandHandler(new(f.TaskId, state.Roles[f.Actor], new("R1"), "codex",
            DateTimeOffset.UtcNow.AddHours(1)), new CommandHandler());
        Assert.Throws<GovernanceException>(() => agent.Handle(agentState, Confirm(agentState) with { CorrelationId = "R1" }, DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("reason\nspoofed confirmation")]
    public async Task ReasonCannotBeOmittedOrSpoofPromptLines(string reason)
    {
        using var f = new FindingsFixture();
        await f.OpenAsync();
        var state = await f.StateAsync();
        Assert.Throws<GovernanceException>(() => new CommandHandler().Handle(state, Confirm(state, reason), DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task ConfirmationCannotBeReboundOrReusedAfterAuthorityChangesOrExpiry()
    {
        using var f = new FindingsFixture();
        await f.OpenAsync();
        var state = await f.StateAsync();
        var command = Confirm(state);
        var kernel = new CommandHandler();
        Assert.Throws<GovernanceException>(() => kernel.Handle(state, command with { Reason = "Different" }, DateTimeOffset.UtcNow));
        Assert.Throws<GovernanceException>(() => kernel.Handle(state with { TaskId = new("other") }, command, DateTimeOffset.UtcNow));
        Assert.Throws<GovernanceException>(() => kernel.Handle(state, command with { ActorId = new("other") }, DateTimeOffset.UtcNow));
        Assert.Throws<GovernanceException>(() => kernel.Handle(state, command, DateTimeOffset.UtcNow.AddMinutes(3)));
        await f.ExecuteAsync(new AssignRoleCommand(f.Actor, null, "delegate", new("admin"), RoleKind.Operator, state.Roles[f.Actor].Capabilities));
        await f.ExecuteAsync(new AssignRoleCommand(new("admin"), null, "change", f.Actor, RoleKind.Operator, state.Roles[f.Actor].Capabilities));
        await Assert.ThrowsAsync<GovernanceException>(() => f.ExecuteAsync(command));
        Assert.Null((await f.StateAsync()).HumanClosure);
    }

    [Fact]
    public async Task ClosureIsDurableTerminalAndReplayValidatedWithoutChangingOldProjection()
    {
        using var f = new FindingsFixture();
        await f.OpenAsync();
        var state = await f.StateAsync();
        Assert.DoesNotContain("humanClosure", JsonSerializer.Serialize(state, LedgerJson.CreateOptions()));
        var result = await f.ExecuteAsync(Confirm(state));
        var closed = await f.StateAsync();
        Assert.Equal(JsonSerializer.Serialize(result.State, LedgerJson.CreateOptions()), JsonSerializer.Serialize(closed, LedgerJson.CreateOptions()));
        await Assert.ThrowsAsync<GovernanceException>(() => f.ExecuteAsync(new AddClaimCommand(f.Actor, null, "late", new("C1"), "Late", null)));
        await Assert.ThrowsAsync<GovernanceException>(() => f.ExecuteAsync(Confirm(closed)));
        var recorded = Assert.Single(result.Events);
        var payload = Assert.IsType<TaskClosedByHuman>(recorded.Data);
        Assert.Throws<GovernanceException>(() => new TaskReducer().Apply(state, recorded with
        { Data = payload with { Closure = payload.Closure with { Reason = "" } } }));
        Assert.Throws<GovernanceException>(() => new TaskReducer().Apply(state, recorded with
        { Data = payload with { Closure = payload.Closure with { InterruptedRuns = [new("invented")] } } }));
        Assert.Equal(state.Version + 1, closed.Version);
    }
}
