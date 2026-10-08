using AILedger.Cli.Cognitive;
using AILedger.Cli.Dispatch;
using AILedger.Cli.Findings;
using AILedger.Core.Contracts;
using AILedger.Tests.Findings;
using AILedger.Tests.Support;

namespace AILedger.Tests.Orchestration;

public sealed class ParallelWorkerHandoffTests
{
    [Fact]
    public async Task WorkerToolDiscoveryDoesNotInviteForbiddenLessonConsultation()
    {
        using var f = await OpenAsync();
        var state = await f.StateAsync();
        var actor = new ActorId("worker1");
        await using var session = ProviderFindingsSession.Start(f.Service(), f.Root, f.TaskId, actor,
            new("R1"), null, "codex", state.Roles[actor], DateTimeOffset.UtcNow.AddMinutes(5));
        using var relay = await CognitiveRelay.OpenAsync(session.Endpoint);
        var tools = await relay.ToolsAsync();
        var handoff = Assert.Single(tools.EnumerateArray().Where(t => t.GetProperty("name").GetString() == "cognitive_handoff"));
        var kinds = handoff.GetProperty("inputSchema").GetProperty("properties").GetProperty("operation")
            .GetProperty("oneOf").EnumerateArray().Select(o => o.GetProperty("properties").GetProperty("kind").GetProperty("const").GetString()).ToArray();
        Assert.DoesNotContain("lesson_consultation", kinds);
        Assert.DoesNotContain("recon_template", kinds);
        Assert.Contains("routing_assessment", kinds);
        Assert.Contains("No lesson consultation purpose", handoff.GetProperty("description").GetString());
        var version = (await f.StateAsync()).Version;
        var refused = await relay.HandoffAsync(new { kind = "lesson_consultation", purpose = "reconsideration",
            question = "Misapplied worker consultation", tags = new[] { "routing" }, claims = Array.Empty<string>() });
        Assert.Equal("refused", refused.GetProperty("status").GetString());
        Assert.Equal(version, (await f.StateAsync()).Version);
        await relay.FinishAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisjointWorkerReportsDoNotInvalidateEachOther(bool reverse)
    {
        using var f = await OpenAsync();
        var first = new RunId(reverse ? "R2" : "R1");
        var second = new RunId(reverse ? "R1" : "R2");
        foreach (var id in new[] { first, second })
        {
            var actor = new ActorId(id.Value == "R1" ? "worker1" : "worker2");
            await f.ExecuteAsync(new AddClaimCommand(actor, null, id.Value, new("claim-" + id.Value), "Independent observation", null));
            await f.ExecuteAsync(new AddEvidenceCommand(actor, null, id.Value, new("evidence-" + id.Value),
                "test", "fixture://proof", "Own proof", [new("claim-" + id.Value)], []));
            await f.ExecuteAsync(new RecordAlternativeCommand(actor, null, id.Value, new("alternative-" + id.Value),
                "Peer-local approach", "Local reason", null));
            var result = await Session(f, actor, id).InvokeAsync(new("report", new RoutingAssessmentHandoff(
                new(CognitiveWorkKind.Implementation, RoutingDisposition.Blocked, "Own work report", [new("evidence-" + id.Value)]))), default);
            Assert.True(result.Status == HostHandoffStatus.Recorded, result.Diagnostic);
        }
        Assert.All((await f.StateAsync()).Runs.Values, run => Assert.NotNull(run.RoutingAssessment));
    }

    [Theory]
    [InlineData("dependency-evidence")]
    [InlineData("operator-evidence")]
    [InlineData("constraint")]
    public async Task RelevantOrGoverningChangesStillRefuseWorkerHandoff(string change)
    {
        using var f = await OpenAsync();
        var worker = new ActorId("worker1");
        await f.ExecuteAsync(new AddEvidenceCommand(worker, null, "R1", new("own"), "test", "fixture://own", "Own proof", [], []));
        if (change == "constraint")
            await f.ExecuteAsync(new AddConstraintCommand(f.Actor, null, "operator-change", new("new-limit"), "New requirement", "operator", []));
        else
            await f.ExecuteAsync(new AddEvidenceCommand(change == "operator-evidence" ? f.Actor : new("worker2"), null,
                change == "operator-evidence" ? "operator-change" : "R2", new("changed"), "test", "fixture://changed",
                "Changed evidence", change == "dependency-evidence" ? [new("basis")] : [], []));
        var result = await Session(f, worker, new("R1")).InvokeAsync(new("report", new RoutingAssessmentHandoff(
            new(CognitiveWorkKind.Implementation, RoutingDisposition.Proceed, "Delayed report", [new("own")]))), default);
        Assert.Equal(HostHandoffStatus.Refused, result.Status);
        Assert.Contains("basis changed", result.Diagnostic);
        Assert.Null((await f.StateAsync()).Runs[new("R1")].RoutingAssessment);
    }

    private static CognitiveHandoffSession Session(FindingsFixture f, ActorId actor, RunId run) =>
        new(f.Service(), new(f.TaskId, actor, run, run.Value));

    private static async Task<FindingsFixture> OpenAsync()
    {
        var f = new FindingsFixture();
        await f.OpenAsync();
        await ContextBrief.RecordAsync(f.Service(), f.TaskId.Value);
        await f.ExecuteAsync(new AddClaimCommand(f.Actor, null, "setup", new("basis"), "Shared premise", null));
        await f.ExecuteAsync(new AddEvidenceCommand(f.Actor, null, "setup", new("basis-proof"), "test", "fixture://basis", "Premise proof", [new("basis")], []));
        await f.ExecuteAsync(new ResolveClaimCommand(f.Actor, null, "setup", new("basis"), ClaimStatus.Validated, [new("basis-proof")]));
        foreach (var stage in new[] { TaskStage.Research, TaskStage.Design, TaskStage.Scope, TaskStage.Ready })
            await f.ExecuteAsync(new RequestStageTransitionCommand(f.Actor, null, "setup", stage,
                WithoutPrerequisitesReason: "Temporary parallel-worker handoff fixture"));
        for (var i = 1; i <= 2; i++)
        {
            var actor = new ActorId("worker" + i);
            await f.ExecuteAsync(new AssignRoleCommand(f.Actor, null, "setup", actor, RoleKind.Worker,
                [Capability.BuildContext, Capability.AddClaim, Capability.AddEvidence, Capability.RecordAlternative]));
            await f.ExecuteAsync(new AddWorkItemCommand(f.Actor, null, "setup", new("W" + i), "Independent work", actor,
                [new("basis")], [Path.Combine(f.Root, "work" + i)], SkillsServedNow: ContextBrief.Served));
        }
        await f.ExecuteAsync(new RequestStageTransitionCommand(f.Actor, null, "setup", TaskStage.Execution,
            WithoutPrerequisitesReason: "Temporary parallel-worker handoff fixture"));
        for (var i = 1; i <= 2; i++)
            await f.ExecuteAsync(new StartRunCommand(f.Actor, null, "setup", new("R" + i), new("W" + i), "codex", null,
                SubjectActorId: new("worker" + i)));
        return f;
    }
}
