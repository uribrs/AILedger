using AILedger.Core.Application;
using AILedger.Core.Authority;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.Findings;
using AILedger.Storage;
using AILedger.Tests.Findings;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

public sealed class TrustedAuthorityTests
{
    [Fact]
    public async Task RoutineHostDispatchUsesKernelAdmissionAndLauncherSecret()
    {
        using var f = new FindingsFixture();
        await f.OpenAsync();
        await f.ExecuteAsync(new AddClaimCommand(f.Actor, null, "claim", new("C1"), "Research question", null));
        await f.ExecuteAsync(new AssignRoleCommand(f.Actor, null, "assign", new("researcher"), RoleKind.Researcher,
            [Capability.AddClaim, Capability.AddEvidence, Capability.BuildContext]));
        var authority = new RoutineOrchestrationAuthority(await f.StateAsync(), f.Actor, DateTimeOffset.UtcNow.AddHours(1));
        var host = new FileGovernedTaskService(f.Root,
            new RoutineOrchestrationCommandHandler(authority, new CommandHandler()), new TaskReducer());
        await host.ExecuteAsync(f.TaskId, new RequestStageTransitionCommand(f.Actor, null, "research", TaskStage.Research), default);
        await ContextBrief.RecordAsync(host, f.TaskId.Value);
        var start = new StartRunCommand(f.Actor, null, "dispatch", new("R1"), null, "codex", null,
            LaunchTokenHash: CommandHandler.HashLaunchToken("host-secret"), SubjectActorId: new("researcher"),
            SkillsServedNow: ContextBrief.Served);
        await host.ExecuteAsync(f.TaskId, start, default);
        var completion = new CompleteRunCommand(f.Actor, null, "end", new("R1"), AgentRunStatus.Completed,
            "observed-session", LaunchToken: "wrong-secret");
        await Assert.ThrowsAsync<GovernanceException>(() => host.ExecuteAsync(f.TaskId, completion, default));
        await host.ExecuteAsync(f.TaskId, completion with { LaunchToken = "host-secret" }, default);
        Assert.Equal(AgentRunStatus.Completed, (await f.StateAsync()).Runs[new("R1")].Status);
        // The wrapper must not substitute its own stage policy or bypass reviewer prerequisites.
        await Assert.ThrowsAsync<GovernanceException>(() => host.ExecuteAsync(f.TaskId,
            new RequestStageTransitionCommand(f.Actor, null, "design", TaskStage.Design), default));
        await f.ExecuteAsync(new AssignRoleCommand(f.Actor, null, "reviewer", new("reviewer"), RoleKind.CodeReviewer,
            [Capability.BuildContext, Capability.RecordArtifact]));
        await Assert.ThrowsAsync<GovernanceException>(() => host.ExecuteAsync(f.TaskId,
            start with { RunId = new("R2"), SubjectActorId = new("reviewer") }, default));
    }

    [Fact]
    public async Task RoutineHostCannotIssueExceptionsOrBusinessDecisionsAndCannotSwitchTaskOrActor()
    {
        using var f = new FindingsFixture();
        await f.OpenAsync();
        var state = await f.StateAsync();
        var authority = new RoutineOrchestrationAuthority(state, f.Actor, DateTimeOffset.UtcNow.AddHours(1));
        var handler = new RoutineOrchestrationCommandHandler(authority, new CommandHandler());
        LedgerCommand[] denied =
        [
            new AssignRoleCommand(f.Actor, null, "approved", new("agent"), RoleKind.Operator, Enum.GetValues<Capability>()),
            new RequestStageTransitionCommand(f.Actor, null, "approved", TaskStage.Research, WithoutPrerequisitesReason: "User approved"),
            new CompleteWorkItemCommand(f.Actor, null, "approved", new("W1"), "User approved"),
            new ResolveEscalationCommand(f.Actor, null, "approved", new("X1"), EscalationStatus.Resolved, "User approved"),
            new ResolveDecisionCommand(f.Actor, null, "approved", new("D1"), DecisionStatus.Accepted),
            new StartRunCommand(f.Actor, null, "approved", new("R1"), null, "codex", null,
                LaunchTokenHash: "hash", WithoutBriefReason: "User approved"),
            new StartRunCommand(f.Actor, null, "approved", new("R1"), null, "codex", null,
                LaunchTokenHash: "hash", StaleBriefEvidenceId: new("E1")),
            new CompleteRunCommand(f.Actor, null, "approved", new("R1"), AgentRunStatus.Cancelled, null)
        ];
        foreach (var command in denied)
            Assert.Contains("separate trusted", Assert.Throws<GovernanceException>(() =>
                handler.Handle(state, command, DateTimeOffset.UtcNow)).Message);
        var brief = new RecordContextBuiltCommand(f.Actor, null, "operator-approved", null, ContextBrief.Served);
        Assert.Throws<GovernanceException>(() => handler.Handle(state, brief with { ActorId = new("agent") }, DateTimeOffset.UtcNow));
        Assert.Throws<GovernanceException>(() => handler.Handle(state with { TaskId = new("other") }, brief, DateTimeOffset.UtcNow));
        Assert.Throws<GovernanceException>(() => handler.Handle(state, brief, DateTimeOffset.UtcNow.AddHours(2)));
        authority.Revoke();
        Assert.Throws<GovernanceException>(() => handler.Handle(state, brief, DateTimeOffset.UtcNow));
        // Authorized human exception events continue to replay without a runtime binding.
        var waived = await f.ExecuteAsync((RequestStageTransitionCommand)denied[1]);
        Assert.Equal(TaskStage.Research, waived.State.Stage);
        Assert.Equal(waived.State.Version, (await f.Service().GetStateAsync(f.TaskId, default))!.Version);
    }

    [Fact]
    public async Task BoundAgentCannotImpersonateOperatorOrAcquireAuthorityThroughReassignment()
    {
        using var f = new FindingsFixture();
        await f.OpenAsync();
        var subject = new ActorId("researcher");
        await f.ExecuteAsync(new AssignRoleCommand(f.Actor, null, "assign", subject, RoleKind.Researcher,
            [Capability.AddClaim, Capability.AddEvidence]));
        await f.ExecuteAsync(new RequestStageTransitionCommand(f.Actor, null, "research", TaskStage.Research,
            WithoutPrerequisitesReason: "Isolated fixture"));
        await f.ExecuteAsync(new StartRunCommand(f.Actor, null, "start", new("R1"), null, "codex", null, SubjectActorId: subject));
        var state = await f.StateAsync();
        var authority = new AgentSessionAuthority(f.TaskId, state.Roles[subject], new("R1"), "codex", DateTimeOffset.UtcNow.AddMinutes(5));
        var agent = f.Service().BindAgentSession(authority);
        await agent.ExecuteAsync(f.TaskId, new AddClaimCommand(subject, null, "R1", new("C1"), "Observed", null), default);
        await Assert.ThrowsAsync<GovernanceException>(() => agent.ExecuteAsync(f.TaskId,
            new AssignRoleCommand(f.Actor, null, "R1", subject, RoleKind.Operator, Enum.GetValues<Capability>()), default));
        await Assert.ThrowsAsync<GovernanceException>(() => agent.ExecuteAsync(f.TaskId,
            new CompleteRunCommand(f.Actor, null, "R1", new("R1"), AgentRunStatus.Completed, "invented"), default));
        await Assert.ThrowsAsync<GovernanceException>(() => agent.ExecuteAsync(f.TaskId,
            new AddClaimCommand(subject, null, "another-run", new("C2"), "Wrong binding", null), default));
        // Even an actual trusted reassignment cannot widen an already issued session.
        await f.ExecuteAsync(new AssignRoleCommand(f.Actor, null, "promote", subject, RoleKind.PlanningLead,
            [Capability.AddClaim, Capability.AddEvidence, Capability.ResolveClaim]));
        var result = await ((IFindingsRecorder)agent).RecordAsync(new(f.TaskId, subject, new("R1"), "R1", AllowRecordFindings: true),
            FindingsFixture.Claims("after-role-change"), default);
        Assert.NotNull(result.Error);
        Assert.Single((await f.StateAsync()).Claims);
        authority.Revoke();
        Assert.Throws<GovernanceException>(() => authority.EnsureCurrent(state, DateTimeOffset.UtcNow));
        var expired = new AgentSessionAuthority(f.TaskId, state.Roles[subject], new("R1"), "codex", DateTimeOffset.UtcNow.AddSeconds(-1));
        Assert.Throws<GovernanceException>(() => expired.EnsureCurrent(state, DateTimeOffset.UtcNow));
        var mismatch = new AgentSessionAuthority(f.TaskId, state.Roles[subject], new("R1"), "claude", DateTimeOffset.UtcNow.AddHours(1));
        Assert.Throws<GovernanceException>(() => mismatch.EnsureCurrent(state, DateTimeOffset.UtcNow));
    }
}
