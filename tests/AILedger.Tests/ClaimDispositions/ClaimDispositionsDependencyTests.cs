using AILedger.Core.Contracts;
using AILedger.Core.ClaimDispositions;
using AILedger.Core.Domain;
using AILedger.Tests.Findings;
using System.Text.Json;
using AILedger.Cli.ClaimDispositions;

namespace AILedger.Tests.ClaimDispositions;

[Collection(ClaimDispositionsRecordingCollection.Name)]
public sealed class ClaimDispositionsDependencyTests
{
    [Theory]
    [InlineData("proposed", WorkItemStatus.Stale)]
    [InlineData("completed", WorkItemStatus.Stale)]
    [InlineData("blocked", WorkItemStatus.Stale)]
    [InlineData("active", WorkItemStatus.Blocked)]
    public async Task RejectionIncludesEveryDependencyConsequenceAndPreservesUnrelatedWork(string setup, WorkItemStatus expected)
    {
        using var f = new ClaimDispositionsFixture();
        await f.OpenAsync();
        await f.ExecuteAsync(new ProposeDecisionCommand(f.Actor, null, "d1", new("D1"), "Proposed", "Reason", [new("C2")], null));
        await f.ExecuteAsync(new ProposeDecisionCommand(f.Actor, null, "d2", new("D2"), "Accepted", "Reason", [new("C2")], null));
        await f.ExecuteAsync(new ResolveDecisionCommand(f.Actor, null, "accept", new("D2"), DecisionStatus.Accepted));
        await f.ExecuteAsync(new ProposeDecisionCommand(f.Actor, null, "d3", new("D3"), "Unrelated", "Reason", [new("C1")], null));
        foreach (var stage in new[] { TaskStage.Research, TaskStage.Design, TaskStage.Scope, TaskStage.Ready })
            await f.ExecuteAsync(new RequestStageTransitionCommand(f.Actor, null, "stage", stage, WithoutPrerequisitesReason: "Disposable dependency fixture"));
        var worker = new ActorId("worker");
        await f.ExecuteAsync(new AssignRoleCommand(f.Actor, null, "assign", worker, RoleKind.Worker, []));
        foreach (var id in new[] { "W1", "W2" })
            await f.ExecuteAsync(new AddWorkItemCommand(f.Actor, null, "add", new(id), id, worker,
                [new(id == "W1" ? "C2" : "C1")], [Path.Combine(f.Root, id)], WithoutBriefReason: "Disposable dependency fixture"));
        if (setup == "completed")
            await f.ExecuteAsync(new CompleteWorkItemCommand(f.Actor, null, "complete", new("W1"), "Fixture completion without assurance"));
        if (setup == "blocked")
            await f.ExecuteAsync(new BlockWorkItemCommand(f.Actor, null, "block", new("W1"), "Manual fixture blocker", null));
        if (setup == "active")
        {
            await f.ExecuteAsync(new RequestStageTransitionCommand(f.Actor, null, "execute", TaskStage.Execution, WithoutPrerequisitesReason: "Disposable dependency fixture"));
            await f.ExecuteAsync(new StartRunCommand(f.Actor, null, "run", new("RW"), new("W1"), "codex", null, SubjectActorId: worker));
        }
        var first = await f.RecordAsync();
        Assert.Null(first.Error);
        Assert.Empty(first.Receipt!.Dispositions[0].Dependencies);
        var changes = first.Receipt.Dispositions[1].Dependencies;
        Assert.Equal(3, changes.Count);
        Assert.Equal(new[] { "D1", "D2", "W1" }, changes.Select(x => x.Id));
        Assert.Equal(5, first.Receipt.EventIds.Count);
        FindingsResponseSchema.AssertValid(ClaimDispositionsResponseWriter.Write(first), "ClaimDispositions.ResponseSchema");
        var state = await f.StateAsync();
        Assert.Equal(expected, state.WorkItems[new("W1")].Status);
        Assert.Equal(WorkItemStatus.Proposed, state.WorkItems[new("W2")].Status);
        Assert.Equal(DecisionStatus.Invalidated, state.Decisions[new("D1")].Status);
        Assert.Equal(DecisionStatus.Invalidated, state.Decisions[new("D2")].Status);
        Assert.Equal(DecisionStatus.Proposed, state.Decisions[new("D3")].Status); // Validation is not decision acceptance.
        if (setup == "active")
        {
            Assert.Equal(AgentRunStatus.Active, state.Runs[new("RW")].Status);
            await f.ExecuteAsync(new CompleteRunCommand(f.Actor, null, "end", new("RW"), AgentRunStatus.Completed, "session"));
            Assert.Equal(WorkItemStatus.Blocked, (await f.StateAsync()).WorkItems[new("W1")].Status);
        }
        Assert.True((await f.RecordAsync()).Replayed);
        await Assert.ThrowsAsync<GovernanceException>(() => f.ExecuteAsync(new ProposeDecisionCommand(f.Actor, null, "invalid", new("D4"), "Invalid", "Reason", [new("C2")], null)));
    }

    [Fact]
    public async Task ProgressiveInvalidationOfSharedDependentsMatchesSequentialCommands()
    {
        using var f = new ClaimDispositionsFixture();
        await f.OpenAsync();
        await f.ExecuteAsync(new ProposeDecisionCommand(f.Actor, null, "d1", new("D1"), "Shared", "Reason", [new("C1"),new("C2")], null));
        var request = ClaimDispositionsFixture.Request();
        request = request with { Dispositions = [request.Dispositions[0] with { Status = ClaimStatus.Rejected, Evidence = [new("E3")] }, request.Dispositions[1]] };
        var result = await f.RecordAsync(request);
        Assert.Null(result.Error);
        Assert.Single(result.Receipt!.Dispositions[0].Dependencies);
        Assert.Empty(result.Receipt.Dispositions[1].Dependencies);
        Assert.Equal(3, result.Receipt.EventIds.Count);
    }

    [Fact]
    public async Task NewClaimAndDecisionAuthorityRemainSeparateAndTerminalClaimsCannotReopen()
    {
        using var f = new ClaimDispositionsFixture();
        await f.OpenAsync();
        await f.RecordAsync();
        var request = ClaimDispositionsFixture.Request("again");
        Assert.Equal("state_conflict", (await f.RecordAsync(request)).Error?.Code);
        request = ClaimDispositionsFixture.Single("same-state") with { Dispositions =
            [request.Dispositions[0] with { ExpectedStatus = ClaimStatus.Validated }] };
        Assert.Equal("kernel_refused", (await f.RecordAsync(request)).Error?.Code);
        var rejection = request with { RequestId = "reverse", Dispositions =
            [request.Dispositions[0] with { Status = ClaimStatus.Rejected, Evidence = [new("E3")] }] };
        Assert.Null((await f.RecordAsync(rejection)).Error);
        Assert.Equal(new[] { "E1", "E3" }, (await f.StateAsync()).Claims[new("C1")].EvidenceIds.Select(x => x.Value));
    }
}
